using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace ShutdownTimer {
    internal sealed class Plan {
        public string Token, Status, Reason = "";
        public long Started, Deadline, StartTick;
        public int Duration, Warn5, WarnFinal;
        public int Remaining(long now, long tick) {
            long ms = StartTick > 0 && tick >= StartTick ? (long)Duration * 1000 - (tick - StartTick) : Deadline - now;
            return (int)Math.Max(0, Math.Min(315360000, (ms + 999) / 1000));
        }
        public bool Active(long now, long tick) { return Status == "scheduled" && Remaining(now, tick) > 0; }
        public string Reminder(long now, long tick) {
            if (!Active(now, tick)) return "";
            int left = Remaining(now, tick);
            if (left <= Math.Min(60, Duration / 2) && WarnFinal == 0) return "final";
            if (Duration > 300 && left <= 300 && Warn5 == 0 && WarnFinal == 0) return "five";
            return "";
        }
        public static string Countdown(int seconds) {
            return (seconds / 3600).ToString("00") + ":" + (seconds / 60 % 60).ToString("00") + ":" + (seconds % 60).ToString("00");
        }
        public string Serialize() {
            return "version=2\r\ntoken=" + Token + "\r\nstatus=" + Status + "\r\nstarted=" + Started + "\r\ndeadline=" + Deadline +
                "\r\ntick=" + StartTick + "\r\nduration=" + Duration + "\r\nwarn5=" + Warn5 + "\r\nwarnFinal=" + WarnFinal + "\r\nreason=" + Reason + "\r\n";
        }
        public static Plan Parse(string text) {
            var d = new Dictionary<string, string>();
            // Explicit arrays bind to the .NET Framework overloads, including when built outside Windows.
            foreach (string line in text.Split(new[] { '\n' })) {
                int i = line.IndexOf('=');
                if (i > 0) d[line.Substring(0, i)] = line.Substring(i + 1).TrimEnd(new[] { '\r' });
            }
            string version, token, status, value;
            if (!d.TryGetValue("version", out version) || (version != "1" && version != "2") || !d.TryGetValue("token", out token) ||
                !System.Text.RegularExpressions.Regex.IsMatch(token, "^[0-9-]+$") || !d.TryGetValue("status", out status) ||
                (status != "scheduled" && status != "cancelled" && status != "expired")) return null;
            var p = new Plan { Token = token, Status = status };
            if (!d.TryGetValue("started", out value) || !long.TryParse(value, out p.Started) || p.Started < 0 ||
                !d.TryGetValue("deadline", out value) || !long.TryParse(value, out p.Deadline) || p.Deadline <= p.Started ||
                p.Deadline - p.Started > 315360000000L) return null;
            p.Duration = (int)((p.Deadline - p.Started) / 1000);
            if (version == "2") {
                if (!d.TryGetValue("tick", out value) || !long.TryParse(value, out p.StartTick) || p.StartTick < 0 ||
                    !d.TryGetValue("duration", out value) || !int.TryParse(value, out p.Duration) || p.Duration < 60 || p.Duration > 315360000 ||
                    (long)p.Duration * 1000 != p.Deadline - p.Started) return null;
            }
            if (d.TryGetValue("warn5", out value) && (!int.TryParse(value, out p.Warn5) || p.Warn5 < 0 || p.Warn5 > 1)) return null;
            if (d.TryGetValue("warnFinal", out value) && (!int.TryParse(value, out p.WarnFinal) || p.WarnFinal < 0 || p.WarnFinal > 1)) return null;
            if (d.TryGetValue("reason", out value)) p.Reason = value;
            return p;
        }
    }
    internal interface IPlanStore { Plan Read(); void Write(Plan p); }
    internal sealed class PlanStore : IPlanStore {
        readonly string path;
        public PlanStore(string file) { path = file; }
        public Plan Read() { return File.Exists(path) ? Plan.Parse(File.ReadAllText(path, Encoding.UTF8)) : null; }
        public void Write(Plan p) {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                File.WriteAllText(temporary, p.Serialize(), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            } finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
    internal interface IShutdown { int Schedule(int seconds); int Cancel(); }
    internal sealed class WindowsShutdown : IShutdown {
        public int Schedule(int seconds) {
            if (seconds < 60 || seconds > 315360000) throw new ArgumentOutOfRangeException("seconds");
            return Run("/s /t " + seconds.ToString(CultureInfo.InvariantCulture));
        }
        public int Cancel() { return Run("/a"); }
        static int Run(string arguments) {
            if (!Native.IsWindows) throw new PlatformNotSupportedException("此功能仅适用于 Windows。");
            string executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "shutdown.exe");
            using (var process = Process.Start(new ProcessStartInfo(executable, arguments) { UseShellExecute = false, CreateNoWindow = true })) {
                process.WaitForExit(); return process.ExitCode;
            }
        }
    }
    internal sealed class Operation {
        public bool Ok;
        public string Message;
        public static Operation Success() { return new Operation { Ok = true, Message = "" }; }
        public static Operation Error(string message) { return new Operation { Message = message }; }
    }
    internal sealed class TimerController {
        readonly IPlanStore store;
        readonly IShutdown shutdown;
        readonly Func<long> now, tick;
        public Plan Current;
        public TimerController(IPlanStore data, IShutdown executor, Func<long> utc, Func<long> uptime) {
            store = data; shutdown = executor; now = utc; tick = uptime;
            Current = store.Read();
            // A previous boot's timer is never scheduled again. The OS owns the shutdown.
            if (Current != null && Current.Status == "scheduled" && (Current.Started < now() - tick() - 60000 ||
                (Current.StartTick > 0 && tick() + 1000 < Current.StartTick))) {
                Current.Status = "cancelled"; Current.Reason = "newboot"; store.Write(Current);
            }
        }
        public bool Active { get { return Current != null && Current.Active(now(), tick()); } }
        public int Remaining { get { return Current == null ? 0 : Current.Remaining(now(), tick()); } }
        public Operation ScheduleMinutes(string text) {
            int minutes;
            if (!Int32.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out minutes) || minutes < 1 || minutes > 5256000)
                return Operation.Error("请输入 1～5256000 的整数分钟数，例如 45。");
            bool replaced = false;
            try {
                if (Active) {
                    int abort = shutdown.Cancel();
                    if (abort != 0 && abort != 1116) return Operation.Error("原计划取消失败。错误代码：" + abort);
                    Current.Status = "cancelled"; Current.Reason = "replaced"; replaced = true; store.Write(Current);
                }
                int seconds = checked(minutes * 60);
                long started = now(), startedTick = tick();
                int result = shutdown.Schedule(seconds);
                if (result != 0) return Operation.Error(result == 1190 ? "Windows 已有其他关机计划。可点击“取消关机”后重新设置。" :
                    (replaced ? "原计划已取消，新计划设置失败。" : "关机计划设置失败。") + "错误代码：" + result);
                var plan = new Plan { Token = started + "-" + (Guid.NewGuid().GetHashCode() & Int32.MaxValue), Status = "scheduled",
                    Started = started, Deadline = started + seconds * 1000L, StartTick = startedTick, Duration = seconds };
                try { store.Write(plan); Current = plan; }
                catch (Exception ex) {
                    int rollback = shutdown.Cancel();
                    if (rollback != 0 && rollback != 1116) {
                        Current = plan;
                        return Operation.Error("计划保存失败且撤销失败，请立即点击“取消关机”。错误代码：" + rollback + "\n" + ex.Message);
                    }
                    return Operation.Error("计划保存失败，已撤销本次关机。\n" + ex.Message);
                }
                return Operation.Success();
            } catch (Exception ex) { return Operation.Error((replaced ? "原计划已取消。" : "") + ex.Message); }
        }
        public Operation Cancel(string expectedToken) {
            if (expectedToken != null && (Current == null || Current.Token != expectedToken || !Active)) return Operation.Success();
            try {
                int result = shutdown.Cancel();
                if (result != 0 && result != 1116) return Operation.Error("取消关机失败。错误代码：" + result);
                if (Current != null) {
                    Current.Status = "cancelled"; Current.Reason = "manual";
                    try { store.Write(Current); } catch (Exception ex) { return Operation.Error("Windows 关机计划已取消，但状态保存失败。\n" + ex.Message); }
                }
                return Operation.Success();
            } catch (Exception ex) { return Operation.Error(ex.Message); }
        }
        public string Tick() {
            if (Current == null || Current.Status != "scheduled") return "";
            if (!Active) { Current.Status = "expired"; store.Write(Current); return ""; }
            string reminder = Current.Reminder(now(), tick());
            if (reminder == "five") Current.Warn5 = 1;
            if (reminder == "final") Current.WarnFinal = 1;
            if (reminder.Length > 0) store.Write(Current);
            return reminder;
        }
    }
}
