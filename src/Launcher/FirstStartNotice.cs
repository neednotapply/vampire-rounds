using System;
using System.Drawing;
using System.Windows.Forms;

namespace VampireRounds.Launcher
{
    /// <summary>
    /// While MelonLoader builds VS's assemblies on a first start (LoaderWatch), a small note in front of the game: what is
    /// happening, that it happens once, how long it takes, and to leave the game open. Only when VS's own window does not
    /// show that note (the interop fix's FirstStartNote). It takes no focus and goes away when the build ends. Melty test
    /// runs never show it (they film the game's window).
    /// </summary>
    public class FirstStartNotice : Form
    {
        private readonly Label step = new Label();
        private readonly Timer tick = new Timer { Interval = 500 };
        private readonly DateTime from = DateTime.Now;
        private string stepText = "Starting...";

        public FirstStartNotice()
        {
            Text = "Vampire Rounds";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = MainForm.Panel;
            ForeColor = MainForm.Ink;
            Font = new Font("Segoe UI", 10f);
            Size = new Size(620, 200);
            var screen = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(screen.Left + (screen.Width - Width) / 2, screen.Top + (screen.Height - Height) / 2);

            var bar = new Panel { Dock = DockStyle.Top, Height = 6, BackColor = MainForm.Crimson };
            var title = new Label { Text = "Getting Vampire Survivors ready for mods", Font = new Font("Segoe UI Semibold", 14f), ForeColor = MainForm.Orange, AutoSize = true, Location = new Point(20, 20) };
            var text = new Label
            {
                Text = "This happens on the first start, and again after a Vampire Survivors update: the mod loader prepares the game, "
                    + "which takes 1 to 3 minutes, and the game's window may look stuck meanwhile. Vampire Rounds opens by itself when "
                    + "it's done, so leave the game open.",
                Location = new Point(22, 58),
                Size = new Size(576, 66),
            };
            step.Location = new Point(22, 130);
            step.Size = new Size(576, 22);
            step.ForeColor = Color.FromArgb(200, 190, 170);
            var progress = new ProgressBar { Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 30, Location = new Point(22, 160), Size = new Size(576, 14) };
            Controls.AddRange(new Control[] { bar, title, text, step, progress });
            tick.Tick += (s, e) => ShowStep(stepText);
            tick.Start();
            ShowStep(stepText);
        }

        public void SetStep(string s) => ShowStep(stepText = s);

        private void ShowStep(string s)
        {
            var t = DateTime.Now - from;
            step.Text = s + "   " + (int)t.TotalMinutes + ":" + t.Seconds.ToString("00");
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x08000000 | 0x80 | 0x8; // no activate, tool window, topmost
                return cp;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) tick.Dispose();
            base.Dispose(disposing);
        }
    }
}
