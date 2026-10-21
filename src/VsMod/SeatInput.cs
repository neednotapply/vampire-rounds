using System;
using Il2CppVampireSurvivors.Objects.Characters;
using UnityEngine;
using CharacterController = Il2CppVampireSurvivors.Objects.Characters.CharacterController;

namespace VampireRounds.Vs
{
    /// <summary>
    /// What each seat does with the gun (sheets/controls.json). The gun fires by itself at the nearest enemy on screen,
    /// as VS's weapons do; the keyboard player's mouse takes the aim for a few seconds after it moves, and holding the left
    /// button fires with no enemy near. Block: right mouse or Space (the keyboard player), the left trigger or shoulder
    /// (a gamepad player). VS player 1 (Rewired player 0) is the keyboard and mouse player.
    /// </summary>
    public static class SeatInput
    {
        private static Vector2 lastMouse = new Vector2(-1f, -1f);
        private static float mouseMovedAt = -100f;
        private static readonly Vector2[] lastAim = { Vector2.right, Vector2.left };

        private static bool IsKeyboardPlayer(CharacterController c) => c._PlayerIndex == 0;

        public static Vector2 MouseViewport()
        {
            Vector3 m = Input.mousePosition;
            return new Vector2(m.x / Mathf.Max(1, Screen.width), m.y / Mathf.Max(1, Screen.height));
        }

        /// <summary>The mouse in VS's world. VS renders its world into a texture and shows it in a picture on its UI, with
        /// bars beside it when the window is wider than the picture (a 16:9 window shows a 16:10 view): the mouse is read in
        /// that picture, then through the camera's viewport (read against the whole window, the aim went off to the sides
        /// by up to a tenth of the screen).</summary>
        public static Vector2 MouseWorld()
        {
            var cam = Session.Cam;
            if (cam == null) return Vector2.zero;
            Vector2 v = MouseViewport();
            Rect view;
            if (GameView(cam, out view) && view.width > 1f && view.height > 1f)
            {
                Vector3 m = Input.mousePosition;
                v = new Vector2((m.x - view.x) / view.width, (m.y - view.y) / view.height);
            }
            Vector3 w = cam.ViewportToWorldPoint(new Vector3(v.x, v.y, 10f));
            return new Vector2(w.x, w.y);
        }

        private static UnityEngine.UI.RawImage gameView;
        private static IntPtr gameViewOf;
        private static float nextViewSearch;
        private static bool viewLogged;

        /// <summary>Where on the screen VS shows its camera's picture (the RawImage holding the camera's target texture), in
        /// screen pixels; false when the camera draws to the screen itself (then the window is the view).</summary>
        private static bool GameView(Camera cam, out Rect rect)
        {
            rect = default(Rect);
            var tex = cam.targetTexture;
            if (tex == null) return false;
            if ((gameView == null || gameViewOf != tex.Pointer) && Time.unscaledTime > nextViewSearch)
            {
                nextViewSearch = Time.unscaledTime + 2f;
                gameView = null;
                foreach (var ri in UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.RawImage>())
                    if (ri != null && ri.isActiveAndEnabled && ri.texture != null && ri.texture.Pointer == tex.Pointer) { gameView = ri; gameViewOf = tex.Pointer; break; }
            }
            if (gameView == null) return false;
            var corners = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3>(4);
            gameView.rectTransform.GetWorldCorners(corners);
            var canvas = gameView.canvas;
            Camera uiCam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            Vector2 a = RectTransformUtility.WorldToScreenPoint(uiCam, corners[0]), b = RectTransformUtility.WorldToScreenPoint(uiCam, corners[2]);
            var uv = gameView.uvRect;
            // A picture showing part of the texture (uvRect) shows that part of the camera's view.
            float w = (b.x - a.x) / Mathf.Max(0.0001f, uv.width), h = (b.y - a.y) / Mathf.Max(0.0001f, uv.height);
            rect = new Rect(a.x - uv.x * w, a.y - uv.y * h, w, h);
            if (!viewLogged) { viewLogged = true; Mod.Log("input: VS shows its world in " + gameView.name + " at " + rect + " of a " + Screen.width + "x" + Screen.height + " window (uv " + uv + ")"); }
            return true;
        }

