using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Diagnostics.Eventing.Reader;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;

internal sealed class Job {
    public string Token, Status, Boot = "", Reason = "";
    public long Started, Deadline, Heartbeat;
    public int Warn5, WarnFinal;
    public static Job Parse(string text) {
        var d = new Dictionary<string,string>();
        foreach (string line in text.Split('\n')) { int n = line.IndexOf('='); if (n > 0) d[line.Substring(0,n)] = line.Substring(n+1).TrimEnd('\r'); }
        string version, token, status;
        if (!d.TryGetValue("version",out version) || version != "1" || !d.TryGetValue("token",out token) ||
            !System.Text.RegularExpressions.Regex.IsMatch(token,@"^[0-9-]+$") || !d.TryGetValue("status",out status) ||
            (status != "scheduled" && status != "cancelled" && status != "expired")) return null;
        var j = new Job { Token=token, Status=status };
        string value;
        if (!d.TryGetValue("started",out value) || !long.TryParse(value,out j.Started) || j.Started < 0 ||
            !d.TryGetValue("deadline",out value) || !long.TryParse(value,out j.Deadline) || j.Deadline <= j.Started || j.Deadline-j.Started > 315360000000L) return null;
        if (d.TryGetValue("heartbeat",out value) && (!long.TryParse(value,out j.Heartbeat) || j.Heartbeat < 0)) return null;
        if (d.TryGetValue("warn5",out value) && !int.TryParse(value,out j.Warn5)) return null;
        if (d.TryGetValue("warnFinal",out value) && !int.TryParse(value,out j.WarnFinal)) return null;
        if (d.TryGetValue("boot",out value)) j.Boot=value;
        if (d.TryGetValue("reason",out value)) j.Reason=value;
        return j;
    }
    public string Serialize() { return "version=1\r\ntoken="+Token+"\r\nstatus="+Status+"\r\nstarted="+Started+"\r\ndeadline="+Deadline+"\r\nboot="+Boot+"\r\nheartbeat="+Heartbeat+"\r\nwarn5="+Warn5+"\r\nwarnFinal="+WarnFinal+"\r\nreason="+Reason+"\r\n"; }
    public int Remaining(long now) { return (int)Math.Max(0,Math.Ceiling((Deadline-now)/1000.0)); }
    public string Reminder(long now) {
        if (Status != "scheduled" || Deadline <= now) return "";
        int left=Remaining(now), duration=(int)((Deadline-Started)/1000);
        if (left <= Math.Min(60,duration/2) && WarnFinal == 0) return "final";
        if (duration > 300 && left <= 300 && Warn5 == 0 && WarnFinal == 0) return "five";
        return "";
    }
    public static string Countdown(int seconds) { return (seconds/3600).ToString("00")+":"+(seconds/60%60).ToString("00")+":"+(seconds%60).ToString("00"); }
}

internal sealed class Store {
    public readonly string Path;
    readonly string lockPath;
    public Store(string path) { Path=path; lockPath=path+".lock"; Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)); }
    public bool Acquire() {
        try {
            if (Directory.Exists(lockPath) && (DateTime.UtcNow-Directory.GetCreationTimeUtc(lockPath)).TotalSeconds > 30) Directory.Delete(lockPath);
            return Native.CreateDirectory(lockPath,IntPtr.Zero);
        } catch { return false; }
    }
    public void Release() { try { Directory.Delete(lockPath); } catch {} }
    public Job Read() { try { return File.Exists(Path) ? Job.Parse(File.ReadAllText(Path,Encoding.ASCII)) : null; } catch { return null; } }
    public void Write(Job j) { File.WriteAllText(Path,j.Serialize(),Encoding.ASCII); }
    public string Cancel(string expectedToken,Func<int> execute,out Job saved,out int code,bool activeOnly=false) {
        saved=null; code=0;
        if(!Acquire()) return "busy";
        try {
            var j=Read();
            if(activeOnly && (j == null || j.Status != "scheduled")) return "success";
            if(expectedToken != null && (j == null || j.Token != expectedToken || j.Status != "scheduled")) return "stale";
            code=execute();
            if(code != 0 && code != 1116) return "error";
            if(j != null) { j.Status="cancelled"; j.Reason="tray"; j.Heartbeat=0; Write(j); saved=j; }
            return "success";
        } finally { Release(); }
    }
}

