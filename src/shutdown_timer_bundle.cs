using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;

// Versioned folders keep a running countdown on its current build. Installation
// never touches the shared state or issues a Windows shutdown/cancellation command.
internal static class BundledApplication {
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
    static extern bool MoveFileEx(string source,string destination,int flags);
    static readonly string[] Assets={"shutdown_timer.hta","shutdown_timer_common.js","shutdown_timer.ico"};
    const string InstalledExe="shutdown_timer_tray.exe";
    internal static bool HasPayload() {
        return Assembly.GetExecutingAssembly().GetManifestResourceInfo("ShutdownTimer.shutdown_timer.hta") != null;
    }
    internal static bool TryLaunch(string[] args,out int result) {
        result=0;
        if(!HasPayload()) return false;
        // QA/custom-host invocations must never install into real user folders.
        foreach(string arg in args) {
            if(arg == "--demo" || arg == "--selftest" || arg == "--app" || arg == "--state" || arg == "--title") return false;
        }
        try {
            string source=Assembly.GetExecutingAssembly().Location;
            string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ShutdownTimer");
            string installed=Prepare(root,source);
            bool external=!String.Equals(Path.GetFullPath(source),Path.GetFullPath(installed),StringComparison.OrdinalIgnoreCase);
            bool show=args.Length == 0 || Array.IndexOf(args,"--show") >= 0;
            if(external && show) {
                try { EnsureShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),"定时关机.lnk"),installed,Path.Combine(root,"shortcut-backups")); }
                catch(Exception) { MessageBox.Show("程序已准备完成，但未能创建桌面快捷方式。以后仍可双击本文件打开。","定时关机",MessageBoxButtons.OK,MessageBoxIcon.Information); }
            }
            if(!external) return false;
            Process.Start(new ProcessStartInfo(installed,Arguments(args)) { WorkingDirectory=Path.GetDirectoryName(installed),UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden });
            return true;
        } catch(Exception ex) {
            MessageBox.Show("无法准备程序文件，请检查当前用户目录是否可写。\n"+ex.Message,"定时关机",MessageBoxButtons.OK,MessageBoxIcon.Warning);
            result=1; return true;
        }
    }
    internal static string Prepare(string root,string source) {
        root=Path.GetFullPath(root);
        byte[] executable=File.ReadAllBytes(source);
        string identity=Hash(executable).Substring(0,24);
        string directory=Path.Combine(Path.Combine(root,"versions"),identity);
        string target=Path.Combine(directory,InstalledExe);
        string lockKey=Hash(Encoding.UTF8.GetBytes(root.ToLowerInvariant())).Substring(0,32);
        using(var mutex=new Mutex(false,"Local\\ShutdownTimer.Install."+lockKey)) {
            bool owns=false;
            try {
                try { owns=mutex.WaitOne(15000); } catch(AbandonedMutexException) { owns=true; }
                if(!owns) throw new IOException("另一次安装正在进行，请稍后重试。");
                Directory.CreateDirectory(directory);
                foreach(string name in Assets) {
                    using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("ShutdownTimer."+name)) {
                        if(stream == null) throw new InvalidDataException("程序缺少内置文件："+name);
                        using(var memory=new MemoryStream()) { stream.CopyTo(memory); WriteIfChanged(Path.Combine(directory,name),memory.ToArray()); }
                    }
                }
                WriteIfChanged(target,executable);
                return target;
            } finally { if(owns) mutex.ReleaseMutex(); }
        }
    }
    static void WriteIfChanged(string path,byte[] bytes) {
        if(File.Exists(path) && Hash(File.ReadAllBytes(path)) == Hash(bytes)) return;
        string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {
            File.WriteAllBytes(temporary,bytes);
            if(Hash(File.ReadAllBytes(temporary)) != Hash(bytes)) throw new IOException("程序文件校验失败。");
            // Atomic rename avoids ReplaceFile's extra ACL/metadata permissions.
            if(!MoveFileEx(temporary,path,1|8)) throw new IOException("无法写入程序文件："+Path.GetFileName(path),new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
        } finally { if(File.Exists(temporary)) File.Delete(temporary); }
    }
    static string Hash(byte[] bytes) {
        using(var sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();
    }
    internal static string Arguments(string[] args) {
        var parts=new string[args.Length];
        for(int i=0;i<args.Length;i++) parts[i]=Quote(args[i]);
        return String.Join(" ",parts);
    }
    static string Quote(string value) {
        var text=new StringBuilder("\""); int slashes=0;
        foreach(char c in value) {
            if(c == '\\') { slashes++; continue; }
            text.Append('\\',slashes*(c == '"' ? 2 : 1)+(c == '"' ? 1 : 0));
            text.Append(c); slashes=0;
        }
        text.Append('\\',slashes*2); text.Append('"'); return text.ToString();
    }
    internal static void EnsureShortcut(string shortcut,string target,string backups) {
        object shell=null,link=null;
        try {
            var type=Type.GetTypeFromProgID("WScript.Shell",true);
            shell=Activator.CreateInstance(type);
            link=type.InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{shortcut});
            Type linkType=link.GetType();
            string existing=Convert.ToString(linkType.InvokeMember("TargetPath",BindingFlags.GetProperty,null,link,null));
            string arguments=Convert.ToString(linkType.InvokeMember("Arguments",BindingFlags.GetProperty,null,link,null));
            string icon=Convert.ToString(linkType.InvokeMember("IconLocation",BindingFlags.GetProperty,null,link,null));
            if(File.Exists(shortcut) && String.Equals(existing,target,StringComparison.OrdinalIgnoreCase) && arguments == "--show" && String.Equals(icon,target+",0",StringComparison.OrdinalIgnoreCase)) return;
            if(File.Exists(shortcut)) {
                Directory.CreateDirectory(backups);
                File.Copy(shortcut,Path.Combine(backups,"定时关机_"+DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff")+"_"+Guid.NewGuid().ToString("N")+".lnk"));
            }
            linkType.InvokeMember("TargetPath",BindingFlags.SetProperty,null,link,new object[]{target});
            linkType.InvokeMember("Arguments",BindingFlags.SetProperty,null,link,new object[]{"--show"});
            linkType.InvokeMember("WorkingDirectory",BindingFlags.SetProperty,null,link,new object[]{Path.GetDirectoryName(target)});
            linkType.InvokeMember("IconLocation",BindingFlags.SetProperty,null,link,new object[]{target+",0"});
            linkType.InvokeMember("Description",BindingFlags.SetProperty,null,link,new object[]{"定时关机：设置时间、托盘倒计时和关机前提醒"});
            linkType.InvokeMember("Save",BindingFlags.InvokeMethod,null,link,null);
        } finally {
            if(link != null && Marshal.IsComObject(link)) Marshal.FinalReleaseComObject(link);
            if(shell != null && Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
        }
    }
}
