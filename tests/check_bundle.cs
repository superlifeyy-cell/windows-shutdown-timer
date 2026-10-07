using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;

internal static class BundleChecks {
    static Type bundle;
    static int count;
    static object Invoke(string method,params object[] args) { return bundle.GetMethod(method,BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,args); }
    static void Check(bool value,string message) { if(!value) throw new Exception(message); count++; }
    static string Hash(byte[] bytes) { using(var sha=SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)); }
    [STAThread] static int Main(string[] args) {
        try {
            string source=Path.GetFullPath(args[0]),workspace=Path.GetFullPath(args[1]);
            Directory.CreateDirectory(workspace);
            var assembly=Assembly.LoadFrom(source); bundle=assembly.GetType("BundledApplication",true);
            Check((bool)Invoke("HasPayload"),"Embedded payload not found.");
            string root=Path.Combine(workspace,"用户 安装目录");
            string sentinel=Path.Combine(workspace,"state.ini"); File.WriteAllText(sentinel,"scheduled-plan-preserve-me");
            string installed=(string)Invoke("Prepare",root,source);
            string directory=Path.GetDirectoryName(installed);
            Check(installed.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),"Extraction escaped its installation root.");
            Check(Directory.GetFiles(directory).Length == 4,"Unexpected runtime files.");
            string[] assets={"shutdown_timer.hta","shutdown_timer_common.js","shutdown_timer.ico"};
            foreach(string name in assets) {
                using(var stream=assembly.GetManifestResourceStream("ShutdownTimer."+name)) using(var memory=new MemoryStream()) {
                    stream.CopyTo(memory); Check(Hash(memory.ToArray()) == Hash(File.ReadAllBytes(Path.Combine(directory,name))),"Extracted bytes differ: "+name);
                }
            }
            Check(Hash(File.ReadAllBytes(source)) == Hash(File.ReadAllBytes(installed)),"Installed executable differs.");
            var stamps=new Dictionary<string,DateTime>(); foreach(string path in Directory.GetFiles(directory)) stamps[path]=File.GetLastWriteTimeUtc(path);
            Check((string)Invoke("Prepare",root,source) == installed,"Reinstall changed the version directory.");
            foreach(var pair in stamps) Check(pair.Value == File.GetLastWriteTimeUtc(pair.Key),"Reinstall rewrote an unchanged file.");
            File.WriteAllText(Path.Combine(directory,assets[0]),"damaged"); File.Delete(Path.Combine(directory,assets[1]));
            Invoke("Prepare",root,source);
            foreach(string name in new string[]{assets[0],assets[1]}) {
                using(var stream=assembly.GetManifestResourceStream("ShutdownTimer."+name)) using(var memory=new MemoryStream()) { stream.CopyTo(memory); Check(Hash(memory.ToArray()) == Hash(File.ReadAllBytes(Path.Combine(directory,name))),"Repair failed: "+name); }
            }
            string relocated=Path.Combine(workspace,"移动后的程序.exe"); File.Copy(source,relocated,true);
            Check((string)Invoke("Prepare",root,relocated) == installed,"Moving the installer created a duplicate installation.");
            Check(File.ReadAllText(sentinel) == "scheduled-plan-preserve-me","Installer modified a plan.");
            Check(Directory.GetFiles(directory,"*.tmp").Length == 0,"Temporary payload files remain.");
            string shortcut=Path.Combine(workspace,"定时关机.lnk"),backups=Path.Combine(workspace,"shortcut-backups");
            Invoke("EnsureShortcut",shortcut,installed,backups);
            var shellType=Type.GetTypeFromProgID("WScript.Shell",true); object shell=Activator.CreateInstance(shellType),link=null;
            try {
                link=shellType.InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{shortcut});
                var t=link.GetType();
                Check(Convert.ToString(t.InvokeMember("TargetPath",BindingFlags.GetProperty,null,link,null)) == installed,"Shortcut targets the wrong program.");
                Check(Convert.ToString(t.InvokeMember("Arguments",BindingFlags.GetProperty,null,link,null)) == "--show","Shortcut does not restore the main window.");
                Check(Convert.ToString(t.InvokeMember("WorkingDirectory",BindingFlags.GetProperty,null,link,null)) == directory,"Shortcut working directory is incorrect.");
                Check(Convert.ToString(t.InvokeMember("IconLocation",BindingFlags.GetProperty,null,link,null)) == installed+",0","Shortcut does not use the built-in icon.");
            } finally { if(link != null) Marshal.FinalReleaseComObject(link); Marshal.FinalReleaseComObject(shell); }
            DateTime shortcutStamp=File.GetLastWriteTimeUtc(shortcut); Invoke("EnsureShortcut",shortcut,installed,backups);
            Check(File.GetLastWriteTimeUtc(shortcut) == shortcutStamp && !Directory.Exists(backups),"Identical shortcut was needlessly replaced.");
            string other=Path.Combine(workspace,"previous-version.exe"); File.Copy(source,other,true);
            Invoke("EnsureShortcut",shortcut,other,backups);
            Check(Directory.GetFiles(backups,"*.lnk").Length == 1,"Existing shortcut was not backed up.");
            foreach(string mode in new string[]{"--demo","--selftest","--app","--state","--title"}) {
                object[] values={new string[]{mode},0}; bool handled=(bool)Invoke("TryLaunch",values);
                Check(!handled,"QA command entered real-user installation: "+mode);
            }
            var failures=new List<Exception>(); var workers=new Thread[4];
            for(int i=0;i<workers.Length;i++) { workers[i]=new Thread(delegate() { try { Invoke("Prepare",root,source); } catch(Exception ex) { lock(failures) failures.Add(ex); } }); workers[i].Start(); }
            foreach(var worker in workers) Check(worker.Join(20000),"Concurrent install did not finish.");
            Check(failures.Count == 0 && Directory.GetFiles(directory).Length == 4,"Concurrent install failed or left extra files.");
            string report=count+" single executable checks passed: embedded assets, install, repair, relocation, shortcut, QA isolation and concurrent launch. All files stayed in the QA workspace.";
            File.WriteAllText(Path.Combine(workspace,"bundle-checks.txt"),report);
            File.WriteAllText(Path.Combine(workspace,"bundle-source-sha256.txt"),Hash(File.ReadAllBytes(source)).Replace("-","").ToLowerInvariant());
            File.WriteAllText(Path.Combine(workspace,"installed-path.txt"),installed);
            Console.WriteLine(report); return 0;
        } catch(Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