internal static class Native {
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] internal static extern bool CreateDirectory(string path,IntPtr attributes);
    [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumProc callback,IntPtr data);
    internal delegate bool EnumProc(IntPtr window,IntPtr data);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] internal static extern int GetWindowText(IntPtr window,StringBuilder text,int max);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] internal static extern int GetClassName(IntPtr window,StringBuilder text,int max);
    [DllImport("kernel32.dll")] internal static extern ulong GetTickCount64();
    [StructLayout(LayoutKind.Sequential)] internal struct Placement { internal int Length,Flags,ShowCmd,MinX,MinY,MaxX,MaxY,Left,Top,Right,Bottom; }
    [DllImport("user32.dll")] internal static extern bool GetWindowPlacement(IntPtr window,ref Placement placement);
    [DllImport("user32.dll")] internal static extern bool SetWindowPlacement(IntPtr window,ref Placement placement);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr window,uint flags);
    [StructLayout(LayoutKind.Sequential)] struct Rect { internal int Left,Top,Right,Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct MonitorInfo { internal int Size; internal Rect Monitor,Work; internal int Flags; }
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
    internal static void ShowLoadedTimer(IntPtr window) {
        var info=new MonitorInfo { Size=Marshal.SizeOf(typeof(MonitorInfo)) };
        if(!GetMonitorInfo(MonitorFromWindow(window,2),ref info)) { ShowWindowAsync(window,1); return; }
        int w=Math.Min(560,info.Work.Right-info.Work.Left-24),h=Math.Min(800,info.Work.Bottom-info.Work.Top-24);
        int x=info.Work.Left+(info.Work.Right-info.Work.Left-w)/2,y=info.Work.Top+(info.Work.Bottom-info.Work.Top-h)/2;
        var p=new Placement { Length=Marshal.SizeOf(typeof(Placement)),ShowCmd=1,Left=x,Top=y,Right=x+w,Bottom=y+h };
        SetWindowPlacement(window,ref p);
    }
    internal static int Mode(IntPtr window) { var p=new Placement { Length=Marshal.SizeOf(typeof(Placement)) }; return GetWindowPlacement(window,ref p) ? p.ShowCmd : 0; }
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
    [DllImport("user32.dll")] internal static extern bool ShowWindowAsync(IntPtr window,int mode);
    [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] internal static extern bool PostMessage(IntPtr window,uint message,IntPtr wParam,IntPtr lParam);
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr window);
    internal static IntPtr FindTimer(string title) {
        IntPtr result=IntPtr.Zero; int found=0;
        EnumWindows(delegate(IntPtr h,IntPtr unused) {
            var text=new StringBuilder(256); GetWindowText(h,text,text.Capacity);
            if (text.ToString() != title) return true;
            var className=new StringBuilder(256); GetClassName(h,className,className.Capacity);
            if(className.ToString() == "HTML Application Host Window Class") { result=h; found++; }
            return true;
        },IntPtr.Zero);
        return found == 1 ? result : IntPtr.Zero;
    }
}

internal sealed class ReminderForm : DpiDialog {
    readonly TrayContext owner;
    readonly string token;
    readonly Label countdown;
    readonly long expires;
    internal ReminderForm(TrayContext context,Job job) {
        owner=context; token=job.Token; expires=TrayContext.Now()+20000;
        Text="自动关机提醒"; Font=new Font("Microsoft YaHei UI",12F);
        AutoScaleMode=AutoScaleMode.None;
        Size=new Size(500,315); FormBorderStyle=FormBorderStyle.FixedDialog;
        MaximizeBox=false; MinimizeBox=false; TopMost=true; StartPosition=FormStartPosition.CenterScreen;
        BackColor=Color.White; Icon=context.AppIcon;
        var title=new Label { Text="即将自动关机",Font=new Font(Font.FontFamily,20F,FontStyle.Bold),AutoSize=false,TextAlign=ContentAlignment.MiddleCenter,Bounds=new Rectangle(20,15,440,42),ForeColor=Color.FromArgb(29,53,86) };
        countdown=new Label { Font=new Font("Segoe UI",28F,FontStyle.Bold),TextAlign=ContentAlignment.MiddleCenter,Bounds=new Rectangle(20,58,440,52),ForeColor=Color.FromArgb(205,75,42) };
        var description=new Label { Text="请先保存文件，仍在使用电脑可取消关机。",TextAlign=ContentAlignment.MiddleCenter,Bounds=new Rectangle(20,111,440,32) };
        var cancel=new RoundedButton { Text="取消关机",Bounds=new Rectangle(35,155,200,48),BackColor=Color.FromArgb(40,107,232),ForeColor=Color.White,FlatStyle=FlatStyle.Flat };
        cancel.FlatAppearance.BorderSize=0; cancel.Click+=delegate { if(owner.Cancel(token)) Close(); };
        var keep=new RoundedButton { Text="继续倒计时",Bounds=new Rectangle(250,155,200,48),BackColor=Color.FromArgb(237,243,251),FlatStyle=FlatStyle.Flat };
        keep.FlatAppearance.BorderColor=Color.FromArgb(212,224,239); keep.Click+=delegate { Close(); };
        var note=new Label { Text="20 秒后自动收起，未选择时保留关机计划。",Font=new Font(Font.FontFamily,12F),ForeColor=Color.Gray,TextAlign=ContentAlignment.MiddleCenter,Bounds=new Rectangle(20,211,440,32) };
        Controls.AddRange(new Control[]{title,countdown,description,cancel,keep,note});
        AcceptButton=keep; CancelButton=keep; RefreshJob(job);
    }
    internal void RefreshJob(Job j) {
        if (j == null || j.Token != token || j.Status != "scheduled" || TrayContext.Now() >= expires || j.Deadline <= TrayContext.Now()) { Close(); return; }
        countdown.Text=Job.Countdown(j.Remaining(TrayContext.Now()));
    }
}

