using System;
using HarmonyLib;
using Il2CppVampireSurvivors.App.Scripts.UI;
using Il2CppVampireSurvivors.App.UI;
using Il2CppVampireSurvivors.UI;
using UnityEngine;
using CharacterController = Il2CppVampireSurvivors.Objects.Characters.CharacterController;

namespace VampireRounds.Vs
{
    /// <summary>
    /// Melty's test run with no one playing (TestMode.On, or VAMPIREROUNDS_AUTOPLAY for development): from VS's warning
    /// and title screens into the Vampire Rounds adventure, Orange on its first stage, through VS's own pages and their
    /// own methods (no input is sent to Windows). In the run, Orange walks a slow circle so the horde keeps coming while
    /// ROUNDS's gun fires; picks are taken by TestMode.PickHelper.
    /// </summary>
    public static class AutoPlay
    {
        public static readonly bool On = TestMode.On || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("VAMPIREROUNDS_AUTOPLAY"));
        private static float nextStep;
        /// <summary>The characters row played (Melty's test: Orange; developers can ask for Blue).</summary>
        private static readonly string Hero = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("VAMPIREROUNDS_AUTOPLAY_HERO")) ? "orange" : Environment.GetEnvironmentVariable("VAMPIREROUNDS_AUTOPLAY_HERO");
        /// <summary>Developer captures: a few seconds on every screen.</summary>
        private static readonly bool Slow = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("VAMPIREROUNDS_AUTOPLAY_SLOW"));
        private static bool adventureConfirmed, characterSelected, characterConfirmed, stageSelected;
        private static string lastStep = "";

        private static T Active<T>() where T : MonoBehaviour
        {
            foreach (var o in UnityEngine.Object.FindObjectsOfType<T>())
                if (o != null && o.isActiveAndEnabled) return o;
            return null;
        }

        private static void Step(string what, Action a)
        {
            if (what != lastStep) Mod.Log("autoplay: " + what);
            lastStep = what;
            try { a(); }
            catch (Exception e) { Mod.Once("autoplay: " + what, e); }
        }

        public static void Tick()
        {
            if (!On || Time.unscaledTime < nextStep) return;
            nextStep = Time.unscaledTime + (Slow ? 4f : 1f);
            // After a game over, back to the menus for another run (Melty records the whole test).
            var over = Active<GameOverPage>();
            if (over != null) { Step("leave the game over", () => over.Quit()); return; }
            var recap = Active<RecapPage>();
            if (recap != null) { Step("close the results", () => { recap.DoneClicked(); characterSelected = characterConfirmed = stageSelected = false; }); return; }
            if (Session.InRun)
            {
                // VS's own start-of-run arcana pick (from the player's main save): nobody plays, so it is skipped.
                var arcana = Active<ArcanaMainSelectionPage>();
                if (arcana != null) Step("skip the arcana pick", () => arcana.Skip());
                return;
            }
            if (!Content.AdventureInData || Content.Types.Count == 0) return;
            // VS's start into the adventure is Boot's (as for players); the test takes over on the adventure's menu.
            if (!Boot.Done) return;
            if (Content.InOurAdventure) adventureConfirmed = true;
            var warning = Active<WarningPage>();
            if (warning != null) { Step("past the warning", () => warning.Complete()); return; }
            var landing = Active<LandingScreenPage>();
            if (landing != null) { Step("past the title", () => landing.MoveToNextView()); return; }
            var stages = Active<StageSelectPage>();
            if (stages != null)
            {
                if (!stageSelected) { Step("select the stage", () => stages.SelectStage()); stageSelected = true; }
                else Step("start the stage", () => stages.ConfirmStage());
                return;
            }
            var chars = Active<CharacterSelectionPage>();
            if (chars != null)
            {
                if (!characterSelected)
                {
                    Step("select " + Hero, () =>
                    {
                        if (Content.Types.TryGetValue(Hero, out var want))
                            foreach (var item in UnityEngine.Object.FindObjectsOfType<CharacterItemUI>())
                                if (item != null && item.Type == want)
                                {
                                    // the menu's own selection (as moving onto the tile with keys or a stick would)
                                    var es = UnityEngine.EventSystems.EventSystem.current;
                                    var sel = item.GetComponentInChildren<UnityEngine.UI.Selectable>();
                                    if (es != null) es.SetSelectedGameObject(sel != null ? sel.gameObject : item.gameObject);
                                    break;
                                }
                    });
                    characterSelected = true;
                }
                else if (!characterConfirmed) { Step("take " + Hero, () => chars.SelectCharacter(false)); characterConfirmed = true; }
                else Step("confirm " + Hero, () => chars.ConfirmCharacter());
                return;
            }
            var advs = Active<SelectAdventuresPage>();
            if (advs != null)
            {
                Step("pick the Vampire Rounds adventure", () =>
                {
                    foreach (var item in UnityEngine.Object.FindObjectsOfType<AdventureItemUI>())
                        if (item != null && item.GetAdventureType() == Content.Adventure) { advs.SelectAdventure(item); break; }
                    advs.ConfirmAdventure();
                    adventureConfirmed = true;
                });
                return;
            }
            var menu = Active<MainMenuPage>();
            if (menu != null)
            {
                if (!adventureConfirmed) Step("open Adventures", () => menu.ShowAdventuresView());
                else Step("open character select", () => menu.ShowCharacterSelect());
            }
        }

        [HarmonyPatch(typeof(CharacterController), nameof(CharacterController.HandlePlayerInput))]
        public static class WalkPatch
        {
            public static void Postfix(CharacterController __instance) => Walk(__instance);
        }

        /// <summary>With no one playing, Orange walks a slow circle (a quarter turn every two seconds).</summary>
        public static void Walk(CharacterController c)
        {
            if (!On || !Session.InRun || !Content.IsOurs(c)) return;
            float a = Time.time * Mathf.PI / 4f;
            c.CurrentDirection = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
        }
    }
}
