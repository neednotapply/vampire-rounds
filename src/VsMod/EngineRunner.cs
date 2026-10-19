using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace VampireRounds.Vs
{
    /// <summary>
    /// Starts the player's own ROUNDS hidden in the background as Vampire Rounds's engine (sheets/systems.json "engine"):
    /// Rounds.exe with Doorstop's arguments pointing at BepInEx as Melty installed it (UserData/VampireRounds/rounds), so
    /// nothing is written into the ROUNDS folder besides the Doorstop proxy Melty installs. It closes only the ROUNDS it
    /// started, when VS closes.
    /// Adapted from Roundhead's GameRunner (MIT, the same creator).
    /// </summary>
    public static class EngineRunner
    {
        public const string RoundsAppId = "1557740";
        public static Process Proc;
        public static string RoundsDir { get; private set; }
        /// <summary>A plain sentence for the player when the engine cannot start (shown on screen by Notice).</summary>
        public static string Problem { get; private set; }
        public static string Status { get; private set; } = "not started";
        private static int restarts;
        private static bool stopping;

        /// <summary>BepInEx and the engine as Melty installed them (UserData/VampireRounds/rounds): ROUNDS loads them from there,
        /// and BepInEx keeps its log, cache and config beside them.</summary>
        public static string Shipped => Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "VampireRounds", "rounds");
        public static string Preloader => Path.Combine(Shipped, "BepInEx", "core", "BepInEx.Preloader.dll");
        public static string EngineLog => Path.Combine(Shipped, "BepInEx", "LogOutput.log");

        /// <summary>Starts the engine on a worker thread (copying and starting take a few seconds; VS's main thread never waits).</summary>
        public static void StartAsync()
        {
            var t = new Thread(() =>
            {
                try { Start(); }
                catch (Exception e) { Problem = "Vampire Rounds could not start ROUNDS: " + e.Message; Mod.Warn("engine: " + e); }
            }) { IsBackground = true, Name = "VampireRounds engine starter" };
            t.Start();
        }

        private static void Start()
        {
            RoundsDir = FindRounds();
            if (RoundsDir == null) { Problem = "Vampire Rounds needs ROUNDS installed (Steam). Install it, then press Play again."; Status = "no ROUNDS"; return; }
            Mod.Log("engine: ROUNDS at " + RoundsDir);
            string exe = Path.Combine(RoundsDir, "Rounds.exe");
            CheckBranch();
            if (Problem != null) return;
            if (!File.Exists(Path.Combine(RoundsDir, "version.dll")))
                Mod.Warn("engine: the Doorstop proxy (version.dll) is not in the ROUNDS folder; ROUNDS will start without the engine");
            // A hidden engine left by an earlier VS session (its VS gone) ends itself within seconds; one that still beats is reused.
            if (Link.EngineBeating()) { Status = "reusing a running engine"; Mod.Log("engine: an engine is already running; using it"); return; }
            var running = Running(exe);
            if (running.Length > 0)
            {
                Problem = "ROUNDS is already open. Close it, then start the Vampire Rounds adventure again (Vampire Rounds runs its own ROUNDS in the background).";
                Status = "ROUNDS already open";
                Mod.Warn("engine: ROUNDS is already running (pid " + string.Join(",", running.Select(p => p.Id)) + "); not starting a second one");
                return;
            }
            if (!File.Exists(Preloader)) { Problem = "The Vampire Rounds files are missing; reinstall it from Melty."; Status = "files missing"; return; }
            Launch(exe);
            var watch = new Thread(Watch) { IsBackground = true, Name = "VampireRounds engine watch" };
            watch.Start();
        }

        private static void Launch(string exe)
        {
            string args = "--doorstop-enabled true --doorstop-target-assembly \"" + Preloader + "\" " + Paths.EngineFlag
                + " -batchmode -screen-fullscreen 0 -screen-width 640 -screen-height 360";
            var psi = new ProcessStartInfo(exe, args) { WorkingDirectory = RoundsDir, UseShellExecute = false, CreateNoWindow = true };
            // SteamAppId tells ROUNDS's Steam API which owned game it is (as Steam does), so it does not hand itself back
            // to Steam and lose the arguments. Steam must be running and ROUNDS owned; nothing about ownership is bypassed.
            psi.EnvironmentVariables["SteamAppId"] = RoundsAppId;
            psi.EnvironmentVariables["SteamGameId"] = RoundsAppId;
            psi.EnvironmentVariables.Remove("DOORSTOP_DISABLE");
            psi.EnvironmentVariables.Remove("DOORSTOP_INITIALIZED");
            Proc = Process.Start(psi);
            Status = "started (pid " + Proc.Id + ")";
            Mod.Log("engine: started ROUNDS hidden, pid " + Proc.Id);
            var tuck = new Thread(() => KeepOutOfSight(Proc)) { IsBackground = true, Name = "VampireRounds engine window" };
            tuck.Start();
        }

        /// <summary>Starts the engine again if it stops while VS runs (VS keeps the run; the players come back).</summary>
        private static void Watch()
        {
            while (!stopping)
            {
                Thread.Sleep(2000);
                var p = Proc;
                if (p == null || stopping) continue;
                bool exited;
                try { exited = p.HasExited; } catch { exited = true; }
                if (!exited) continue;
                int code = -1;
                try { code = p.ExitCode; } catch { }
                Mod.Warn("engine: the background ROUNDS stopped (exit code " + code + ")");
                if (++restarts > 3) { Problem = "ROUNDS keeps stopping in the background; see " + EngineLog; Proc = null; continue; }
                try { Launch(Path.Combine(RoundsDir, "Rounds.exe")); }
                catch (Exception e) { Mod.Warn("engine: could not start ROUNDS again: " + e.Message); }
            }
        }

        private delegate bool EnumProc(IntPtr h, IntPtr l);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc f, IntPtr l);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int GetWindowThreadProcessId(IntPtr h, out int pid);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int index);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr h, int index, int value);

        /// <summary>
        /// The hidden ROUNDS still opens a window (full screen, in front): for its first half minute every window it shows is
        /// made a tool window that takes no focus and moved far off screen, so VS stays the game on screen and in recordings.
        /// The engine does the same from inside ROUNDS and hands VS the focus back.
        /// </summary>
        private static void KeepOutOfSight(Process p)
        {
            var until = DateTime.UtcNow.AddSeconds(30);
            int pid;
            try { pid = p.Id; } catch { return; }
            while (DateTime.UtcNow < until && !stopping)
            {
                try
                {
                    if (p.HasExited) return;
                    EnumWindows((h, l) =>
                    {
                        int wp;
                        GetWindowThreadProcessId(h, out wp);
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

        /// <summary>Ends the ROUNDS this session started (only that one). The engine also ends itself within 2 s of VS closing.</summary>
        public static void Stop()
        {
            stopping = true;
            var p = Proc;
            Proc = null;
            if (p == null) return;
            try
            {
                if (p.HasExited) return;
                if (p.WaitForExit(2500)) return;
                p.Kill();
                Mod.Log("engine: closed the background ROUNDS (pid " + p.Id + ")");
            }
            catch (Exception e) { Mod.Warn("engine: could not close ROUNDS: " + e.Message); }
        }

        private static Process[] Running(string exe)
        {
            string name = Path.GetFileNameWithoutExtension(exe);
            return Process.GetProcessesByName(name).Where(p =>
            {
                try { return string.Equals(Path.GetFullPath(p.MainModule.FileName), Path.GetFullPath(exe), StringComparison.OrdinalIgnoreCase); }
                catch { return true; }
            }).ToArray();
        }

        /// <summary>
        /// The engine and ROUNDS card mods are built for ROUNDS's "old-rounds-for-mods" Steam branch (Unity 2018.4). ROUNDS's
        /// current branch (a newer Unity) lacks the types the engine uses, so say so before anything starts.
        /// </summary>
        private static void CheckBranch()
        {
            string version;
            try { version = FileVersionInfo.GetVersionInfo(Path.Combine(RoundsDir, "UnityPlayer.dll")).FileVersion ?? ""; }
            catch { return; }
            Mod.Log("engine: ROUNDS runs on Unity " + version);
            if (version.Length == 0 || version.StartsWith("2018.")) return;
            Problem = "Vampire Rounds needs ROUNDS's \"old-rounds-for-mods\" version. In Steam: right-click ROUNDS > Properties > Betas, choose \"old-rounds-for-mods\", let it update, then play again.";
            Status = "wrong ROUNDS version";
        }

        /// <summary>ROUNDS's folder: what Melty passes (--vr-rounds or VAMPIREROUNDS_ROUNDS), else the Steam libraries.</summary>
        private static string FindRounds()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "--vr-rounds" && Ok(args[i + 1])) return args[i + 1];
            string env = Environment.GetEnvironmentVariable("VAMPIREROUNDS_ROUNDS");
            if (Ok(env)) return env;
            foreach (string lib in SteamLibraries())
            {
                string d = Path.Combine(lib, "steamapps", "common", "ROUNDS");
                if (Ok(d)) return d;
            }
            return null;
        }

        private static bool Ok(string d) => !string.IsNullOrEmpty(d) && File.Exists(Path.Combine(d, "Rounds.exe"));

        private static string[] SteamLibraries()
        {
            string steam = null;
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")) steam = k?.GetValue("SteamPath") as string;
            }
            catch { }
            if (string.IsNullOrEmpty(steam)) steam = @"C:\Program Files (x86)\Steam";
            var libs = new System.Collections.Generic.List<string> { steam };
            try
            {
                string vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
                if (File.Exists(vdf))
                    foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                        libs.Add(m.Groups[1].Value.Replace("\\\\", "\\"));
            }
            catch { }
            return libs.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
    }
}