internal sealed class TrayContext : ApplicationContext {
    readonly Store store;
    readonly string appPath,title;
    volatile string boot="";
    readonly bool demo;
    readonly string demoExitAnswer;
    readonly NotifyIcon tray;
    readonly System.Windows.Forms.Timer timer;
    readonly EventWaitHandle hideEvent,showEvent,exitEvent,readyEvent;
    readonly ToolStripMenuItem cancelItem,exitItem;
    ReminderForm reminder;
    Job current;
    bool hideRequested;
    bool restoreRequested;
    bool exitRequested,exiting,mainSeen,checkingExit;
    bool loadingReported;
    long ignoreExitUntil;
    int hideAttempts,restoreAttempts;
    IntPtr verifyHide=IntPtr.Zero,verifyRestore=IntPtr.Zero;
    IntPtr readyWindow=IntPtr.Zero;
    internal readonly Icon AppIcon;
    internal static long Now() { return (long)(DateTime.UtcNow-new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc)).TotalMilliseconds; }
    internal TrayContext(string dataPath,string htaPath,string caption,string eventKey,bool initialHide,bool initialShow,bool initialExit,bool initialReady,bool isDemo,string simulatedExitAnswer) {
        store=new Store(dataPath); appPath=htaPath; title=caption; demo=isDemo; demoExitAnswer=isDemo ? simulatedExitAnswer : null;
        ThreadPool.QueueUserWorkItem(delegate { boot=BootStamp(); });
        hideEvent=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\ShutdownTimer.Hide."+eventKey);
        showEvent=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\ShutdownTimer.Show."+eventKey);
        exitEvent=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\ShutdownTimer.Exit."+eventKey);
        readyEvent=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\ShutdownTimer.Ready."+eventKey);
        try { AppIcon=new Icon(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(appPath),"shutdown_timer.ico")); } catch { AppIcon=SystemIcons.Information; }
        tray=new NotifyIcon { Icon=AppIcon,Text="定时关机",Visible=true };
        var menu=new ContextMenuStrip();
        menu.Items.Add("显示主界面",null,delegate { Restore(); });
        cancelItem=new ToolStripMenuItem("取消关机",null,delegate { Cancel(null); }); menu.Items.Add(cancelItem);
        menu.Items.Add(new ToolStripSeparator());
        exitItem=new ToolStripMenuItem("退出",null,delegate { ExitProgram(); }); menu.Items.Add(exitItem);
        tray.ContextMenuStrip=menu; tray.DoubleClick+=delegate { Restore(); };
        hideRequested=initialHide; exitRequested=initialExit;
        if(initialReady) { readyWindow=Native.FindTimer(title); restoreRequested=true; }
        timer=new System.Windows.Forms.Timer { Interval=200 }; timer.Tick+=delegate { Tick(); }; timer.Start();
        if(!initialExit) { Tick(); if(initialShow) Restore(); }
    }
    static string BootStamp() {
        try {
            var query=new EventLogQuery("System",PathType.LogName,"*[System[Provider[@Name='Microsoft-Windows-Kernel-General'] and EventID=12]]") { ReverseDirection=true };
            using(var reader=new EventLogReader(query)) using(var record=reader.ReadEvent()) {
                if(record != null && record.TimeCreated.HasValue) return "event12-"+record.TimeCreated.Value.ToUniversalTime().Ticks;
            }
        } catch {}
        return "";
    }
    void Log(string text) { if(demo) File.AppendAllText(store.Path+".qa-log",text+Environment.NewLine); }
    void Tick() {
        if(exiting || checkingExit) return;
        if(exitEvent.WaitOne(0) && !restoreRequested && Now() >= ignoreExitUntil) exitRequested=true;
        if(exitRequested) { exitRequested=false; ExitProgram(); return; }
        if(verifyHide != IntPtr.Zero && !Native.IsWindowVisible(verifyHide)) { Log("hidden=0"); verifyHide=IntPtr.Zero; }
        if(verifyRestore != IntPtr.Zero && Native.Mode(verifyRestore) == 1) { Log("restored=1"); verifyRestore=IntPtr.Zero; }
        if(hideEvent.WaitOne(0)) { hideRequested=true; hideAttempts=0; }
        if(readyEvent.WaitOne(0)) { readyWindow=Native.FindTimer(title); restoreRequested=true; restoreAttempts=0; Log("layout-ready"); }
        if(showEvent.WaitOne(0)) Restore();
        IntPtr mainWindow=Native.FindTimer(title);
        if(mainWindow != IntPtr.Zero) {
            mainSeen=true;
            if(Native.IsWindowVisible(mainWindow) && Native.IsIconic(mainWindow)) { Native.ShowWindowAsync(mainWindow,0); verifyHide=mainWindow; Log("titlebar-minimize"); }
        } else if(mainSeen) { mainSeen=false; ExitProgram(); return; }
        string warning="";
        if(store.Acquire()) {
            try {
                var j=store.Read();
                if(j != null) {
                    if(j.Status == "scheduled") {
                        if(!String.IsNullOrEmpty(boot) && !String.IsNullOrEmpty(j.Boot) && j.Boot != boot) { j.Status="cancelled"; j.Reason="newboot"; j.Heartbeat=0; }
                        else if(j.Deadline <= Now()) { j.Status="expired"; j.Reason="deadline"; j.Heartbeat=0; }
                        else { if(String.IsNullOrEmpty(j.Boot) && !String.IsNullOrEmpty(boot)) j.Boot=boot; j.Heartbeat=Now(); warning=j.Reminder(Now()); if(warning == "final") j.WarnFinal=1; if(warning == "five") j.Warn5=1; }
                        store.Write(j);
                    }
                    current=j;
                }
            } catch { tray.Text="定时关机 · 状态读取失败"; }
            finally { store.Release(); }
        }
        if(reminder != null && !reminder.IsDisposed) reminder.RefreshJob(current);
        if(warning.Length > 0 && current != null && current.Status == "scheduled") {
            if(reminder != null && !reminder.IsDisposed) reminder.Close();
            reminder=new ReminderForm(this,current); reminder.Show(); reminder.Activate();
            if(demo) { using(var bmp=new Bitmap(reminder.Width,reminder.Height)) { reminder.DrawToBitmap(bmp,new Rectangle(0,0,bmp.Width,bmp.Height)); bmp.Save(store.Path+".reminder.png"); } Log("reminder="+warning); }
        }
        string text=current != null && current.Status == "scheduled" ? "定时关机 · 剩余 "+Job.Countdown(current.Remaining(Now())) : "定时关机 · 暂无倒计时";
        tray.Text=text.Length > 63 ? text.Substring(0,63) : text;
        exitItem.Text=current != null && current.Status == "scheduled" ? "取消关机并退出" : "退出";
        if(hideRequested) {
            IntPtr window=Native.FindTimer(title);
            if(window != IntPtr.Zero) { Native.ShowWindowAsync(window,0); hideRequested=false; verifyHide=window; restoreRequested=false; }
            else if(++hideAttempts > 30) hideRequested=false;
        }
        if(restoreRequested) {
            IntPtr window=Native.FindTimer(title);
            if(window != IntPtr.Zero && window == readyWindow) { Native.ShowLoadedTimer(window); Native.SetForegroundWindow(window); restoreRequested=false; verifyRestore=window; Log("show-after-ready"); }
            else {
                if(window != IntPtr.Zero && !loadingReported) { Log("waiting-layout;normal-visible="+(Native.IsWindowVisible(window) && !Native.IsIconic(window))); loadingReported=true; }
                if(window != IntPtr.Zero && Native.IsWindowVisible(window)) { Native.ShowWindowAsync(window,0); Log("loading-hidden"); }
                if(++restoreAttempts > 150) { restoreRequested=false; tray.ShowBalloonTip(5000,"定时关机","主界面加载超时，请检查程序文件是否完整后重新打开。",ToolTipIcon.Warning); }
            }
        }
    }
    internal bool Cancel(string expectedToken,bool activeOnly=false,bool notify=true) {
        try {
            Job saved; int code;
            string result=store.Cancel(expectedToken,delegate { return RunShutdown("-a"); },out saved,out code,activeOnly);
            if(result == "busy") { MessageBox.Show("正在处理关机计划，请稍后再试。","定时关机",MessageBoxButtons.OK,MessageBoxIcon.Information); return false; }
            if(result == "stale") return true;
            if(result == "error") { MessageBox.Show("取消关机未成功。错误代码："+code,"定时关机",MessageBoxButtons.OK,MessageBoxIcon.Warning); return false; }
            if(saved != null) current=saved;
            if(reminder != null && !reminder.IsDisposed) reminder.Close();
            if(notify) tray.ShowBalloonTip(3500,"定时关机",code == 0 ? "已取消自动关机。" : "当前没有待执行的定时关机。",ToolTipIcon.Info);
            return true;
        } catch(Exception ex) { MessageBox.Show("无法取消关机："+ex.Message,"定时关机",MessageBoxButtons.OK,MessageBoxIcon.Warning); return false; }
    }
    int RunShutdown(string args) {
        if(demo) { File.AppendAllText(store.Path+".qa-log",args+Environment.NewLine); return 1116; }
        using(var p=Process.Start(new ProcessStartInfo(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"shutdown.exe"),args) { CreateNoWindow=true,UseShellExecute=false,WindowStyle=ProcessWindowStyle.Hidden })) { p.WaitForExit(); return p.ExitCode; }
    }
    void ExitProgram() {
        if(exiting || checkingExit) return;
        checkingExit=true; mainSeen=false;
        try {
            Job job;
            if(!store.Acquire()) { MessageBox.Show("正在处理关机计划，请稍后再退出。","定时关机",MessageBoxButtons.OK,MessageBoxIcon.Information); Restore(); return; }
            try { job=store.Read(); } finally { store.Release(); }
            bool accepted=ExitPolicy.Accept(job,Now(),delegate {
                Log("exit-confirm");
                if(demoExitAnswer != null) return demoExitAnswer == "yes";
                using(var prompt=new ExitConfirmationForm(AppIcon,job)) return prompt.ShowDialog() == DialogResult.Yes;
            });
            // A close request can arrive through both unload IPC and window monitoring.
            while(exitEvent.WaitOne(0)) {}
            if(!accepted) {
                Log("exit=declined"); ignoreExitUntil=Now()+1000;
                restoreRequested=false; hideRequested=false;
                IntPtr openWindow=Native.FindTimer(title);
                if(openWindow != IntPtr.Zero) { Native.ShowWindowAsync(openWindow,0); verifyHide=openWindow; }
                return;
            }
            if(!Cancel(null,true,false)) { Restore(); return; }
            exiting=true; Log("exit=cancelled");
            IntPtr window=Native.FindTimer(title);
            if(window != IntPtr.Zero) Native.PostMessage(window,0x0010,IntPtr.Zero,IntPtr.Zero);
            ExitThread();
        } finally { checkingExit=false; }
    }
    void Restore() {
        hideRequested=false; loadingReported=false;
        IntPtr window=Native.FindTimer(title);
        if(window != IntPtr.Zero) {
            if(window == readyWindow) { Native.ShowWindowAsync(window,9); Native.SetForegroundWindow(window); verifyRestore=window; }
            else { Native.ShowWindowAsync(window,0); restoreRequested=true; restoreAttempts=0; }
            return;
        }
        if(!File.Exists(appPath)) { tray.ShowBalloonTip(4000,"定时关机","找不到主界面文件，请重新双击定时关机 EXE 修复程序文件。",ToolTipIcon.Warning); return; }
        readyWindow=IntPtr.Zero;
        Process.Start(new ProcessStartInfo(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"mshta.exe"),"\""+appPath+"\"") { WorkingDirectory=System.IO.Path.GetDirectoryName(appPath),UseShellExecute=true,WindowStyle=ProcessWindowStyle.Hidden });
        restoreRequested=true; restoreAttempts=0;
    }
    protected override void ExitThreadCore() { timer.Stop(); timer.Dispose(); if(reminder != null) reminder.Close(); tray.Visible=false; tray.Dispose(); hideEvent.Dispose(); showEvent.Dispose(); exitEvent.Dispose(); readyEvent.Dispose(); base.ExitThreadCore(); }
}

