using System;
using UnityEngine;
using CharacterController = Il2CppVampireSurvivors.Objects.Characters.CharacterController;

namespace VampireRounds.Vs
{
    /// <summary>
    /// ROUNDS's picture in VS (sheets/systems.json "frames"). In a run, the world picture (Orange, Blue, their guns,
    /// bullets and effects, rendered by the hidden ROUNDS over exactly VS's camera view) is a sprite laid over VS's stage
    /// at the camera, drawn where VS draws the character it stands in for (its sorting layer and order): VS sorts enemies
    /// and props around its player by height, so what stands in front of Orange Guy covers him as it would cover a VS
    /// hero (as Roundhead draws ROUNDS on Cuphead's player layer). ROUNDS's game UI (its card bar) is drawn over the screen. During a card pick, ROUNDS's own pick
    /// screen fills the screen. VS's own sprite of Orange or Blue is hidden while ROUNDS draws them.
    /// </summary>
    public static class FrameView
    {
        private static GameObject layer;
        private static SpriteRenderer layerRenderer;
        private static Texture2D world, ui;
        private static Sprite worldSprite;
        private static int lastSerial = -1, w, h;
        private static float lastFrameAt = -100f;
        private static bool picking, materialLogged;
        private static float camX, camY, ortho;
        public static bool Showing => Time.unscaledTime - lastFrameAt < 1f;
        public static bool PickShowing => picking && Showing;

        public static void Tick()
        {
            var L = Link.L;
            bool run = Session.InRun && L != null && Link.ArenaUp;
            if (!run)
            {
                if (layer != null && layer.activeSelf) layer.SetActive(false);
                ShowVsSprites(true);
                return;
            }
            int serial = L.I(ArenaLink.ResultOff + ArenaLink.R_FrameSerial);
            if (serial != lastSerial)
            {
                lastSerial = serial;
                Upload(L);
            }
            picking = L.I(ArenaLink.ResultOff + ArenaLink.R_State) == ArenaLink.StatePicking || Picks.Open;
            EnsureLayer();
            bool show = Showing && !picking && world != null;
            if (layer.activeSelf != show) layer.SetActive(show);
            if (show) Place();
            ShowVsSprites(!Showing);
            if (Showing && !picking) TestMode.Mark("rounds-drawn");
        }

        private static void Upload(ArenaLink L)
        {
            int ready = L.I(ArenaLink.ResultOff + ArenaLink.R_Ready);
            int fw = L.I(ArenaLink.ResultOff + ArenaLink.R_FrameW), fh = L.I(ArenaLink.ResultOff + ArenaLink.R_FrameH);
            if (ready < 0 || ready > 2 || fw < 16 || fh < 16 || fw > ArenaLink.MaxW || fh > ArenaLink.MaxH) return;
            L.I(ArenaLink.C_Reading, ready);
            try
            {
                if (world == null || fw != w || fh != h) Make(fw, fh);
                world.LoadRawTextureData(L.Frame(ready), fw * fh * 4);
                world.Apply(false, false);
                ui.LoadRawTextureData(L.UiFrame(ready), fw * fh * 4);
                ui.Apply(false, false);
                camX = L.F(ArenaLink.ResultOff + ArenaLink.R_FrameCamX);
                camY = L.F(ArenaLink.ResultOff + ArenaLink.R_FrameCamY);
                ortho = L.F(ArenaLink.ResultOff + ArenaLink.R_FrameOrtho);
                lastFrameAt = Time.unscaledTime;
            }
            finally { L.I(ArenaLink.C_Reading, -1); }
        }

        private static void Make(int fw, int fh)
        {
            w = fw; h = fh;
            world = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "Vampire Rounds ROUNDS layer" };
            world.hideFlags = HideFlags.DontUnloadUnusedAsset;
            ui = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, name = "Vampire Rounds ROUNDS UI" };
            ui.hideFlags = HideFlags.DontUnloadUnusedAsset;
            worldSprite = Sprite.Create(world, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
            worldSprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
            if (layerRenderer != null) layerRenderer.sprite = worldSprite;
        }

        private static void EnsureLayer()
        {
            if (layer != null) return;
            layer = new GameObject("Vampire Rounds ROUNDS layer");
            UnityEngine.Object.DontDestroyOnLoad(layer);
            layerRenderer = layer.AddComponent<SpriteRenderer>();
            layerRenderer.sprite = worldSprite;
            layer.SetActive(false);
        }

