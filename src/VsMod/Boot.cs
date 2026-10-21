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
    /// </summary>
    public static class Boot
    {
        public static bool Done { get; private set; }
        private static float nextStep, giveUpAt = -1f;
        private static string lastStep = "";

        private static T Active<T>() where T : MonoBehaviour
        {
            foreach (var o in UnityEngine.Object.FindObjectsOfType<T>())
                if (o != null && o.isActiveAndEnabled) return o;
            return null;
        }

        private static void Step(string what, Action a)
        {
            if (what != lastStep) Mod.Log("boot: " + what);
            lastStep = what;
            try { a(); }
            catch (Exception e) { Mod.Once("boot: " + what, e); }
        }

        public static void Tick()
        {
            if (Done || Time.unscaledTime < nextStep) return;
            nextStep = Time.unscaledTime + 0.5f;
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
            Mod.Log("boot: done, " + why);
        }
    }
}
