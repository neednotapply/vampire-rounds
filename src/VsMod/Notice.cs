using UnityEngine;

namespace VampireRounds.Vs
{
    /// <summary>A plain on-screen note when ROUNDS cannot play Orange and Blue (missing, wrong version, already open) or is
    /// still starting during a Vampire Rounds run. It never covers VS's menus otherwise.</summary>
    public static class Notice
    {
        private static Texture2D bg;
        private static GUIStyle style;
        private static float runSince = -1f;

        public static void Draw()
        {
            string text = null;
            if (EngineRunner.Problem != null && (Session.InRun || Time.unscaledTime < 40f)) text = EngineRunner.Problem;
            else if (Session.InRun && !Link.ArenaUp)
            {
                if (runSince < 0f) runSince = Time.unscaledTime;
                if (Time.unscaledTime - runSince > 2f)
                    text = Time.unscaledTime - runSince < Tuning.engineWaitSeconds
                        ? "ROUNDS is starting in the background (" + (Link.CardCount > 0 ? Link.CardCount + " cards loaded" : "loading its cards") + ")... Orange and Blue get ROUNDS's gun in a moment."
                        : "ROUNDS has not started. Card picks wait until it does; see %LOCALAPPDATA%\\VampireRounds for its log.";
            }
            if (Link.ArenaUp || !Session.InRun) runSince = -1f;
            if (text == null) return;
            float k = Screen.height / 1080f;
            if (bg == null) { bg = new Texture2D(1, 1); bg.SetPixel(0, 0, new Color(0.05f, 0.05f, 0.08f, 0.88f)); bg.Apply(); bg.hideFlags = HideFlags.DontUnloadUnusedAsset; }
            if (style == null) style = new GUIStyle(GUI.skin.label) { wordWrap = true, alignment = TextAnchor.MiddleCenter };
            style.fontSize = Mathf.RoundToInt(22f * k);
            style.normal.textColor = Color.white;
            var r = new Rect(Screen.width / 2f - 560f * k, Screen.height - 150f * k, 1120f * k, 110f * k);
            GUI.DrawTexture(r, bg);
            GUI.Label(new Rect(r.x + 20f * k, r.y + 10f * k, r.width - 40f * k, r.height - 20f * k), text, style);
        }
    }
}