        /// <summary>At the camera the picture was rendered for, as big as that view.</summary>
        private static void Place()
        {
            if (ortho <= 0f) return;
            var c = Session.SeatChar[0] ?? Session.SeatChar[1];
            float z = 0f;
            if (c != null && c._CharacterRenderer != null)
            {
                // VS's own sprite setup (its material, layer and sorting layer), so VS's camera draws it like a character.
                var r = c._CharacterRenderer;
                if (layerRenderer.sortingLayerID != r.sortingLayerID) layerRenderer.sortingLayerID = r.sortingLayerID;
                if (layerRenderer.sortingOrder != r.sortingOrder) layerRenderer.sortingOrder = r.sortingOrder;
                if (layer.layer != r.gameObject.layer) layer.layer = r.gameObject.layer;
                if (r.sharedMaterial != null && layerRenderer.sharedMaterial != r.sharedMaterial && !materialLogged)
                {
                    layerRenderer.sharedMaterial = r.sharedMaterial;
                    materialLogged = true;
                    Mod.Log("frames: ROUNDS layer uses VS's sprite material '" + r.sharedMaterial.name + "' (shader " + (r.sharedMaterial.shader != null ? r.sharedMaterial.shader.name : "-") + "), layer " + r.gameObject.layer + ", sorting layer " + r.sortingLayerName);
                }
                z = r.transform.position.z - 0.01f;
            }
            float hh = ortho * 2f, ww = hh * w / Mathf.Max(1, h);
            layer.transform.position = new Vector3(camX, camY, z);
            layer.transform.localScale = new Vector3(ww / (w / 100f), hh / (h / 100f), 1f);
        }

        private static readonly SpriteRenderer[][] vsRenderers = new SpriteRenderer[ArenaLink.Seats][];
        private static readonly IntPtr[] vsRenderersOf = new IntPtr[ArenaLink.Seats];
        private static float nextRendererScan;

        /// <summary>VS's own sprite of Orange or Blue (every renderer of the character showing one of the mod's pictures)
        /// shows only while ROUNDS does not draw them (the stand-in).</summary>
        private static void ShowVsSprites(bool show)
        {
            bool rescan = Time.unscaledTime > nextRendererScan;
            if (rescan) nextRendererScan = Time.unscaledTime + 1f;
            for (int s = 0; s < Session.SeatChar.Length; s++)
            {
                var c = Session.SeatChar[s];
                if (c == null) continue;
                if (rescan || vsRenderersOf[s] != c.Pointer || vsRenderers[s] == null)
                {
                    vsRenderersOf[s] = c.Pointer;
                    var list = new System.Collections.Generic.List<SpriteRenderer>();
                    foreach (var r in c.GetComponentsInChildren<SpriteRenderer>(true))
                        if (r != null && (r == c._CharacterRenderer || (r.sprite != null && r.sprite.texture != null && Content.IsOurTexture(r.sprite.texture)))) list.Add(r);
                    vsRenderers[s] = list.ToArray();
                }
                foreach (var r in vsRenderers[s])
                    if (r != null && r.enabled != show) r.enabled = show;
                // VS's after-image trail (SpriteTrail) copies the sprite: it goes with it.
                var trail = c._spriteTrail;
                if (trail != null && trail.enabled != show) trail.enabled = show;
                // ROUNDS draws its own health bar over the player; VS's red one under the sprite would cross ROUNDS's body.
                var bar = c.HealthBar;
                if (bar != null && bar.gameObject.activeSelf != show) bar.gameObject.SetActive(show);
                HideTrails(c, show);
            }
        }

        private static Transform trails;

        /// <summary>VS's after-image trails of a character (SpriteTrails/"Trail - &lt;character&gt;") copy its sprite: they go
        /// with it.</summary>
        private static void HideTrails(CharacterController c, bool show)
        {
            if (trails == null)
            {
                var go = GameObject.Find("SpriteTrails");
                if (go == null) return;
                trails = go.transform;
            }
            string name = c.gameObject.name;
            for (int i = 0; i < trails.childCount; i++)
            {
                var t = trails.GetChild(i);
                if (!t.name.Contains(name)) continue;
                var r = t.GetComponent<SpriteRenderer>();
                if (r != null && r.enabled != show) r.enabled = show;
            }
        }

        /// <summary>OnGUI: ROUNDS's pick screen over everything while picking, else its card bar over the run.</summary>
        public static void Gui()
        {
            if (Event.current == null || Event.current.type != EventType.Repaint) return;
            if (!Session.InRun || world == null || !Showing) return;
            var full = new Rect(0, 0, Screen.width, Screen.height);
            if (picking)
            {
                // ROUNDS's own pick view, rendered at VS's shape on ROUNDS's dark background.
                GUI.DrawTexture(full, world, ScaleMode.StretchToFill, false);
                return;
            }
            GUI.DrawTexture(full, ui, ScaleMode.StretchToFill, true);
        }
    }
}