internal static class RoundedDrawing {
    internal static GraphicsPath Path(RectangleF rect,float radius) {
        var p=new GraphicsPath(); float d=Math.Min(radius*2,Math.Min(rect.Width,rect.Height));
        p.AddArc(rect.Left,rect.Top,d,d,180,90); p.AddArc(rect.Right-d,rect.Top,d,d,270,90);
        p.AddArc(rect.Right-d,rect.Bottom-d,d,d,0,90); p.AddArc(rect.Left,rect.Bottom-d,d,d,90,90); p.CloseFigure();
        return p;
    }
}

internal sealed class RoundedButton : Button {
    bool hover;
    internal Color FocusBorderColor=Color.FromArgb(145,183,255);
    internal float FocusBorderWidth=2F;
    internal RoundedButton() { SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.Opaque,true); FlatStyle=FlatStyle.Flat; FlatAppearance.BorderSize=0; }
    protected override void OnMouseEnter(EventArgs e) { hover=true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover=false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnPaint(PaintEventArgs e) {
        // Button can skip background painting. Clear the full buffer here so
        // the area outside the rounded path always matches the dialog.
        e.Graphics.Clear(Parent == null ? Color.White : Parent.BackColor);
        float scale=e.Graphics.DpiX/96F;
        var rect=new RectangleF(scale,scale,ClientSize.Width-2*scale,ClientSize.Height-2*scale);
        Color fill=hover ? ControlPaint.Light(BackColor,.07F) : BackColor;
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        using(var path=RoundedDrawing.Path(rect,10*scale)) using(var brush=new SolidBrush(fill)) using(var pen=new Pen(Focused ? FocusBorderColor : FlatAppearance.BorderColor,Focused ? FocusBorderWidth*scale : scale)) {
            e.Graphics.FillPath(brush,path); e.Graphics.DrawPath(pen,path);
        }
        TextRenderer.DrawText(e.Graphics,Text,Font,ClientRectangle,ForeColor,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine);
    }
}