        public static void Aim(CharacterController c, Vector2 pos, out Vector2 aim, out bool fire)
        {
            int seat = Array.IndexOf(Session.SeatChar, c);
            if (seat < 0) seat = 0;
            fire = false;
            aim = lastAim[seat];
            bool mouseAim = false;
            // A run that plays by itself aims by itself: the mouse of someone at the PC (watching a test) never steers it.
            if (IsKeyboardPlayer(c) && !AutoPlay.On)
            {
                try
                {
                    Vector2 m = Input.mousePosition;
                    if ((m - lastMouse).sqrMagnitude > 4f) { lastMouse = m; mouseMovedAt = Time.unscaledTime; }
                    mouseAim = Time.unscaledTime - mouseMovedAt < Tuning.mouseAimSeconds || Input.GetMouseButton(0);
                    if (mouseAim)
                    {
                        Vector2 d = MouseWorld() - pos;
                        if (d.sqrMagnitude > 0.0001f) aim = d.normalized;
                        if (Input.GetMouseButton(0)) fire = true;
                    }
                }
                catch (Exception e) { Mod.Once("input: mouse", e); }
            }
            var target = Enemies.Nearest(pos, Session.CamHalfH * Session.CamAspect * Tuning.autoAimRange);
            if (target != null)
            {
                fire = true;
                if (!mouseAim)
                {
                    Vector3 t = target._cachedTransform.position;
                    Vector2 d = new Vector2(t.x, t.y) - pos;
                    if (d.sqrMagnitude > 0.0001f) aim = d.normalized;
                }
            }
            lastAim[seat] = aim;
        }

        public static bool Block(CharacterController c)
        {
            try
            {
                if (IsKeyboardPlayer(c) && (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Space))) return true;
                var p = c.PlayerInput;
                if (p != null && (p.GetButtonDown(19) || p.GetButtonDown(16))) return true; // LeftTrigger, ActionBottomRow2
            }
            catch (Exception e) { Mod.Once("input: block", e); }
            return false;
        }

        /// <summary>The keyboard player's mouse in the world, for ROUNDS's cursor cards (Remote).</summary>
        public static bool Cursor(CharacterController c, out Vector2 world)
        {
            world = Vector2.zero;
            if (!IsKeyboardPlayer(c)) return false;
            try { world = MouseWorld(); return true; }
            catch { return false; }
        }

        // ------------------------------------------------------------------ card pick

        private static readonly float[] navHeldSince = { -1f, -1f };

        /// <summary>-1 / +1 on a fresh left / right press (keys, d-pad or stick), else 0.</summary>
        public static int PickNav(CharacterController c, int seat)
        {
            int dir = 0;
            try
            {
                if (IsKeyboardPlayer(c))
                {
                    if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow)) dir = -1;
                    if (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow)) dir = 1;
                }
                if (dir == 0 && c != null && c.PlayerInput != null)
                {
                    float x = c.PlayerInput.GetAxis(0);
                    if (Mathf.Abs(x) > 0.6f)
                    {
                        if (navHeldSince[seat] < 0f) { navHeldSince[seat] = Time.unscaledTime; dir = x > 0 ? 1 : -1; }
                    }
                    else navHeldSince[seat] = -1f;
                }
            }
            catch (Exception e) { Mod.Once("input: pick nav", e); }
            return dir;
        }

        public static bool PickAccept(CharacterController c)
        {
            try
            {
                if (IsKeyboardPlayer(c) && (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetMouseButtonDown(0))) return true;
                var p = c != null ? c.PlayerInput : null;
                if (p != null && (p.GetButtonDown(11) || p.GetButtonDown(5))) return true; // ActionBottomRow1 (A), UISubmit
            }
            catch (Exception e) { Mod.Once("input: pick accept", e); }
            return false;
        }
    }
}
