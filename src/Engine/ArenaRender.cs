using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;
using UnityEngine.Rendering;

namespace VampireRounds
{
    /// <summary>
    /// Renders the ROUNDS layer for Vampire Survivors after everything moved: in a run a camera over VS's view (players,
    /// bullets, effects) and, as a second picture, ROUNDS's game UI (card bar, pick texts); while picking, ROUNDS's card
    /// pick view. Both go into the shared frame buffers, un-premultiplied so VS can draw them with its standard sprite
    /// shader: the world picture over VS's stage and enemies, the UI over everything.
    /// </summary>
    [DefaultExecutionOrder(30000)]
    public class ArenaRenderer : MonoBehaviour
    {
        private Camera cam, uiCam;
        private RenderTexture rt, rtUi, rtUiW;
        private int w, h;
        private Canvas uiCanvas;
        private bool uiMissingLogged;
        private static string TPath(Transform t) => t.parent == null ? t.name : TPath(t.parent) + "/" + t.name;

        private struct Pending { public AsyncGPUReadbackRequest world, ui, uiW; public float[] meta; }
        private readonly Queue<Pending> pending = new Queue<Pending>();
        private Texture2D readTex, readTexUi, readTexUiW;
        private int frameSerial;

        private float statAt, statDt, statRender, statPublish;
        private int statFrames, statPublished;
        private readonly System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();

        private void Stats()
        {
            statFrames++;
            statDt += Time.unscaledDeltaTime;
            if (Time.unscaledTime - statAt < 5f) return;
            if (statFrames > 0 && Arena.I != null && Arena.I.Mode != 0)
                EnginePlugin.L.LogInfo(string.Format("arena perf: {0:0.0} fps, render {1:0.0} ms, publish {2:0.0} ms, {3} frames sent, {4}x{5}", statFrames / statDt, statRender / statFrames, statPublish / Mathf.Max(1, statPublished), statPublished, w, h));
            statAt = Time.unscaledTime; statFrames = 0; statDt = 0f; statRender = 0f; statPublish = 0f; statPublished = 0;
        }

        private void LateUpdate()
        {
            Stats();
            var a = Arena.I;
            if (a == null || !a.WorldReady || (a.Mode != 1 && a.Mode != 2)) { DrainOnly(); return; }
            var link = a.link;
            int fw = Mathf.Clamp(link.I(ArenaLink.C_FrameW), 64, ArenaLink.MaxW), fh = Mathf.Clamp(link.I(ArenaLink.C_FrameH), 64, ArenaLink.MaxH);
            if (rt == null || rtUi == null || rtUiW == null || fw != w || fh != h) Setup(fw, fh);
            float camX, camY, ortho;
            if (a.Mode == 1 && !a.Picking)
            {
                Vector2 c = a.ToRounds(link.F(ArenaLink.C_CamX), link.F(ArenaLink.C_CamY));
                camX = c.x; camY = c.y;
                ortho = Mathf.Max(1f, link.F(ArenaLink.C_CamOrtho) / Mathf.Max(0.0001f, a.Scale));
            }
            else
            {
                // ROUNDS's own card pick view (its main camera frame).
                camX = 0f;
                camY = 0f;
                ortho = 20f;
            }
            cam.transform.position = new Vector3(camX, camY, -100f);
            cam.orthographicSize = ortho;
            // In a run the layer is see-through over VS; the pick screen is ROUNDS's own dark screen.
            cam.backgroundColor = a.Mode == 1 && !a.Picking ? new Color(0f, 0f, 0f, 0f) : PickBackground;
            PickCam = cam;
            sw.Reset(); sw.Start();
            cam.Render();
            HookUi();
            // The UI picture twice, over clear black and over white (its coverage is the difference: exact for Unity's UI
            // shader, text and additive glows alike); cleared every frame even while the canvas is missing.
            uiCam.targetTexture = rtUi;
            uiCam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            uiCam.Render();
            uiCam.targetTexture = rtUiW;
            uiCam.backgroundColor = Color.white;
            uiCam.Render();
            statRender += (float)sw.Elapsed.TotalMilliseconds;
            var meta = new[] { a.ToVs(new Vector2(camX, camY)).x, a.ToVs(new Vector2(camX, camY)).y, ortho * a.Scale, a.Mode == 1 && !a.Picking ? 1f : 2f };
            if (SystemInfo.supportsAsyncGPUReadback && !SyncRead)
            {
                if (pending.Count < 6) pending.Enqueue(new Pending { world = AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32), ui = AsyncGPUReadback.Request(rtUi, 0, TextureFormat.RGBA32), uiW = AsyncGPUReadback.Request(rtUiW, 0, TextureFormat.RGBA32), meta = meta });
                GL.Flush();
                DrainOnly();
            }
            else
            {
                sw.Reset(); sw.Start();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                readTex.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
                RenderTexture.active = rtUi;
                readTexUi.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
                RenderTexture.active = rtUiW;
                readTexUiW.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
                RenderTexture.active = prev;
                statRender += (float)sw.Elapsed.TotalMilliseconds;
                Publish(readTex.GetRawTextureData<byte>(), readTexUi.GetRawTextureData<byte>(), readTexUiW.GetRawTextureData<byte>(), meta);
            }
        }

