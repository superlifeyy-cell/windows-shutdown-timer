using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
namespace ShutdownTimer {
    internal sealed class FakeShutdown : IShutdown {
        public readonly List<string> Calls=new List<string>(); public int ScheduleResult,CancelResult;
        public int Schedule(int seconds) { Calls.Add("schedule:"+seconds); return ScheduleResult; }
        public int Cancel() { Calls.Add("cancel"); return CancelResult; }
    }
    internal sealed class FakeStore : IPlanStore {
        public Plan Saved; public bool Fail;
        public Plan Read() { return Saved; }
        public void Write(Plan p) { if(Fail) throw new IOException("disk full"); Saved=p; }
    }
    internal static class NativeTests {
        static int count;
        static void Check(bool value,string name) { if(!value) throw new Exception(name); count++; }
        static Color Rgb(int value) { return Color.FromArgb((value>>16)&255,(value>>8)&255,value&255); }
        static void Mouse(Control control,string method,EventArgs args) {
            typeof(RoundButton).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(control,new object[]{args});
        }
        static void ButtonPaint(RoundButton button,int fill,int text,string state) {
            using(var bitmap=new Bitmap(button.Width,button.Height)) {
                button.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));
                int inset=Math.Max(2,(int)Math.Round(3*button.ScaleFactor));
                Check(bitmap.GetPixel(bitmap.Width/2,inset)==Rgb(fill),state+" background: "+button.Text);
                int visible=0;
                Color ink=Rgb(text),background=Rgb(fill);
                for(int y=inset;y<bitmap.Height-inset;y++) for(int x=inset;x<bitmap.Width-inset;x++) {
                    Color pixel=bitmap.GetPixel(x,y);
                    int inkDistance=(pixel.R-ink.R)*(pixel.R-ink.R)+(pixel.G-ink.G)*(pixel.G-ink.G)+(pixel.B-ink.B)*(pixel.B-ink.B);
                    int backgroundDistance=(pixel.R-background.R)*(pixel.R-background.R)+(pixel.G-background.G)*(pixel.G-background.G)+(pixel.B-background.B)*(pixel.B-background.B);
                    // Include antialiased/ClearType glyph edges rather than
                    // requiring every visible glyph pixel to be pure RGB ink.
                    if(inkDistance*3<backgroundDistance) visible++;
                }
                if(visible<=20*button.ScaleFactor*button.ScaleFactor) bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"paint-failure.png"));
                Check(visible>20*button.ScaleFactor*button.ScaleFactor,state+" text remains visible: "+button.Text+" (pixels="+visible+", scale="+button.ScaleFactor+")");
            }
        }
        static void ButtonStates(RoundButton button) {
            int normalFill,normalText,hoverFill,hoverText,pressedFill,pressedText;
            switch(button.Tone) {
                case ButtonTone.Primary: normalFill=0x286be8;normalText=0xffffff;hoverFill=0x205fd5;hoverText=0xffffff;pressedFill=0x1b51bc;pressedText=0xffffff;break;
                case ButtonTone.Tray: normalFill=0xf4f8ff;normalText=0x2465d6;hoverFill=pressedFill=0xe7f0ff;hoverText=pressedText=0x2465d6;break;
                case ButtonTone.Cancel: normalFill=0xfff4f3;normalText=0xc2383e;hoverFill=0xffe9e7;hoverText=0xad2931;pressedFill=0xffdedb;pressedText=0x9c222a;break;
                default: normalFill=0xf0f5ff;normalText=0x2465d6;hoverFill=0x286be8;hoverText=0xffffff;pressedFill=0x205fd5;pressedText=0xffffff;break;
            }
            Rectangle bounds=button.Bounds; Font font=button.Font; string text=button.Text;
            Mouse(button,"OnMouseLeave",EventArgs.Empty); ButtonPaint(button,normalFill,normalText,"normal");
            Mouse(button,"OnMouseEnter",EventArgs.Empty); ButtonPaint(button,hoverFill,hoverText,"hover");
            Mouse(button,"OnMouseDown",new MouseEventArgs(MouseButtons.Left,1,button.Width/2,button.Height/2,0)); ButtonPaint(button,pressedFill,pressedText,"pressed");
            Mouse(button,"OnMouseLeave",EventArgs.Empty); ButtonPaint(button,normalFill,normalText,"leave");
            Check(button.Bounds==bounds && button.Font==font && button.Text==text,"button states retain text and geometry: "+text);
        }
        static void StatusGroup(MainForm window) {
            foreach(Control card in window.Controls) foreach(Control frame in card.Controls) if(frame is RoundPanel && frame.Controls.Count==4) {
                int top=Int32.MaxValue,bottom=0;
                foreach(Control line in frame.Controls) if(line.Text.Length>0) {top=Math.Min(top,line.Top);bottom=Math.Max(bottom,line.Bottom);}
                Check(Math.Abs(top-(frame.Height-bottom))<=(int)Math.Ceiling(2*window.LayoutScale),"visible status group vertically centered");
            }
        }
        static void WindowsShortcutIntegration() {
            if (!Native.IsWindows) { Console.WriteLine("Windows Shell shortcut integration checks skipped on this platform."); return; }
            string root=Path.Combine(Path.GetTempPath(),"ShutdownTimer-Shortcut-QA-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try {
                string destination=Path.Combine(root,"定时关机.lnk"),backups=Path.Combine(root,"backups");
                string first=Path.Combine(root,"旧版本","ShutdownTimer.exe"),next=Path.Combine(root,"新版本","ShutdownTimer (1).exe");
                Check(Shortcut.Ensure(first,destination,backups) && File.Exists(destination),"first manual launch creates a shortcut");
                string original=Convert.ToBase64String(File.ReadAllBytes(destination));
                Check(!Shortcut.Ensure(first,destination,backups) && Convert.ToBase64String(File.ReadAllBytes(destination))==original,"repeated launch leaves current shortcut unchanged");
                Check(!Directory.Exists(backups),"no backup created for an unchanged shortcut");
                Check(Shortcut.Ensure(next,destination,backups),"new version repairs old shortcut");
                Check(Directory.GetFiles(backups,"*.lnk").Length==1 && Convert.ToBase64String(File.ReadAllBytes(Directory.GetFiles(backups,"*.lnk")[0]))==original,"updated shortcut has one exact backup");
                Check(!Shortcut.Ensure(next,destination,backups),"new shortcut target, arguments and icon are current");
                File.Delete(destination);
                Check(Shortcut.Ensure(next,destination,backups) && File.Exists(destination),"missing shortcut is recreated on manual launch");
                Shortcut.Create(Path.Combine(root,"other.exe"),destination,backups);
                string foreign=Convert.ToBase64String(File.ReadAllBytes(destination));
                Check(!Shortcut.Ensure(next,destination,backups) && Convert.ToBase64String(File.ReadAllBytes(destination))==foreign,"automatic creation preserves an unrelated same-name shortcut");
            } finally { Directory.Delete(root,true); }
        }
        static void DeadlineDisplay() {
            long origin=1700000000375,originTick=3600000,now=origin,tick=originTick;
            var controller=new TimerController(new MemoryStore(),new DemoShutdown(),delegate{return now;},delegate{return tick;});
            using(var context=new TimerContext(controller,new StartupSetting(new DemoStartup(),@"C:\Tools\ShutdownTimer.exe"),false,null,true)) {
                var caption=(Control)typeof(MainForm).GetField("deadline",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(context.Window);
                var counter=(Control)typeof(MainForm).GetField("countdown",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(context.Window);
                Check(controller.ScheduleMinutes("30").Ok,"schedule stable deadline display plan");
                string timestamp=new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddMilliseconds(origin+1800000).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
                foreach(int elapsed in new[]{0,250,750,999,1000,1250,1999,2000,5250}) {
                    now=origin+elapsed;tick=originTick+elapsed;context.Window.RefreshStatus();
                    Check(caption.Text=="预计关机时间："+timestamp,"deadline caption stays fixed across fractional-second refreshes: "+elapsed);
                    Check(counter.Text==Plan.Countdown((1800000-elapsed+999)/1000),"countdown continues across deadline refreshes: "+elapsed);
                }
                string remaining=counter.Text;now+=3600000;context.Window.RefreshStatus();
                Check(caption.Text=="预计关机时间："+timestamp && counter.Text==remaining,"wall-clock adjustment does not rewrite the stored deadline or countdown");
                Check(controller.ScheduleMinutes("45").Ok,"replace stable deadline display plan");
                string replacement=new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddMilliseconds(now+2700000).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
                context.Window.RefreshStatus();
                Check(caption.Text=="预计关机时间："+replacement && replacement!=timestamp,"new plan updates the displayed deadline");
                now+=2700000;tick+=2700000;controller.Tick();context.Window.RefreshStatus();
                Check(caption.Text=="上次计划关机："+replacement && counter.Text=="00:00:00","expired plan retains the same recorded deadline");
                Check(controller.Cancel(null).Ok,"cancel stable deadline display plan");context.Window.RefreshStatus();
                Check(caption.Text=="" && counter.Text=="--:--:--","cancelled plan clears deadline and countdown");
                context.Exiting=true;context.ExitThread();
            }
        }
        [STAThread] public static int Main() {
            try {
                long now=1700000000000,tick=3600000;
                var store=new FakeStore(); var shutdown=new FakeShutdown();
                var controller=new TimerController(store,shutdown,delegate{return now;},delegate{return tick;});
                foreach(string invalid in new[]{"","0","-1","1.5","1e2","abc","5256001","2147483648"}) Check(!controller.ScheduleMinutes(invalid).Ok,"reject "+invalid);
                Check(shutdown.Calls.Count==0,"invalid input never calls OS");
                Operation first=controller.ScheduleMinutes("30"); Check(first.Ok,"30 minute plan: "+first.Message); Check(controller.Remaining==1800,"initial remaining");
                Check(shutdown.Calls.Count==1 && shutdown.Calls[0]=="schedule:1800","no automatic abort of unrelated timers");
                now+=3600000; tick+=1000; Check(controller.Remaining==1799,"wall clock change does not alter countdown");
                Plan parsed=Plan.Parse(store.Saved.Serialize()); Check(parsed!=null && parsed.StartTick==store.Saved.StartTick,"state roundtrip");
                string legacy="version=1\r\ntoken=1-2\r\nstatus=scheduled\r\nstarted=1700000000000\r\ndeadline=1700001800000\r\nwarn5=0\r\nwarnFinal=0\r\nreason=\r\n";
                Plan previous=Plan.Parse(legacy); Check(previous!=null && previous.Duration==1800 && previous.Reason=="","legacy CRLF state read");
                Check(Plan.Parse(legacy.Replace("\r\n","\n")).Deadline==previous.Deadline,"LF state read");
                Check(Plan.Parse("bad")==null,"invalid state"); Check(Plan.Parse(store.Saved.Serialize().Replace("duration=1800","duration=1"))==null,"reject inconsistent duration");
                Check(controller.Cancel("different-token").Ok && shutdown.Calls.Count==1,"stale reminder cannot cancel replacement");
                Check(controller.ScheduleMinutes("45").Ok && shutdown.Calls[1]=="cancel" && shutdown.Calls[2]=="schedule:2700","replace own plan");
                shutdown.CancelResult=5; Check(!controller.Cancel(null).Ok && controller.Active,"failed cancel retains active state");
                shutdown.CancelResult=1116; Check(controller.Cancel(null).Ok && !controller.Active,"already absent timer handled");
                shutdown.CancelResult=0; shutdown.ScheduleResult=1190; Check(!controller.ScheduleMinutes("5").Ok && shutdown.Calls[shutdown.Calls.Count-1]=="schedule:300","external plan is not aborted");
                shutdown.ScheduleResult=0; store.Fail=true; Check(!controller.ScheduleMinutes("5").Ok && shutdown.Calls[shutdown.Calls.Count-1]=="cancel","save failure rolls back OS timer"); store.Fail=false;
                Check(controller.ScheduleMinutes("1").Ok,"short plan"); now+=30000; tick+=30000;
                Check(controller.Tick()=="final","short reminder at halfway"); Check(controller.Tick()=="","reminder only once");
                tick+=30000; now+=30000; Check(controller.Tick()=="" && controller.Current.Status=="expired","deadline expires without executing shutdown again");
                Check(controller.ScheduleMinutes("30").Ok,"long reminder plan"); tick+=1500000; now+=1500000; Check(controller.Tick()=="five","five minute reminder");
                tick+=240000; now+=240000; Check(controller.Tick()=="final","one minute reminder");
                Check(Plan.Countdown(5400)=="01:30:00","countdown format");
                controller.Cancel(null);
                var fresh=new FakeStore(); var safe=new FakeShutdown();
                new TimerController(fresh,safe,delegate{return now;},delegate{return tick;}); Check(safe.Calls.Count==0,"launch never schedules shutdown");
                fresh.Saved=new Plan{Token="1-2",Status="scheduled",Started=now-tick-120000,Deadline=now+3600000,StartTick=tick+10000,Duration=3600};
                var reboot=new TimerController(fresh,safe,delegate{return now;},delegate{return tick;}); Check(!reboot.Active && safe.Calls.Count==0,"previous boot invalidated without rearming");
                var startupStore=new DemoStartup(); var startup=new StartupSetting(startupStore,@"C:\Tools\定时关机\ShutdownTimer.exe");
                Check(!startup.Enabled && startupStore.Read()==null,"startup default off"); startup.Set(true); Check(startup.Enabled && startupStore.Read()=="\"C:\\Tools\\定时关机\\ShutdownTimer.exe\" --startup","quoted unicode startup path");
                startup.Set(false); Check(startupStore.Read()==null,"startup disabled"); startupStore.Write("\"C:\\Old\\ShutdownTimer.exe\" --startup"); Check(startup.PreviousVersion && startupStore.Read()=="\"C:\\Old\\ShutdownTimer.exe\" --startup","read does not silently modify old startup");
                startup.Set(true); Check(startup.Enabled,"user explicitly migrates owned startup");
                startupStore.Write("\"C:\\other.exe\" --startup");
                foreach(bool enabled in new[]{false,true}) {bool rejected=false;try{startup.Set(enabled);}catch(InvalidOperationException){rejected=true;}Check(rejected && startupStore.Read()=="\"C:\\other.exe\" --startup","foreign startup entry remains unchanged");}
                startupStore.Delete();
                string exe=@"C:\Tools\ShutdownTimer.exe";
                Check(Shortcut.OwnsTarget(exe,"",exe),"recognize current shortcut target");
                Check(Shortcut.OwnsTarget(@"C:\Old\ShutdownTimer.exe","定时关机：倒计时、托盘和关机前提醒",exe),"recognize older own shortcut even when target is missing");
                Check(Shortcut.OwnsTarget(@"C:\Downloads\ShutdownTimer (1).exe","定时关机：倒计时、托盘和关机前提醒",exe),"recognize download duplicate filename");
                Check(!Shortcut.OwnsTarget(@"C:\Other\ShutdownTimer.exe","其他程序",exe),"protect same-name unrelated shortcut");
                Check(!Shortcut.OwnsTarget(@"C:\Other\other.exe","定时关机",exe),"description alone does not authorize replacing another program");
                Check(!Shortcut.OwnsTarget(null,"定时关机",exe),"protect empty shortcut target");
                string ownDirectory=Path.GetDirectoryName(exe);
                Check(!Shortcut.NeedsRefresh(exe,"--show",ownDirectory,exe,0,exe),"current shortcut is not rewritten every launch");
                Check(Shortcut.NeedsRefresh(@"C:\Old\ShutdownTimer.exe","--show",ownDirectory,exe,0,exe),"refresh previous EXE target");
                Check(Shortcut.NeedsRefresh(exe,"",ownDirectory,exe,0,exe),"repair launch arguments");
                Check(Shortcut.NeedsRefresh(exe,"--show",ownDirectory,@"C:\Old\ShutdownTimer.exe",0,exe),"repair icon location");
                Check(Shortcut.NeedsRefresh(exe,"--show",ownDirectory,exe,1,exe),"repair icon index");
                string legacyTarget=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ShutdownTimer","versions","1234567890abcdef12345678","shutdown_timer_tray.exe");
                Check(Shortcut.OwnsTarget(legacyTarget,"",exe),"recognize original script-version tray shortcut");
                Check(!Shortcut.OwnsTarget(legacyTarget.Replace("1234567890abcdef12345678","unrelated"),"",exe),"protect unrelated file in legacy directory");
                WindowsShortcutIntegration();
                string path=Path.Combine(Path.GetTempPath(),"ShutdownTimer-QA-"+Guid.NewGuid().ToString("N"),"state.ini");
                try {
                    var disk=new PlanStore(path); disk.Write(parsed);
                    File.WriteAllText(path,legacy);
                    var restored=new TimerController(disk,safe,delegate{return 1700000000000L;},delegate{return 3600000L;});
                    Check(restored.Active && restored.Remaining==1800 && safe.Calls.Count==0,"startup reads existing legacy file without scheduling shutdown");
                    disk.Write(parsed); Check(disk.Read().Token==parsed.Token,"atomic state replace");
                } finally { Directory.Delete(Path.GetDirectoryName(path),true); }
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                DeadlineDisplay();
                using(var context=new TimerContext(new TimerController(new MemoryStore(),new DemoShutdown(),Native.Now,Native.Tick),new StartupSetting(new DemoStartup(),@"C:\Tools\ShutdownTimer.exe"),false,null,true)) {
                    Check(context.Window.FormBorderStyle==FormBorderStyle.FixedSingle && !context.Window.MaximizeBox,"fixed main window");
                    foreach(float scale in new[]{1F,1.25F,1.5F,2F,3F,1F}) {
                        context.Window.SizeForScale(scale); Application.DoEvents(); Check(context.Window.ClientSize==new Size((int)Math.Round(294*scale),(int)Math.Round(425*scale)),"fixed client dimensions scaled only by DPI");
                        Check(Math.Abs(context.Window.LayoutScale-scale)<.001F,"layout does not derive scale from screen resolution");
                        foreach(Control card in context.Window.Controls) foreach(Control child in card.Controls) {
                            Check(child.Left>=0 && child.Top>=0 && child.Right<=card.ClientSize.Width && child.Bottom<=card.ClientSize.Height,"control inside card: "+child.GetType().Name);
                            if(child is RoundButton || child is PixelLabel) {
                                Size measured=TextRenderer.MeasureText(child.Text,child.Font,new Size(9999,9999),TextFormatFlags.NoPadding|TextFormatFlags.SingleLine);
                                Check(measured.Width<=child.Width,"text width: "+child.Text); Check(measured.Height<=child.Height+2,"text height: "+child.Text);
                            }
                        }
                        var cards=context.Window.Controls; var panel=cards[0];
                        Control input=null,set=null,hide=null,cancel=null,heading=null,picture=null;
                        foreach(Control child in panel.Controls) {
                            if(child is RoundPanel && child.Controls.Count==2) input=child;
                            if(child.Text=="设置关机") set=child;
                            if(child.Text=="隐藏到托盘") hide=child;
                            if(child.Text=="取消关机") cancel=child;
                            if(child.Text=="定时关机") heading=child;
                            if(child is ClockLogo) picture=child;
                        }
                        Check(input.Width==hide.Width && set.Width==cancel.Width && hide.Width==cancel.Width,"common column widths");
                        Check(input.Left==hide.Left && set.Left==cancel.Left && hide.Left<cancel.Left,"column positions");
                        Check(Math.Abs((heading.Top*2+heading.Height)-(picture.Top*2+picture.Height))<=2,"title and icon centers");
                        Check(panel.Height-hide.Bottom<=(int)Math.Ceiling(16*scale),"compact bottom margin");
                        foreach(Control child in input.Controls) if(child is TextBox) Check(child.Font.Bold && child.ForeColor==Color.FromArgb(32,50,77),"bold dark-blue custom input");
                        foreach(Control child in panel.Controls) {
                            var button=child as RoundButton; if(button!=null) ButtonStates(button);
                            if(child is RoundPanel && child.Controls.Count==4) {
                                Check(child.Height==(int)Math.Round(108*scale),"status frame retains its fixed dimensions");
                                foreach(Control line in child.Controls) {
                                    Check(line.Top>=0 && line.Bottom<=child.Height,"status line remains inside frame");
                                    if(line.Text=="--:--:--") {
                                        Check(line.Top==(int)Math.Round(38*scale),"idle countdown shifted down to center content group");
                                        Check(Math.Abs((int)Math.Round(18*scale)-(child.Height-line.Bottom))<=(int)Math.Ceiling(2*scale),"balanced idle top and bottom padding");
                                    }
                                }
                            }
                        }
                        Control quick=null,customSection=null;
                        foreach(Control child in panel.Controls) { if(child.Text=="3 小时") quick=child;if(child.Text=="自定义关机时间") customSection=child; }
                        Check(customSection.Top-quick.Bottom>=(int)Math.Floor(18*scale),"slightly increased section gap");
                        foreach(Control child in panel.Controls) if(child.Text=="快速设定") {
                            Control frame=null;foreach(Control candidate in panel.Controls) if(candidate is RoundPanel && candidate.Controls.Count==4) frame=candidate;
                            Check(child.Top-frame.Bottom>=(int)Math.Floor(4*scale),"slightly increased gap above quick settings");
                        }
                        StatusGroup(context.Window);
                        Check(context.Controller.ScheduleMinutes("30").Ok,"active layout plan"); context.Window.RefreshStatus(); StatusGroup(context.Window);
                        var layout=new List<Rectangle>(); foreach(Control child in panel.Controls) layout.Add(child.Bounds);
                        context.Controller.Cancel(null); context.Window.RefreshStatus(); StatusGroup(context.Window);
                        int position=0; foreach(Control child in panel.Controls) Check(child.Bounds==layout[position++],"idle and active retain the same layout");
                    }
                    Check(context.Controller.ScheduleMinutes("1").Ok,"urgent layout plan");context.Window.RefreshStatus();StatusGroup(context.Window);
                    context.Controller.Cancel(null);context.Window.RefreshStatus();
                    context.HideMain(); Check(!context.Window.Visible,"hide to tray"); context.ShowMain(); Check(context.Window.Visible,"restore from tray");
                    Check(context.Controller.ScheduleMinutes("1").Ok,"preview uses fake scheduler"); context.Controller.Cancel(null); context.Exiting=true; context.ExitThread();
                }
                Console.WriteLine("Passed "+count+" checks; no real shutdown command or startup registry write executed."); return 0;
            } catch(Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
    }
}
