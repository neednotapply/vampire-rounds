using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace VampireRounds.Launcher
{
    /// <summary>
    /// After Play starts VS: reads MelonLoader's Latest.log while MelonLoader gets going (sheets/systems.json "first_start").
    /// On a first start (and after a VS or MelonLoader update) MelonLoader builds VS's assemblies for one to three minutes
    /// before Vampire Rounds itself loads; this says when that build starts, how far it is and when it ends, so the
    /// launcher can tell the player to leave the game open (FirstStartNotice) and the launcher log keeps how long it took.
    /// </summary>
    public static class LoaderWatch
    {
        /// <summary>MelonLoader started building VS's assemblies.</summary>
        public static event Action Began;
        /// <summary>The build's step, in plain words.</summary>
        public static event Action<string> Step;
        /// <summary>The build is over: true when VS goes on to load the mods.</summary>
        public static event Action<bool> Ended;

        public static string LogPath => Path.Combine(Paths.Vs, "MelonLoader", "Latest.log");

        public static void Start(Process vs, DateTime startedAt)
        {
            new Thread(() =>
            {
                try { Run(vs, startedAt); }
                catch (Exception e) { Log.Write("MelonLoader log watch stopped: " + e.Message); }
            }) { IsBackground = true, Name = "Vampire Rounds MelonLoader log watch" }.Start();
        }

        /// <summary>The interop fix's line when VS's own window shows the note (FirstStartNote): then the launcher shows none.</summary>
        private const string GameShowsNote = "VS's window shows what is happening";

        private static void Run(Process vs, DateTime startedAt)
        {
            var until = DateTime.UtcNow.AddMinutes(15);
            bool building = false, noticeDue = false;
            var buildClock = new Stopwatch();
            string step = "Starting...";
            int done = 0;
            while (DateTime.UtcNow < until)
            {
                bool exited;
                try { exited = vs.HasExited; } catch { exited = true; }
                string text = ReadShared(LogPath);
                // MelonLoader starts a new Latest.log at each start: lines are read only once it is this start's.
                if (text != null && ThisStart(text, startedAt))
                {
                    int end = text.LastIndexOf('\n') + 1;
                    if (done > end) done = 0;
                    foreach (string line in text.Substring(done, Math.Max(0, end - done)).Split('\n'))
                    {
                        if (line.Contains("Assembly Generation Needed!") && !building)
                        {
                            building = noticeDue = true;
                            buildClock.Start();
                            Log.Write("First start: Vampire Survivors's mod loader is building the game's assemblies (once, 1 to 3 minutes)...");
                        }
                        else if (!building) { if (line.Contains("No Generation Needed") || line.Contains("Loading Mods...")) return; }
                        else if (line.Contains(GameShowsNote))
                        {
                            noticeDue = false;
                            Log.Write("The game's window shows what is happening meanwhile.");
                        }
                        else if (line.Contains("Executing Cpp2IL")) Step?.Invoke(step = "Reading Vampire Survivors's code (step 1 of 2)");
                        else if (line.Contains("Generating Interop Assemblies")) Step?.Invoke(step = "Building the mod library (step 2 of 2)");
                        else if (line.Contains("Assembly Generation Successful!")) Step?.Invoke(step = "Almost done...");
                        else if (line.Contains("Loading Mods..."))
                        {
                            Log.Write("The mod loader was ready after " + (int)buildClock.Elapsed.TotalSeconds + " s; Vampire Rounds is starting.");
                            Ended?.Invoke(true);
                            return;
                        }
                    }
                    done = Math.Max(done, end);
                }
                // The interop fix says within seconds when VS's window shows the note itself; without it (another MelonLoader,
                // the fix not loaded) the launcher's note is shown instead.
                if (noticeDue && buildClock.Elapsed.TotalSeconds >= 6)
                {
                    noticeDue = false;
                    Began?.Invoke();
                    Step?.Invoke(step);
                }
                if (exited)
                {
                    if (building)
                    {
                        Log.Write("Vampire Survivors closed while its mod loader was building the game's assemblies (after " + (int)buildClock.Elapsed.TotalSeconds + " s); the next start builds them again.");
                        Ended?.Invoke(false);
                    }
                    return;
                }
                Thread.Sleep(500);
            }
            if (building) Ended?.Invoke(false);
        }

        /// <summary>Latest.log as it is now, read beside MelonLoader (which keeps it open), or null.</summary>
        private static string ReadShared(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var r = new StreamReader(fs, Encoding.UTF8))
                    return r.ReadToEnd();
            }
            catch { return null; }
        }

        /// <summary>The log's first line is stamped [HH:mm:ss.fff]: this start's log begins within a few minutes after Play.</summary>
        private static bool ThisStart(string text, DateTime startedAt)
        {
            if (text.Length < 14 || text[0] != '[') return false;
            if (!TimeSpan.TryParseExact(text.Substring(1, 12), @"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture, out var t)) return false;
            double d = (t - startedAt.TimeOfDay).TotalSeconds;
            if (d < -43200) d += 86400;
            if (d > 43200) d -= 86400;
            return d >= -5 && d <= 600;
        }
    }
}
