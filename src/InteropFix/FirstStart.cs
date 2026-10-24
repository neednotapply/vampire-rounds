using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Threading;
using MelonLoader;

namespace VampireRounds.InteropFix
{
    /// <summary>
    /// On its first start on a game (and after every game or MelonLoader update) MelonLoader builds the game's Il2Cpp
    /// assemblies: one to three minutes on VS's main thread, during which VS's window answers no messages. Windows marks it
    /// "Not Responding", players close it (Melty reports a freeze) and the build starts over at the next start
    /// (sheets/systems.json "first_start"). Here MelonLoader's own generator (its module's Core.Run, unchanged) runs on a
    /// worker thread while the main thread keeps answering the window's messages, so the window stays responsive and the
    /// close button MelonLoader greys out during the build holds. The main thread does nothing else meanwhile.
    /// A rebuild (after a game or MelonLoader update) first deletes the old assemblies; one that a plugin already loaded
    /// (HarmonyX's TypeByName walking every type loads Il2Cppmscorlib) cannot be deleted, and MelonLoader's start then fails
    /// at every start. Such a file is renamed aside instead (Windows allows that) and deleted at the next start.
    /// VAMPIREROUNDS_DEV_NOPUMP=1 leaves the generator on the main thread, VAMPIREROUNDS_DEV_NOSETASIDE=1 leaves files in
    /// use as they are (to see the old behaviour).
    /// </summary>
    internal static class FirstStart
    {
        private const string GeneratorAssembly = "Il2CppAssemblyGenerator";
        private const string GeneratorType = "MelonLoader.Il2CppAssemblyGenerator.Core";
        private const string GeneratorConfig = "MelonLoader.Il2CppAssemblyGenerator.Config";
        private const string AsideMark = ".vr-old";
        private const BindingFlags Statics = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private static HarmonyLib.Harmony harmony;
        private static MelonLogger.Instance log;
        private static int mainThread;
        private static bool patched, pump, setAside;
        private static FieldInfo configValues, interopPackage;
        [ThreadStatic] private static bool onWorker;

        public static void Watch(HarmonyLib.Harmony h, MelonLogger.Instance logger)
        {
            harmony = h;
            log = logger;
            mainThread = Environment.CurrentManagedThreadId;
            pump = Environment.GetEnvironmentVariable("VAMPIREROUNDS_DEV_NOPUMP") != "1";
            setAside = Environment.GetEnvironmentVariable("VAMPIREROUNDS_DEV_NOSETASIDE") != "1";
            if (!pump) log.Msg("first start: developer switch, MelonLoader's assembly generator stays on the main thread");
            if (!setAside) log.Msg("first start: developer switch, old assemblies in use are left as they are");
            DeleteSetAside();
            // MelonLoader loads its generator module after the plugins (Core.Start), so it is patched as it loads.
            AppDomain.CurrentDomain.AssemblyLoad += OnLoad;
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies()) TryPatch(a);
        }

        /// <summary>Old assemblies an earlier rebuild had to set aside: nothing has them open at this start.</summary>
        private static void DeleteSetAside()
        {
            try
            {
                string dir = MelonLoader.Utils.MelonEnvironment.Il2CppAssembliesDirectory;
                if (!Directory.Exists(dir)) return;
                foreach (string f in Directory.GetFiles(dir, "*" + AsideMark + "*"))
                    try { File.Delete(f); } catch { }
            }
            catch { }
        }

        private static void OnLoad(object sender, AssemblyLoadEventArgs e)
        {
            try { TryPatch(e.LoadedAssembly); }
            catch { }
        }