internal sealed class RoundedPanel : Panel {
    internal RoundedPanel() { SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true); }
    protected override void OnPaintBackground(PaintEventArgs e) {
        e.Graphics.Clear(Parent == null ? Color.White : Parent.BackColor); float scale=e.Graphics.DpiX/96F;
        var rect=new RectangleF(scale/2,scale/2,ClientSize.Width-scale,ClientSize.Height-scale);
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        using(var path=RoundedDrawing.Path(rect,14*scale)) using(var brush=new SolidBrush(BackColor)) using(var pen=new Pen(Color.FromArgb(198,220,250),scale)) { e.Graphics.FillPath(brush,path); e.Graphics.DrawPath(pen,path); }
    }
}

internal sealed class TrackedTitleLabel : Label {
    const TextFormatFlags GlyphFlags=TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix|TextFormatFlags.SingleLine;
    internal TrackedTitleLabel() { SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true); }
    int Gap(Graphics graphics) { return Math.Max(1,(int)Math.Round(1.25F*graphics.DpiX/96F)); }
    Size GlyphSize(Graphics graphics,string glyph) { return TextRenderer.MeasureText(graphics,glyph,Font,new Size(Int32.MaxValue,Int32.MaxValue),GlyphFlags); }
    internal Size MeasureTitle(Graphics graphics) {
        int width=0,height=0;
        for(int i=0;i<Text.Length;i++) { Size glyph=GlyphSize(graphics,Text.Substring(i,1)); width+=glyph.Width; height=Math.Max(height,glyph.Height); }
        return new Size(width+Math.Max(0,Text.Length-1)*Gap(graphics),height);
    }
    protected override void OnPaint(PaintEventArgs e) {
        Size title=MeasureTitle(e.Graphics); int x=(ClientSize.Width-title.Width)/2,gap=Gap(e.Graphics);
        for(int i=0;i<Text.Length;i++) {
            string glyph=Text.Substring(i,1); Size size=GlyphSize(e.Graphics,glyph);
            TextRenderer.DrawText(e.Graphics,glyph,Font,new Rectangle(x,0,size.Width,ClientSize.Height),ForeColor,GlyphFlags|TextFormatFlags.VerticalCenter);
            x+=size.Width+gap;
        }
    }
}

