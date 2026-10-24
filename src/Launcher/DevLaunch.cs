using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace VampireRounds.Launcher
{
    /// <summary>
    /// Developer runs only (Melty test runs included, which start the launcher the way players do): while
    /// %LOCALAPPDATA%\VampireRounds\dev-launch.txt exists, each start of VS takes (and removes) its first line.
    /// "regenerate" makes MelonLoader build VS's assemblies again, as on a player's first start; VAMPIREROUNDS_NAME=value
    /// sets one of the VS side's developer switches for that start. Without the file nothing changes.
    /// </summary>
    public static class DevLaunch
    {
        public static string FilePath => Path.Combine(Paths.Data, "dev-launch.txt");

        public static void Next(out string args, out Dictionary<string, string> env)
        {
            args = "";
            env = new Dictionary<string, string>();
            if (!File.Exists(FilePath)) return;
            try
            {
                var lines = File.ReadAllLines(FilePath).ToList();
                string line = lines.Count > 0 ? lines[0].Trim() : "";
                if (lines.Count > 0) lines.RemoveAt(0);
                if (lines.Count == 0) File.Delete(FilePath);
                else File.WriteAllLines(FilePath, lines);
                foreach (string t in line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    int eq = t.IndexOf('=');
                    if (t == "regenerate") args += " --melonloader.agfregenerate";
                    else if (eq > 0 && t.StartsWith("VAMPIREROUNDS_")) env[t.Substring(0, eq)] = t.Substring(eq + 1);
                }
                Log.Write("Developer switches for this start: " + (line.Length > 0 ? line : "none"));
            }
            catch (Exception e) { Log.Write("Developer switches not read: " + e.Message); }
        }
    }
}