        private static void TryPatch(Assembly a)
        {
            if (patched || a.GetName().Name != GeneratorAssembly) return;
            patched = true;
            AppDomain.CurrentDomain.AssemblyLoad -= OnLoad;
            var core = a.GetType(GeneratorType, false);
            if (pump)
                try
                {
                    var run = core?.GetMethod("Run", Statics, null, Type.EmptyTypes, null);
                    if (run == null || run.ReturnType != typeof(int)) log.Msg("first start: this MelonLoader's assembly generator has no Core.Run; it runs as MelonLoader runs it");
                    else harmony.Patch(run, prefix: Prefix(nameof(RunPrefix)));
                }
                catch (Exception e) { log.Msg("first start: MelonLoader's assembly generator runs as MelonLoader runs it (" + e.Message + ")"); }
            if (setAside)
                try
                {
                    configValues = a.GetType(GeneratorConfig, false)?.GetField("Values", Statics);
                    interopPackage = core?.GetField("il2cppinterop", Statics);
                    var cleanup = core?.GetMethod("OldFiles_Cleanup", Statics, null, Type.EmptyTypes, null);
                    var move = core?.GetMethod("OldFiles_LAM", Statics, null, Type.EmptyTypes, null);
                    if (cleanup != null && configValues != null) harmony.Patch(cleanup, prefix: Prefix(nameof(CleanupPrefix)));
                    if (move != null && interopPackage != null) harmony.Patch(move, prefix: Prefix(nameof(MovePrefix)));
                }
                catch (Exception e) { log.Msg("first start: old assemblies are removed as MelonLoader removes them (" + e.Message + ")"); }
        }

