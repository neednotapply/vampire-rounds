using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MelonLoader;

[assembly: MelonInfo(typeof(VampireRounds.InteropFix.Plugin), "Vampire Rounds interop fix", "0.3.0", "Vampire Rounds contributors")]
[assembly: MelonGame("poncle", "Vampire Survivors")]

namespace VampireRounds.InteropFix
{
    /// <summary>
    /// On Vampire Survivors's Unity 6 build, Il2CppInterop (MelonLoader 0.7.3) cannot locate il2cpp's
    /// Class::GetDefaultFieldValue and hooks the wrong code, and the game crashes in coreclr about two seconds after start
    /// (sheets/systems.json "interop_fix"). Before Il2CppInterop starts, this finds the function in the player's own
    /// GameAssembly.dll by the bytes of its start (the same on every copy of a build) and hands its address to
    /// Il2CppInterop's hook. If the bytes are not found exactly once (another game version), Il2CppInterop's own search
    /// stays in place. The byte pattern was first found for this game by the Isaac Survivors mashup on Melty.
    /// It also keeps VS's window answering while MelonLoader builds the game's assemblies on a first start (FirstStart).
    /// </summary>
    public class Plugin : MelonPlugin
    {
        // mov [rsp+8],rbx; push rdi; sub rsp,40h; mov rax,[rcx+10h]; mov rbx,rcx; mov rdi,rdx; sub rbx,[rax+??]; sar rbx,5
        private const string Pattern = "48 89 5C 24 08 57 48 83 EC 40 48 8B 41 10 48 8B D9 48 8B FA 48 2B 98 ?? ?? ?? ?? 48 C1 FB 05";
        private const string HookTypeName = "Il2CppInterop.Runtime.Injection.Hooks.Class_GetFieldDefaultValue_Hook";
        private static IntPtr found = IntPtr.Zero;
        private static bool searched;

        public override void OnPreInitialization()
        {
            FirstStart.Watch(HarmonyInstance, LoggerInstance);
            var hook = HookType();
            var find = hook != null ? AccessTools.Method(hook, "FindTargetMethod") : null;
            if (find == null) { LoggerInstance.Msg("Il2CppInterop's GetDefaultFieldValue hook not present; nothing to do."); return; }
            HarmonyInstance.Patch(find, prefix: new HarmonyMethod(typeof(Plugin).GetMethod(nameof(UseFound), BindingFlags.Static | BindingFlags.NonPublic)));
            LoggerInstance.Msg("Il2CppInterop's GetDefaultFieldValue lookup takes the address from GameAssembly.dll's code.");
        }

        /// <summary>
        /// Il2CppInterop's hook class, asked of Il2CppInterop.Runtime itself. (HarmonyX's TypeByName walks every type of
        /// every loaded assembly; before MelonLoader has built the game's assemblies on a first start, some of
        /// Il2CppInterop.Runtime's types cannot load without Il2Cppmscorlib, and it logged a ReflectionTypeLoadException
        /// warning that players' Melty reported as errors in Vampire Rounds's code.)
        /// </summary>
        private static Type HookType()
        {
            var runtime = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Il2CppInterop.Runtime");
            if (runtime == null)
            {
                try { runtime = Assembly.Load(new AssemblyName("Il2CppInterop.Runtime")); }
                catch { return null; }
            }
            try { return runtime.GetType(HookTypeName, false); }
            catch { return null; }
        }

        private static bool UseFound(ref IntPtr __result)
        {
            if (!searched) { searched = true; found = Search(); }
            if (found == IntPtr.Zero) { Melon<Plugin>.Logger.Warning("GetDefaultFieldValue not found exactly once; Il2CppInterop searches by itself."); return true; }
            Melon<Plugin>.Logger.Msg("GetDefaultFieldValue at 0x" + found.ToInt64().ToString("X"));
            __result = found;
            return false;
        }

        private static (byte[] bytes, bool[] any) Parse(string p)
        {
            var parts = p.Split(' ');
            var bytes = new byte[parts.Length];
            var any = new bool[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i] == "??") any[i] = true;
                else bytes[i] = Convert.ToByte(parts[i], 16);
            }
            return (bytes, any);
        }

        /// <summary>The pattern in GameAssembly.dll's executable sections, when it occurs exactly once.</summary>
        private static unsafe IntPtr Search()
        {
            var (pat, any) = Parse(Pattern);
            foreach (ProcessModule m in Process.GetCurrentProcess().Modules)
            {
                if (!string.Equals(m.ModuleName, "GameAssembly.dll", StringComparison.OrdinalIgnoreCase)) continue;
                byte* image = (byte*)m.BaseAddress;
                int pe = *(int*)(image + 0x3C);
                int sections = *(ushort*)(image + pe + 6);
                int optSize = *(ushort*)(image + pe + 20);
                byte* sec = image + pe + 24 + optSize;
                IntPtr hit = IntPtr.Zero;
                int hits = 0;
                for (int s = 0; s < sections; s++, sec += 40)
                {
                    uint characteristics = *(uint*)(sec + 36);
                    if ((characteristics & 0x20000000u) == 0) continue; // executable code only
                    byte* start = image + *(uint*)(sec + 12);
                    long size = *(uint*)(sec + 8);
                    for (long i = 0; i + pat.Length <= size; i++)
                    {
                        if (start[i] != pat[0]) continue;
                        int k = 1;
                        while (k < pat.Length && (any[k] || start[i + k] == pat[k])) k++;
                        if (k < pat.Length) continue;
                        if (++hits == 1) hit = (IntPtr)(start + i);
                    }
                }
                return hits == 1 ? hit : IntPtr.Zero;
            }
            return IntPtr.Zero;
        }
    }
}
