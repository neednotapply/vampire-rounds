using System;
using MelonLoader;

[assembly: MelonInfo(typeof(VampireRounds.Vs.Mod), "Vampire Rounds", "0.4.1", "Vampire Rounds contributors")]
[assembly: MelonGame("poncle", "Vampire Survivors")]

namespace VampireRounds.Vs
{
    /// <summary>
    /// Vampire Rounds inside Vampire Survivors (sheets/systems.json "vs_mod"): the Vampire Rounds adventure with ROUNDS's
    /// Orange and Blue, played by the player's own ROUNDS running hidden next to VS (EngineRunner). Harmony patches in
    /// this assembly (the hooks sheet) are applied by MelonLoader at start.
    /// </summary>
    public class Mod : MelonMod
    {
        public static Mod Instance { get; private set; }

        public static void Log(string msg) => Instance?.LoggerInstance.Msg(msg);
        public static void Warn(string msg) => Instance?.LoggerInstance.Warning(msg);

        public override void OnInitializeMelon()
        {
            Instance = this;
            Log("Vampire Rounds " + Info.Version + ": data in " + Paths.DataDir());
            TestMode.Init();
            Link.Open();
            EngineRunner.StartAsync();
        }

        public override void OnUpdate()
        {
            try { Session.Tick(); }
            catch (Exception e) { Once("session", e); }
            try { Boot.Tick(); }
            catch (Exception e) { Once("boot", e); }
            try { TestMode.Tick(); }
            catch (Exception e) { Once("test", e); }
            try { AutoPlay.Tick(); }
            catch (Exception e) { Once("autoplay", e); }
            try { DevConsole.Tick(); }
            catch (Exception e) { Once("dev", e); }
        }

        public override void OnLateUpdate()
        {
            try { FrameView.Tick(); }
            catch (Exception e) { Once("frame", e); }
            try { CardSlots.Tick(); }
            catch (Exception e) { Once("card slots", e); }
        }

        public override void OnGUI()
        {
            try { FrameView.Gui(); Notice.Draw(); Boot.Gui(); }
            catch (Exception e) { Once("gui", e); }
        }

        public override void OnApplicationQuit()
        {
            Link.Goodbye();
            EngineRunner.Stop();
        }

        private static readonly System.Collections.Generic.HashSet<string> seen = new System.Collections.Generic.HashSet<string>();

        /// <summary>A failure in a per-frame step is logged once per message; the next frame goes on.</summary>
        public static void Once(string what, Exception e)
        {
            if (seen.Add(what + ": " + e.Message)) Warn(what + " failed: " + e);
        }
    }
}
