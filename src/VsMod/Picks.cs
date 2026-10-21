using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppVampireSurvivors;
using Il2CppVampireSurvivors.Framework;
using Il2CppVampireSurvivors.Signals;
using UnityEngine;
using CharacterController = Il2CppVampireSurvivors.Objects.Characters.CharacterController;

namespace VampireRounds.Vs
{
    /// <summary>
    /// ROUNDS card picks in place of VS's level-up choice and chest rewards (sheets/progression.json). VS has already done
    /// its level and XP bookkeeping when it would show its level-up page; here the game pauses instead, the hidden ROUNDS
    /// shows its own pick screen (5 cards, its rarities and rules) for that player's seat, the card goes into the ROUNDS
    /// player, and VS's level-up completes and the game resumes. A level-up while ROUNDS is still starting is kept and
    /// picked as soon as it is ready.
    /// </summary>
    public static class Picks
    {
        private struct Pending { public int seat, count; public bool levelUp; }
        private static readonly Queue<Pending> queue = new Queue<Pending>();
        public static bool Open { get; private set; }
        private static Pending current;
        private static int serial;
        private static float openedAt;
        private static bool resumeAfter;

        /// <summary>VS's run clock when the last chest card was given (tuning chestCardSeconds).</summary>
        private static float lastChestCard = float.NegativeInfinity;

        public static void Reset()
        {
            lastChestCard = float.NegativeInfinity;
            queue.Clear();
            if (Open) Finish(false);
            Open = false;
        }

        private static int SeatOf(CharacterController c)
        {
            int s = c != null ? Array.IndexOf(Session.SeatChar, c) : -1;
            if (s >= 0) return s;
            for (int i = 0; i < Session.SeatChar.Length; i++) if (Session.SeatChar[i] != null) return i;
            return 0;
        }

        // ------------------------------------------------------------------ hooks

        [HarmonyPatch(typeof(GameManager), nameof(GameManager.SwapToLevelUpScreen))]
        public static class LevelUpPatch
        {
            public static bool Prefix(GameManager __instance) => OnLevelUp(__instance);
        }

        public static bool OnLevelUp(GameManager gm)
        {
            if (!Session.InRun) return true;
            CharacterController who = null;
            try { who = gm.InteractingPlayer ?? gm.Player; } catch { }
            int seat = SeatOf(who);
            Mod.Log("picks: level " + (who != null ? who.Level : 0) + " for seat " + seat + ": ROUNDS card pick" + (Link.ArenaUp ? "" : " (kept until ROUNDS is ready)"));
            queue.Enqueue(new Pending { seat = seat, count = (int)Tuning.levelUpPicks, levelUp = true });
            if (!Link.ArenaUp)
            {
                // VS's level-up ends now; the card comes when ROUNDS is up.
                CompleteLevelUp(gm);
                var p = queue.Dequeue();
                p.levelUp = false;
                queue.Enqueue(p);
                return false;
            }
            TryOpen();
            return false;
        }

        [HarmonyPatch(typeof(GameManager), nameof(GameManager.SwapToTreasureScreen))]
        public static class ChestPatch
        {
            public static bool Prefix(GameManager __instance, CharacterController player) => OnChest(__instance, player);
        }

        public static bool OnChest(GameManager gm, CharacterController player)
        {
            if (!Session.InRun) return true;
            int seat = SeatOf(player);
            float now = 0f;
            try { now = gm.SurvivedSeconds; } catch { }
            // Bosses and elites drop chests in bunches: a chest is a card pick at most once per chestCardSeconds of the run,
            // the others give their gold alone (0.3.0 balance).
            if (now - lastChestCard < Tuning.chestCardSeconds)
            {
                Mod.Log("picks: treasure chest for seat " + seat + " at " + now.ToString("0") + " s: gold only (the last chest card was " + (now - lastChestCard).ToString("0") + " s ago)");
                Notice.Toast("Treasure: gold this time. A chest gives a ROUNDS card at most once a minute.", 3.5f);
                return false;
            }
            lastChestCard = now;
            Mod.Log("picks: treasure chest for seat " + seat + " at " + now.ToString("0") + " s: ROUNDS card pick");
            queue.Enqueue(new Pending { seat = seat, count = (int)Tuning.chestPicks, levelUp = false });
            TryOpen();
            return false;
        }

