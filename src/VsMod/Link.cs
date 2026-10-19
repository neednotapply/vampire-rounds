using System;
using System.Diagnostics;
using UnityEngine;

namespace VampireRounds.Vs
{
    /// <summary>
    /// VS's side of the shared-memory link (sheets/link.json): the heartbeat and process id, and whether the hidden ROUNDS
    /// is there (its beat moves), has its cards and has its arena up. Everything per run is written by <see cref="Session"/>.
    /// </summary>
    public static class Link
    {
        public static ArenaLink L;
        private static int lastRoundsBeat = -1;
        private static float lastRoundsBeatAt = -100f;
        private static int beat;

        public static void Open()
        {
            L = ArenaLink.Open();
            if (L == null) { Mod.Warn("link: no shared memory: " + ArenaLink.LastOpenNote); return; }
            Mod.Log("link: " + ArenaLink.LastOpenNote);
            L.I(ArenaLink.C_Pid, Process.GetCurrentProcess().Id);
            L.I(ArenaLink.C_Mode, 0);
            L.I(ArenaLink.C_Magic, ArenaLink.Magic);
        }

        /// <summary>Before the engine starts: is another (older) engine already beating in this section?</summary>
        public static bool EngineBeating()
        {
            if (L == null || L.I(ArenaLink.ResultOff + ArenaLink.R_Magic) != ArenaLink.Magic) return false;
            int b0 = L.I(ArenaLink.ResultOff + ArenaLink.R_Beat);
            System.Threading.Thread.Sleep(600);
            return L.I(ArenaLink.ResultOff + ArenaLink.R_Beat) != b0;
        }

        /// <summary>Every VS frame: the heartbeat, and the engine's.</summary>
        public static void Beat()
        {
            if (L == null) return;
            L.I(ArenaLink.C_Magic, ArenaLink.Magic);
            L.I(ArenaLink.C_Beat, ++beat);
            int rb = L.I(ArenaLink.ResultOff + ArenaLink.R_Beat);
            if (rb != lastRoundsBeat) { lastRoundsBeat = rb; lastRoundsBeatAt = Time.unscaledTime; }
        }

        /// <summary>The hidden ROUNDS is running and beating.</summary>
        public static bool EngineAlive => L != null && L.I(ArenaLink.ResultOff + ArenaLink.R_Magic) == ArenaLink.Magic && Time.unscaledTime - lastRoundsBeatAt < Tuning.engineStaleSeconds;
        /// <summary>The arena (ROUNDS's sandbox with Orange and Blue) is up and answering.</summary>
        public static bool ArenaUp => EngineAlive && L.I(ArenaLink.ResultOff + ArenaLink.R_State) != ArenaLink.StateOff;
        public static int CardCount => L == null ? 0 : L.I(ArenaLink.ResultOff + ArenaLink.R_CardCount);

        public static void Goodbye()
        {
            try { if (L != null) { L.I(ArenaLink.C_Mode, 0); L.I(ArenaLink.C_Magic, 0); } } catch { }
        }
    }
}
