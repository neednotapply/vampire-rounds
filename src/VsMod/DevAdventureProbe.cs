using System;
using HarmonyLib;
using Il2CppVampireSurvivors.App.Scripts.UI;
using Il2CppVampireSurvivors.Data;
using Il2CppVampireSurvivors.App.Scripts.Framework.Adventures;

namespace VampireRounds.Vs
{
    /// <summary>Developer runs only: which checks VS's adventure list makes (SelectAdventuresPage.Populate).</summary>
    public static class DevAdventureProbe
    {
        private static bool inPopulate;

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
            public static void Postfix(AdventureType type, bool __result) { if (inPopulate) Mod.Log("dev: adventure list asks IsOwned(" + type + ") = " + __result); }
        }
    }
}
