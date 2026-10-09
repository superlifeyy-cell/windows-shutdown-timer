using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ShutdownTimer {
    internal static class Native {
        public static bool IsWindows { get { return Environment.OSVersion.Platform == PlatformID.Win32NT; } }
        [DllImport("kernel32.dll")] static extern ulong GetTickCount64();
        [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr window);
        public static long Now() { return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds; }
        public static long Tick() { return IsWindows ? (long)GetTickCount64() : (long)(Environment.TickCount & Int32.MaxValue); }
        public static float Dpi(Control control) {
            if (IsWindows) { try { uint dpi = GetDpiForWindow(control.Handle); if (dpi > 0) return dpi / 96F; } catch (EntryPointNotFoundException) {} }
            using (var g = control.CreateGraphics()) return g.DpiX / 96F;
        }
        [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
    }
    internal interface IStartupStore { string Read(); void Write(string command); void Delete(); }
    internal sealed class RegistryStartup : IStartupStore {
        const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string Name = "WindowsShutdownTimer";
        public string Read() { using (var key = Registry.CurrentUser.OpenSubKey(Key)) return key == null ? null : key.GetValue(Name) as string; }
        public void Write(string command) { using (var key = Registry.CurrentUser.CreateSubKey(Key)) key.SetValue(Name, command, RegistryValueKind.String); }
        public void Delete() { using (var key = Registry.CurrentUser.OpenSubKey(Key, true)) { if (key != null) key.DeleteValue(Name, false); } }
    }
    internal sealed class StartupSetting {
        readonly IStartupStore store;
        readonly string path;
        public StartupSetting(IStartupStore data, string executable) { store = data; path = executable; }
        public string Command { get { return "\"" + path + "\" --startup"; } }
        public bool Enabled { get { return String.Equals(store.Read(), Command, StringComparison.OrdinalIgnoreCase); } }
        bool Owns(string value) {
            if (String.Equals(value, Command, StringComparison.OrdinalIgnoreCase)) return true;
            if (String.IsNullOrEmpty(value)) return false;
            if (System.Text.RegularExpressions.Regex.IsMatch(value, "^\"[^\"\\r\\n]+[\\\\/]ShutdownTimer\\.exe\" --startup$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return true;
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ShutdownTimer", "versions");
            string prefix = "\"" + root + Path.DirectorySeparatorChar;
            return value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && System.Text.RegularExpressions.Regex.IsMatch(value.Substring(prefix.Length), "^[0-9a-f]{24}[\\\\/]shutdown_timer_tray\\.exe\" --startup$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        public void Set(bool enabled) {
            string value = store.Read();
            if (value != null && !Owns(value)) throw new InvalidOperationException("同名启动项指向其他程序，未修改该启动项。");
            if (enabled) {
                if (Command.Length > 260) throw new InvalidOperationException("程序路径过长，无法设置开机自启动。");
                if (!String.Equals(value, Command, StringComparison.OrdinalIgnoreCase)) store.Write(Command);
            } else if (value != null) store.Delete();
        }
        // Reading never writes, migrates or enables a startup item.
        public bool PreviousVersion { get { string value = store.Read(); return Owns(value) && !String.Equals(value, Command, StringComparison.OrdinalIgnoreCase); } }
    }
    internal static class Shortcut {
        // Direct Windows Shell COM, without a script host.
        [ComImport, Guid("00021401-0000-0000-C000-000000000046")] class ShellLink {}
        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
        interface IShellLink {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int count, IntPtr data, uint flags);
            void GetIDList(out IntPtr list); void SetIDList(IntPtr list);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder description, int count);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string description);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int count);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int count);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
            void GetHotkey(out short hotkey); void SetHotkey(short hotkey);
            void GetShowCmd(out int command); void SetShowCmd(int command);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int count, out int index);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
            void Resolve(IntPtr window, uint flags); void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
        }
        static string Destination { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "定时关机.lnk"); } }
        static string Backups { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ShutdownTimer", "shortcut-backups"); } }
        internal static bool OwnsTarget(string target, string description, string executable) {
            if (String.IsNullOrEmpty(target)) return false;
            if (String.Equals(target, executable, StringComparison.OrdinalIgnoreCase)) return true;
            string normalized = target.Replace('/', '\\');
            string filename = normalized.Substring(normalized.LastIndexOf('\\') + 1);
            bool timerName = System.Text.RegularExpressions.Regex.IsMatch(filename, @"^ShutdownTimer(?:\s?\([0-9]+\))?\.exe$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (timerName && (description ?? "").IndexOf("定时关机", StringComparison.Ordinal) >= 0) return true;
            string legacyRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ShutdownTimer", "versions").Replace('/', '\\') + "\\";
            if (normalized.StartsWith(legacyRoot, StringComparison.OrdinalIgnoreCase) &&
                System.Text.RegularExpressions.Regex.IsMatch(normalized.Substring(legacyRoot.Length), @"^[0-9a-f]{24}\\shutdown_timer_tray\.exe$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) return true;
            if (!File.Exists(target)) return false;
            try {
                var version = System.Diagnostics.FileVersionInfo.GetVersionInfo(target);
                return version.ProductName == "Windows Shutdown Timer" || version.FileDescription == "定时关机";
            } catch (IOException) { return false; }
        }
        internal static bool NeedsRefresh(string target, string arguments, string directory, string icon, int iconIndex, string executable) {
            return !String.Equals(target, executable, StringComparison.OrdinalIgnoreCase) || arguments != "--show" ||
                !String.Equals(directory, Path.GetDirectoryName(executable), StringComparison.OrdinalIgnoreCase) ||
                !String.Equals(icon, executable, StringComparison.OrdinalIgnoreCase) || iconIndex != 0;
        }
        public static bool Ensure(string executable) { return Ensure(executable, Destination, Backups); }
        internal static bool Ensure(string executable, string destination, string backups) {
            if (!File.Exists(destination)) { Create(executable, destination, backups); return true; }
            var link = (IShellLink)new ShellLink();
            bool update = false;
            try {
                // Load without resolving/launching the old executable. Broken
                // shortcuts to a known older version can still be repaired.
                ((System.Runtime.InteropServices.ComTypes.IPersistFile)link).Load(destination, 0);
                var target = new StringBuilder(32768); var description = new StringBuilder(1024);
                var arguments = new StringBuilder(1024); var directory = new StringBuilder(32768); var icon = new StringBuilder(32768); int iconIndex;
                link.GetPath(target, target.Capacity, IntPtr.Zero, 4); link.GetDescription(description, description.Capacity);
                link.GetArguments(arguments, arguments.Capacity); link.GetWorkingDirectory(directory, directory.Capacity); link.GetIconLocation(icon, icon.Capacity, out iconIndex);
                update = OwnsTarget(target.ToString(), description.ToString(), executable) &&
                    NeedsRefresh(target.ToString(), arguments.ToString(), directory.ToString(), icon.ToString(), iconIndex, executable);
            } finally { Marshal.FinalReleaseComObject(link); }
            if (update) Create(executable, destination, backups);
            return update;
        }
        public static void Create(string executable) { Create(executable, Destination, Backups); }
        internal static void Create(string executable, string destination, string backups) {
            if (File.Exists(destination)) {
                Directory.CreateDirectory(backups);
                File.Copy(destination, Path.Combine(backups, "定时关机-" + DateTime.Now.ToString("yyyyMMddHHmmssfff") + "-" + Guid.NewGuid().ToString("N") + ".lnk"));
            }
            var link = (IShellLink)new ShellLink();
            try {
                link.SetPath(executable); link.SetArguments("--show"); link.SetWorkingDirectory(Path.GetDirectoryName(executable));
                link.SetIconLocation(executable, 0); link.SetDescription("定时关机：倒计时、托盘和关机前提醒");
                ((System.Runtime.InteropServices.ComTypes.IPersistFile)link).Save(destination, true);
            } finally { Marshal.FinalReleaseComObject(link); }
        }
    }
}
