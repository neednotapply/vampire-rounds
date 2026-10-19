using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace VampireRounds
{
    /// <summary>
    /// The live link between Vampire Survivors and the hidden ROUNDS (sheets/link.json): one block of shared memory.
    /// VS writes the control part every frame (camera, each seat's input and position, the enemies, events such as
    /// damage); ROUNDS writes the result part every frame (health, block, bullet hits on VS enemies, card picks, enemy
    /// statuses) and its rendered layer (players, bullets, effects; ROUNDS UI) into one of three frame buffers.
    /// Counters only ever go up, so a reader never misses a press, a hit or an event. Offsets: Link.g.cs.
    /// </summary>
    public unsafe sealed partial class ArenaLink : IDisposable
    {
        public const string DefaultMapName = "Local\\VampireRoundsArena1";
        /// <summary>Developer runs can use a private link (VAMPIREROUNDS_DEV_ARENA) so they never meet a real session.</summary>
        public static readonly string MapName = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("VAMPIREROUNDS_DEV_ARENA")) ? DefaultMapName : Environment.GetEnvironmentVariable("VAMPIREROUNDS_DEV_ARENA");
        public const int FrameBytes = MaxW * MaxH * 4;
        public const long UiFramesOff = FramesOff + 3L * FrameBytes;
        public const long Size = FramesOff + 6L * FrameBytes;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateFileMapping(IntPtr file, IntPtr attr, uint protect, uint sizeHigh, uint sizeLow, string name);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr MapViewOfFile(IntPtr map, uint access, uint offHigh, uint offLow, UIntPtr bytes);
        [DllImport("kernel32.dll")] private static extern bool UnmapViewOfFile(IntPtr addr);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll")] private static extern UIntPtr VirtualQuery(IntPtr addr, out MemoryBasicInformation info, UIntPtr len);
        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryBasicInformation { public IntPtr BaseAddress, AllocationBase; public uint AllocationProtect; public UIntPtr RegionSize; public uint State, Protect, Type; }

        private IntPtr handle, view;
        public byte* Base { get; private set; }

        /// <summary>What the last Open did (for the callers' logs): the section's name and whether it was made or found.</summary>
        public static string LastOpenNote = "";

        public static ArenaLink Open()
        {
            var l = new ArenaLink();
            l.handle = CreateFileMapping(new IntPtr(-1), IntPtr.Zero, 0x04, (uint)(Size >> 32), (uint)(Size & 0xFFFFFFFF), MapName);
            int err = Marshal.GetLastWin32Error();
            if (l.handle == IntPtr.Zero) { LastOpenNote = MapName + ": CreateFileMapping failed (error " + err + ")"; return null; }
            bool existed = err == 183; // ERROR_ALREADY_EXISTS
            l.view = MapViewOfFile(l.handle, 0x0002 | 0x0004, 0, 0, UIntPtr.Zero);
            if (l.view == IntPtr.Zero) { LastOpenNote = MapName + ": MapViewOfFile failed (error " + Marshal.GetLastWin32Error() + ")"; CloseHandle(l.handle); return null; }
            // An existing section comes at its creator's size: one made by a build with a smaller layout is refused.
            MemoryBasicInformation info;
            long region = -1;
            if (VirtualQuery(l.view, out info, (UIntPtr)(ulong)Marshal.SizeOf(typeof(MemoryBasicInformation))) != UIntPtr.Zero) region = (long)(ulong)info.RegionSize;
            if (region >= 0 && region < Size)
            {
                LastOpenNote = MapName + " exists with a smaller layout (" + (region >> 20) + " MB, this build needs " + (Size >> 20) + " MB): refused";
                UnmapViewOfFile(l.view);
                CloseHandle(l.handle);
                return null;
            }
            LastOpenNote = MapName + (existed ? " found" : " made") + " (" + (Size >> 20) + " MB)";
            l.Base = (byte*)l.view.ToPointer();
            return l;
        }

        public void Dispose()
        {
            if (view != IntPtr.Zero) UnmapViewOfFile(view);
            if (handle != IntPtr.Zero) CloseHandle(handle);
            view = handle = IntPtr.Zero;
            Base = null;
        }

        public int I(int off) => *(int*)(Base + off);
        public float F(int off) => *(float*)(Base + off);
        public void I(int off, int v) => *(int*)(Base + off) = v;
        public void F(int off, float v) => *(float*)(Base + off) = v;
        public void Inc(int off) { Thread.MemoryBarrier(); *(int*)(Base + off) += 1; }
        public IntPtr Frame(int i) => new IntPtr(Base + FramesOff + (long)i * FrameBytes);
        public IntPtr UiFrame(int i) => new IntPtr(Base + UiFramesOff + (long)i * FrameBytes);

        public string Str(int off, int max)
        {
            var b = new char[max];
            int n = 0;
            while (n < max && Base[off + n] != 0) { b[n] = (char)Base[off + n]; n++; }
            return new string(b, 0, n);
        }

        public void Str(int off, int max, string s)
        {
            int n = Math.Min(max - 1, s == null ? 0 : s.Length);
            for (int i = 0; i < n; i++) Base[off + i] = (byte)(s[i] < 128 ? s[i] : '?');
            Base[off + n] = 0;
        }

        public static int Slot(int seat) => C_Slots + seat * SlotSize;
        public static int Slot2(int seat) => C_Slot2 + seat * Slot2Size;
        public static int RSlot(int seat) => ResultOff + R_Slots + seat * SlotSize;
        public static int Target(int row) => C_Targets + row * TargetSize;

        /// <summary>VS: queue an event for ROUNDS (ROUNDS reads every frame).</summary>
        public void PostEvent(int type, int seat, float a = 0f, float b = 0f, float c = 0f, float d = 0f, int life = 0)
        {
            int n = I(C_EventWrite);
            int e = C_Events + (n % EventRing) * EventSize;
            I(e + E_Serial, n + 1);
            I(e + E_Type, type);
            I(e + E_Slot, seat);
            F(e + E_A, a); F(e + E_B, b); F(e + E_C, c); F(e + E_D, d);
            I(e + E_Life, life);
            Inc(C_EventWrite);
        }

        /// <summary>ROUNDS: record a hit on a VS enemy (VS reads every frame). rgba 0 = no tint.</summary>
        public void PostHit(int target, float damage, float x, float y, int seat, int kind = HitBullet, int rgba = 0)
        {
            int n = I(ResultOff + R_HitWrite);
            int h = ResultOff + R_Hits + (n % HitRing) * HitSize;
            // The serial goes last (0 while the entry is written): a reader that sees serial == its index + 1 before and after
            // copying the fields has the whole entry, never a half-written or lapped one.
            I(h + H_Serial, 0);
            I(h + H_Target, target);
            F(h + H_Damage, damage);
            F(h + H_X, x); F(h + H_Y, y);
            I(h + H_Slot, seat);
            I(h + H_Kind, kind);
            I(h + H_Color, rgba);
            Thread.MemoryBarrier();
            I(h + H_Serial, n + 1);
            Inc(ResultOff + R_HitWrite);
        }

        /// <summary>ROUNDS: a one-shot status event for a VS enemy (VS reads every frame).</summary>
        public void PostFx(int target, int type, float a, float b = 0f, float c = 0f, float d = 0f, int seat = -1)
        {
            int n = I(ResultOff + R_FxWrite);
            int e = ResultOff + R_Fx + (n % FxRing) * FxSize;
            I(e + FX_Serial, 0); // serial last, as in PostHit
            I(e + FX_Target, target);
            I(e + FX_Type, type);
            F(e + FX_A, a); F(e + FX_B, b); F(e + FX_C, c); F(e + FX_D, d);
            I(e + FX_Slot, seat);
            Thread.MemoryBarrier();
            I(e + FX_Serial, n + 1);
            Inc(ResultOff + R_FxWrite);
        }

        /// <summary>A colour as one int, r&lt;&lt;24 | g&lt;&lt;16 | b&lt;&lt;8 | a (0 = none).</summary>
        public static int Rgba(float r, float g, float b, float a)
        {
            if (a <= 0f || (r <= 0f && g <= 0f && b <= 0f)) return 0;
            int R8 = (int)(Math.Max(0f, Math.Min(1f, r)) * 255f), G8 = (int)(Math.Max(0f, Math.Min(1f, g)) * 255f), B8 = (int)(Math.Max(0f, Math.Min(1f, b)) * 255f), A8 = (int)(Math.Max(0f, Math.Min(1f, a)) * 255f);
            return (R8 << 24) | (G8 << 16) | (B8 << 8) | A8;
        }

        public static void FromRgba(int v, out float r, out float g, out float b, out float a)
        {
            r = ((v >> 24) & 0xFF) / 255f; g = ((v >> 16) & 0xFF) / 255f; b = ((v >> 8) & 0xFF) / 255f; a = (v & 0xFF) / 255f;
        }
    }

    public static class Paths
    {
        /// <summary>%LOCALAPPDATA%\VampireRounds: the ROUNDS profile (BepInEx), logs and the character portraits.</summary>
        public static string DataDir()
        {
            string dev = Environment.GetEnvironmentVariable("VAMPIREROUNDS_DEV_DATA");
            if (!string.IsNullOrEmpty(dev)) return dev;
            string local = Environment.GetEnvironmentVariable("LOCALAPPDATA");
            if (string.IsNullOrEmpty(local)) local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return System.IO.Path.Combine(local, "VampireRounds");
        }

        public const string EngineFlag = "--vampirerounds-engine";
    }
}
