using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("定时关机")]
[assembly: AssemblyDescription("Windows 定时关机、系统托盘及关机前提醒")]
[assembly: AssemblyProduct("Windows Shutdown Timer")]
[assembly: AssemblyVersion("2.1.11.0")]
[assembly: AssemblyFileVersion("2.1.11.0")]
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.0")]

namespace ShutdownTimer {
    internal sealed class MainForm : PixelForm {
        readonly TimerContext owner;
        readonly RoundPanel card = new RoundPanel { BackColor = Color.White };
        readonly RoundPanel status = new RoundPanel { BackColor = Color.FromArgb(244, 247, 251) };
        readonly PixelLabel title = new PixelLabel { Text = "定时关机", ForeColor = Theme.Ink };
        readonly ClockLogo icon = new ClockLogo();
        readonly PixelLabel statusLabel = new PixelLabel { Text = "暂无关机计划", ForeColor = Theme.Muted };
        readonly PixelLabel countdown = new PixelLabel { Text = "--:--:--", ForeColor = Color.FromArgb(133, 151, 177) };
        readonly PixelLabel deadline = new PixelLabel { ForeColor = Theme.Muted };
        readonly PixelLabel note = new PixelLabel { ForeColor = Theme.Muted };
        readonly PixelLabel presetLabel = new PixelLabel { Text = "快速设定", Align = ContentAlignment.MiddleLeft, ForeColor = Theme.Ink };
        readonly PixelLabel customLabel = new PixelLabel { Text = "自定义关机时间", Align = ContentAlignment.MiddleLeft, ForeColor = Theme.Ink };
        readonly CheckBox startup = new CheckBox { Text = "开机自启动", AutoSize = false, ForeColor = Theme.Muted, FlatStyle = FlatStyle.Standard };
        readonly RoundPanel inputPanel = new RoundPanel { BackColor = Color.FromArgb(248, 250, 255) };
        readonly TextBox minutes = new TextBox { Text = "45", BorderStyle = BorderStyle.None, BackColor = Color.FromArgb(248, 250, 255), ForeColor = Color.FromArgb(32, 50, 77), TextAlign = HorizontalAlignment.Left, MaxLength = 7 };
        readonly PixelLabel unit = new PixelLabel { Text = "分钟后", ForeColor = Theme.Muted };
        readonly RoundButton custom = new RoundButton { Text = "设置关机", Tone = ButtonTone.Primary };
        readonly RoundButton hide = new RoundButton { Text = "隐藏到托盘", Tone = ButtonTone.Tray };
        readonly RoundButton cancel = new RoundButton { Text = "取消关机", Tone = ButtonTone.Cancel };
        readonly RoundButton[] presets = new RoundButton[5];
        readonly ToolTip hints = new ToolTip();
        readonly Panel divider = new Panel { BackColor = Color.FromArgb(232, 237, 245) };
        bool syncing, ready;
        int countdownY = 38;
        public MainForm(TimerContext context) {
            owner = context; Text = "定时关机"; Icon = context.AppIcon; BackColor = Color.FromArgb(237, 243, 251);
            AutoScaleMode = AutoScaleMode.None; ClientSize = DesignClientSize; FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false; StartPosition = FormStartPosition.CenterScreen; DoubleBuffered = true;
            Controls.Add(card); card.Controls.AddRange(new Control[] { icon, title, status, presetLabel, divider, customLabel, startup, inputPanel, custom, hide, cancel });
            status.Controls.AddRange(new Control[] { statusLabel, countdown, deadline, note }); inputPanel.Controls.AddRange(new Control[] { minutes, unit });
            string[] labels = { "30 分钟", "1 小时", "1.5 小时", "2 小时", "3 小时" }; int[] values = { 30, 60, 90, 120, 180 };
            for (int i = 0; i < 5; i++) { int value = values[i]; presets[i] = new RoundButton { Text = labels[i], TabIndex = i }; presets[i].Click += delegate { owner.Schedule(value.ToString()); }; card.Controls.Add(presets[i]); }
            minutes.TabIndex = 6; custom.TabIndex = 7; hide.TabIndex = 8; cancel.TabIndex = 9; startup.TabIndex = 10;
            custom.Click += delegate { owner.Schedule(minutes.Text); };
            hide.Click += delegate { owner.HideMain(); }; cancel.Click += delegate { owner.Cancel(null); };
            minutes.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.Handled = e.SuppressKeyPress = true; owner.Schedule(minutes.Text); } };
            minutes.Enter += delegate { inputPanel.BorderColor = Theme.Blue; inputPanel.Invalidate(); owner.CancelAutoHide(); };
            minutes.Leave += delegate { inputPanel.BorderColor = Theme.Border; inputPanel.Invalidate(); };
            unit.BaselinePeer = minutes; unit.Click += delegate { minutes.Focus(); }; inputPanel.Click += delegate { minutes.Focus(); };
            startup.CheckedChanged += delegate { if (!syncing) { try { owner.Startup.Set(startup.Checked); } catch (Exception ex) { owner.Error("自启动设置失败：" + ex.Message); } SyncStartup(); } };
            hints.SetToolTip(startup, "登录 Windows 后只驻留托盘，不自动设置关机。开启后请保留本程序所在位置。");
            Activated += delegate { owner.CancelAutoHide(); };
            Resize += delegate { if (ready) { LayoutPixels(); if (WindowState == FormWindowState.Minimized) owner.HideMain(); } };
            FormClosing += delegate(object sender, FormClosingEventArgs e) {
                if (owner.Exiting || e.CloseReason == CloseReason.WindowsShutDown || e.CloseReason == CloseReason.TaskManagerClosing) return;
                e.Cancel = true; owner.ExitProgram();
            };
            Load += delegate { SizeForScale(Native.Dpi(this)); CenterToScreen(); ready = true; SyncStartup(); RefreshStatus(); };
            Shown += delegate { ActiveControl = null; };
        }
        internal void SizeForScale(float scale) {
            ApplyDpiScale(scale);
        }
        protected override void LayoutPixels() {
            if (LayingOut) return; LayingOut = true;
            try {
                Place(card, 9, 9, 276, 407, 14, false);
                Place(title, 91, 8, 130, 38, 24, true); Place(icon, 57, 14, 25, 25, 14, false);
                // Measure the complete title as one string; keep the icon and title centered as a group.
                int titleWidth = TextRenderer.MeasureText(title.Text, title.Font, new Size(1000, 1000), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
                int group = S(25) + S(7) + titleWidth; icon.Left = (card.Width - group) / 2; title.Left = icon.Right + S(7); title.Width = titleWidth;
                icon.Top = title.Top + (title.Height - icon.Height) / 2;
                Place(status, 15, 48, 246, 108, 13, false);
                LayoutStatusContent();
                Place(presetLabel, 15, 162, 246, 25, 14, true);
                Place(presets[0], 15, 188, 78, 34, 14, true); Place(presets[1], 99, 188, 78, 34, 14, true); Place(presets[2], 183, 188, 78, 34, 14, true);
                Place(presets[3], 15, 228, 120, 34, 14, true); Place(presets[4], 141, 228, 120, 34, 14, true);
                Place(divider, 15, 273, 246, 1, 14, false);
                Place(customLabel, 15, 281, 154, 25, 14, true); Place(startup, 169, 281, 92, 25, 12, false);
                Place(inputPanel, 15, 311, 120, 36, 14, false); Place(minutes, 10, 8, 52, 22, 14, true); Place(unit, 70, 6, 42, 25, 12, true);
                // Native edit controls use their preferred line height; center that height in the input frame.
                minutes.Top = (inputPanel.Height - minutes.PreferredHeight) / 2; minutes.Height = minutes.PreferredHeight;
                Place(custom, 141, 311, 120, 36, 14, true);
                Place(hide, 15, 357, 120, 36, 14, true); Place(cancel, 141, 357, 120, 36, 14, true);
                FitCountdown();
            } finally { LayingOut = false; }
        }
        void LayoutStatusContent() {
            // Keep the frame and every surrounding section fixed. Center the
            // visible content group when cancelled/idle; reserve lower rows
            // only when an actual deadline or warning is displayed.
            bool hasDeadline = deadline.Text.Length > 0, hasNote = note.Text.Length > 0;
            int labelY = hasNote ? 2 : hasDeadline ? 9 : 18;
            countdownY = labelY + 20;
            Place(statusLabel, 8, labelY, 230, 23, 13, true);
            Place(deadline, 3, hasNote ? 72 : hasDeadline ? 79 : 72, 240, 20, 12, true);
            Place(note, 3, 90, 240, 18, 11, false);
            FitCountdown();
        }
        void FitCountdown() {
            Place(countdown, 8, countdownY, 230, 51, 36, true, true);
            Size measured = TextRenderer.MeasureText(countdown.Text, countdown.Font, new Size(10000, 1000), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            float ratio = Math.Min((float)countdown.Width / Math.Max(1, measured.Width), (float)countdown.Height / Math.Max(1, measured.Height));
            if (ratio < 1) Place(countdown, 8, countdownY, 230, 51, Math.Max(16, (float)Math.Floor(36F * ratio * .98F)), true, true);
        }
        internal void SyncStartup() {
            syncing = true;
            try {
                startup.Checked = owner.Startup.Enabled;
                if (owner.Startup.PreviousVersion) hints.SetToolTip(startup, "检测到其他版本的自启动项。勾选后将改为启动此版本；取消勾选将移除本程序启动项。");
                else hints.SetToolTip(startup, "登录 Windows 后只驻留托盘，不自动设置关机。开启后请保留本程序所在位置。");
            } catch (Exception ex) { startup.Enabled = false; hints.SetToolTip(startup, "无法读取自启动设置：" + ex.Message); }
            finally { syncing = false; }
        }
        internal void RefreshStatus() {
            Plan plan = owner.Controller.Current; bool active = owner.Controller.Active; int left = owner.Controller.Remaining;
            statusLabel.Text = active ? "自动关机已启动" : plan == null ? "暂无关机计划" : plan.Status == "cancelled" ? (plan.Reason == "newboot" ? "上次计划已结束" : "自动关机已取消") : "已到计划关机时间";
            countdown.Text = active ? Plan.Countdown(left) : plan == null || plan.Status == "cancelled" ? "--:--:--" : "00:00:00";
            Color accent = active ? left <= 60 ? Theme.Red : left <= 300 ? Color.FromArgb(180, 108, 22) : Theme.Blue : Color.FromArgb(133, 151, 177);
            countdown.ForeColor = accent; statusLabel.ForeColor = active ? accent : Theme.Muted;
            status.BackColor = active ? left <= 60 ? Color.FromArgb(255, 240, 237) : left <= 300 ? Color.FromArgb(255, 248, 235) : Color.FromArgb(238, 246, 255) : Color.FromArgb(244, 247, 251);
            // Keep the recorded shutdown time fixed; rounded remaining seconds
            // are for the countdown and must not reconstruct this timestamp.
            deadline.Text = plan != null && plan.Status != "cancelled" ? (active ? "预计关机时间：" : "上次计划关机：") + new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(plan.Deadline).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : "";
            note.Text = active && left <= 300 ? "若仍在使用电脑，可随时取消关机" : "";
            cancel.Urgent = active && left <= 60;
            LayoutStatusContent();
            status.Invalidate(true);
        }
        protected override void Dispose(bool disposing) { if (disposing) hints.Dispose(); base.Dispose(disposing); }
    }
    internal sealed class PlanDialog : PixelForm {
        protected override Size DesignClientSize { get { return new Size(360, exit ? 168 : 192); } }
        readonly TimerContext owner;
        readonly string token;
        readonly bool exit;
        readonly long expires;
        readonly PixelLabel heading, countdown, description, note;
        readonly RoundButton cancel, keep;
        readonly System.Windows.Forms.Timer timer;
        internal PlanDialog(TimerContext context, bool isExit) {
            owner = context; token = context.Controller.Current.Token; exit = isExit; expires = Native.Now() + 20000;
            Text = exit ? "退出定时关机" : "自动关机提醒"; Icon = owner.AppIcon; BackColor = Color.White; AutoScaleMode = AutoScaleMode.None;
            ClientSize = DesignClientSize; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.CenterScreen;
            heading = new PixelLabel { Text = exit ? "取消关机并退出？" : "即将自动关机", ForeColor = Theme.Ink };
            countdown = new PixelLabel { ForeColor = exit ? Theme.Blue : Theme.Red };
            description = new PixelLabel { Text = "请先保存文件，仍在使用电脑可取消关机" + (exit ? "" : "。"), Align = ContentAlignment.MiddleCenter, ForeColor = Theme.Muted };
            cancel = new RoundButton { Text = exit ? "是，取消并退出" : "取消关机", Tone = ButtonTone.Cancel };
            keep = new RoundButton { Text = exit ? "否，继续并隐藏" : "继续倒计时", Tone = ButtonTone.Primary, DialogResult = DialogResult.No };
            Controls.AddRange(new Control[] { heading, countdown, description, cancel, keep });
            if (!exit) { note = new PixelLabel { Text = "20 秒后自动收起，未选择时保留计划。", ForeColor = Theme.Muted }; Controls.Add(note); }
            cancel.Click += delegate { if (owner.Cancel(token)) { DialogResult = DialogResult.Yes; Close(); } };
            keep.Click += delegate { DialogResult = DialogResult.No; Close(); }; AcceptButton = CancelButton = keep;
            Load += delegate { ApplyDpiScale(Native.Dpi(this)); CenterToScreen(); RefreshPlan(); };
            timer = new System.Windows.Forms.Timer { Interval = 250 }; timer.Tick += delegate { RefreshPlan(); }; timer.Start();
        }
        internal void CaptureScale(float scale) { ApplyDpiScale(scale); }
        protected override void LayoutPixels() {
            Place(heading, 12, 8, 336, 32, 20, true); Place(countdown, 12, 39, 336, 42, 28, true, true);
            Place(description, exit ? 0 : 8, 82, exit ? 360 : 344, 23, exit ? 12 : 13, false); Place(cancel, 16, 112, 159, 38, 14, true); Place(keep, 185, 112, 159, 38, 14, true);
            if (note != null) Place(note, 8, 158, 344, 23, 12, false);
        }
        void RefreshPlan() {
            if (!owner.Controller.Active || owner.Controller.Current.Token != token || (!exit && Native.Now() >= expires)) { Close(); return; }
            countdown.Text = Plan.Countdown(owner.Controller.Remaining);
        }
        protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }
    }
    internal sealed class TimerContext : ApplicationContext {
        internal readonly TimerController Controller;
        internal readonly StartupSetting Startup;
        internal readonly Icon AppIcon;
        internal readonly MainForm Window;
        internal bool Exiting;
        readonly NotifyIcon tray;
        readonly ToolStripMenuItem startupMenu, exitMenu;
        readonly System.Windows.Forms.Timer timer;
        readonly EventWaitHandle showEvent;
        readonly bool preview;
        PlanDialog reminder;
        long hideAt;
        bool prompting, errorShown;
        internal TimerContext(TimerController controller, StartupSetting startup, bool hidden, EventWaitHandle signal, bool isPreview) {
            Controller = controller; Startup = startup; showEvent = signal; preview = isPreview;
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ShutdownTimer.icon")) AppIcon = stream == null ? (Icon)SystemIcons.Information.Clone() : new Icon(stream, 32, 32);
            Window = new MainForm(this);
            tray = new NotifyIcon { Icon = AppIcon, Text = "定时关机", Visible = !preview };
            var menu = new ContextMenuStrip(); menu.Items.Add("显示主界面", null, delegate { ShowMain(); }); menu.Items.Add("取消关机", null, delegate { Cancel(null); });
            startupMenu = new ToolStripMenuItem("开机自启动"); startupMenu.Click += delegate {
                try { Startup.Set(!Startup.Enabled); Window.SyncStartup(); } catch (Exception ex) { Error(ex.Message); }
            }; menu.Items.Add(startupMenu);
            menu.Items.Add("创建／更新桌面快捷方式", null, delegate {
                try { if (!preview) Shortcut.Create(Application.ExecutablePath); MessageBox.Show("桌面快捷方式已指向当前版本。请保留本程序所在位置。", "定时关机"); } catch (Exception ex) { Error("更新快捷方式失败：" + ex.Message); }
            });
            menu.Items.Add(new ToolStripSeparator()); exitMenu = new ToolStripMenuItem("退出", null, delegate { ExitProgram(); }); menu.Items.Add(exitMenu);
            menu.Opening += delegate { try { startupMenu.Checked = Startup.Enabled; } catch { startupMenu.Enabled = false; } };
            tray.ContextMenuStrip = menu; tray.DoubleClick += delegate { ShowMain(); };
            timer = new System.Windows.Forms.Timer { Interval = 250 }; timer.Tick += delegate { Tick(); }; timer.Start();
            if (hidden) { Window.CreateControl(); } else Window.Show();
        }
        internal void Schedule(string text) {
            CancelAutoHide();
            if (Controller.Active && MessageBox.Show(Window, "新计划将替代当前关机计划，是否继续？", "定时关机", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            Operation result = Controller.ScheduleMinutes(text); Window.RefreshStatus();
            if (!result.Ok) { Error(result.Message); return; }
            if (reminder != null && !reminder.IsDisposed) reminder.Close(); hideAt = Native.Now() + 2000;
        }
        internal bool Cancel(string token) {
            CancelAutoHide(); Operation result = Controller.Cancel(token); Window.RefreshStatus();
            if (!result.Ok) Error(result.Message);
            if (result.Ok && reminder != null && !reminder.IsDisposed && token == null) reminder.Close(); return result.Ok;
        }
        internal void Error(string message) { MessageBox.Show(Window.Visible ? Window : null, message, "定时关机", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        internal void CancelAutoHide() { hideAt = 0; }
        internal void ShowMain() { CancelAutoHide(); Window.Show(); Window.WindowState = FormWindowState.Normal; Window.Activate(); if (Native.IsWindows) Native.SetForegroundWindow(Window.Handle); }
        internal void HideMain() { CancelAutoHide(); Window.Hide(); }
        internal void ExitProgram() {
            if (Exiting || prompting) return; prompting = true; CancelAutoHide();
            try {
                if (Controller.Active) {
                    using (var dialog = new PlanDialog(this, true)) if (dialog.ShowDialog(Window.Visible ? Window : null) != DialogResult.Yes) { HideMain(); return; }
                    if (Controller.Active) return;
                }
                Exiting = true; ExitThread();
            } finally { prompting = false; }
        }
        void Tick() {
            if (Exiting || prompting) return;
            if (showEvent != null && showEvent.WaitOne(0)) ShowMain();
            try {
                string key = Controller.Tick(); Window.RefreshStatus();
                if (key.Length > 0 && !preview) { if (reminder != null && !reminder.IsDisposed) reminder.Close(); reminder = new PlanDialog(this, false); reminder.Show(); reminder.Activate(); }
            } catch (Exception ex) { if (!errorShown) { errorShown = true; Error("计划状态保存失败，请保持程序运行或取消计划。\n" + ex.Message); } }
            string text = Controller.Active ? "定时关机 · 剩余 " + Plan.Countdown(Controller.Remaining) : "定时关机 · 暂无倒计时"; tray.Text = text;
            exitMenu.Text = Controller.Active ? "取消关机并退出" : "退出";
            if (hideAt > 0 && Native.Now() >= hideAt) { hideAt = 0; if (Controller.Active) HideMain(); }
        }
        protected override void ExitThreadCore() {
            timer.Stop(); timer.Dispose(); tray.Visible = false; tray.ContextMenuStrip.Dispose(); tray.Dispose();
            if (reminder != null) reminder.Dispose(); Window.Dispose(); AppIcon.Dispose(); base.ExitThreadCore();
        }
    }
    internal sealed class MemoryStore : IPlanStore { public Plan Value; public Plan Read() { return Value; } public void Write(Plan p) { Value = p; } }
    internal sealed class DemoShutdown : IShutdown { public int Schedule(int seconds) { return 0; } public int Cancel() { return 0; } }
    internal sealed class DemoStartup : IStartupStore { string value; public string Read() { return value; } public void Write(string command) { value = command; } public void Delete() { value = null; } }
    internal static class Program {
        [STAThread] internal static int Main(string[] args) {
            bool preview = Array.IndexOf(args, "--preview") >= 0, startup = Array.IndexOf(args, "--startup") >= 0;
            if (!preview && !Native.IsWindows) return 1;
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            try {
                if (preview) return Capture(args);
                if (!startup) {
                    try { Shortcut.Ensure(Application.ExecutablePath); }
                    catch (Exception ex) { MessageBox.Show("桌面快捷方式创建／更新失败，可从托盘菜单重新创建。\n" + ex.Message, "定时关机", MessageBoxButtons.OK, MessageBoxIcon.Information); }
                }
                string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ShutdownTimer", "state.ini");
                string key; using (var sha = SHA256.Create()) key = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Environment.UserName + "|" + data.ToLowerInvariant()))).Replace("-", "").Substring(0, 32);
                bool created;
                using (var mutex = new Mutex(true, @"Local\ShutdownTimer.Tray." + key, out created)) {
                    if (!created) {
                        try { using (var signal = EventWaitHandle.OpenExisting(@"Local\ShutdownTimer.Native.Show." + key)) { if (!startup) signal.Set(); } }
                        catch (WaitHandleCannotBeOpenedException) { if (!startup) MessageBox.Show("旧版定时关机仍在运行。请先从旧版托盘退出，再打开新版。", "定时关机"); }
                        return 0;
                    }
                    try {
                        using (var signal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\ShutdownTimer.Native.Show." + key)) {
                            var controller = new TimerController(new PlanStore(data), new WindowsShutdown(), Native.Now, Native.Tick);
                            var setting = new StartupSetting(new RegistryStartup(), Application.ExecutablePath);
                            Application.Run(new TimerContext(controller, setting, startup, signal, false));
                        }
                    } finally { mutex.ReleaseMutex(); }
                }
                return 0;
            } catch (Exception ex) { MessageBox.Show("程序启动失败：" + ex.Message, "定时关机", MessageBoxButtons.OK, MessageBoxIcon.Error); return 1; }
        }
        static int Capture(string[] args) {
            int index = Array.IndexOf(args, "--preview"); if (index + 1 >= args.Length) return 2;
            string directory = Path.GetFullPath(args[index + 1]); Directory.CreateDirectory(directory);
            using (var context = new TimerContext(new TimerController(new MemoryStore(), new DemoShutdown(), Native.Now, Native.Tick), new StartupSetting(new DemoStartup(), "C:\\Tools\\ShutdownTimer.exe"), false, null, true)) {
                float[] scales = Array.IndexOf(args, "--preview-base-only") >= 0 ? new [] { 1F } : new [] { 1F, 1.25F, 1.5F, 2F, 3F };
                foreach (float scale in scales) {
                    context.Window.SizeForScale(scale); context.Window.Location = new Point(50, 50); Cursor.Position = new Point(5, 5);
                    Application.DoEvents(); Save(context.Window, Path.Combine(directory, "idle-" + (int)(scale * 100) + ".png"));
                    Operation scheduled = context.Controller.ScheduleMinutes("30"); if (!scheduled.Ok) throw new Exception(scheduled.Message);
                    context.Window.RefreshStatus(); Application.DoEvents(); Save(context.Window, Path.Combine(directory, "active-" + (int)(scale * 100) + ".png"));
                    context.Controller.Cancel(null); context.Window.RefreshStatus();
                    Application.DoEvents(); Save(context.Window, Path.Combine(directory, "cancelled-" + (int)(scale * 100) + ".png"));
                }
                context.Window.SizeForScale(1); context.Window.Location = new Point(50, 50);
                context.Window.RefreshStatus(); Application.DoEvents();
                foreach (Control card in context.Window.Controls) foreach (Control control in card.Controls) {
                    var button = control as RoundButton; if (button == null) continue;
                    // Exercise the same event and repaint path without relying
                    // on a simulated desktop's pointer/window-manager behavior.
                    typeof(RoundButton).GetMethod("OnMouseEnter", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(button, new object[] { EventArgs.Empty });
                    for (int i = 0; i < 5; i++) { Application.DoEvents(); Thread.Sleep(10); }
                    Save(context.Window, Path.Combine(directory, "hover-" + button.Text.Replace(" ", "") + ".png"));
                    typeof(RoundButton).GetMethod("OnMouseLeave", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(button, new object[] { EventArgs.Empty });
                    Application.DoEvents();
                }
                context.Controller.ScheduleMinutes("1");
                context.Window.RefreshStatus(); Save(context.Window, Path.Combine(directory, "urgent.png"));
                using (var dialog = new PlanDialog(context, false)) { dialog.Show(); dialog.CaptureScale(1); Application.DoEvents(); Save(dialog, Path.Combine(directory, "reminder.png")); }
                using (var dialog = new PlanDialog(context, true)) {
                    dialog.Show();
                    foreach (float scale in scales) {
                        dialog.CaptureScale(scale); Application.DoEvents();
                        Save(dialog, Path.Combine(directory, "exit-" + (int)(scale * 100) + ".png"));
                        if (scale == 1F) Save(dialog, Path.Combine(directory, "exit.png"));
                    }
                }
                context.Controller.Cancel(null); context.Exiting = true; context.ExitThread();
            }
            return 0;
        }
        static void Save(Form form, string file) {
            form.Refresh(); Application.DoEvents();
            using (var bitmap = new Bitmap(form.Width, form.Height)) {
                if (Native.IsWindows) form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                else using (var graphics = Graphics.FromImage(bitmap)) graphics.CopyFromScreen(form.Location, Point.Empty, bitmap.Size);
                bitmap.Save(file);
            }
        }
    }
}
