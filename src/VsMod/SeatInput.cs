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

        /// <summary>The mouse in VS's world (VS renders into a texture, so through the viewport, not the screen).</summary>
        public static Vector2 MouseWorld()
        {
            var cam = Session.Cam;
            if (cam == null) return Vector2.zero;
            Vector2 v = MouseViewport();
            Vector3 w = cam.ViewportToWorldPoint(new Vector3(v.x, v.y, 10f));
            return new Vector2(w.x, w.y);
        }

        public static void Aim(CharacterController c, Vector2 pos, out Vector2 aim, out bool fire)
        {
            int seat = Array.IndexOf(Session.SeatChar, c);
            if (seat < 0) seat = 0;
            fire = false;
            aim = lastAim[seat];
            bool mouseAim = false;
            if (IsKeyboardPlayer(c))
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
