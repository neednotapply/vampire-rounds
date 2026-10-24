using System;
using System.Runtime.InteropServices;

namespace VampireRounds.InteropFix
{
    /// <summary>
    /// What VS's window shows while MelonLoader builds (VS draws nothing yet; the window is blank white): what is
    /// happening, that it happens once, how long it takes, to leave the game open, and the time so far. Painted with GDI
    /// on the main thread from the build's message loop only; VS's first frame replaces it.
    /// </summary>
    internal static class FirstStartNote
    {
        private const string Title = "Getting Vampire Survivors ready for mods";
        private const string Body = "This happens on the first start, and again after a Vampire Survivors update: the mod loader "
            + "prepares the game, which takes 1 to 3 minutes. Vampire Rounds opens by itself when it's done, so leave the game open.";

        [StructLayout(LayoutKind.Sequential)] private struct Rect { public int left, top, right, bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct PaintStruct
        {
            public IntPtr hdc;
            public int erase;
            public Rect paint;
            public int restore, incUpdate;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] reserved;
        }

        private delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] private static extern bool EnumThreadWindows(uint thread, EnumProc f, IntPtr l);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(IntPtr h, System.Text.StringBuilder s, int n);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] private static extern bool InvalidateRect(IntPtr h, IntPtr rect, bool erase);
        [DllImport("user32.dll")] private static extern IntPtr BeginPaint(IntPtr h, out PaintStruct ps);
        [DllImport("user32.dll")] private static extern bool EndPaint(IntPtr h, ref PaintStruct ps);
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr h, out Rect r);
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("user32.dll")] private static extern int FillRect(IntPtr dc, ref Rect r, IntPtr brush);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int DrawTextW(IntPtr dc, string s, int n, ref Rect r, uint format);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateSolidBrush(uint color);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr o);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
        [DllImport("gdi32.dll")] private static extern int SetBkMode(IntPtr dc, int mode);
        [DllImport("gdi32.dll")] private static extern uint SetTextColor(IntPtr dc, uint color);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateFontW(int height, int width, int escapement, int orientation, int weight, uint italic, uint underline,
            uint strikeOut, uint charSet, uint outPrecision, uint clipPrecision, uint quality, uint pitchAndFamily, string face);

        // COLORREF is 0x00BBGGRR: the launcher's night, orange and ink.
        private const uint Night = 0x00120E16, Orange = 0x001A8CFF, Ink = 0x00D6E8F0;
        private const uint DT_CENTER = 0x1, DT_WORDBREAK = 0x10, DT_NOPREFIX = 0x800;

        /// <summary>VS's window: the visible UnityWndClass window of this (the main) thread.</summary>
        public static IntPtr Window()
        {
            IntPtr found = IntPtr.Zero;
            try
            {
                EnumThreadWindows(GetCurrentThreadId(), (h, l) =>
                {
                    var c = new System.Text.StringBuilder(64);
                    GetClassNameW(h, c, c.Capacity);
                    if (c.ToString() != "UnityWndClass" || !IsWindowVisible(h)) return true;
                    found = h;
                    return false;
                }, IntPtr.Zero);
            }
            catch { }
            return found;
        }

        /// <summary>Asks for a new picture without erasing first (no white flash).</summary>
        public static void Redraw(IntPtr window)
        {
            if (window != IntPtr.Zero) InvalidateRect(window, IntPtr.Zero, false);
        }

        public static void Paint(IntPtr window, TimeSpan since)
        {
            IntPtr dc = BeginPaint(window, out var ps);
            try
            {
                if (dc != IntPtr.Zero && GetClientRect(window, out var area)) Draw(dc, area.right - area.left, area.bottom - area.top, Clock(since));
            }
            finally { EndPaint(window, ref ps); }
        }

        /// <summary>When the build is done: MelonLoader still loads the mods (a few seconds, VS's window not drawn), so the
        /// note says so instead of a stopped clock. Drawn straight away (the build's message loop has ended).</summary>
        public static void Done(IntPtr window)
        {
            if (window == IntPtr.Zero) return;
            IntPtr dc = GetDC(window);
            if (dc == IntPtr.Zero) return;
            try
            {
                if (GetClientRect(window, out var area)) Draw(dc, area.right - area.left, area.bottom - area.top, "Done: starting Vampire Rounds...");
            }
            finally { ReleaseDC(window, dc); }
        }

        private static string Clock(TimeSpan since) => (int)since.TotalMinutes + ":" + since.Seconds.ToString("00");

        /// <summary>The note on a w x h device context, with <paramref name="status"/> (the time so far) under it.</summary>
        public static void Draw(IntPtr dc, int w, int h, string status)
        {
            if (w <= 0 || h <= 0) return;
            var area = new Rect { right = w, bottom = h };
            IntPtr night = CreateSolidBrush(Night);
            FillRect(dc, ref area, night);
            DeleteObject(night);
            SetBkMode(dc, 1);
            IntPtr big = CreateFontW(-Math.Max(18, h / 16), 0, 0, 0, 700, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
            IntPtr small = CreateFontW(-Math.Max(13, h / 34), 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
            IntPtr old = SelectObject(dc, big);
            SetTextColor(dc, Orange);
            var title = new Rect { left = w / 12, right = w - w / 12, top = h * 30 / 100, bottom = h * 44 / 100 };
            DrawTextW(dc, Title, -1, ref title, DT_CENTER | DT_WORDBREAK | DT_NOPREFIX);
            SelectObject(dc, small);
            SetTextColor(dc, Ink);
            var body = new Rect { left = w / 8, right = w - w / 8, top = h * 47 / 100, bottom = h * 80 / 100 };
            DrawTextW(dc, Body + "\n\n" + status, -1, ref body, DT_CENTER | DT_WORDBREAK | DT_NOPREFIX);
            SelectObject(dc, old);
            DeleteObject(big);
            DeleteObject(small);
        }
    }
}