        private static HarmonyLib.HarmonyMethod Prefix(string name) => new HarmonyLib.HarmonyMethod(typeof(FirstStart).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic));

        /// <summary>How long the main thread simply waits for the generator before it answers the window: an ordinary start's
        /// check (no build) is done by then, so only a real build has the window answered meanwhile. Windows calls a window
        /// "Not Responding" after 5 s without answers.</summary>
        private const int AnswerAfterMs = 2000;

        /// <summary>Core.Run on a worker thread; the main thread answers the window until it returns, then returns its result.</summary>
        private static bool RunPrefix(MethodBase __originalMethod, ref int __result)
        {
            if (onWorker || Environment.CurrentManagedThreadId != mainThread) return true;
            int result = 1;
            ExceptionDispatchInfo failed = null;
            var clock = Stopwatch.StartNew();
            int messages = 0;
            long longest = 0;
            string longestWhy = "";
            bool answered = false;
            using (var done = new ManualResetEvent(false))
            {
                var worker = new Thread(() =>
                {
                    onWorker = true;
                    try { result = (int)__originalMethod.Invoke(null, null); }
                    catch (TargetInvocationException e) { failed = ExceptionDispatchInfo.Capture(e.InnerException ?? e); }
                    catch (Exception e) { failed = ExceptionDispatchInfo.Capture(e); }
                    finally { done.Set(); }
                }) { IsBackground = true, Name = "Vampire Rounds: MelonLoader's assembly generator" };
                worker.Start();
                if (!done.WaitOne(AnswerAfterMs))
                {
                    answered = true;
                    // The build allocates gigabytes; a blocking full collection stops the answering thread too (seen: 6.5 s).
                    var latency = GCSettings.LatencyMode;
                    try { GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency; } catch { }
                    try { Pump(done, clock, out messages, out longest, out longestWhy); }
                    finally { try { GCSettings.LatencyMode = latency; } catch { } }
                }
            }
            if (answered)
                log.Msg("first start: MelonLoader's assembly generator took " + clock.Elapsed.TotalSeconds.ToString("0.0") + " s; VS's window answered "
                    + messages + " message(s) meanwhile, the longest wait between answers " + longest + " ms" + longestWhy);
            failed?.Throw();
            __result = result;
            return false;
        }

        /// <summary>Before MelonLoader deletes the old assemblies (Config.Values.OldFiles): each is deleted here, or set aside
        /// when something has it open. MelonLoader then finds them gone.</summary>
        private static void CleanupPrefix()
        {
            try
            {
                var values = configValues.GetValue(null);
                if (values?.GetType().GetField("OldFiles")?.GetValue(values) is List<string> old)
                    foreach (string name in old) Remove(name);
            }
            catch (Exception e) { log.Msg("first start: could not look at the old assemblies (" + e.Message + ")"); }
        }

        /// <summary>Before MelonLoader moves the new assemblies in (each replacing any file of its name): the same.</summary>
        private static void MovePrefix()
        {
            try
            {
                var package = interopPackage.GetValue(null);
                string built = package?.GetType().GetField("OutputFolder", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(package) as string;
                if (built == null || !Directory.Exists(built)) return;
                foreach (string f in Directory.GetFiles(built)) Remove(Path.GetFileName(f));
            }
            catch (Exception e) { log.Msg("first start: could not look at the new assemblies (" + e.Message + ")"); }
        }

        private static void Remove(string name)
        {
            string path = Path.Combine(MelonLoader.Utils.MelonEnvironment.Il2CppAssembliesDirectory, name);
            if (!File.Exists(path)) return;
            try { File.Delete(path); return; }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
            try
            {
                string aside = path + AsideMark;
                for (int n = 2; File.Exists(aside); n++) aside = path + AsideMark + n;
                File.Move(path, aside);
                log.Msg("first start: " + name + " is in use (a plugin loaded it before MelonLoader's build), so it is set aside and deleted at the next start");
            }
            catch (Exception e) { log.Msg("first start: " + name + " is in use and could not be set aside (" + e.Message + ")"); }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Msg
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam, lParam;
            public uint time;
            public int x, y;
            public uint priv;
        }

        [DllImport("user32.dll")] private static extern bool PeekMessageW(out Msg msg, IntPtr hwnd, uint min, uint max, uint remove);
        [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Msg msg);
        [DllImport("user32.dll")] private static extern IntPtr DispatchMessageW(ref Msg msg);
        [DllImport("user32.dll")] private static extern uint MsgWaitForMultipleObjectsEx(uint count, IntPtr[] handles, uint ms, uint wake, uint flags);
        [DllImport("user32.dll")] private static extern void PostQuitMessage(int code);

        private const uint PM_REMOVE = 1, WM_QUIT = 0x12, WM_PAINT = 0x0F, QS_ALLINPUT = 0x1CFF, MWMO_INPUTAVAILABLE = 4, WAIT_OBJECT_0 = 0, WAIT_FAILED = 0xFFFFFFFF;

        /// <summary>The main thread's message loop (as VS's own loop runs it) until <paramref name="done"/> is set. VS's window
        /// paints the build note (FirstStartNote) instead of staying blank; a quit message is kept for VS's own loop.</summary>
        private static void Pump(ManualResetEvent done, Stopwatch since, out int messages, out long longest, out string longestWhy)
        {
            var handles = new[] { done.SafeWaitHandle.DangerousGetHandle() };
            var gap = Stopwatch.StartNew();
            var redraw = Stopwatch.StartNew();
            IntPtr window = FirstStartNote.Window();
            FirstStartNote.Redraw(window);
            // The launcher reads this line and leaves its own note out (LoaderWatch.GameShowsNote).
            if (window != IntPtr.Zero) log.Msg("first start: VS's window shows what is happening while MelonLoader builds");
            bool quit = false;
            int quitCode = 0;
            messages = 0;
            longest = 0;
            longestWhy = "";
            var each = new Stopwatch();
            int fullCollections = GC.CollectionCount(2);
            while (true)
            {
                uint r = MsgWaitForMultipleObjectsEx(1, handles, 100, QS_ALLINPUT, MWMO_INPUTAVAILABLE);
                if (r == WAIT_FAILED) Thread.Sleep(10);
                long slowest = 0;
                uint slowestMessage = 0;
                while (PeekMessageW(out var m, IntPtr.Zero, 0, 0, PM_REMOVE))
                {
                    messages++;
                    each.Restart();
                    if (m.message == WM_QUIT) { quit = true; quitCode = (int)m.wParam; continue; }
                    if (m.message == WM_PAINT && m.hwnd == window && window != IntPtr.Zero) FirstStartNote.Paint(window, since.Elapsed);
                    else
                    {
                        TranslateMessage(ref m);
                        DispatchMessageW(ref m);
                    }
                    if (each.ElapsedMilliseconds > slowest) { slowest = each.ElapsedMilliseconds; slowestMessage = m.message; }
                }
                if (redraw.ElapsedMilliseconds >= 1000) { redraw.Restart(); FirstStartNote.Redraw(window); }
                int collections = GC.CollectionCount(2);
                if (gap.ElapsedMilliseconds > longest)
                {
                    longest = gap.ElapsedMilliseconds;
                    longestWhy = " (at " + since.Elapsed.TotalSeconds.ToString("0") + " s; " + (collections - fullCollections) + " full collection(s) meanwhile; slowest message 0x"
                        + slowestMessage.ToString("X") + " " + slowest + " ms)";
                }
                fullCollections = collections;
                gap.Restart();
                if (r == WAIT_OBJECT_0 || done.WaitOne(0)) break;
            }
            FirstStartNote.Done(window);
            if (quit) PostQuitMessage(quitCode);
        }
    }
}
