// Bouncing Circle experiment app.
// Targets .NET Framework 4.x, which ships with Windows 10/11, so the compiled
// .exe runs without installing anything.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace BouncingCircle
{
    // ------------------------------------------------------------------ settings

    class ExperimentSettings
    {
        // Allowed ranges. Adjust here if the protocol changes.
        public const int SpeedMin = 1, SpeedMax = 20000;
        public const int DiameterMin = 2, DiameterMax = 2000;
        public const int BackgroundMin = 0, BackgroundMax = 255;
        public const int CircleMin = 0, CircleMax = 255;

        public string MonitorMode = "1";   // "1", "2" or "Random"
        public int Monitor1Display = 0;    // index into DisplayInfo.All()
        public int Monitor2Display = 1;
        public int SpeedPxPerSec = 500;
        public int DiameterPx = 100;
        public int BackgroundGray = 255;   // white
        public int CircleGray = 0;         // black
        public string Subject = "";

        public ExperimentSettings Clone() { return (ExperimentSettings)MemberwiseClone(); }

        static string FilePath { get { return Path.Combine(AppPaths.DataDir, "last_settings.ini"); } }

        public void Save()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("MonitorMode=" + MonitorMode);
                sb.AppendLine("Monitor1Display=" + Monitor1Display);
                sb.AppendLine("Monitor2Display=" + Monitor2Display);
                sb.AppendLine("SpeedPxPerSec=" + SpeedPxPerSec);
                sb.AppendLine("DiameterPx=" + DiameterPx);
                sb.AppendLine("BackgroundGray=" + BackgroundGray);
                sb.AppendLine("CircleGray=" + CircleGray);
                sb.AppendLine("Subject=" + Subject);
                File.WriteAllText(FilePath, sb.ToString(), Encoding.UTF8);
            }
            catch { /* non-critical */ }
        }

        public static ExperimentSettings Load()
        {
            var s = new ExperimentSettings();
            try
            {
                if (!File.Exists(FilePath)) return s;
                foreach (var line in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = line.Substring(0, eq), v = line.Substring(eq + 1);
                    int n;
                    bool isNum = int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n);
                    switch (k)
                    {
                        case "MonitorMode": if (v == "1" || v == "2" || v == "Random") s.MonitorMode = v; break;
                        case "Monitor1Display": if (isNum) s.Monitor1Display = n; break;
                        case "Monitor2Display": if (isNum) s.Monitor2Display = n; break;
                        case "SpeedPxPerSec": if (isNum) s.SpeedPxPerSec = Clamp(n, SpeedMin, SpeedMax); break;
                        case "DiameterPx": if (isNum) s.DiameterPx = Clamp(n, DiameterMin, DiameterMax); break;
                        case "BackgroundGray": if (isNum) s.BackgroundGray = Clamp(n, BackgroundMin, BackgroundMax); break;
                        case "CircleGray": if (isNum) s.CircleGray = Clamp(n, CircleMin, CircleMax); break;
                        case "Subject": s.Subject = v; break;
                    }
                }
            }
            catch { }
            return s;
        }

        public static int Clamp(int v, int lo, int hi) { return v < lo ? lo : v > hi ? hi : v; }
    }

    static class AppPaths
    {
        static string dataDir;

        // Next to the .exe if writable, otherwise Documents\BouncingCircle.
        public static string DataDir
        {
            get
            {
                if (dataDir != null) return dataDir;
                string exeDir = AppDomain.CurrentDomain.BaseDirectory;
                try
                {
                    string probe = Path.Combine(exeDir, ".write_test");
                    File.WriteAllText(probe, "");
                    File.Delete(probe);
                    dataDir = exeDir;
                }
                catch
                {
                    dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "BouncingCircle");
                    Directory.CreateDirectory(dataDir);
                }
                return dataDir;
            }
        }

        public static string LogFile { get { return Path.Combine(DataDir, "experiments_log.csv"); } }
    }

    // ------------------------------------------------------------------ displays

    static class DisplayInfo
    {
        // Screens ordered left-to-right, top-to-bottom so numbering is stable.
        public static Screen[] All()
        {
            return Screen.AllScreens.OrderBy(s => s.Bounds.X).ThenBy(s => s.Bounds.Y).ToArray();
        }

        public static string Describe(Screen s, int index)
        {
            return string.Format("Display {0}: {1}x{2} at ({3},{4}){5}",
                index + 1, s.Bounds.Width, s.Bounds.Height, s.Bounds.X, s.Bounds.Y,
                s.Primary ? " [primary]" : "");
        }

        // Default: the two external (non-primary) displays if there are 3+,
        // otherwise the first two displays.
        public static void DefaultAssignment(out int m1, out int m2)
        {
            var all = All();
            var ext = Enumerable.Range(0, all.Length).Where(i => !all[i].Primary).ToList();
            if (all.Length >= 3 && ext.Count >= 2) { m1 = ext[0]; m2 = ext[1]; }
            else { m1 = 0; m2 = all.Length > 1 ? 1 : 0; }
        }
    }

    // ------------------------------------------------------------------ CSV log

    class ExperimentRecord
    {
        public int Id;
        public DateTime Start, End;
        public double DurationSec;
        public ExperimentSettings Settings;
        public int MonitorUsed;          // 1 or 2
        public string DisplayDesc;
        public double StartAngleDeg;
        public string Result = "";       // "Success" / "Fail"
        public string Notes = "";
    }

    static class CsvLog
    {
        static readonly string[] Header = {
            "experiment_id", "start_time", "end_time", "duration_s",
            "monitor_mode", "monitor_used", "display",
            "speed_px_per_s", "circle_diameter_px", "background_gray", "circle_gray",
            "start_angle_deg", "subject", "result", "notes"
        };

        public static int NextId()
        {
            int max = 0;
            try
            {
                if (!File.Exists(AppPaths.LogFile)) return 1;
                foreach (var line in File.ReadAllLines(AppPaths.LogFile, Encoding.UTF8).Skip(1))
                {
                    int comma = line.IndexOf(',');
                    int n;
                    if (comma > 0 && int.TryParse(line.Substring(0, comma), out n) && n > max) max = n;
                }
            }
            catch { }
            return max + 1;
        }

        public static void Append(ExperimentRecord r)
        {
            var inv = CultureInfo.InvariantCulture;
            var s = r.Settings;
            string[] fields = {
                r.Id.ToString(inv),
                r.Start.ToString("yyyy-MM-dd HH:mm:ss.fff", inv),
                r.End.ToString("yyyy-MM-dd HH:mm:ss.fff", inv),
                r.DurationSec.ToString("0.000", inv),
                s.MonitorMode,
                r.MonitorUsed.ToString(inv),
                r.DisplayDesc,
                s.SpeedPxPerSec.ToString(inv),
                s.DiameterPx.ToString(inv),
                s.BackgroundGray.ToString(inv),
                s.CircleGray.ToString(inv),
                r.StartAngleDeg.ToString("0.0", inv),
                s.Subject,
                r.Result,
                r.Notes
            };

            bool newFile = !File.Exists(AppPaths.LogFile);
            var sb = new StringBuilder();
            if (newFile) sb.AppendLine(string.Join(",", Header));
            sb.AppendLine(string.Join(",", fields.Select(Escape)));

            // Retry in case the file is open in Excel (which locks it).
            while (true)
            {
                try
                {
                    File.AppendAllText(AppPaths.LogFile, sb.ToString(), new UTF8Encoding(newFile));
                    return;
                }
                catch (IOException ex)
                {
                    var ans = MessageBox.Show(
                        "Could not write to the log file:\n" + AppPaths.LogFile + "\n\n" + ex.Message +
                        "\n\nIf it is open in Excel, close it and press Retry.",
                        "Log write failed", MessageBoxButtons.RetryCancel, MessageBoxIcon.Warning);
                    if (ans != DialogResult.Retry) return;
                }
            }
        }

        static string Escape(string v)
        {
            if (v == null) return "";
            v = v.Replace("\r", " ").Replace("\n", " ");
            if (v.IndexOfAny(new[] { ',', '"', ';' }) >= 0) return "\"" + v.Replace("\"", "\"\"") + "\"";
            return v;
        }
    }

    // ------------------------------------------------------------------ native

    static class Native
    {
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
        [DllImport("dwmapi.dll")] public static extern int DwmFlush();
        [DllImport("winmm.dll")] public static extern uint timeBeginPeriod(uint ms);
        [DllImport("winmm.dll")] public static extern uint timeEndPeriod(uint ms);

        [StructLayout(LayoutKind.Sequential)]
        public struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public Point pt; }
        [DllImport("user32.dll")] public static extern bool PeekMessage(out MSG msg, IntPtr hWnd, uint min, uint max, uint remove);

        public static bool AppIsIdle() { MSG m; return !PeekMessage(out m, IntPtr.Zero, 0, 0, 0); }

        // Use physical pixels on every monitor so "pixels per second" is exact.
        public static void EnableDpiAwareness()
        {
            try { if (SetProcessDpiAwarenessContext(new IntPtr(-4))) return; } catch { } // PER_MONITOR_AWARE_V2
            try { SetProcessDPIAware(); } catch { }
        }
    }

    // ------------------------------------------------------------------ experiment windows

    // Full-screen window filled with the background color; optionally draws the circle.
    class StimulusForm : Form
    {
        readonly bool hasCircle;
        readonly Color bg;
        readonly SolidBrush circleBrush;
        readonly float diameter;
        readonly double speed;
        public event Action StopRequested;

        double x, y, vx, vy;              // top-left of circle and velocity (px/s)
        Stopwatch clock;
        double lastT;
        RectangleF lastDrawn;
        bool running;

        public double StartAngleDeg { get; private set; }

        public StimulusForm(Screen screen, ExperimentSettings s, bool hasCircle, Random rng)
        {
            this.hasCircle = hasCircle;
            bg = Color.FromArgb(s.BackgroundGray, s.BackgroundGray, s.BackgroundGray);
            circleBrush = new SolidBrush(Color.FromArgb(s.CircleGray, s.CircleGray, s.CircleGray));

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = screen.Bounds;
            BackColor = bg;
            TopMost = true;
            ShowInTaskbar = false;
            KeyPreview = true;
            Text = "Bouncing Circle";
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);

            int w = screen.Bounds.Width, h = screen.Bounds.Height;
            diameter = Math.Min(s.DiameterPx, Math.Min(w, h));
            speed = s.SpeedPxPerSec;

            // Start in the centre with a random diagonal-ish direction
            // (avoids near-horizontal/vertical paths that look like a ping-pong).
            x = (w - diameter) / 2.0;
            y = (h - diameter) / 2.0;
            double angle = 20 + rng.NextDouble() * 50 + 90 * rng.Next(4);
            StartAngleDeg = angle;
            double rad = angle * Math.PI / 180.0;
            vx = Math.Cos(rad) * speed;
            vy = Math.Sin(rad) * speed;

            KeyDown += (o, e) =>
            {
                if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter)
                    if (StopRequested != null) StopRequested();
            };
        }

        public void StartAnimation()
        {
            if (!hasCircle) return;
            clock = Stopwatch.StartNew();
            lastT = 0;
            running = true;
            Application.Idle += OnIdle;
        }

        public void StopAnimation()
        {
            if (!running) return;
            running = false;
            Application.Idle -= OnIdle;
        }

        // Render loop: advance by real elapsed time, repaint, then wait for the
        // compositor's next vblank (DwmFlush) for smooth, tear-free motion.
        void OnIdle(object sender, EventArgs e)
        {
            while (running && Native.AppIsIdle())
            {
                double t = clock.Elapsed.TotalSeconds;
                Step(t - lastT);
                lastT = t;

                var now = CircleRect();
                var dirty = RectangleF.Union(lastDrawn, now);
                dirty.Inflate(2, 2);
                Invalidate(Rectangle.Round(dirty));
                Update();

                if (Native.DwmFlush() != 0) System.Threading.Thread.Sleep(1);
            }
        }

        void Step(double dt)
        {
            double maxX = ClientSize.Width - diameter, maxY = ClientSize.Height - diameter;
            x += vx * dt;
            y += vy * dt;
            // Reflect off the edges (loop handles very large dt safely).
            for (int i = 0; i < 8 && (x < 0 || x > maxX || y < 0 || y > maxY); i++)
            {
                if (x < 0) { x = -x; vx = Math.Abs(vx); }
                if (x > maxX) { x = 2 * maxX - x; vx = -Math.Abs(vx); }
                if (y < 0) { y = -y; vy = Math.Abs(vy); }
                if (y > maxY) { y = 2 * maxY - y; vy = -Math.Abs(vy); }
            }
            x = Math.Max(0, Math.Min(maxX, x));
            y = Math.Max(0, Math.Min(maxY, y));
        }

        RectangleF CircleRect() { return new RectangleF((float)x, (float)y, diameter, diameter); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            using (var b = new SolidBrush(bg)) g.FillRectangle(b, e.ClipRectangle);
            if (!hasCircle) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = CircleRect();
            g.FillEllipse(circleBrush, r);
            lastDrawn = r;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            StopAnimation();
            circleBrush.Dispose();
            base.OnFormClosed(e);
        }
    }

    // Runs one experiment: covers both experiment monitors, animates the circle
    // on the chosen one, and returns timing info when the operator presses Esc.
    static class ExperimentRunner
    {
        static readonly Random rng = new Random();

        public static ExperimentRecord Run(ExperimentSettings s, int id)
        {
            var screens = DisplayInfo.All();
            int d1 = ExperimentSettings.Clamp(s.Monitor1Display, 0, screens.Length - 1);
            int d2 = ExperimentSettings.Clamp(s.Monitor2Display, 0, screens.Length - 1);

            int used = s.MonitorMode == "2" ? 2 : s.MonitorMode == "Random" ? rng.Next(1, 3) : 1;
            int targetIdx = used == 1 ? d1 : d2;
            int otherIdx = used == 1 ? d2 : d1;

            var target = new StimulusForm(screens[targetIdx], s, true, rng);
            StimulusForm other = otherIdx != targetIdx ? new StimulusForm(screens[otherIdx], s, false, rng) : null;

            var rec = new ExperimentRecord
            {
                Id = id,
                Settings = s.Clone(),
                MonitorUsed = used,
                DisplayDesc = DisplayInfo.Describe(screens[targetIdx], targetIdx),
                StartAngleDeg = target.StartAngleDeg
            };

            Stopwatch sw = null;
            Action stop = () =>
            {
                if (sw == null || !sw.IsRunning) return;
                sw.Stop();
                rec.End = DateTime.Now;
                target.StopAnimation();
                if (other != null) other.Close();
                target.Close();
            };
            target.StopRequested += stop;
            if (other != null) other.StopRequested += stop;

            target.Shown += (o, e) =>
            {
                if (other != null) other.Show();
                target.Activate();
                Cursor.Hide();
                rec.Start = DateTime.Now;
                sw = Stopwatch.StartNew();
                target.StartAnimation();
            };

            Native.timeBeginPeriod(1);
            try { target.ShowDialog(); }
            finally
            {
                Native.timeEndPeriod(1);
                Cursor.Show();
                if (other != null && !other.IsDisposed) other.Close();
            }

            if (sw == null) return null;
            if (sw.IsRunning) { sw.Stop(); rec.End = DateTime.Now; }
            rec.DurationSec = sw.Elapsed.TotalSeconds;
            return rec;
        }
    }

    // ------------------------------------------------------------------ result dialog

    class ResultForm : Form
    {
        readonly TextBox notes = new TextBox();
        readonly CheckBox relaunch = new CheckBox();
        public string Result { get; private set; }
        public string Notes { get { return notes.Text.Trim(); } }
        public bool RelaunchNext { get { return relaunch.Checked; } }

        public ResultForm(ExperimentRecord r)
        {
            Text = "Experiment #" + r.Id + " — record result";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            ControlBox = false;   // a result must be chosen
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 10f);
            TopMost = true;
            Padding = new Padding(12);
            AutoSize = true;
            FormClosing += (o, e) => { if (Result == null) e.Cancel = true; };
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            var layout = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Dock = DockStyle.Fill };

            var summary = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(460, 0),
                Text = string.Format(CultureInfo.InvariantCulture,
                    "Duration: {0:0.000} s\nMonitor: {1} (mode: {2})\n{3}\nSpeed: {4} px/s   Diameter: {5} px\nBackground: {6}   Circle: {7}",
                    r.DurationSec, r.MonitorUsed, r.Settings.MonitorMode, r.DisplayDesc,
                    r.Settings.SpeedPxPerSec, r.Settings.DiameterPx, r.Settings.BackgroundGray, r.Settings.CircleGray)
            };
            layout.Controls.Add(summary);

            layout.Controls.Add(new Label { Text = "Notes (optional):", AutoSize = true, Margin = new Padding(0, 12, 0, 2) });
            notes.Width = 460;
            layout.Controls.Add(notes);

            relaunch.Text = "Relaunch immediately with the same settings";
            relaunch.AutoSize = true;
            relaunch.Margin = new Padding(0, 10, 0, 0);
            layout.Controls.Add(relaunch);

            var buttons = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 14, 0, 0) };
            var ok = new Button { Text = "✔  Success", Width = 150, Height = 44, BackColor = Color.FromArgb(200, 240, 200) };
            var fail = new Button { Text = "✘  Fail", Width = 150, Height = 44, BackColor = Color.FromArgb(245, 205, 205) };
            ok.Click += (o, e) => { Result = "Success"; DialogResult = DialogResult.OK; };
            fail.Click += (o, e) => { Result = "Fail"; DialogResult = DialogResult.OK; };
            buttons.Controls.Add(ok);
            buttons.Controls.Add(fail);
            layout.Controls.Add(buttons);

            Controls.Add(layout);
        }
    }

    // ------------------------------------------------------------------ main window

    class MainForm : Form
    {
        readonly RadioButton rbMon1 = new RadioButton { Text = "Monitor 1", AutoSize = true };
        readonly RadioButton rbMon2 = new RadioButton { Text = "Monitor 2", AutoSize = true };
        readonly RadioButton rbRandom = new RadioButton { Text = "Random", AutoSize = true };
        readonly ComboBox cbDisp1 = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 330 };
        readonly ComboBox cbDisp2 = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 330 };
        readonly NumericUpDown nudSpeed = new NumericUpDown { Width = 100 };
        readonly NumericUpDown nudDiameter = new NumericUpDown { Width = 100 };
        readonly NumericUpDown nudBg = new NumericUpDown { Width = 100 };
        readonly NumericUpDown nudCircle = new NumericUpDown { Width = 100 };
        readonly TextBox txtSubject = new TextBox { Width = 200 };
        readonly Panel preview = new Panel { Width = 160, Height = 90, BorderStyle = BorderStyle.FixedSingle };
        readonly Button btnStart = new Button { Text = "▶  Start experiment", Width = 200, Height = 44 };
        readonly Button btnRelaunch = new Button { Text = "↻  Relaunch last", Width = 160, Height = 44, Enabled = false };
        readonly ListView history = new ListView { View = View.Details, FullRowSelect = true, Height = 170, Width = 640 };
        readonly Label lblLog = new Label { AutoSize = true, ForeColor = Color.DimGray };

        ExperimentSettings lastRun;

        public MainForm()
        {
            Text = "Bouncing Circle — Experiment Control";
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 10f);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(12);

            SetRange(nudSpeed, ExperimentSettings.SpeedMin, ExperimentSettings.SpeedMax);
            SetRange(nudDiameter, ExperimentSettings.DiameterMin, ExperimentSettings.DiameterMax);
            SetRange(nudBg, ExperimentSettings.BackgroundMin, ExperimentSettings.BackgroundMax);
            SetRange(nudCircle, ExperimentSettings.CircleMin, ExperimentSettings.CircleMax);
            nudSpeed.Increment = 50;

            var root = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Dock = DockStyle.Fill };

            // Monitor selection
            var gbMon = Group("Monitor with the moving circle");
            var monGrid = Grid(3);
            var modeRow = new FlowLayoutPanel { AutoSize = true };
            modeRow.Controls.AddRange(new Control[] { rbMon1, rbMon2, rbRandom });
            monGrid.Controls.Add(modeRow, 0, 0);
            monGrid.SetColumnSpan(modeRow, 3);
            monGrid.Controls.Add(Lbl("Monitor 1 ="), 0, 1);
            monGrid.Controls.Add(cbDisp1, 1, 1);
            var btnIdentify = new Button { Text = "Identify displays", AutoSize = true };
            btnIdentify.Click += (o, e) => IdentifyDisplays();
            monGrid.Controls.Add(btnIdentify, 2, 1);
            monGrid.Controls.Add(Lbl("Monitor 2 ="), 0, 2);
            monGrid.Controls.Add(cbDisp2, 1, 2);
            var btnRefresh = new Button { Text = "Refresh list", AutoSize = true };
            btnRefresh.Click += (o, e) => FillDisplays(cbDisp1.SelectedIndex, cbDisp2.SelectedIndex);
            monGrid.Controls.Add(btnRefresh, 2, 2);
            gbMon.Controls.Add(monGrid);
            root.Controls.Add(gbMon);

            // Stimulus parameters
            var gbStim = Group("Stimulus");
            var stimRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            var stimGrid = Grid(3);
            AddRow(stimGrid, 0, "Circle speed:", nudSpeed, "px/s");
            AddRow(stimGrid, 1, "Circle diameter:", nudDiameter, "px");
            AddRow(stimGrid, 2, "Background color:", nudBg, string.Format("gray {0}–{1} (255 = white)", ExperimentSettings.BackgroundMin, ExperimentSettings.BackgroundMax));
            AddRow(stimGrid, 3, "Circle color:", nudCircle, string.Format("gray {0}–{1} (0 = black)", ExperimentSettings.CircleMin, ExperimentSettings.CircleMax));
            AddRow(stimGrid, 4, "Subject / label:", txtSubject, "optional");
            stimRow.Controls.Add(stimGrid);
            var pvBox = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Margin = new Padding(16, 0, 0, 0) };
            pvBox.Controls.Add(Lbl("Preview"));
            pvBox.Controls.Add(preview);
            stimRow.Controls.Add(pvBox);
            gbStim.Controls.Add(stimRow);
            root.Controls.Add(gbStim);
            preview.Paint += PaintPreview;
            nudBg.ValueChanged += (o, e) => preview.Invalidate();
            nudCircle.ValueChanged += (o, e) => preview.Invalidate();

            // Actions
            var actions = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 8, 0, 4) };
            btnStart.Font = new Font(Font, FontStyle.Bold);
            btnStart.Click += (o, e) => RunLoop(ReadForm());
            btnRelaunch.Click += (o, e) => { if (lastRun != null) RunLoop(lastRun.Clone()); };
            var btnOpenLog = new Button { Text = "Open log", Width = 110, Height = 44 };
            btnOpenLog.Click += (o, e) => OpenPath(AppPaths.LogFile, "No experiments have been logged yet.");
            var btnOpenFolder = new Button { Text = "Open folder", Width = 110, Height = 44 };
            btnOpenFolder.Click += (o, e) => OpenPath(AppPaths.DataDir, null);
            actions.Controls.AddRange(new Control[] { btnStart, btnRelaunch, btnOpenLog, btnOpenFolder });
            root.Controls.Add(actions);
            root.Controls.Add(Lbl("During an experiment press Esc (or Space / Enter) to stop."));

            // History
            root.Controls.Add(new Label { Text = "This session:", AutoSize = true, Margin = new Padding(0, 10, 0, 2) });
            foreach (var c in new[] { "#", "Start", "Duration (s)", "Mode", "Used", "Speed", "BG", "Circle", "Result" })
                history.Columns.Add(c, c == "Start" ? 140 : c == "Result" ? 80 : 62);
            root.Controls.Add(history);
            lblLog.Text = "Log file: " + AppPaths.LogFile;
            root.Controls.Add(lblLog);

            Controls.Add(root);

            LoadIntoForm(ExperimentSettings.Load());
        }

        // ---- helpers for layout

        static void SetRange(NumericUpDown n, int min, int max) { n.Minimum = min; n.Maximum = max; }
        static Label Lbl(string t) { return new Label { Text = t, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 3, 3) }; }
        static GroupBox Group(string t) { return new GroupBox { Text = t, AutoSize = true, Padding = new Padding(10), Dock = DockStyle.Fill }; }
        static TableLayoutPanel Grid(int cols) { return new TableLayoutPanel { ColumnCount = cols, AutoSize = true, Dock = DockStyle.Fill }; }
        static void AddRow(TableLayoutPanel g, int row, string label, Control c, string hint)
        {
            g.Controls.Add(Lbl(label), 0, row);
            g.Controls.Add(c, 1, row);
            var h = Lbl(hint); h.ForeColor = Color.DimGray;
            g.Controls.Add(h, 2, row);
        }

        void PaintPreview(object sender, PaintEventArgs e)
        {
            int bg = (int)nudBg.Value, fg = (int)nudCircle.Value;
            e.Graphics.Clear(Color.FromArgb(bg, bg, bg));
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(Color.FromArgb(fg, fg, fg)))
                e.Graphics.FillEllipse(b, preview.Width / 2 - 25, preview.Height / 2 - 25, 50, 50);
        }

        void FillDisplays(int sel1, int sel2)
        {
            var screens = DisplayInfo.All();
            foreach (var cb in new[] { cbDisp1, cbDisp2 })
            {
                cb.Items.Clear();
                for (int i = 0; i < screens.Length; i++) cb.Items.Add(DisplayInfo.Describe(screens[i], i));
            }
            if (sel1 < 0 || sel1 >= screens.Length || sel2 < 0 || sel2 >= screens.Length)
                DisplayInfo.DefaultAssignment(out sel1, out sel2);
            cbDisp1.SelectedIndex = sel1;
            cbDisp2.SelectedIndex = sel2;
        }

        void IdentifyDisplays()
        {
            var screens = DisplayInfo.All();
            var forms = new List<Form>();
            for (int i = 0; i < screens.Length; i++)
            {
                string role = i == cbDisp1.SelectedIndex && i == cbDisp2.SelectedIndex ? "Monitor 1 & 2"
                            : i == cbDisp1.SelectedIndex ? "Monitor 1"
                            : i == cbDisp2.SelectedIndex ? "Monitor 2" : "(not used)";
                var f = new Form
                {
                    FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual,
                    TopMost = true, ShowInTaskbar = false, BackColor = Color.FromArgb(30, 90, 200),
                    Size = new Size(420, 220)
                };
                var b = screens[i].Bounds;
                f.Location = new Point(b.X + (b.Width - f.Width) / 2, b.Y + (b.Height - f.Height) / 2);
                f.Controls.Add(new Label
                {
                    Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.White,
                    Font = new Font("Segoe UI", 26f, FontStyle.Bold), Text = "Display " + (i + 1) + "\n" + role
                });
                f.Show();
                forms.Add(f);
            }
            var t = new Timer { Interval = 2500 };
            t.Tick += (o, e) => { t.Stop(); t.Dispose(); forms.ForEach(f => f.Close()); };
            t.Start();
        }

        void LoadIntoForm(ExperimentSettings s)
        {
            rbMon1.Checked = s.MonitorMode == "1";
            rbMon2.Checked = s.MonitorMode == "2";
            rbRandom.Checked = s.MonitorMode == "Random";
            FillDisplays(s.Monitor1Display, s.Monitor2Display);
            nudSpeed.Value = s.SpeedPxPerSec;
            nudDiameter.Value = s.DiameterPx;
            nudBg.Value = s.BackgroundGray;
            nudCircle.Value = s.CircleGray;
            txtSubject.Text = s.Subject;
        }

        ExperimentSettings ReadForm()
        {
            return new ExperimentSettings
            {
                MonitorMode = rbMon2.Checked ? "2" : rbRandom.Checked ? "Random" : "1",
                Monitor1Display = cbDisp1.SelectedIndex,
                Monitor2Display = cbDisp2.SelectedIndex,
                SpeedPxPerSec = (int)nudSpeed.Value,
                DiameterPx = (int)nudDiameter.Value,
                BackgroundGray = (int)nudBg.Value,
                CircleGray = (int)nudCircle.Value,
                Subject = txtSubject.Text.Trim()
            };
        }

        // Run an experiment, ask for the result, log it; repeat while the
        // operator ticks "Relaunch immediately".
        void RunLoop(ExperimentSettings s)
        {
            if (s.Monitor1Display == s.Monitor2Display && s.MonitorMode == "Random")
            {
                if (MessageBox.Show("Monitor 1 and Monitor 2 are the same display, so \"Random\" has no effect.\nContinue anyway?",
                    "Check monitors", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            }
            if (s.BackgroundGray == s.CircleGray)
            {
                if (MessageBox.Show("Background and circle colors are identical — the circle will be invisible.\nContinue anyway?",
                    "Check colors", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            }

            s.Save();
            lastRun = s.Clone();
            btnRelaunch.Enabled = true;

            bool again = true;
            while (again)
            {
                Hide();
                ExperimentRecord rec;
                try { rec = ExperimentRunner.Run(s, CsvLog.NextId()); }
                finally { Show(); }
                if (rec == null) return;

                using (var rf = new ResultForm(rec))
                {
                    rf.ShowDialog(this);
                    rec.Result = rf.Result;
                    rec.Notes = rf.Notes;
                    again = rf.RelaunchNext;
                }
                CsvLog.Append(rec);
                AddHistory(rec);
            }
            Activate();
        }

        void AddHistory(ExperimentRecord r)
        {
            var inv = CultureInfo.InvariantCulture;
            var item = new ListViewItem(new[] {
                r.Id.ToString(inv), r.Start.ToString("yyyy-MM-dd HH:mm:ss", inv), r.DurationSec.ToString("0.000", inv),
                r.Settings.MonitorMode, r.MonitorUsed.ToString(inv), r.Settings.SpeedPxPerSec.ToString(inv),
                r.Settings.BackgroundGray.ToString(inv), r.Settings.CircleGray.ToString(inv), r.Result });
            item.BackColor = r.Result == "Success" ? Color.FromArgb(225, 245, 225) : Color.FromArgb(250, 225, 225);
            history.Items.Insert(0, item);
        }

        static void OpenPath(string path, string missingMsg)
        {
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                MessageBox.Show(missingMsg ?? ("Not found: " + path), "Bouncing Circle");
                return;
            }
            try { Process.Start(path); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Could not open"); }
        }
    }

    static class Program
    {
        [STAThread]
        static void Main()
        {
            Native.EnableDpiAwareness();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