        public static Camera PickCam;
        public static readonly Color PickBackground = new Color(0.118f, 0.118f, 0.13f, 1f);
        public static bool SyncRead = Environment.GetCommandLineArgs().Contains("--arena-sync");

        private void DrainOnly()
        {
            while (pending.Count > 0)
            {
                var head = pending.Peek();
                if (!head.world.done || !head.ui.done || !head.uiW.done) break;
                pending.Dequeue();
                if (head.world.hasError || head.ui.hasError || head.uiW.hasError) continue;
                var data = head.world.GetData<byte>();
                var ui = head.ui.GetData<byte>();
                var uiW = head.uiW.GetData<byte>();
                if (data.Length == w * h * 4 && ui.Length == w * h * 4 && uiW.Length == w * h * 4) Publish(data, ui, uiW, head.meta);
            }
        }

        private void Setup(int fw, int fh)
        {
            w = fw; h = fh;
            // Readbacks still in flight read the old texture: finish them before it goes, and never release it while active.
            foreach (var q in pending) { if (!q.world.done) q.world.WaitForCompletion(); if (!q.ui.done) q.ui.WaitForCompletion(); if (!q.uiW.done) q.uiW.WaitForCompletion(); }
            pending.Clear();
            if (RenderTexture.active == rt || RenderTexture.active == rtUi || RenderTexture.active == rtUiW) RenderTexture.active = null;
            if (cam != null) cam.targetTexture = null;
            if (uiCam != null) uiCam.targetTexture = null;
            if (rt != null) { rt.Release(); Destroy(rt); }
            if (rtUi != null) { rtUi.Release(); Destroy(rtUi); }
            if (rtUiW != null) { rtUiW.Release(); Destroy(rtUiW); }
            if (readTex != null) Destroy(readTex);
            if (readTexUi != null) Destroy(readTexUi);
            if (readTexUiW != null) Destroy(readTexUiW);
            rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            rt.Create();
            rtUi = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32);
            rtUi.Create();
            rtUiW = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32);
            rtUiW.Create();
            readTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            readTexUi = new Texture2D(w, h, TextureFormat.RGBA32, false);
            readTexUiW = new Texture2D(w, h, TextureFormat.RGBA32, false);
            pending.Clear();
            if (cam == null)
            {
                var go = new GameObject("Vampire Rounds Arena Camera");
                DontDestroyOnLoad(go);
                cam = go.AddComponent<Camera>();
                cam.enabled = false;
                cam.orthographic = true;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = 1000f;
                var main = MainCam.instance != null ? MainCam.instance.cam : Camera.main;
                cam.cullingMask = main != null ? main.cullingMask : ~0;
                var ugo = new GameObject("Vampire Rounds UI Camera");
                DontDestroyOnLoad(ugo);
                ugo.transform.position = new Vector3(5000f, 5000f, -100f);
                uiCam = ugo.AddComponent<Camera>();
                uiCam.enabled = false;
                uiCam.orthographic = true;
                uiCam.clearFlags = CameraClearFlags.SolidColor;   // its own picture, over black then over white
                uiCam.cullingMask = 1 << 5;
                uiCam.nearClipPlane = 0.1f;
                uiCam.farClipPlane = 1000f;
            }
            cam.targetTexture = rt;
            uiCam.targetTexture = rtUi;
        }

        /// <summary>ROUNDS's game UI canvas (card bar and pick texts) renders through our UI camera.</summary>
        private void HookUi()
        {
            if (uiCanvas != null) return;
            var go = GameObject.Find("Game/UI/UI_Game/Canvas");
            if (go == null) { if (!uiMissingLogged) { uiMissingLogged = true; EnginePlugin.L.LogWarning("arena: ROUNDS game UI canvas not found"); } return; }
            uiCanvas = go.GetComponent<Canvas>();
            if (uiCanvas == null) return;
            var kids = new List<string>();
            foreach (Transform k in go.transform) kids.Add(k.name + (k.gameObject.activeSelf ? "" : "[off]"));
            EnginePlugin.L.LogInfo("arena: ROUNDS game UI: " + string.Join(", ", kids.ToArray()) + " | CardBarHandler " + (CardBarHandler.instance != null ? TPath(CardBarHandler.instance.transform) + (CardBarHandler.instance.gameObject.activeInHierarchy ? "" : " [inactive]") : "none"));
            uiCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            uiCanvas.worldCamera = uiCam;
            uiCanvas.planeDistance = 10f;
            // VS shows each guy's cards as their logos in its own item slots (CardIcons, the VS mod's CardSlots); ROUNDS's
            // letter squares in the corner would show the same deck twice. The bar still works (cards are added to it).
            if (CardBarHandler.instance != null)
            {
                var g = CardBarHandler.instance.GetComponent<CanvasGroup>() ?? CardBarHandler.instance.gameObject.AddComponent<CanvasGroup>();
                g.alpha = 0f;
                g.blocksRaycasts = false;
                EnginePlugin.L.LogInfo("arena: ROUNDS's card bar hidden (VS shows the card logos)");
            }
        }

        private unsafe void Publish(Unity.Collections.NativeArray<byte> src, Unity.Collections.NativeArray<byte> ui, Unity.Collections.NativeArray<byte> uiW, float[] meta)
        {
            byte* p = (byte*)Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(src);
            byte* u = (byte*)Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(ui);
            byte* uw = (byte*)Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(uiW);
            Publish(p, u, uw, src.Length, meta);
        }

        /// <summary>Both pictures into the same buffer index, the world one then the UI one, then the metadata and the
        /// ready index and serial (the order VS's reader relies on).</summary>
        private unsafe void Publish(byte* src, byte* ui, byte* uiW, int len, float[] meta)
        {
            var link = Arena.I.link;
            sw.Reset(); sw.Start();
            int r = ArenaLink.ResultOff;
            int ready = link.I(r + ArenaLink.R_Ready), reading = link.I(ArenaLink.C_Reading);
            int target = 0;
            while (target == ready || target == reading) target++;
            Unpremultiply((uint*)src, (uint*)((byte*)link.Frame(target).ToPointer()), len / 4);
            UiFrame((uint*)ui, (uint*)uiW, (uint*)((byte*)link.UiFrame(target).ToPointer()), len / 4);
            link.F(r + ArenaLink.R_FrameCamX, meta[0]);
            link.F(r + ArenaLink.R_FrameCamY, meta[1]);
            link.F(r + ArenaLink.R_FrameOrtho, meta[2]);
            link.I(r + ArenaLink.R_FrameW, w);
            link.I(r + ArenaLink.R_FrameH, h);
            link.I(r + ArenaLink.R_Ready, target);
            link.I(r + ArenaLink.R_FrameSerial, ++frameSerial);
            statPublish += (float)sw.Elapsed.TotalMilliseconds;
            statPublished++;
        }

        /// <summary>
        /// The UI picture's coverage, exactly, from its two renders: over black a pixel holds a·c, over white a·c + (1 - a),
        /// so the difference is 1 - a whatever the shader blended with (Unity's UI shader writes a·a as alpha, text blends
        /// premultiplied, glows add). ROUNDS draws its card bar for its own dark screen (a 21 % fill, a 70 % border): over
        /// VS's stages the coverage is raised with 1 - (1 - a)^uiOpacityBoost so the bar reads as solid as the
        /// players (sheets/tuning.json). Colours stay as ROUNDS draws them.
        /// </summary>
        private static readonly byte[] UiAlpha = BuildTable(a => 1.0 - Math.Pow(1.0 - a / 255.0, Tuning.uiOpacityBoost));
        private static byte[] BuildTable(Func<int, double> f)
        {
            var t = new byte[256];
            for (int i = 0; i < 256; i++) t[i] = (byte)Math.Round(Math.Max(0.0, Math.Min(1.0, f(i))) * 255.0);
            return t;
        }

        /// <summary>One pixel of a UI picture from its renders over black and over white, as UiFrame makes them (the card
        /// bar's squares for VS's item area, CardIcons).</summary>
        public static Color32 UiPixel(Color32 b, Color32 w)
        {
            uint px;
            uint bb = (uint)(b.r | (b.g << 8) | (b.b << 16) | (b.a << 24)), ww = (uint)(w.r | (w.g << 8) | (w.b << 16) | (w.a << 24));
            unsafe { UiFrame(&bb, &ww, &px, 1); }
            return new Color32((byte)(px & 0xFF), (byte)((px >> 8) & 0xFF), (byte)((px >> 16) & 0xFF), (byte)(px >> 24));
        }

        private static unsafe void UiFrame(uint* black, uint* white, uint* d, int n)
        {
            for (int i = 0; i < n; i++)
            {
                uint b = black[i], wpx = white[i];
                uint br = b & 0xFF, bg = (b >> 8) & 0xFF, bb = (b >> 16) & 0xFF;
                uint wr = wpx & 0xFF, wg = (wpx >> 8) & 0xFF, wb = (wpx >> 16) & 0xFF;
                // 1 - a per channel (the white gained what the UI did not cover): a blend leaves the same gap in all three,
                // an additive glow (the card bar's fill) saturates the white and leaves gaps of 255 - B, so the largest gap
                // is the one closest to 1 - a in either case.
                uint tr = wr > br ? wr - br : 0u, tg = wg > bg ? wg - bg : 0u, tb = wb > bb ? wb - bb : 0u;
                uint gap = tr > tg ? (tr > tb ? tr : tb) : (tg > tb ? tg : tb);
                uint al = gap >= 247u ? 0u : 255u - gap;
                if (al == 0u)
                {
                    // Nothing covered (an additive glow, or next to nothing): keep what was drawn over black as a soft
                    // coloured pixel, as strong as its brightest channel.
                    uint m = br > bg ? (br > bb ? br : bb) : (bg > bb ? bg : bb);
                    if (m < 8u) { d[i] = 0u; continue; }
                    al = m;
                }
                uint cr = Math.Min(255u, br * 255u / al), cg = Math.Min(255u, bg * 255u / al), cb = Math.Min(255u, bb * 255u / al);
                d[i] = ((uint)UiAlpha[al] << 24) | (cb << 16) | (cg << 8) | cr;
            }
        }

        /// <summary>Unity's render target holds premultiplied colour; VS's sprite shader wants straight colour.</summary>
        private static unsafe void Unpremultiply(uint* s, uint* d, int n)
        {
            for (int i = 0; i < n; i++)
            {
                uint px = s[i];
                if (px == 0u) { d[i] = 0u; continue; }
                uint al = px >> 24;
                if (al == 255u) { d[i] = px; continue; }
                uint cr = px & 0xFF, cg = (px >> 8) & 0xFF, cb = (px >> 16) & 0xFF;
                if (al == 0u)
                {
                    // Additive glow (no alpha written): keep it as a soft coloured pixel.
                    uint m = cr > cg ? (cr > cb ? cr : cb) : (cg > cb ? cg : cb);
                    if (m < 8u) { d[i] = 0u; continue; }
                    al = m;
                }
                cr = Math.Min(255u, cr * 255u / al);
                cg = Math.Min(255u, cg * 255u / al);
                cb = Math.Min(255u, cb * 255u / al);
                d[i] = (al << 24) | (cb << 16) | (cg << 8) | cr;
            }
        }
    }

    /// <summary>Harmony patches that hand ROUNDS's players to VS's input and run (sheets/hooks.json, rounds rows).</summary>
    public static class ArenaPatches
    {
        [HarmonyPrefix, HarmonyPatch(typeof(GeneralInput), "Update")]
        public static bool InputUpdate(GeneralInput __instance)
        {
            var a = Arena.I;
            if (a == null || __instance.controlledElseWhere) return true;
            int slot = a.SlotOf(__instance.GetComponent<CharacterData>());
            if (slot < 0) return true;
            a.FeedInput(__instance, slot);
            return false;
        }

        /// <summary>The hidden engine still hears the player's gamepad (InControl reads XInput without focus): ROUNDS's escape menu
        /// would stop its time, and its QUIT GAME / MAIN MENU would close or reload the engine in the middle of a level.</summary>
        [HarmonyPrefix, HarmonyPatch(typeof(EscapeMenuHandler), "Update")]
        public static bool EscapeMenu()
        {
            if (!EnginePlugin.Hidden) return true;
            EscapeMenuHandler.isEscMenu = false;
            return false;
        }

        [HarmonyPrefix, HarmonyPatch(typeof(QuitButton), "Quit")]
        public static bool QuitPressed()
        {
            if (!EnginePlugin.Hidden) return true;
            EnginePlugin.L.LogWarning("arena: ROUNDS's QUIT GAME was pressed; ignored");
            return false;
        }

        /// <summary>Only the arena makes players: a gamepad or keyboard press must not join an extra ROUNDS player.</summary>
        [HarmonyPrefix, HarmonyPatch(typeof(PlayerAssigner), "LateUpdate")]
        public static bool Joining() => !EnginePlugin.Hidden;

        /// <summary>ROUNDS's world stands still while VS is paused or away (ROUNDS's own pause: its time scale at 0).</summary>
        [HarmonyPostfix, HarmonyPatch(typeof(TimeHandler), "Update")]
        public static void PauseWithVs()
        {
            if (Arena.I == null || !Arena.I.WorldReady || !Arena.I.Paused || Arena.I.Picking) return;
            // Unity's own time stops (physics, timed waits such as damage over time); ROUNDS's scale stays 1, so nothing it
            // divides by it breaks.
            Time.timeScale = 0f;
            TimeHandler.deltaTime = 0f;
            TimeHandler.fixedDeltaTime = 0f;
        }

        /// <summary>Orange and Blue fight VS's enemies together: no ROUNDS bullet hurts them, a partner's or their own
        /// (VS's weapons never hurt their owner either; Bouncy bullets fly back across the screen all the time).</summary>
        [HarmonyPrefix, HarmonyPatch(typeof(ProjectileHit), "Hit")]
        public static bool PartnerBullet(ProjectileHit __instance, HitInfo hit)
        {
            var a = Arena.I;
            if (a == null || hit == null || hit.transform == null) return true;
            var target = hit.transform.GetComponentInParent<Player>();
            if (target == null || a.SlotOf(target) < 0) return true;
            return false;
        }

        /// <summary>Co-op: no damage from a partner of any kind (explosions, card effects).</summary>
        [HarmonyPrefix, HarmonyPatch(typeof(HealthHandler), "DoDamage")]
        public static bool PartnerDamage(HealthHandler __instance, Player damagingPlayer)
        {
            var a = Arena.I;
            if (a == null || damagingPlayer == null) return true;
            var me = __instance.GetComponent<Player>();
            return me == null || damagingPlayer == me || a.SlotOf(me) < 0 || a.SlotOf(damagingPlayer) < 0;
        }

        /// <summary>VS's camera decides the screen edges; ROUNDS's fixed arena box does not apply.</summary>
        [HarmonyPrefix, HarmonyPatch(typeof(OutOfBoundsHandler), "LateUpdate")]
        public static bool OutOfBounds() => Arena.I == null || !Arena.I.WorldReady;

        /// <summary>VS decides when a fallen player comes back (its revivals, a new run).</summary>
        [HarmonyPrefix, HarmonyPatch(typeof(GM_Test), "PlayerDied")]
        public static bool SandboxDied() => Arena.I == null || !Arena.I.WorldReady;

        /// <summary>Seen from above, nothing falls: ROUNDS's bullets in the arena fly straight (their gravity is off).</summary>
        [HarmonyPrefix, HarmonyPatch(typeof(MoveTransform), "Update")]
        public static void BulletsFlyStraight(MoveTransform __instance)
        {
            var a = Arena.I;
            if (a == null || !a.WorldReady || __instance.gravity == 0f) return;
            if (__instance.transform.position.y < Arena.OffsetY * 0.5f) __instance.gravity = 0f;
        }

        /// <summary>Counts each seat's shots (the Melty test's shots-fired check, and the logs).</summary>
        [HarmonyPostfix, HarmonyPatch(typeof(Gun), "Attack")]
        public static void ShotFired(Gun __instance, bool __result)
        {
            if (!__result || Arena.I == null || __instance.player == null) return;
            Arena.I.CountShot(__instance.player);
        }

        [HarmonyPrefix, HarmonyPatch(typeof(CardChoice), "Pick")]
        public static void Picked(CardChoice __instance, GameObject pickedCard)
        {
            if (Arena.I == null || pickedCard == null) return;
            int pickr = Traverse.Create(__instance).Field("pickrID").GetValue<int>();
            Arena.I.RecordPicked(pickr, pickedCard.GetComponent<CardInfo>());
        }

        private static readonly AccessTools.FieldRef<CardVisuals, ScaleShake> ShakeRef = AccessTools.FieldRefAccess<CardVisuals, ScaleShake>("shake");

        /// <summary>ROUNDS's card pick, driven by the VS player's keys, stick or mouse.</summary>
        [HarmonyPrefix, HarmonyPatch(typeof(CardChoice), "DoPlayerSelect")]
        public static bool PlayerSelect(CardChoice __instance)
        {
            var a = Arena.I;
            if (a == null || !a.Picking) return true;
            var t = Traverse.Create(__instance);
            int pickr = t.Field("pickrID").GetValue<int>();
            int slot = a.SlotOfPlayerId(pickr);
            if (slot < 0) return true;
            var spawned = t.Field("spawnedCards").GetValue<List<GameObject>>();
            if (spawned == null || spawned.Count == 0) return false;
            int cur = t.Field("currentlySelectedCard").GetValue<int>();
            cur += a.TakeNav(slot);
            int mouse = a.MouseCard(slot, spawned, ArenaRenderer.PickCam);
            if (mouse >= 0) cur = mouse;
            cur = Mathf.Clamp(cur, 0, spawned.Count - 1);
            // Only a card whose state changes is told. ROUNDS also re-sends a card's state every 0.2 s (a network resync the
            // offline sandbox does not need), and MapsExtended's ChangeSelected postfix scales the card's target size by
            // ROUNDS's main camera size / 20 on every call, no-op ones included: the first card shrank away (Roundhead
            // 0.1.4, Vampire Rounds 0.3.0). The pick is drawn at ROUNDS's own size (ortho 20), so the size a change sets is
            // ROUNDS's own as well (1.15 selected, 0.9 not), whatever the main camera shows.
            for (int j = 0; j < spawned.Count; j++)
            {
                if (spawned[j] == null) continue;
                var vis = spawned[j].GetComponentInChildren<CardVisuals>();
                if (vis == null || vis.isSelected == (cur == j)) continue;
                spawned[j].GetComponent<PhotonView>().RPC("RPCA_ChangeSelected", RpcTarget.All, cur == j);
                var shake = ShakeRef(vis);
                if (shake != null) shake.targetScale = cur == j ? 1.15f : 0.9f;
            }
            t.Field("currentlySelectedCard").SetValue(cur);
            bool busy = t.Field("isPlaying").GetValue<bool>();
            if (a.TakeAccept(slot) && !busy && spawned[cur] != null)
            {
                __instance.Pick(spawned[cur]);
                t.Field("pickrID").SetValue(-1);
            }
            if (CardChoiceVisuals.instance.currentCardSelected != cur) Traverse.Create(CardChoiceVisuals.instance).Method("SetCurrentSelected", cur).GetValue();
            return false;
        }
    }
    /// <summary>
    /// Orange and Blue start every life with tuning startHealth instead of ROUNDS's 100 (sheets/hooks.json r_start_health):
    /// set wherever ROUNDS resets a player's stats (its own reset between games, a mod's), so health cards multiply it as
    /// ROUNDS does. ROUNDS sizes a player by its health, so they start a little smaller, as a ROUNDS player with less health is.
    /// </summary>
    public static class StartHealth
    {
        private static readonly AccessTools.FieldRef<CharacterStatModifiers, CharacterData> DataRef = AccessTools.FieldRefAccess<CharacterStatModifiers, CharacterData>("data");

        [HarmonyPostfix, HarmonyPatch(typeof(CharacterStatModifiers), "ResetStats")]
        public static void Reset(CharacterStatModifiers __instance)
        {
            if (Arena.I == null || !EnginePlugin.Hidden) return;
            Apply(__instance);
        }

        public static void Apply(CharacterStatModifiers stats)
        {
            if (stats == null) return;
            try
            {
                var data = DataRef(stats) ?? stats.GetComponent<CharacterData>();
                if (data == null) return;
                data.maxHealth = Tuning.startHealth;
                data.health = Tuning.startHealth;
                Traverse.Create(stats).Method("ConfigureMassAndSize").GetValue();
            }
            catch (Exception e) { EnginePlugin.L.LogWarning("arena: start health: " + e.Message); }
        }
    }

    /// <summary>
    /// ROUNDS's heals on Orange and Blue in VS's measure (sheets/hooks.json r_heal_scale): every HealthHandler.Heal on an arena
    /// player (regeneration such as Beetle's, lifesteal, healing fields, heal on block) is scaled by tuning roundsHealScale.
    /// VS's own heals come already scaled (Arena.VsHeal). ROUNDS made its heals for its own fights; against VS's damage they
    /// kept a guy at full health in a horde (0.3.0).
    /// </summary>
    public static class HealScale
    {
        [HarmonyPrefix, HarmonyPatch(typeof(HealthHandler), "Heal")]
        public static void Scale(HealthHandler __instance, ref float healAmount)
        {
            var a = Arena.I;
            if (a == null || Arena.VsHeal || healAmount <= 0f || !EnginePlugin.Hidden || __instance == null) return;
            var p = __instance.GetComponent<Player>();
            if (p != null && a.SlotOf(p) >= 0)
            {
                healAmount *= Tuning.roundsHealScale;
                if (Dev && healAmount > 3f && logged++ < 20)
                    EnginePlugin.L.LogInfo("arena: dev heal " + healAmount.ToString("0.0") + " on seat " + a.SlotOf(p) + " from " + new System.Diagnostics.StackTrace(1, false).ToString().Replace("\r\n", " <").Replace("\n", " <"));
            }
        }

        private static readonly bool Dev = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("VAMPIREROUNDS_DEV"));
        private static int logged;
    }

    /// <summary>Every card ROUNDS shows in the hidden engine (the pick, the logos) in its own colours (CardLook).</summary>
    public static class CardLookPatches
    {
        [HarmonyPostfix, HarmonyPatch(typeof(CardVisuals), "Start")]
        public static void Started(CardVisuals __instance)
        {
            if (!EnginePlugin.Hidden || __instance == null) return;
            var info = __instance.GetComponentInParent<CardInfo>();
            try { CardLook.Emblem(__instance, info); }
            catch (Exception e) { EnginePlugin.L.LogWarning("cards: emblem for " + (info != null ? info.cardName : "?") + ": " + e.Message); }
            // In the pick the cards stand in front of the picking guy's big face (a top hat covered them, 0.2.0): his face
            // parts are drawn with Unity's sprite material here (FaceLook), sorted with the cards, not under them.
            var canvas = __instance.GetComponentInChildren<Canvas>(true);
            if (canvas != null && canvas.sortingOrder < CardLook.CardOrder) canvas.sortingOrder = CardLook.CardOrder;
            CardLook.Apply(info != null ? info.gameObject : __instance.gameObject);
            CardLook.EmblemSelected(__instance, __instance.isSelected);
        }

        [HarmonyPostfix, HarmonyPatch(typeof(CardVisuals), "ChangeSelected")]
        public static void Selected(CardVisuals __instance)
        {
            if (!EnginePlugin.Hidden || __instance == null) return;
            CardLook.EmblemSelected(__instance, __instance.isSelected);
        }
    }
}