internal sealed class ExitConfirmationForm : DpiDialog {
    readonly Label countdown;
    readonly System.Windows.Forms.Timer timer;
    internal ExitConfirmationForm(Icon icon,Job job) {
        Text="退出定时关机"; Icon=icon; BackColor=Color.White; Font=new Font("Microsoft YaHei UI",11F);
        AutoScaleMode=AutoScaleMode.None;
        ClientSize=new Size(400,216); FormBorderStyle=FormBorderStyle.FixedDialog;
        MaximizeBox=false; MinimizeBox=false; ShowInTaskbar=false; TopMost=true; StartPosition=FormStartPosition.CenterScreen;
        var question=new TrackedTitleLabel { Text="取消关机并退出？",Font=new Font(Font.FontFamily,17F,FontStyle.Bold),ForeColor=Color.FromArgb(29,53,86),TextAlign=ContentAlignment.MiddleCenter,Bounds=new Rectangle(18,12,364,34) };
        var panel=new RoundedPanel { BackColor=Color.FromArgb(238,246,255),Bounds=new Rectangle(18,54,364,80) };
        var remaining=new Label { Text="距离关机还剩",BackColor=Color.Transparent,Font=new Font(Font.FontFamily,10.5F),ForeColor=Color.FromArgb(103,123,152),TextAlign=ContentAlignment.MiddleCenter,Bounds=new Rectangle(12,6,340,22) };
        countdown=new Label { Text=Job.Countdown(job.Remaining(TrayContext.Now())),BackColor=Color.Transparent,Font=new Font("Segoe UI",24F,FontStyle.Bold),ForeColor=Color.FromArgb(36,101,214),TextAlign=ContentAlignment.MiddleCenter,Bounds=new Rectangle(12,28,340,48) };
        panel.Controls.AddRange(new Control[]{remaining,countdown});
        var yes=new RoundedButton { Name="cancelExitButton",Text="是，取消并退出",Font=new Font(Font.FontFamily,11.5F,FontStyle.Bold),DialogResult=DialogResult.Yes,BackColor=Color.FromArgb(255,244,243),ForeColor=Color.FromArgb(194,56,62),Bounds=new Rectangle(18,146,176,48),FocusBorderColor=Color.FromArgb(225,143,148) };
        yes.FlatAppearance.BorderColor=Color.FromArgb(240,199,199);
        var no=new RoundedButton { Name="continueHideButton",Text="否，继续并隐藏",Font=new Font(Font.FontFamily,11.5F,FontStyle.Bold),DialogResult=DialogResult.No,BackColor=Color.FromArgb(40,107,232),ForeColor=Color.White,Bounds=new Rectangle(206,146,176,48),FocusBorderColor=Color.White,FocusBorderWidth=1F };
        no.FlatAppearance.BorderColor=Color.White;
        Controls.AddRange(new Control[]{question,panel,yes,no}); AcceptButton=no; CancelButton=no;
        Shown+=delegate { no.Focus(); };
        timer=new System.Windows.Forms.Timer { Interval=250 }; timer.Tick+=delegate { countdown.Text=Job.Countdown(job.Remaining(TrayContext.Now())); }; timer.Start();
    }
    protected override void OnFormClosing(FormClosingEventArgs e) { if(DialogResult != DialogResult.Yes) DialogResult=DialogResult.No; base.OnFormClosing(e); }
    protected override void Dispose(bool disposing) { if(disposing && timer != null) timer.Dispose(); base.Dispose(disposing); }
}

