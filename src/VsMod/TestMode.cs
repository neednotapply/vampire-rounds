using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using UnityEngine;

namespace VampireRounds.Vs
{
    /// <summary>
    /// Melty's test run (sheets/checks.json; recipe test.checks). While %LOCALAPPDATA%\Melty\test\vampire-rounds.json exists,
    /// the mod plays by itself: it goes into the Vampire Rounds adventure as Orange (AutoPlay), walks in circles while
    /// ROUNDS's gun fires, takes ROUNDS's card picks after a short look, and writes "pass &lt;check&gt;" to the checks file
    /// Melty names, once per check, as the run proves it. It never quits the game; Melty closes it.
    /// </summary>
    public static class TestMode
    {
        public const string Slug = "vampire-rounds";
        public static bool On { get; private set; }
        public static int Seed { get; private set; }
        private static string checksFile;
        private static readonly HashSet<string> marked = new HashSet<string>();
        private static float nextBeat;

        public static string TestFile => Path.Combine(Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? "", "Melty", "test", Slug + ".json");

        public static void Init()
        {
            try
            {
                if (!File.Exists(TestFile)) return;
                using (var doc = JsonDocument.Parse(File.ReadAllText(TestFile)))
                {
                    var root = doc.RootElement;
                    if (root.TryGetProperty("checks", out var c) && c.ValueKind == JsonValueKind.String) checksFile = c.GetString();
                    if (root.TryGetProperty("seed", out var s) && s.ValueKind == JsonValueKind.Number) Seed = s.GetInt32();
                }
                On = true;
                UnityEngine.Random.InitState(Seed);
                Mod.Log("test: Melty test run (checks to " + checksFile + ", seed " + Seed + ")");
            }
            catch (Exception e) { Mod.Warn("test: could not read " + TestFile + ": " + e.Message); }
        }

        /// <summary>A check the run has proved: written once to Melty's checks file (and the log).</summary>
        public static void Mark(string check)
        {
            if (!marked.Add(check)) return;
            Mod.Log("check passed: " + check);
            if (!On || string.IsNullOrEmpty(checksFile)) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(checksFile));
                File.AppendAllText(checksFile, "pass " + check + Environment.NewLine);
            }
            catch (Exception e) { Mod.Warn("test: could not write the check " + check + ": " + e.Message); }
        }

        public static void Tick()
        {
            if (!On && !AutoPlay.On) return;
            if (Session.InRun && Link.L != null)
            {
                int shots = 0;
                for (int s = 0; s < ArenaLink.Seats; s++) shots += Link.L.I(ArenaLink.RSlot(s) + ArenaLink.Q_Shots);
                if (shots > 0) Mark("gun-fired");
            }
            if (Time.unscaledTime > nextBeat)
            {
                nextBeat = Time.unscaledTime + 15f;
                Mod.Log("test: heartbeat: run " + Session.InRun + ", engine " + (Link.EngineAlive ? "alive" : "away") + " (" + EngineRunner.Status + "), arena " + Link.ArenaUp + ", cards " + Link.CardCount
                    + ", enemies " + Enemies.Count + ", hits " + Enemies.HitsApplied + " (" + Enemies.HitsMissed + " on gone enemies)" + ", picking " + Picks.Open + ", checks " + string.Join(",", marked));
            }
        }

        /// <summary>In a test run nobody plays: the pick takes the highlighted card after a short look.</summary>
        public static void PickHelper(ArenaLink L, int slotOff, float openedAt)
        {
            if (!AutoPlay.On) return;
            float t = Time.unscaledTime - openedAt;
            if (t < 1f)
            {
                acceptedThisPick = false;
                // A different card each pick (one to four steps right, or none), so test runs try many cards.
                stepsLeft = rng.Next(0, 5);
                nextStep = 1f;
            }
            // One step at a time: ROUNDS reads each change of the press count as one step.
            if (stepsLeft > 0 && t >= nextStep && t < 3f)
            {
                stepsLeft--;
                nextStep = t + 0.3f;
                L.I(slotOff + ArenaLink.S_NavDir, 1);
                L.I(slotOff + ArenaLink.S_NavPresses, L.I(slotOff + ArenaLink.S_NavPresses) + 1);
            }
            if (t > 3.5f && ((int)(t * 2f)) % 4 == 0 && !acceptedThisPick)
            {
                acceptedThisPick = true;
                L.I(slotOff + ArenaLink.S_AcceptPresses, L.I(slotOff + ArenaLink.S_AcceptPresses) + 1);
            }
        }

        private static bool acceptedThisPick;
        private static int stepsLeft;
        private static float nextStep;
        private static readonly System.Random rng = new System.Random();
        private static bool noSaveLogged;

        [HarmonyLib.HarmonyPatch(typeof(Il2CppVampireSurvivors.Objects.PlayerOptions), nameof(Il2CppVampireSurvivors.Objects.PlayerOptions.Save))]
        public static class NoSavePatch
        {
            public static bool Prefix() => NoSave();
        }

        /// <summary>A run that plays by itself (Melty's test, a developer's autoplay) never writes VS's save: the player's
        /// progress, gold and the adventure's unlocks stay exactly as they were.</summary>
        public static bool NoSave()
        {
            if (!AutoPlay.On) return true;
            if (!noSaveLogged) { noSaveLogged = true; Mod.Log("test: this run plays by itself; VS's save is left as it was"); }
            return false;
        }
    }
}
