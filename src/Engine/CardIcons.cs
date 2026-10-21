using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace VampireRounds
{
    /// <summary>
    /// The ROUNDS card logos Vampire Survivors shows in its item slots (sheets/systems.json "card_logos"). Each seat's deck
    /// (its ROUNDS player's cards, in the order taken) goes to %LOCALAPPDATA%\VampireRounds\deck&lt;seat&gt;.txt, and the seat's
    /// Q_DeckSerial goes up whenever the deck or one of its logos changes. A logo is the art of the real ROUNDS card: the
    /// card is made with ROUNDS's own AddCardVisual (as its card bar's hover shows a card), far from the arena on a layer of
    /// its own, and the art square at the top of its face is pictured into cards\&lt;key&gt;.rgba (a .png beside it for people).
    /// A developer run with --vampirerounds-cards pictures every card (whole, and its logo) and lists what each is drawn
    /// with (cards\audit.txt), then ends.
    /// </summary>
    public static class CardIcons
    {
        public const string SheetFlag = "--vampirerounds-cards";
        /// <summary>Logo size in pixels (VS draws it in its slot, about 40 px on a 1080p screen).</summary>
        public const int Size = 96;
        /// <summary>A layer only the logo camera draws (portraits use 30, the face parts 31).</summary>
        private const int Layer = 29;
        /// <summary>The logo square, as a share of the card face's width, around the art's centre.</summary>
        private const float ArtShare = 0.8f;
        public static string Dir => Path.Combine(Paths.DataDir(), "cards");
        public static string DeckFile(int seat) => Path.Combine(Paths.DataDir(), "deck" + seat + ".txt");
        public static bool SheetMode => Environment.GetCommandLineArgs().Any(a => a == SheetFlag);

        private static readonly Queue<CardInfo> queue = new Queue<CardInfo>();
        private static readonly HashSet<string> done = new HashSet<string>(), queued = new HashSet<string>();
        private static readonly string[] deckSig = new string[ArenaLink.Seats];
        private static readonly int[] deckSerial = new int[ArenaLink.Seats];
        /// <summary>Serials start somewhere new each engine start, so VS never takes a restarted engine's deck for one it read.</summary>
        private static readonly int serialBase = (Environment.TickCount & 0x3FFF) * 10000;
        private static bool busy, dirty;
        private static float nextCheck;
        private static Camera cam;

        /// <summary>A file name for a card: its ROUNDS id with anything odd replaced, and a hash of the id (two mods may
        /// name cards alike).</summary>
        public static string Key(CardInfo c)
        {
            string id = Cards.Id(c);
            var sb = new StringBuilder();
            foreach (char ch in id) sb.Append(char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' ? ch : '_');
            if (sb.Length > 40) sb.Length = 40;
            uint h = 2166136261;
            foreach (char ch in id) { h ^= ch; h *= 16777619; }
            return sb + "-" + h.ToString("x8");
        }

        // ------------------------------------------------------------------ decks (each arena frame)

        /// <summary>Each seat's cards: a changed deck is written for VS, and its cards' logos are drawn if not yet.</summary>
        public static void Tick(Player[] players, ArenaLink link, MonoBehaviour host)
        {
            if (Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + 0.25f;
            for (int s = 0; s < players.Length && s < ArenaLink.Seats; s++)
            {
                var p = players[s];
                var cards = p != null && p.data != null && p.data.currentCards != null ? p.data.currentCards.Where(c => c != null).ToList() : new List<CardInfo>();
                string sig = string.Join("|", cards.Select(Cards.Id).ToArray());
                if (sig == deckSig[s] && !dirty) continue;
                bool changed = sig != deckSig[s];
                deckSig[s] = sig;
                foreach (var c in cards) Want(c);
                if (changed || dirty) WriteDeck(s, cards, link);
            }
            dirty = false;
            if (!busy && queue.Count > 0) host.StartCoroutine(DrawQueued());
        }

        /// <summary>A new run (or a ROUNDS started again): every deck is written afresh.</summary>
        public static void Reset()
        {
            for (int s = 0; s < deckSig.Length; s++) deckSig[s] = null;
        }

        private static void Want(CardInfo c)
        {
            string key = Key(c);
            if (done.Contains(key) || queued.Contains(key)) return;
            queued.Add(key);
            queue.Enqueue(c);
        }

        /// <summary>"key TAB name TAB rarity TAB colour" per card, in the order taken; written whole, then the serial.</summary>
        private static void WriteDeck(int seat, List<CardInfo> cards, ArenaLink link)
        {
            try
            {
                var sb = new StringBuilder();
                foreach (var c in cards)
                {
                    Color col = c.cardColor;
                    try { if (CardChoice.instance != null) col = CardChoice.instance.GetCardColor(c.colorTheme); } catch { }
                    sb.Append(Key(c)).Append('\t').Append((c.cardName ?? "").Replace('\t', ' ').Replace('\n', ' '))
                      .Append('\t').Append(c.rarity.ToString()).Append('\t').Append(ColorUtility.ToHtmlStringRGB(col)).Append('\n');
                }
                string f = DeckFile(seat);
                Directory.CreateDirectory(Path.GetDirectoryName(f));
                File.WriteAllText(f + ".tmp", sb.ToString(), new UTF8Encoding(false));
                if (File.Exists(f)) File.Delete(f);
                File.Move(f + ".tmp", f);
                link.I(ArenaLink.RSlot(seat) + ArenaLink.Q_DeckSerial, serialBase + ++deckSerial[seat]);
            }
            catch (Exception e) { EnginePlugin.L.LogWarning("logos: deck " + seat + ": " + e.Message); }
        }

        private static IEnumerator DrawQueued()
        {
            busy = true;
            var batch = new List<CardInfo>();
            while (queue.Count > 0 && batch.Count < 12) batch.Add(queue.Dequeue());
            var pics = new Picture[batch.Count];
            for (int i = 0; i < batch.Count; i++) pics[i] = Picture.Make(batch[i], i);
            yield return Settle(pics);
            int n = 0;
            for (int i = 0; i < batch.Count; i++)
            {
                string key = Key(batch[i]);
                queued.Remove(key);
                done.Add(key);
                if (pics[i] == null) continue;
                try { if (pics[i].SaveLogo(Path.Combine(Dir, key + ".rgba"))) n++; }
                catch (Exception e) { EnginePlugin.L.LogWarning("logos: " + batch[i].cardName + ": " + e.Message); }
                finally { pics[i].Destroy(); }
            }
            EnginePlugin.L.LogInfo("logos: " + n + " of " + batch.Count + " card logos drawn (" + string.Join(", ", batch.Select(c => c.cardName).ToArray()) + ")");
            dirty = true;  // the decks are written again, so VS loads the new logos
            busy = false;
        }

        /// <summary>The cards' own animations start and their particles fill in, in real time (VS may have ROUNDS paused,
        /// its time scale at 0); then each art's animator is run on past its opening (Critical Hit's starburst pops in a
        /// second after the card appears: at half a second the logo showed only the card's background).</summary>
        private static IEnumerator Settle(Picture[] pics)
        {
            float until = Time.realtimeSinceStartup + 0.6f;
            while (Time.realtimeSinceStartup < until)
            {
                foreach (var p in pics) p?.Relayer();
                yield return null;
            }
            foreach (var p in pics) { p?.Rest(); p?.Advance(1.5f); }
            yield return null;
            foreach (var p in pics) { p?.Rest(); p?.Relayer(); }
        }

        // ------------------------------------------------------------------ one card, pictured

        /// <summary>A real ROUNDS card made for a picture: far from everything, on the logo layer, its colliders off.</summary>
        private sealed class Picture
        {
            public CardInfo card;
            public GameObject go;

            public static Picture Make(CardInfo card, int i)
            {
                try
                {
                    var go = CardChoice.instance.AddCardVisual(card, new Vector3(30000f + i * 60f, 30000f, 0f));
                    go.name = "Vampire Rounds logo " + card.cardName;
                    foreach (var col in go.GetComponentsInChildren<Collider2D>(true)) col.enabled = false;
                    var ray = go.GetComponentInChildren<GraphicRaycaster>(true);
                    if (ray != null) ray.enabled = false;
                    var zero = go.GetComponentInChildren<SetScaleToZero>(true);
                    if (zero != null) { zero.enabled = false; zero.transform.localScale = Vector3.one; }
                    var p = new Picture { card = card, go = go };
                    p.Relayer();
                    return p;
                }
                catch (Exception e) { EnginePlugin.L.LogWarning("logos: could not make " + card.cardName + ": " + e.Message); return null; }
            }

            public void Relayer()
            {
                if (go == null) return;
                foreach (var t in go.GetComponentsInChildren<Transform>(true)) if (t.gameObject.layer != Layer) t.gameObject.layer = Layer;
                CardLook.Apply(go);
                // The picture's animations run in real time, whether or not ROUNDS's own time stands still.
                foreach (var a in go.GetComponentsInChildren<Animator>(true)) if (a.updateMode != AnimatorUpdateMode.UnscaledTime) a.updateMode = AnimatorUpdateMode.UnscaledTime;
                foreach (var c in go.GetComponentsInChildren<CurveAnimation>(true)) c.useTimeScale = false;
                foreach (var c in go.GetComponentsInChildren<ScaleShake>(true)) c.useTimeScale = false;
                foreach (var c in go.GetComponentsInChildren<GeneralParticleSystem>(true)) c.useTimeScale = false;
            }

            /// <summary>The card at rest, face up, as its fly-in ends: ROUNDS's CurveAnimations start a card turned to its
            /// back and turn it over in ROUNDS's own time, which stands still while VS is paused (a run's first moments): the
            /// logos of cards dealt then were pictures of the card's back. Each one is stopped where its animation ends.</summary>
            public void Rest()
            {
                if (go == null) return;
                foreach (var c in go.GetComponentsInChildren<CurveAnimation>(true))
                {
                    if (c == null) continue;
                    c.StopAllCoroutines();
                    var t = c.transform;
                    t.localEulerAngles = c.startRotation;
                    t.localScale = c.startScale;
                    var rt = t as RectTransform;
                    if (rt != null) rt.anchoredPosition = c.startAnchoredPos;
                }
            }

            /// <summary>Every animator of the card run on by this many seconds at once.</summary>
            public void Advance(float seconds)
            {
                if (go == null) return;
                foreach (var a in go.GetComponentsInChildren<Animator>(false))
                    try { if (a.isActiveAndEnabled && a.runtimeAnimatorController != null) a.Update(seconds); } catch { }
            }

            private RectTransform Front()
            {
                var vis = go.GetComponentInChildren<CardVisuals>(true);
                Transform t = vis != null ? vis.transform.Find("Canvas/Front") : null;
                if (t == null) foreach (var c in go.GetComponentsInChildren<Transform>(true)) if (c.name == "Front") { t = c; break; }
                return t as RectTransform;
            }

            private Transform Art()
            {
                var vis = go.GetComponentInChildren<CardVisuals>(true);
                return vis != null ? vis.transform.Find("Canvas/Front/Background/Art") : null;
            }

            /// <summary>The square around the art: as wide as most of the card's face, centred where ROUNDS puts the art.</summary>
            public bool ArtSquare(out Vector3 center, out float side, out Quaternion rot)
            {
                center = Vector3.zero; side = 0f; rot = Quaternion.identity;
                var front = Front();
                if (front == null) return false;
                var corners = new Vector3[4];
                front.GetWorldCorners(corners);
                float width = Vector3.Distance(corners[0], corners[3]);
                if (width < 0.0001f) return false;
                var art = Art();
                center = art != null ? art.position : (corners[1] + corners[2]) / 2f - (corners[1] - corners[0]).normalized * width * 0.45f;
                side = width * ArtShare;
                rot = Quaternion.LookRotation(front.forward, front.up);
                return true;
            }

            /// <summary>The whole card face with its title (developer sheet).</summary>
            public bool CardRect(out Vector3 center, out float w, out float h, out Quaternion rot)
            {
                center = Vector3.zero; w = h = 0f; rot = Quaternion.identity;
                var front = Front();
                if (front == null) return false;
                var c = new Vector3[4];
                front.GetWorldCorners(c);
                Vector3 right = (c[3] - c[0]).normalized, up = (c[1] - c[0]).normalized;
                float minY = 0f, maxY = Vector3.Distance(c[0], c[1]);
                var vis = go.GetComponentInChildren<CardVisuals>(true);
                if (vis != null && vis.nameText != null)
                {
                    var tc = new Vector3[4];
                    vis.nameText.rectTransform.GetWorldCorners(tc);
                    foreach (var p in tc) { float d = Vector3.Dot(p - c[0], up); minY = Mathf.Min(minY, d); maxY = Mathf.Max(maxY, d); }
                }
                w = Vector3.Distance(c[0], c[3]);
                h = maxY - minY;
                center = c[0] + right * w / 2f + up * (minY + maxY) / 2f;
                rot = Quaternion.LookRotation(front.forward, front.up);
                return w > 0.0001f && h > 0.0001f;
            }

            public bool SaveLogo(string file)
            {
                if (!ArtSquare(out var center, out var side, out var rot)) { EnginePlugin.L.LogWarning("logos: " + card.cardName + " has no card face"); return false; }
                Relayer();
                var px = Shoot(Size, Size, center, rot, side / 2f);
                WriteRgba(file, Size, Size, px);
                return true;
            }

            public void SaveCardPng(string file)
            {
                if (!CardRect(out var center, out var w, out var h, out var rot)) return;
                Relayer();
                int ph = 420, pw = Mathf.Clamp(Mathf.RoundToInt(ph * w / h), 120, 800);
                var px = Shoot(pw, ph, center, rot, h / 2f * 1.04f);
                WritePng(file, pw, ph, px);
            }

            public void Destroy()
            {
                if (go != null) UnityEngine.Object.Destroy(go);
                go = null;
            }
        }

        // ------------------------------------------------------------------ camera and files

        private static Color32[] Shoot(int pw, int ph, Vector3 center, Quaternion rot, float halfHeight)
        {
            if (cam == null)
            {
                var cgo = new GameObject("Vampire Rounds logo camera");
                UnityEngine.Object.DontDestroyOnLoad(cgo);
                cam = cgo.AddComponent<Camera>();
                cam.enabled = false;
                cam.orthographic = true;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                cam.cullingMask = 1 << Layer;
                cam.nearClipPlane = 0.01f;
                cam.farClipPlane = 1000f;
            }
            var rt = RenderTexture.GetTemporary(pw, ph, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var tex = new Texture2D(pw, ph, TextureFormat.RGBA32, false);
            try
            {
                cam.targetTexture = rt;
                cam.transform.rotation = rot;
                cam.transform.position = center - (rot * Vector3.forward) * 100f;
                cam.orthographicSize = halfHeight;
                cam.aspect = pw / (float)ph;
                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, pw, ph), 0, 0, false);
                tex.Apply(false);
                RenderTexture.active = prev;
                var px = tex.GetPixels32();
                for (int k = 0; k < px.Length; k++)
                {
                    var q = px[k];
                    if (q.a == 0 || q.a == 255) continue;
                    px[k] = new Color32((byte)Math.Min(255, q.r * 255 / q.a), (byte)Math.Min(255, q.g * 255 / q.a), (byte)Math.Min(255, q.b * 255 / q.a), q.a);
                }
                return px;
            }
            finally
            {
                cam.targetTexture = null;
                RenderTexture.ReleaseTemporary(rt);
                UnityEngine.Object.Destroy(tex);
            }
        }

        /// <summary>RawImage format ("VRI1", width, height, RGBA rows bottom-up), written whole; a PNG beside it for people.</summary>
        private static void WriteRgba(string file, int w, int h, Color32[] px)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            try
            {
                tex.SetPixels32(px);
                tex.Apply(false);
                var raw = tex.GetRawTextureData();
                using (var fs = new FileStream(file + ".tmp", FileMode.Create))
                using (var bw = new BinaryWriter(fs))
                {
                    bw.Write(new[] { (byte)'V', (byte)'R', (byte)'I', (byte)'1' });
                    bw.Write(w);
                    bw.Write(h);
                    bw.Write(raw);
                }
                if (File.Exists(file)) File.Delete(file);
                File.Move(file + ".tmp", file);
                File.WriteAllBytes(Path.ChangeExtension(file, ".png"), tex.EncodeToPNG());
            }
            finally { UnityEngine.Object.Destroy(tex); }
        }

        private static void WritePng(string file, int w, int h, Color32[] px)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            try
            {
                tex.SetPixels32(px);
                tex.Apply(false);
                File.WriteAllBytes(file, tex.EncodeToPNG());
            }
            finally { UnityEngine.Object.Destroy(tex); }
        }

        // ------------------------------------------------------------------ developer sheet (--vampirerounds-cards)

        /// <summary>Every card ROUNDS has (mod cards included): its whole face and its logo as PNGs in cards\sheet, and what
        /// it is drawn with (renderers, layers, materials, shaders) in cards\audit.txt. Then the engine ends.</summary>
        public static IEnumerator DrawSheet()
        {
            string dir = Path.Combine(Dir, "sheet");
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
            // No arena here (it applies the engine's patches): the card look's own patches, so the sheet shows cards as a run does.
            try { new HarmonyLib.Harmony("vampirerounds.cards").CreateClassProcessor(typeof(CardLookPatches), true).Patch(); }
            catch (Exception e) { EnginePlugin.L.LogWarning("cards sheet: card look patches: " + e.Message); }
            Directory.CreateDirectory(dir);
            var all = Cards.All();
            // Developers: VAMPIREROUNDS_DEV_SHEET_PAUSED draws the sheet with ROUNDS's time standing still, as during VS's pauses.
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("VAMPIREROUNDS_DEV_SHEET_PAUSED"))) { Time.timeScale = 0f; TimeHandler.deltaTime = 0f; }
            var main = MainCam.instance != null ? MainCam.instance.cam : Camera.main;
            int mainMask = main != null ? main.cullingMask : ~0;
            var audit = new StringBuilder();
            audit.Append("cards ").Append(all.Count).Append(", ROUNDS main camera mask ").Append(Convert.ToString(mainMask, 2)).Append('\n');
            foreach (var c in Camera.allCameras) audit.Append("camera ").Append(c.name).Append(" mask ").Append(Convert.ToString(c.cullingMask, 2)).Append(" depth ").Append(c.depth).Append('\n');
            for (int start = 0; start < all.Count; start += 16)
            {
                var batch = all.Skip(start).Take(16).ToList();
                var pics = new Picture[batch.Count];
                for (int i = 0; i < batch.Count; i++) pics[i] = Picture.Make(batch[i], i);
                yield return Settle(pics);
                for (int i = 0; i < batch.Count; i++)
                {
                    var card = batch[i];
                    string key = Key(card);
                    try
                    {
                        audit.Append("\n== ").Append(card.cardName).Append(" [").Append(Cards.Id(card)).Append("] mod ").Append(card.GetType().Assembly.GetName().Name)
                             .Append(" rarity ").Append(card.rarity).Append(" theme ").Append(card.colorTheme).Append(" art ").Append(card.cardArt != null ? card.cardArt.name : "NONE").Append('\n');
                        if (card.cardArt != null) { audit.Append(" art prefab:\n"); Layers(card.cardArt.transform, card.cardArt.transform, audit, mainMask); }
                        if (pics[i] == null) { audit.Append("  (could not be made)\n"); continue; }
                        audit.Append(" as drawn:\n");
                        Describe(pics[i].go.transform, pics[i].go.transform, audit, mainMask);
                        pics[i].SaveCardPng(Path.Combine(dir, key + ".png"));
                        pics[i].SaveLogo(Path.Combine(dir, key + "-logo.rgba"));
                    }
                    catch (Exception e) { audit.Append("  failed: ").Append(e.Message).Append('\n'); }
                    finally { pics[i]?.Destroy(); }
                }
                EnginePlugin.L.LogInfo("cards sheet: " + Math.Min(all.Count, start + 16) + " of " + all.Count);
                EnginePlugin.FlushLog();
            }
            File.WriteAllText(Path.Combine(Dir, "audit.txt"), audit.ToString(), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(dir, "done.txt"), DateTime.Now.ToString("o"));
            EnginePlugin.Exit("card sheet drawn");
        }

        /// <summary>The art prefab's own layers (the drawn copy is on the logo layer): one ROUNDS's main camera does not
        /// draw would be missing from the pick screen.</summary>
        private static void Layers(Transform t, Transform root, StringBuilder sb, int mainMask)
        {
            string path = t == root ? "." : t.name;
            for (var p = t.parent; p != null && p != root; p = p.parent) path = p.name + "/" + path;
            int l = t.gameObject.layer;
            var kinds = string.Join(",", t.GetComponents<Component>().Where(c => c != null && !(c is Transform)).Select(c => c.GetType().Name).ToArray());
            sb.Append("  L ").Append(path).Append(" layer ").Append(l).Append((mainMask & (1 << l)) != 0 ? "" : " NOT-IN-MAIN-CAMERA").Append(t.gameObject.activeSelf ? "" : " (inactive)").Append(" [").Append(kinds).Append("]\n");
            foreach (Transform c in t) Layers(c, root, sb, mainMask);
        }

        private static void Describe(Transform t, Transform root, StringBuilder sb, int mainMask)
        {
            string path = t == root ? "." : t.name;
            for (var p = t.parent; p != null && p != root; p = p.parent) path = p.name + "/" + path;
            foreach (var r in t.GetComponents<Renderer>())
            {
                var m = r.sharedMaterial;
                sb.Append("  R ").Append(path).Append(' ').Append(r.GetType().Name).Append(r.enabled && r.gameObject.activeInHierarchy ? "" : " (off)")
                  .Append(" layer ").Append(t.gameObject.layer).Append(" mat ").Append(m != null ? m.name : "-").Append(" shader ").Append(m != null && m.shader != null ? m.shader.name : "-")
                  .Append(" sorting ").Append(r.sortingLayerName).Append('/').Append(r.sortingOrder).Append('\n');
            }
            foreach (var g in t.GetComponents<Graphic>())
            {
                var m = g.material;
                sb.Append("  G ").Append(path).Append(' ').Append(g.GetType().Name).Append(g.enabled && g.gameObject.activeInHierarchy ? "" : " (off)")
                  .Append(" color ").Append(ColorUtility.ToHtmlStringRGBA(g.color)).Append(" mat ").Append(m != null ? m.name : "-")
                  .Append(" shader ").Append(m != null && m.shader != null ? m.shader.name : "-");
                var img = g as Image;
                if (img != null) sb.Append(" sprite ").Append(img.sprite != null ? img.sprite.name : "-");
                sb.Append('\n');
            }
            foreach (var cv in t.GetComponents<Canvas>()) sb.Append("  C ").Append(path).Append(" canvas ").Append(cv.renderMode).Append(" sorting ").Append(cv.sortingLayerName).Append('/').Append(cv.sortingOrder).Append(cv.overrideSorting ? " override" : "").Append('\n');
            foreach (var ps in t.GetComponents<ParticleSystem>()) sb.Append("  P ").Append(path).Append(" particles\n");
            foreach (Transform c in t) Describe(c, root, sb, mainMask);
        }
    }

    /// <summary>
    /// A ROUNDS card as it should look wherever the engine draws it (sheets/hooks.json r_card_look). Parts drawn with ROUNDS's
    /// soft-shadow material (shader Sprites/SFSoftShadow, lit by ROUNDS's own light pass that the engine's cameras do not run)
    /// would come out dark, so they get Unity's plain sprite material, as the faces do (FaceLook). A card that comes with no
    /// art at all (41 of PCE's and other mods' cards) gets an emblem in its art space: the two letters ROUNDS's own card bar
    /// shows for it, big, in the card's colour, so it never shows an empty picture (in the pick, in VS's item slots).
    /// </summary>
    public static class CardLook
    {
        public const string EmblemName = "Vampire Rounds emblem";
        /// <summary>A card's canvas order in the hidden engine (ROUNDS's is 10 on MostFront): above the pick's face parts.</summary>
        public const int CardOrder = 500;
        private static Material unlit;

        /// <summary>ROUNDS's card bar letters for a card (CardBar.AddCard): its name's first two, "Co" for Comb.</summary>
        public static string Letters(string name)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0) return "?";
            string a = name.Substring(0, 1).ToUpperInvariant();
            return name.Length > 1 ? a + name.Substring(1, 1).ToLowerInvariant() : a;
        }

        /// <summary>The emblem of a card with no art, in its art space (called once its CardVisuals has started).</summary>
        public static void Emblem(CardVisuals vis, CardInfo info)
        {
            if (vis == null || info == null || info.cardArt != null) return;
            var art = vis.transform.Find("Canvas/Front/Background/Art");
            var bg = vis.transform.Find("Canvas/Front/Background") as RectTransform;
            if (art == null || bg == null || art.Find(EmblemName) != null) return;
            var go = new GameObject(EmblemName, typeof(RectTransform));
            go.layer = art.gameObject.layer;
            var rt = (RectTransform)go.transform;
            rt.SetParent(art, false);
            rt.localPosition = Vector3.zero;
            rt.localScale = Vector3.one;
            float w = bg.rect.width;
            rt.sizeDelta = new Vector2(w * 0.8f, w * 0.6f);
            var t = go.AddComponent<TMPro.TextMeshProUGUI>();
            if (vis.nameText != null) { t.font = vis.nameText.font; t.fontSharedMaterial = vis.nameText.fontSharedMaterial; }
            t.text = Letters(info.cardName);
            t.alignment = TMPro.TextAlignmentOptions.Center;
            t.enableWordWrapping = false;
            t.overflowMode = TMPro.TextOverflowModes.Overflow;
            t.enableAutoSizing = false;
            // About four times the card's title, filling most of the art space as a card's picture does.
            t.fontSize = (vis.nameText != null && vis.nameText.fontSize > 0f ? vis.nameText.fontSize : w * 0.08f) * 4f;
            t.raycastTarget = false;
            Color c = info.cardColor;
            try { if (CardChoice.instance != null) c = CardChoice.instance.GetCardColor(info.colorTheme); } catch { }
            t.color = c;
            go.transform.SetAsLastSibling();
        }

        /// <summary>As ROUNDS dims a card that is not the chosen one, the emblem dims with it.</summary>
        public static void EmblemSelected(CardVisuals vis, bool selected)
        {
            var art = vis != null ? vis.transform.Find("Canvas/Front/Background/Art") : null;
            var e = art != null ? art.Find(EmblemName) : null;
            var t = e != null ? e.GetComponent<TMPro.TextMeshProUGUI>() : null;
            if (t == null) return;
            var c = t.color;
            c.a = selected ? 1f : 0.3f;
            t.color = c;
        }

        private static Material Unlit
        {
            get
            {
                if (unlit == null)
                {
                    var shader = Shader.Find("Sprites/Default");
                    if (shader != null) unlit = new Material(shader) { name = "Vampire Rounds card (unlit)" };
                }
                return unlit;
            }
        }

        public static void Apply(GameObject card)
        {
            if (card == null || Unlit == null) return;
            foreach (var r in card.GetComponentsInChildren<SpriteRenderer>(true))
            {
                var m = r.sharedMaterial;
                if (m != null && m != unlit && m.shader != null && m.shader.name.Contains("SFSoftShadow")) r.sharedMaterial = unlit;
            }
        }
    }
}