internal class DpiDialog : Form {
    bool sized;
    static void ScaleDescendants(Control parent,float factor) {
        foreach(Control child in parent.Controls) {
            var r=child.Bounds;
            child.Bounds=new Rectangle((int)Math.Round(r.X*factor),(int)Math.Round(r.Y*factor),(int)Math.Round(r.Width*factor),(int)Math.Round(r.Height*factor));
            ScaleDescendants(child,factor);
        }
    }
    protected override void OnLoad(EventArgs e) {
        if(!sized) {
            sized=true;
            using(var graphics=CreateGraphics()) {
                float factor=graphics.DpiX/96F;
                if(Math.Abs(factor-1F) > .01F) {
                    ScaleDescendants(this,factor);
                    ClientSize=new Size((int)Math.Round(ClientSize.Width*factor),(int)Math.Round(ClientSize.Height*factor));
                }
            }
            if(StartPosition == FormStartPosition.CenterScreen) {
                var area=Screen.FromControl(this).WorkingArea;
                Location=new Point(area.Left+(area.Width-Width)/2,area.Top+(area.Height-Height)/2);
            }
        }
        base.OnLoad(e);
    }
}

internal static class ExitPolicy {
    internal static bool Accept(Job job,long now,Func<bool> confirm) {
        return job == null || job.Status != "scheduled" || job.Deadline <= now || confirm();
    }
}

