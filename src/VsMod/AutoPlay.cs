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
        /// <summary>The stage played (a stages row id or its slot; developers): Melty's test plays the first stage. A comma
        /// list plays them in turn, one run each.</summary>
        private static readonly string[] StagesWanted = (Environment.GetEnvironmentVariable("VAMPIREROUNDS_AUTOPLAY_STAGE") ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
        private static int stageTurn;
        private static string StageWanted => StagesWanted.Length == 0 ? "" : StagesWanted[stageTurn % StagesWanted.Length].Trim();
        /// <summary>Developers: the life ends after this many seconds of VS's run clock (to see a run's end and its unlocks).</summary>
        private static readonly float DieAt = float.TryParse(Environment.GetEnvironmentVariable("VAMPIREROUNDS_DEV_DIE_AT"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var die) ? die : 0f;
        /// <summary>Developer captures: a few seconds on every screen.</summary>
        private static readonly bool Slow = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("VAMPIREROUNDS_AUTOPLAY_SLOW"));
        private static bool adventureConfirmed, characterSelected, characterConfirmed, stageSelected, stageChosen;
        private static float stagePageSince = -1f;
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
            if (recap != null) { Step("close the results", () => { recap.DoneClicked(); characterSelected = characterConfirmed = stageSelected = stageChosen = false; stageTurn++; }); return; }
            if (Session.InRun)
            {
                // VS's own start-of-run arcana pick (from the player's main save): nobody plays, so it is skipped.
                var arcana = Active<ArcanaMainSelectionPage>();
                if (arcana != null) Step("skip the arcana pick", () => arcana.Skip());
                if (DieAt > 0f)
                {
                    float now = 0f;
                    try { now = Il2CppVampireSurvivors.Framework.GM.Core.SurvivedSeconds; } catch { }
                    if (now > DieAt) Session.DevDie(0);
                }
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
                if (stagePageSince < 0f) stagePageSince = Time.unscaledTime;
                var row = Array.Find(Sheets.Stages, r => r.Id == StageWanted || r.Slot == StageWanted);
                if (row != null && !stageSelected)
                {
                    // The page highlights the last stage played a moment after it opens; the wanted one is chosen after that,
                    // as moving onto its tile would (the menu's own selection), then taken.
                    if (Time.unscaledTime - stagePageSince < 1.5f) return;
                    Step("highlight " + row.Name, () =>
                    {
                        foreach (var item in UnityEngine.Object.FindObjectsOfType<StageItemUI>())
                            if (item != null && item.GetStageType().ToString() == row.Slot)
                            {
                                var es = UnityEngine.EventSystems.EventSystem.current;
                                var sel = item.GetComponentInChildren<UnityEngine.UI.Selectable>();
                                if (es != null) es.SetSelectedGameObject(sel != null ? sel.gameObject : item.gameObject);
                                stages.HighlightStage(item);
                                break;
                            }
                    });
                    row = null;
                    stageSelected = true;
                    stageChosen = false;
                    return;
                }
                if (!stageChosen) { Step("select the stage", () => stages.SelectStage()); stageChosen = true; stageSelected = true; }
                else Step("start the stage", () => stages.ConfirmStage());
                return;
            }
            stagePageSince = -1f;
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

        /// <summary>With no one playing, Orange walks a slow circle (a quarter turn every two seconds), and steps away from
        /// the horde when it closes in, as a player would (without that it stood in the first bat swarm and fell in two minutes).</summary>
        public static void Walk(CharacterController c)
        {
            if (!On || !Session.InRun || !Content.IsOurs(c)) return;
            float a = Time.time * Mathf.PI / 4f;
            var circle = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            Vector3 p = c.CachedTransform.position;
            var away = Enemies.Away(new Vector2(p.x, p.y), Session.CamHalfH * 0.6f);
            if (away.sqrMagnitude < 1e-4f) { c.CurrentDirection = circle; return; }
            var dir = away.normalized;
            // A little sideways too, so it slips past the horde instead of backing into a wall of it.
            c.CurrentDirection = (dir + new Vector2(-dir.y, dir.x) * 0.35f + circle * 0.15f).normalized;
        }
    }
}
