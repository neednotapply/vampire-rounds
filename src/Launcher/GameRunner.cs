using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace VampireRounds.Launcher
{
    /// <summary>
    /// Play: start Vampire Survivors with the Vampire Rounds mod, which starts the player's ROUNDS hidden in the background
    /// (with this profile's Thunderstore mods) and goes straight into the Vampire Rounds adventure. The launcher waits until
    /// VS closes (the mod closes the ROUNDS it started). Adapted from Roundhead's GameRunner (MIT, the same creator).
    /// </summary>
    public static class GameRunner
    {
        public const string RoundsAppId = "1557740";
        public const string VsAppId = "1794680";
        public static Process VsProc;
        public static event Action<string> Status;

        private static void Say(string s)
        {
            Log.Write(s);
            Status?.Invoke(s);
        }

        public static Process[] Running(string exe)
        {
            string name = Path.GetFileNameWithoutExtension(exe);
            return Process.GetProcessesByName(name).Where(p =>
            {
                try { return string.Equals(Path.GetFullPath(p.MainModule.FileName), Path.GetFullPath(exe), StringComparison.OrdinalIgnoreCase); }
                catch { return true; }
            }).ToArray();
        }

        public static void Play()
        {
            string vsExe = Path.Combine(Paths.Vs, "VampireSurvivors.exe");
            string roundsExe = Path.Combine(Paths.Rounds, "Rounds.exe");
            if (!File.Exists(vsExe)) throw new Exception("Vampire Survivors was not found at " + Paths.Vs);
            if (!File.Exists(roundsExe)) throw new Exception("ROUNDS was not found at " + Paths.Rounds);
            if (!File.Exists(Path.Combine(Paths.ProfileBepInEx, "core", "BepInEx.Preloader.dll")))
                throw new Exception("the Vampire Rounds files are missing (" + Paths.Profile + "); reinstall it from Melty");
            CheckRoundsBranch();
            Faces.StopDrawing();
            if (Running(vsExe).Length > 0) throw new Exception("Vampire Survivors is already open. Close it, then press Play.");
            if (Running(roundsExe).Length > 0) throw new Exception("ROUNDS is already open. Close it, then press Play (Vampire Rounds starts its own ROUNDS in the background).");

            Say("Starting Vampire Survivors with " + ProfileManager.Mods.Count(m => m.Enabled) + " Thunderstore mod(s) for ROUNDS...");
            // SteamAppId tells VS's Steam API which owned game it is (as Steam itself does when it starts a game), so it does
            // not hand itself back to Steam and lose its arguments. Steam must be running and the game owned.
            // Melty test runs record the game's window, which needs VS windowed; players keep their own display setting.
            string display = Program.AutoPlay ? " -screen-fullscreen 0 -screen-width 1280 -screen-height 720" : "";
            var psi = new ProcessStartInfo(vsExe, "--vr-rounds \"" + Paths.Rounds + "\"" + display) { WorkingDirectory = Paths.Vs, UseShellExecute = false };
            psi.EnvironmentVariables["SteamAppId"] = VsAppId;
            psi.EnvironmentVariables["SteamGameId"] = VsAppId;
            psi.EnvironmentVariables["VAMPIREROUNDS_ROUNDS"] = Paths.Rounds;
            VsProc = Process.Start(psi);
            Thread.Sleep(6000);
            if (VsProc.HasExited) throw new Exception("Vampire Survivors closed straight away (is Steam running and signed in?)");
            Say("Vampire Rounds is running. Have fun!");
            VsProc.WaitForExit();
            Say("Vampire Survivors closed.");
        }

        private delegate bool EnumProc(IntPtr h, IntPtr l);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc f, IntPtr l);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int GetWindowThreadProcessId(IntPtr h, out int pid);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int index);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr h, int index, int value);

        /// <summary>A hidden ROUNDS still opens a window (full screen, in front): every window it shows is made a tool window
        /// that takes no focus and moved far off screen, as the VS mod's EngineRunner does.</summary>
        public static void KeepOutOfSight(Process p)
        {
            int pid;
            try { pid = p.Id; } catch { return; }
            var until = DateTime.UtcNow.AddSeconds(150);
            while (DateTime.UtcNow < until)
            {
                try
                {
                    if (p.HasExited) return;
                    EnumWindows((h, l) =>
                    {
                        GetWindowThreadProcessId(h, out int wp);
                        if (wp != pid || !IsWindowVisible(h)) return true;
                        int ex = GetWindowLong(h, -20);
                        int want = (ex | 0x80 | 0x08000000) & ~0x40000; // tool window, no activate, not in the taskbar
                        if (ex != want) SetWindowLong(h, -20, want);
                        SetWindowPos(h, new IntPtr(1), -32000, -32000, 640, 360, 0x10 | 0x20);
                        return true;
                    }, IntPtr.Zero);
                }
                catch { }
                Thread.Sleep(100);
            }
        }

        /// <summary>
        /// The engine and Thunderstore's card mods are built for ROUNDS's "old-rounds-for-mods" Steam branch (Unity 2018.4).
        /// ROUNDS's current branch (a newer Unity) lacks the types they hook; say so before anything starts.
        /// </summary>
        private static void CheckRoundsBranch()
        {
            string player = Path.Combine(Paths.Rounds, "UnityPlayer.dll");
            string version;
            try { version = FileVersionInfo.GetVersionInfo(player).FileVersion ?? ""; }
            catch { return; }
            Log.Write("ROUNDS runs on Unity " + version);
            if (version.Length == 0 || version.StartsWith("2018.")) return;
            throw new WrongRoundsVersion();
        }
    }

    public class WrongRoundsVersion : Exception
    {
        public WrongRoundsVersion() : base("Vampire Rounds needs ROUNDS's \"old-rounds-for-mods\" version, the one Thunderstore card mods are made for, and your ROUNDS is the newer one.\n\n"
            + "In Steam: right-click ROUNDS > Properties > Betas, choose \"old-rounds-for-mods\", wait for the small update to finish, then press PLAY again.") { }
    }
}