internal static class Program {
    [STAThread] static int Main(string[] args) {
        int bundleResult;
        if(BundledApplication.TryLaunch(args,out bundleResult)) return bundleResult;
        string dir=AppDomain.CurrentDomain.BaseDirectory;
        string data=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"ShutdownTimer","state.ini");
        string app=System.IO.Path.Combine(dir,"shutdown_timer.hta"),title="定时关机";
        bool hide=false,show=args.Length == 0,exit=false,ready=false,demo=false;
        string demoExitAnswer=null;
        for(int i=0;i<args.Length;i++) {
            if(args[i] == "--hide") hide=true;
            if(args[i] == "--show") show=true;
            if(args[i] == "--exit") exit=true;
            if(args[i] == "--ready") ready=true;
            if(args[i] == "--demo") demo=true;
            if(args[i] == "--demo-exit-answer" && i+1<args.Length) { string answer=args[++i]; if(answer == "yes" || answer == "no") demoExitAnswer=answer; }
            if(args[i] == "--state" && i+1<args.Length) data=System.IO.Path.GetFullPath(args[++i]);
            if(args[i] == "--app" && i+1<args.Length) app=System.IO.Path.GetFullPath(args[++i]);
            if(args[i] == "--title" && i+1<args.Length) title=args[++i];
            if(args[i] == "--selftest" && i+1<args.Length) { SelfTest(args[++i]); return 0; }
        }
        string key;
        using(var hash=SHA256.Create()) key=BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(Environment.UserName+"|"+data.ToLowerInvariant()))).Replace("-","").Substring(0,32);
        bool created;
        using(var mutex=new Mutex(true,"Local\\ShutdownTimer.Tray."+key,out created)) {
            if(!created) {
                string eventName=exit ? "Exit" : ready ? "Ready" : hide ? "Hide" : show ? "Show" : "";
                if(eventName.Length > 0) { for(int attempt=0;attempt<30;attempt++) { try { using(var signal=EventWaitHandle.OpenExisting("Local\\ShutdownTimer."+eventName+"."+key)) signal.Set(); break; } catch { Thread.Sleep(100); } } }
                return 0;
            }
            if(exit) {
                var saved=new Store(data).Read();
                if(saved == null || saved.Status != "scheduled" || saved.Deadline <= TrayContext.Now()) {
                    if(demo) File.AppendAllText(data+".qa-log","exit=idle"+Environment.NewLine);
                    mutex.ReleaseMutex(); return 0;
                }
            }
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            try { Application.Run(new TrayContext(data,app,title,key,hide,show,exit,ready,demo,demoExitAnswer)); }
            catch(Exception ex) { MessageBox.Show("后台提醒无法启动："+ex.Message,"定时关机",MessageBoxButtons.OK,MessageBoxIcon.Warning); return 1; }
            finally { mutex.ReleaseMutex(); }
        }
        return 0;
    }
    static void SelfTest(string output) {
        int count=0; Action<bool> check=delegate(bool ok) { if(!ok) throw new Exception("Self-test failed at "+count); count++; };
        var j=new Job { Token="1000-123",Status="scheduled",Started=1000000,Deadline=2800000,Boot="boot",Heartbeat=1 };
        var parsed=Job.Parse(j.Serialize()); check(parsed != null && parsed.Deadline == j.Deadline && parsed.Token == j.Token);
        check(Job.Parse("invalid") == null); check(Job.Parse(j.Serialize().Replace("deadline=2800000","deadline=0")) == null);
        check(j.Remaining(1000000) == 1800); check(j.Remaining(2799999) == 1); check(j.Remaining(2800000) == 0);
        check(j.Reminder(2499999) == ""); check(j.Reminder(2500000) == "five"); j.Warn5=1;
        check(j.Reminder(2600000) == ""); check(j.Reminder(2740000) == "final"); j.WarnFinal=1; check(j.Reminder(2750000) == "");
        j.Warn5=0; j.WarnFinal=0; check(j.Reminder(2740000) == "final"); check(j.Reminder(2800000) == "");
        j.Status="cancelled"; check(j.Reminder(2740000) == "");
        j.Status="scheduled"; j.Deadline=j.Started+60000; check(j.Reminder(j.Started+29000) == ""); check(j.Reminder(j.Started+30000) == "final");
        check(Job.Countdown(5400) == "01:30:00"); check(Job.Countdown(315360000) == "87600:00:00");
        string statePath=System.IO.Path.ChangeExtension(output,".state.ini");
        var store=new Store(statePath); var other=new Store(statePath); Job saved; int code,calls=0;
        j.Deadline=j.Started+1800000; j.Status="scheduled"; store.Write(j);
        check(store.Acquire()); check(!other.Acquire());
        check(other.Cancel(null,delegate { calls++; return 0; },out saved,out code) == "busy" && calls == 0);
        store.Release(); check(other.Acquire()); other.Release();
        check(store.Cancel("old-token",delegate { calls++; return 0; },out saved,out code) == "stale" && calls == 0 && store.Read().Status == "scheduled");
        check(store.Cancel(j.Token,delegate { calls++; return 5; },out saved,out code) == "error" && code == 5 && store.Read().Status == "scheduled");
        check(store.Cancel(j.Token,delegate { calls++; return 0; },out saved,out code) == "success" && saved.Status == "cancelled" && store.Read().Status == "cancelled");
        check(store.Cancel(j.Token,delegate { calls++; return 0; },out saved,out code) == "stale" && calls == 2);
        j.Status="scheduled"; store.Write(j);
        check(store.Cancel(j.Token,delegate { calls++; return 1116; },out saved,out code) == "success" && saved.Status == "cancelled");
        int prompts=0;
        Func<bool> yes=delegate { prompts++; return true; };
        Func<bool> no=delegate { prompts++; return false; };
        check(ExitPolicy.Accept(null,1000000,no) && prompts == 0);
        j.Status="cancelled"; check(ExitPolicy.Accept(j,1000000,no) && prompts == 0);
        j.Status="expired"; check(ExitPolicy.Accept(j,1000000,no) && prompts == 0);
        j.Status="scheduled"; check(ExitPolicy.Accept(j,j.Deadline,no) && prompts == 0);
        check(!ExitPolicy.Accept(j,1000000,no) && prompts == 1);
        check(ExitPolicy.Accept(j,1000000,yes) && prompts == 2);
        store.Write(j);
        check(store.Cancel(null,delegate { calls++; return 5; },out saved,out code,true) == "error" && store.Read().Status == "scheduled");
        check(store.Cancel(null,delegate { calls++; return 0; },out saved,out code,true) == "success" && store.Read().Status == "cancelled");
        int before=calls;
        check(store.Cancel(null,delegate { calls++; return 0; },out saved,out code,true) == "success" && calls == before);
        File.WriteAllText(output,"Passed "+count+" native helper model checks; no shutdown command executed.",Encoding.UTF8);
    }
}