        [HarmonyPatch(typeof(GameStatePlaying), nameof(GameStatePlaying.Update))]
        public static class HoldPlayingPatch
        {
            public static bool Prefix() => HoldPlaying();
        }

        /// <summary>While ROUNDS's pick is open, VS's playing state (its pause button) waits.</summary>
        public static bool HoldPlaying() => !Open;

        // ------------------------------------------------------------------ the pick

        private static void TryOpen()
        {
            if (Open || queue.Count == 0 || !Link.ArenaUp) return;
            var gm = GM.Core;
            if (gm == null || !Session.InRun) return;
            current = queue.Dequeue();
            var L = Link.L;
            serial = L.I(ArenaLink.C_Pick + ArenaLink.P_Serial) + 1;
            L.I(ArenaLink.C_Pick + ArenaLink.P_Slot, current.seat);
            L.I(ArenaLink.C_Pick + ArenaLink.P_Count, Math.Max(1, current.count));
            L.I(ArenaLink.C_Pick + ArenaLink.P_Serial, serial);
            Open = true;
            openedAt = Time.unscaledTime;
            resumeAfter = !gm.IsPaused;
            if (resumeAfter) gm.PauseGame();
        }

        public static void Tick(ArenaLink L)
        {
            if (!Open) { if (queue.Count > 0 && Link.ArenaUp && GM.Core != null && !GM.Core.IsPaused && GM.Core.IsGameRunning) TryOpen(); return; }
            int s = ArenaLink.Slot(current.seat);
            var c = Session.SeatChar[current.seat];
            int nav = SeatInput.PickNav(c, current.seat);
            if (nav != 0) { L.I(s + ArenaLink.S_NavDir, nav); L.I(s + ArenaLink.S_NavPresses, L.I(s + ArenaLink.S_NavPresses) + 1); }
            if (SeatInput.PickAccept(c)) L.I(s + ArenaLink.S_AcceptPresses, L.I(s + ArenaLink.S_AcceptPresses) + 1);
            if (c != null && c._PlayerIndex == 0)
            {
                Vector2 v = SeatInput.MouseViewport();
                L.F(s + ArenaLink.S_MouseX, v.x);
                L.F(s + ArenaLink.S_MouseY, v.y);
            }
            else L.F(s + ArenaLink.S_MouseX, -1f);
            TestMode.PickHelper(L, s, openedAt);
            bool done = L.I(ArenaLink.ResultOff + ArenaLink.R_PickDone) == serial;
            if (!done && !Link.EngineAlive) { Mod.Warn("picks: ROUNDS went away during a pick; going on without the card"); done = true; }
            if (done) Finish(true);
        }

        private static void Finish(bool read)
        {
            Open = false;
            var L = Link.L;
            if (read && L != null)
            {
                int n = L.I(ArenaLink.ResultOff + ArenaLink.R_PickedCount);
                for (int i = 0; i < n && i < ArenaLink.MaxPicked; i++)
                {
                    string card = L.Str(ArenaLink.ResultOff + ArenaLink.R_Picked + i * ArenaLink.PickedSize, ArenaLink.PickedSize);
                    Mod.Log("picks: seat " + current.seat + " took " + card);
                    TestMode.Mark("card-picked");
                }
            }
            var gm = GM.Core;
            if (gm == null) return;
            if (current.levelUp) CompleteLevelUp(gm);
            if (resumeAfter && gm.IsPaused) gm.ResumeGame();
            foreach (var c in Session.SeatChar) if (c != null) { try { c.SetInvulForMilliSeconds(750f); } catch { } }
        }

        /// <summary>VS's own end of a level-up (its LevelUpCompletedSignal: bookkeeping, next player's turn).</summary>
        private static void CompleteLevelUp(GameManager gm)
        {
            // VS's HUD shows the new level when its level-up page opens (skipped here): set it the same way.
            try { gm.MainUI?.AssignLevel(); }
            catch (Exception e) { Mod.Once("picks: level label", e); }
            try { gm.SignalBus.Fire<GameplaySignals.LevelUpCompletedSignal>(new GameplaySignals.LevelUpCompletedSignal()); return; }
            catch (Exception e) { Mod.Once("picks: level-up signal", e); }
            try { gm.OnLevelUpCompleted(); }
            catch (Exception e) { Mod.Once("picks: level-up completed", e); }
        }
    }
}
