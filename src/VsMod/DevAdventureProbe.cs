using System;
using HarmonyLib;
using Il2CppVampireSurvivors.App.Scripts.UI;
using Il2CppVampireSurvivors.Data;
using Il2CppVampireSurvivors.App.Scripts.Framework.Adventures;

namespace VampireRounds.Vs
{
    /// <summary>Developer runs only: which checks VS's adventure list makes (SelectAdventuresPage.Populate), and switches
    /// that show it as a newer player's save does: VAMPIREROUNDS_DEV_NO_ATLAS (the Atlas relic not found),
    /// VAMPIREROUNDS_DEV_NO_DLC (no DLC adventure owned) and VAMPIREROUNDS_DEV_FRESH (Adventures never opened: VS's intro
    /// tutorial pops up). Together they are the player of the 0.3.0 report, whose adventure list was empty.</summary>
    public static class DevAdventureProbe
    {
        private static bool inPopulate;
        private static readonly bool NoDlc = DevConsole.On && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("VAMPIREROUNDS_DEV_NO_DLC"));
        private static readonly bool Fresh = DevConsole.On && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("VAMPIREROUNDS_DEV_FRESH"));

        [HarmonyPatch(typeof(SelectAdventuresPage), "OnShowStart")]
        public static class ShowWatch
        {
            public static bool Prepare() => DevConsole.On;
            public static void Prefix(SelectAdventuresPage __instance)
            {
                if (!Fresh) return;
                try
                {
                    // In memory only: the run plays by itself (VAMPIREROUNDS_AUTOPLAY), so VS's save is never written.
                    __instance.PlayerOptions.Config.HasSeenAdventuresIntroTutorial = false;
                    Mod.Log("dev: adventure list: shown as to a player who never opened Adventures");
                }
                catch (Exception e) { Mod.Log("dev: fresh adventurer: " + e.Message); }
            }
        }

        [HarmonyPatch(typeof(SelectAdventuresPage), "Populate")]
        public static class PopulateWatch
        {
            public static bool Prepare() => DevConsole.On;
            public static void Prefix() { if (DevConsole.On) { inPopulate = true; Mod.Log("dev: adventure list: Populate starts"); } }
            public static void Postfix(SelectAdventuresPage __instance)
            {
                if (!DevConsole.On) return;
                inPopulate = false;
                try { Mod.Log("dev: adventure list: " + (__instance._spawned != null ? __instance._spawned.Count : -1) + " cards, hide unavailable " + __instance.PlayerOptions.Config.HideUnavailableAdventures); }
                catch (Exception e) { Mod.Log("dev: adventure list: " + e.Message); }
            }
        }

        [HarmonyPatch(typeof(PlayerOptionsData), nameof(PlayerOptionsData.HasCollectedItem))]
        public static class CollectedWatch
        {
            public static bool Prepare() => DevConsole.On;
            public static void Postfix(ItemType item, ref bool __result)
            {
                // VAMPIREROUNDS_DEV_NO_ATLAS: the adventure list as a save that never found the Atlas sees it.
                if (NoAtlas && item == ItemType.RELIC_ATLAS) __result = false;
                if (inPopulate) Mod.Log("dev: adventure list asks HasCollectedItem(" + item + ") = " + __result);
            }

            private static readonly bool NoAtlas = DevConsole.On && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("VAMPIREROUNDS_DEV_NO_ATLAS"));
        }

        [HarmonyPatch(typeof(AdventureManager), nameof(AdventureManager.IsOwned))]
        public static class OwnedWatch
        {
            public static bool Prepare() => DevConsole.On;
            public static void Postfix(AdventureType type, ref bool __result)
            {
                if (NoDlc && type != Content.Adventure) __result = false;
                if (inPopulate) Mod.Log("dev: adventure list asks IsOwned(" + type + ") = " + __result);
            }
        }
    }
}
