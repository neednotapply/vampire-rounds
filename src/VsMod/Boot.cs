using System;
using Il2CppVampireSurvivors.App.Scripts.UI;
using Il2CppVampireSurvivors.App.UI;
using Il2CppVampireSurvivors.UI;
using UnityEngine;

namespace VampireRounds.Vs
{
    /// <summary>
    /// Straight into the game (sheets/systems.json "boot"): once per VS start, VS's warning and title screens are passed
    /// and the Vampire Rounds adventure is opened, so VS starts on the adventure's own menu. It goes through VS's own pages
    /// and their own methods, as a player's presses would; afterwards the menus are the player's (Back leaves the adventure).
    /// While it does, the adventure's own backdrop and title cover the screen (from the mod's first frame), so VS opens on
    /// the adventure's menu instead of being seen to click its way there.
    /// </summary>
    public static class Boot
    {
        public static bool Done { get; private set; }
        private static float nextStep, giveUpAt = -1f;
        private static string lastStep = "";
        /// <summary>The cover lifts when the boot has not got a page further for CoverStuck seconds (a VS popup it does not
        /// know would wait under it), or after CoverMax in all (VS's own loading comes first and takes a while).</summary>
        private const float CoverMax = 60f, CoverStuck = 12f, CoverFade = 0.4f;
        private static float coverFrom = -1f, doneAt = -1f, lastStepAt = -1f;
        private static Texture2D coverBg, coverTitle;
        private static bool coverTried;
        private static GUIStyle coverStyle;

        private static T Active<T>() where T : MonoBehaviour
        {
            foreach (var o in UnityEngine.Object.FindObjectsOfType<T>())
                if (o != null && o.isActiveAndEnabled) return o;
            return null;
        }

        private static void Step(string what, Action a)
        {
            if (what != lastStep) { Mod.Log("boot: " + what); lastStepAt = Time.unscaledTime; }
            lastStep = what;
            try { a(); }
            catch (Exception e) { Mod.Once("boot: " + what, e); }
        }

        public static void Tick()
        {
            if (Done || Time.unscaledTime < nextStep) return;
            // Under the cover each page is passed as soon as it is up.
            nextStep = Time.unscaledTime + 0.1f;
            if (!Content.AdventureInData || Content.Types.Count == 0) return;
            if (giveUpAt < 0f) giveUpAt = Time.unscaledTime + 60f;
            if (Session.InRun || Time.unscaledTime > giveUpAt) { Finish("not needed (" + (Session.InRun ? "a run is on" : "the menus never came") + ")"); return; }
            if (Content.InOurAdventure && Active<MainMenuPage>() != null) { Finish("on the Vampire Rounds adventure's menu"); return; }
            var warning = Active<WarningPage>();
            if (warning != null) { Step("past the warning", () => warning.Complete()); return; }
            var landing = Active<LandingScreenPage>();
            if (landing != null) { Step("past the title", () => landing.MoveToNextView()); return; }
            var advs = Active<SelectAdventuresPage>();
            if (advs != null)
            {
                Step("open the Vampire Rounds adventure", () =>
                {
                    foreach (var item in UnityEngine.Object.FindObjectsOfType<AdventureItemUI>())
                        if (item != null && item.GetAdventureType() == Content.Adventure) { advs.SelectAdventure(item); break; }
                    advs.ConfirmAdventure();
                });
                return;
            }
            var menu = Active<MainMenuPage>();
            if (menu != null && !Content.InOurAdventure) Step("open Adventures", () => menu.ShowAdventuresView());
        }

        private static void Finish(string why)
        {
            Done = true;
            doneAt = Time.unscaledTime;
            Mod.Log("boot: done, " + why + (coverFrom >= 0f ? " (" + (Time.unscaledTime - coverFrom).ToString("0.0") + " s under the cover)" : ""));
        }

        /// <summary>OnGUI: the adventure's backdrop and title over everything until the boot is done, then a short fade.</summary>
        public static void Gui()
        {
            if (Event.current == null || Event.current.type != EventType.Repaint) return;
            if (coverFrom < 0f) coverFrom = Time.unscaledTime;
            float end = Done ? doneAt : Mathf.Min(coverFrom + CoverMax, lastStepAt >= 0f ? lastStepAt + CoverStuck : float.MaxValue);
            if (!Done && Time.unscaledTime > end && doneAt < 0f) { doneAt = end; Mod.Log("boot: the cover lifts before the adventure's menu came"); }
            float a = Time.unscaledTime < end ? 1f : 1f - (Time.unscaledTime - end) / CoverFade;
            if (a <= 0f) return;
            if (!coverTried)
            {
                coverTried = true;
                try
                {
                    coverBg = RawImage.Load(System.IO.Path.Combine(Content.ArtDir, "adventure_bg.rgba"));
                    if (coverBg != null) coverBg.filterMode = FilterMode.Point;
                    coverTitle = RawImage.Load(System.IO.Path.Combine(Content.ArtDir, "title.rgba"));
                }
                catch (Exception e) { Mod.Once("boot: cover art", e); }
            }
            var full = new Rect(0, 0, Screen.width, Screen.height);
            var keep = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, a);
            GUI.DrawTexture(full, Texture2D.blackTexture, ScaleMode.StretchToFill, false);
            if (coverBg != null) GUI.DrawTexture(full, coverBg, ScaleMode.ScaleAndCrop, true);
            float k = Screen.height / 1080f;
            if (coverTitle != null)
            {
                float tw = Mathf.Min(Screen.width * 0.6f, coverTitle.width * 3f * k), th = tw * coverTitle.height / Mathf.Max(1, coverTitle.width);
                GUI.DrawTexture(new Rect((Screen.width - tw) / 2f, Screen.height * 0.30f - th / 2f, tw, th), coverTitle, ScaleMode.ScaleToFit, true);
            }
            if (coverStyle == null) coverStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
            coverStyle.fontSize = Mathf.RoundToInt(24f * k);
            coverStyle.normal.textColor = new Color(1f, 0.93f, 0.78f, 0.9f);
            GUI.Label(new Rect(0, Screen.height * 0.86f, Screen.width, 40f * k), "Opening the Vampire Rounds adventure...", coverStyle);
            GUI.color = keep;
        }
    }
}
