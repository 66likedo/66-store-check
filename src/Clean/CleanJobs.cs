using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace StoreCheck
{
    public enum Risk { Low = 0, Mid = 1, High = 2 }

    internal enum CleanKind { TempFiles, RecycleBin, RegRun, Shortcut, BrowserCache, Thumbs, UpdateCache, Dumps }

    internal class CleanJob
    {
        public int Id;
        public string Name = "";
        public string Desc = "";
        public Risk Risk = Risk.Low;
        public bool NeedsAdmin = false;
        public CleanKind Kind;
        public string[] Roots = new string[0];
        public string RootLabel = "";
        public bool AgeGuard = true;    // 只处理 30 分钟以前的文件（临时目录用，防止动到正在安装的临时文件）

        // 扫描结果
        public long Bytes = 0;
        public int Files = 0;
        public int Skipped = 0;
        public string Note = "";

        // 执行结果
        public bool Selected = false;
        public long DoneBytes = 0;
        public int DoneFiles = 0;
        public int FailFiles = 0;
        public List<string> FailSample = new List<string>();
        public List<string> Removed = new List<string>();
        public string Result = "";
    }

    /// 清理/修复执行引擎：只动白名单目录，删除前校验路径，配置类先备份
    internal static class Cleaner
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHEmptyRecycleBin(IntPtr hwnd, string pszRootPath, uint dwFlags);

        private const uint SHERB_NOCONFIRMATION = 0x1;
        private const uint SHERB_NOPROGRESSUI = 0x2;
        private const uint SHERB_NOSOUND = 0x4;

        public static string BackupDir = "";
        public static string LogDir = "";
        private static string _stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        private static List<string> _log = new List<string>();

        // ------------------------------------------------------------
        // 任务定义
        // ------------------------------------------------------------
        public static List<CleanJob> BuildJobs()
        {
            List<CleanJob> list = new List<CleanJob>();
            string temp = Path.GetTempPath();
            string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            CleanJob j;

            j = new CleanJob();
            j.Id = 1; j.Name = "用户临时文件"; j.Kind = CleanKind.TempFiles; j.Risk = Risk.Low;
            j.Desc = "当前用户的 %TEMP%，软件安装/解压留下的临时文件";
            j.Roots = new string[] { temp };
            j.RootLabel = temp;
            list.Add(j);

            j = new CleanJob();
            j.Id = 2; j.Name = "系统临时文件"; j.Kind = CleanKind.TempFiles; j.Risk = Risk.Low; j.NeedsAdmin = true;
            j.Desc = "C:\\Windows\\Temp，系统和安装程序用过的临时文件";
            j.Roots = new string[] { Path.Combine(winDir, "Temp") };
            j.RootLabel = Path.Combine(winDir, "Temp");
            list.Add(j);

            j = new CleanJob();
            j.Id = 3; j.Name = "Windows 更新缓存"; j.Kind = CleanKind.UpdateCache; j.Risk = Risk.Mid; j.NeedsAdmin = true;
            j.Desc = "SoftwareDistribution\\Download，已安装更新的残留包（正在更新时会重建）";
            j.AgeGuard = false;
            j.Roots = new string[] { Path.Combine(winDir, @"SoftwareDistribution\Download") };
            j.RootLabel = @"C:\Windows\SoftwareDistribution\Download";
            list.Add(j);

            j = new CleanJob();
            j.Id = 4; j.Name = "崩溃转储文件"; j.Kind = CleanKind.Dumps; j.Risk = Risk.Low; j.NeedsAdmin = true;
            j.Desc = "MEMORY.DMP 与 Minidump，蓝屏后留下的内存镜像，动辄几百 MB";
            j.Roots = new string[] { Path.Combine(winDir, "Minidump") };
            j.RootLabel = @"C:\Windows\MEMORY.DMP + Minidump";
            list.Add(j);

            j = new CleanJob();
            j.Id = 5; j.Name = "回收站"; j.Kind = CleanKind.RecycleBin; j.Risk = Risk.Mid;
            j.Desc = "清空所有用户的回收站。注意：里面可能有客户刚误删还没找回的文件";
            j.Roots = new string[0];
            j.RootLabel = "所有磁盘的 $Recycle.Bin";
            list.Add(j);

            j = new CleanJob();
            j.Id = 6; j.Name = "浏览器缓存"; j.Kind = CleanKind.BrowserCache; j.Risk = Risk.Mid;
            j.Desc = "Edge / Chrome 的 Cache、Code Cache、GPUCache（请先关掉浏览器，否则会跳过占用文件）";
            j.AgeGuard = false;
            j.Roots = new string[] { localApp };
            j.RootLabel = "%LOCALAPPDATA%\\...\\User Data\\*\\Cache";
            list.Add(j);

            j = new CleanJob();
            j.Id = 7; j.Name = "缩略图缓存"; j.Kind = CleanKind.Thumbs; j.Risk = Risk.Low;
            j.Desc = "thumbcache_*.db，删完要重启资源管理器才重建；本工具不会替你重启";
            j.AgeGuard = false;
            j.Roots = new string[] { Path.Combine(localApp, @"Microsoft\Windows\Explorer") };
            j.RootLabel = @"%LOCALAPPDATA%\Microsoft\Windows\Explorer";
            list.Add(j);

            j = new CleanJob();
            j.Id = 8; j.Name = "失效启动项"; j.Kind = CleanKind.RegRun; j.Risk = Risk.Low; j.NeedsAdmin = true;
            j.Desc = "Run 里指向的程序已经不存在的项（删前会把整个键导出成 .reg 备份）";
            j.Roots = new string[0];
            j.RootLabel = "HKLM/HKCU 的 Run 键";
            list.Add(j);

            j = new CleanJob();
            j.Id = 9; j.Name = "失效快捷方式"; j.Kind = CleanKind.Shortcut; j.Risk = Risk.Low;
            j.Desc = "桌面和开始菜单里目标已不存在的 .lnk（移动到备份目录，可还原）";
            j.Roots = new string[0];
            j.RootLabel = "桌面 + 开始菜单";
            list.Add(j);

            return list;
        }

        // ------------------------------------------------------------
        // 目录选择
        // ------------------------------------------------------------
        public static void PrepareDirs(string exeDir)
        {
            string want = "";
            try { want = Path.Combine(exeDir, "清理记录"); } catch (Exception) { }
            // 只试程序目录是否可写，记录目录等到真要写日志时再建
            if (!CanWrite(exeDir))
            {
                try { want = Path.Combine(Path.GetTempPath(), "66check_clean"); } catch (Exception) { }
                if (!CanWrite(Path.GetTempPath())) want = Path.GetTempPath();
            }
            LogDir = want;
            try { BackupDir = Path.Combine(LogDir, "备份_" + _stamp); }
            catch (Exception) { BackupDir = LogDir; }
        }

        /// 只有真的要备份时才建目录，避免点开就留一堆空文件夹
        private static void EnsureBackup()
        {
            try
            {
                if (string.IsNullOrEmpty(BackupDir)) BackupDir = LogDir;
                if (!Directory.Exists(BackupDir)) Directory.CreateDirectory(BackupDir);
            }
            catch (Exception) { }
        }

        private static bool CanWrite(string dir)
        {
            try
            {
                if (string.IsNullOrEmpty(dir)) return false;
                if (!Directory.Exists(dir)) return false;
                string p = Path.Combine(dir, ".w.tmp");
                File.WriteAllText(p, "x");
                File.Delete(p);
                return true;
            }
            catch (Exception) { return false; }
        }

        // ------------------------------------------------------------
        // 扫描
        // ------------------------------------------------------------
        public static void Scan(CleanJob j)
        {
            j.Bytes = 0; j.Files = 0; j.Skipped = 0; j.Note = "";
            try
            {
                switch (j.Kind)
                {
                    case CleanKind.TempFiles:
                    case CleanKind.UpdateCache:
                        foreach (string r in j.Roots) MeasureTree(j, r, j.AgeGuard);
                        break;
                    case CleanKind.Dumps:
                        MeasureDumps(j);
                        break;
                    case CleanKind.BrowserCache:
                        foreach (string dir in BrowserCacheDirs()) MeasureTree(j, dir, j.AgeGuard);
                        break;
                    case CleanKind.Thumbs:
                        MeasureThumbs(j);
                        break;
                    case CleanKind.RecycleBin:
                        MeasureRecycle(j);
                        break;
                    case CleanKind.RegRun:
                        ScanRegRun(j, false);
                        break;
                    case CleanKind.Shortcut:
                        ScanShortcuts(j, false);
                        break;
                }
            }
            catch (Exception ex)
            {
                j.Note = "扫描异常：" + Fmt.Cut(Fmt.Clean(ex.Message), 40);
            }

            if (j.Files == 0 && j.Note.Length == 0)
            {
                if (j.Kind == CleanKind.BrowserCache) j.Note = "未发现浏览器缓存目录";
                else if (j.Kind == CleanKind.RecycleBin) j.Note = "回收站本来就是空的";
                else if (j.Kind == CleanKind.Dumps) j.Note = "没有转储文件";
                else j.Note = "没有可清理的内容";
            }
        }

        private static void MeasureTree(CleanJob j, string root, bool skipBusy)
        {
            if (!Directory.Exists(root)) { j.Note = "目录不存在"; return; }
            DateTime now = DateTime.UtcNow;
            Stack<string> stack = new Stack<string>();
            stack.Push(root);
            int guard = 0;
            while (stack.Count > 0)
            {
                if (guard++ > 400000) { j.Note = "文件太多，只统计了前 40 万个"; break; }
                string dir = stack.Pop();
                string[] files;
                string[] subs;
                try { files = Directory.GetFiles(dir); }
                catch (Exception)
                {
                    j.Skipped++;
                    if (string.Equals(dir, root, StringComparison.OrdinalIgnoreCase)) j.Note = "读不到（需管理员）";
                    continue;
                }
                try { subs = Directory.GetDirectories(dir); }
                catch (Exception) { subs = new string[0]; }

                foreach (string f in files)
                {
                    try
                    {
                        FileInfo fi = new FileInfo(f);
                        if ((fi.Attributes & FileAttributes.ReparsePoint) != 0) { j.Skipped++; continue; }
                        if (skipBusy && (now - fi.LastWriteTimeUtc).TotalMinutes < 30) { j.Skipped++; continue; }
                        j.Bytes += fi.Length;
                        j.Files++;
                    }
                    catch (Exception) { j.Skipped++; }
                }
                foreach (string d in subs)
                {
                    try
                    {
                        FileAttributes at = File.GetAttributes(d);
                        if ((at & FileAttributes.ReparsePoint) != 0) { j.Skipped++; continue; }
                        stack.Push(d);
                    }
                    catch (Exception) { j.Skipped++; }
                }
            }
        }

        private static void MeasureDumps(CleanJob j)
        {
            string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string dmp = Path.Combine(winDir, "MEMORY.DMP");
            try
            {
                if (File.Exists(dmp))
                {
                    FileInfo fi = new FileInfo(dmp);
                    if ((DateTime.UtcNow - fi.LastWriteTimeUtc).TotalMinutes >= 30) { j.Bytes += fi.Length; j.Files++; }
                }
            }
            catch (Exception) { }
            MeasureTree(j, Path.Combine(winDir, "Minidump"), true);
        }

        private static void MeasureThumbs(CleanJob j)
        {
            foreach (string r in j.Roots)
            {
                if (!Directory.Exists(r)) continue;
                try
                {
                    foreach (string f in Directory.GetFiles(r, "thumbcache_*.db"))
                    {
                        try
                        {
                            FileInfo fi = new FileInfo(f);
                            j.Bytes += fi.Length;
                            j.Files++;
                        }
                        catch (Exception) { j.Skipped++; }
                    }
                }
                catch (Exception) { j.Skipped++; }
            }
        }

        private static void MeasureRecycle(CleanJob j)
        {
            foreach (DriveInfo d in DriveInfo.GetDrives())
            {
                try
                {
                    if (!d.IsReady) continue;
                    string rb = Path.Combine(d.Name, "$Recycle.Bin");
                    if (!Directory.Exists(rb)) continue;
                    CleanJob tmp = new CleanJob();
                    tmp.Kind = CleanKind.TempFiles;
                    MeasureTree(tmp, rb, false);
                    j.Bytes += tmp.Bytes;
                    j.Files += tmp.Files;
                    j.Skipped += tmp.Skipped;
                }
                catch (Exception) { }
            }
        }

        // ------------------------------------------------------------
        // 执行
        // ------------------------------------------------------------
        public static void Execute(CleanJob j)
        {
            j.DoneBytes = 0; j.DoneFiles = 0; j.FailFiles = 0;
            j.FailSample.Clear();
            j.Removed.Clear();
            DateTime cutoff = DateTime.UtcNow.AddMinutes(-30);

            try
            {
                switch (j.Kind)
                {
                    case CleanKind.TempFiles:
                    case CleanKind.UpdateCache:
                        foreach (string r in j.Roots) DeleteTree(j, r, cutoff);
                        break;
                    case CleanKind.Dumps:
                        {
                            string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                            string dmp = Path.Combine(winDir, "MEMORY.DMP");
                            DeleteOne(j, dmp, cutoff, winDir);
                            DeleteTree(j, Path.Combine(winDir, "Minidump"), cutoff);
                        }
                        break;
                    case CleanKind.BrowserCache:
                        foreach (string dir in BrowserCacheDirs()) DeleteTree(j, dir, cutoff);
                        break;
                    case CleanKind.Thumbs:
                        foreach (string r in j.Roots) DeleteThumbs(j, r, cutoff);
                        break;
                    case CleanKind.RecycleBin:
                        EmptyRecycle(j);
                        break;
                    case CleanKind.RegRun:
                        ExecRegRun(j);
                        break;
                    case CleanKind.Shortcut:
                        ExecShortcuts(j);
                        break;
                }
            }
            catch (Exception ex)
            {
                j.Result = "异常：" + Fmt.Cut(Fmt.Clean(ex.Message), 50);
            }
        }

        private static void DeleteTree(CleanJob j, string root, DateTime cutoff)
        {
            if (!Directory.Exists(root)) return;
            if (!IsSafeRoot(root)) { j.FailSample.Add("拒绝处理该根目录：" + root); return; }

            List<string> dirs = new List<string>();
            Stack<string> stack = new Stack<string>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                string dir = stack.Pop();
                dirs.Add(dir);
                string[] files;
                try { files = Directory.GetFiles(dir); }
                catch (Exception) { j.FailFiles++; continue; }
                foreach (string f in files) DeleteOne(j, f, cutoff, root);
                string[] subs;
                try { subs = Directory.GetDirectories(dir); }
                catch (Exception) { subs = new string[0]; }
                foreach (string d in subs)
                {
                    try
                    {
                        FileAttributes at = File.GetAttributes(d);
                        if ((at & FileAttributes.ReparsePoint) != 0) continue;
                        stack.Push(d);
                    }
                    catch (Exception) { }
                }
            }
            // 自底向上删空目录
            for (int i = dirs.Count - 1; i >= 0; i--)
            {
                if (string.Equals(dirs[i].TrimEnd('\\'), root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    if (Directory.GetFileSystemEntries(dirs[i]).Length == 0) Directory.Delete(dirs[i], false);
                }
                catch (Exception) { }
            }
        }

        private static void DeleteThumbs(CleanJob j, string root, DateTime cutoff)
        {
            if (!Directory.Exists(root)) return;
            try
            {
                foreach (string f in Directory.GetFiles(root, "thumbcache_*.db"))
                    DeleteOne(j, f, cutoff, root);
            }
            catch (Exception) { j.FailFiles++; }
        }

        private static void DeleteOne(CleanJob j, string file, DateTime cutoff, string root)
        {
            try
            {
                if (!InRoot(file, root)) { j.FailFiles++; return; }
                FileInfo fi = new FileInfo(file);
                if ((fi.Attributes & FileAttributes.ReparsePoint) != 0) return;
                if (j.AgeGuard && fi.LastWriteTimeUtc > cutoff) return;   // 最近半小时内动过的，可能正在用
                long len = fi.Length;
                fi.Delete();
                j.DoneBytes += len;
                j.DoneFiles++;
            }
            catch (Exception ex)
            {
                j.FailFiles++;
                if (j.FailSample.Count < 5) j.FailSample.Add(Path.GetFileName(file) + "：" + Fmt.Cut(Fmt.Clean(ex.Message), 36));
            }
        }

        private static void EmptyRecycle(CleanJob j)
        {
            long before = j.Bytes;
            int cnt = j.Files;
            int hr = SHEmptyRecycleBin(IntPtr.Zero, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);
            if (hr == 0 || hr == -2147418113)
            {
                j.DoneBytes = before;
                j.DoneFiles = cnt;
                j.Result = "已清空回收站";
            }
            else
            {
                j.FailFiles++;
                j.FailSample.Add("SHEmptyRecycleBin 返回 0x" + hr.ToString("X8"));
                j.Result = "清空失败（可能被占用）";
            }
        }

        // ------------------------------------------------------------
        // 注册表：失效启动项
        // ------------------------------------------------------------
        private class RegTarget
        {
            public RegistryHive Hive;
            public RegistryView View;
            public string Path = "";
            public string ValueName = "";
            public string Command = "";
        }

        private static List<RegTarget> FindDeadRunEntries(List<string> seen)
        {
            List<RegTarget> list = new List<RegTarget>();
            RegistryHive[] hives = new RegistryHive[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser };
            string[] paths = new string[] {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run"
            };
            foreach (RegistryHive h in hives)
            {
                foreach (string p in paths)
                {
                    try
                    {
                        using (RegistryKey baseKey = RegistryKey.OpenBaseKey(h, RegistryView.Registry64))
                        using (RegistryKey k = baseKey.OpenSubKey(p, false))
                        {
                            if (k == null) continue;
                            foreach (string vn in k.GetValueNames())
                            {
                                string cmd = Convert.ToString(k.GetValue(vn));
                                string exe = ExtractExe(cmd);
                                if (exe.Length == 0) continue;
                                if (File.Exists(exe)) continue;
                                RegTarget t = new RegTarget();
                                t.Hive = h;
                                t.View = RegistryView.Registry64;
                                t.Path = p;
                                t.ValueName = vn;
                                t.Command = cmd;
                                list.Add(t);
                                if (seen != null) seen.Add(vn + "  →  " + exe);
                            }
                        }
                    }
                    catch (Exception) { }
                }
            }
            return list;
        }

        private static string ExtractExe(string cmd)
        {
            try
            {
                if (string.IsNullOrEmpty(cmd)) return "";
                string s = cmd.Trim();
                if (s.StartsWith("\"")) s = s.Substring(1);
                int q = s.IndexOf('"');
                if (q > 0) s = s.Substring(0, q);
                int e = s.ToLower().IndexOf(".exe", StringComparison.Ordinal);
                if (e > 0) return s.Substring(0, e + 4).Trim();
                int sp = s.IndexOf(' ');
                if (sp > 0) s = s.Substring(0, sp);
                return s.Trim();
            }
            catch (Exception) { return ""; }
        }

        private static void ScanRegRun(CleanJob j, bool quiet)
        {
            List<string> seen = new List<string>();
            List<RegTarget> list = FindDeadRunEntries(seen);
            j.Files = list.Count;
            foreach (string s in seen)
            {
                j.Removed.Add(s);
                _log.Add("    失效启动项：" + s);
            }
            j.Note = list.Count == 0 ? "没有失效项" : "";
        }

        private static void ExecRegRun(CleanJob j)
        {
            List<RegTarget> list = FindDeadRunEntries(null);
            if (list.Count == 0) { j.Result = "没有失效项"; return; }
            EnsureBackup();

            // 先备份整个键
            List<string> keys = new List<string>();
            foreach (RegTarget t in list)
            {
                string k = (t.Hive == RegistryHive.LocalMachine ? "HKLM\\" : "HKCU\\") + t.Path;
                if (!keys.Contains(k)) keys.Add(k);
            }
            foreach (string k in keys)
            {
                string safe = k.Replace("\\", "_").Replace(":", "");
                string file = Path.Combine(BackupDir, "注册表_" + safe + ".reg");
                RunCmd("reg.exe", "export \"" + k + "\" \"" + file + "\" /y");
                if (File.Exists(file)) j.Removed.Add("已备份：" + Path.GetFileName(file));
            }

            int ok = 0;
            foreach (RegTarget t in list)
            {
                try
                {
                    using (RegistryKey baseKey = RegistryKey.OpenBaseKey(t.Hive, t.View))
                    using (RegistryKey k = baseKey.OpenSubKey(t.Path, true))
                    {
                        if (k == null) continue;
                        k.DeleteValue(t.ValueName, false);
                        ok++;
                        j.Removed.Add("已删除启动项：" + t.ValueName);
                    }
                }
                catch (Exception ex)
                {
                    j.FailFiles++;
                    if (j.FailSample.Count < 5) j.FailSample.Add(t.ValueName + "：" + Fmt.Cut(Fmt.Clean(ex.Message), 36));
                }
            }
            j.DoneFiles = ok;
            j.Result = "删除 " + ok + " 个失效启动项";
        }

        // ------------------------------------------------------------
        // 快捷方式
        // ------------------------------------------------------------
        private static List<string> ShortcutRoots()
        {
            List<string> roots = new List<string>();
            try
            {
                string pub = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
                if (!string.IsNullOrEmpty(pub)) roots.Add(pub);
                string usr = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                if (!string.IsNullOrEmpty(usr)) roots.Add(usr);
                string pubStart = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu);
                if (!string.IsNullOrEmpty(pubStart)) roots.Add(pubStart);
                string usrStart = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
                if (!string.IsNullOrEmpty(usrStart)) roots.Add(usrStart);
            }
            catch (Exception) { }
            return roots;
        }

        private static string ShortcutTarget(string lnk)
        {
            try
            {
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                if (t == null) return "";
                object shell = Activator.CreateInstance(t);
                object sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
                if (sc == null) return "";
                object tp = sc.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, sc, null);
                return tp == null ? "" : Convert.ToString(tp);
            }
            catch (Exception) { return ""; }
        }

        private static List<string> FindDeadShortcuts()
        {
            List<string> dead = new List<string>();
            foreach (string root in ShortcutRoots())
            {
                if (!Directory.Exists(root)) continue;
                string[] files;
                try { files = Directory.GetFiles(root, "*.lnk", SearchOption.TopDirectoryOnly); }
                catch (Exception) { continue; }
                foreach (string f in files)
                {
                    string t = ShortcutTarget(f);
                    if (t.Length == 0) continue;             // 指向网址/特殊对象的跳过
                    if (t.StartsWith("::{", StringComparison.Ordinal)) continue;
                    if (File.Exists(t) || Directory.Exists(t)) continue;
                    dead.Add(f);
                }
            }
            return dead;
        }

        private static void ScanShortcuts(CleanJob j, bool quiet)
        {
            List<string> dead = FindDeadShortcuts();
            j.Files = dead.Count;
            foreach (string d in dead)
            {
                j.Removed.Add(Path.GetFileName(d));
                _log.Add("    失效快捷方式：" + d);
            }
            j.Note = dead.Count == 0 ? "没有失效项" : "";
        }

        private static void ExecShortcuts(CleanJob j)
        {
            List<string> dead = FindDeadShortcuts();
            if (dead.Count == 0) { j.Result = "没有失效项"; return; }
            EnsureBackup();
            string dir = Path.Combine(BackupDir, "快捷方式");
            try { Directory.CreateDirectory(dir); } catch (Exception) { }
            int ok = 0;
            foreach (string f in dead)
            {
                try
                {
                    string dst = Path.Combine(dir, Path.GetFileName(f));
                    int n = 2;
                    while (File.Exists(dst))
                    {
                        dst = Path.Combine(dir, Path.GetFileNameWithoutExtension(f) + "_" + n + ".lnk");
                        n++;
                    }
                    File.Move(f, dst);
                    ok++;
                    j.Removed.Add("已移出：" + Path.GetFileName(f));
                }
                catch (Exception ex)
                {
                    j.FailFiles++;
                    if (j.FailSample.Count < 5) j.FailSample.Add(Path.GetFileName(f) + "：" + Fmt.Cut(Fmt.Clean(ex.Message), 36));
                }
            }
            j.DoneFiles = ok;
            j.Result = "移出 " + ok + " 个失效快捷方式（备份可还原）";
        }

        // ------------------------------------------------------------
        // 浏览器缓存目录
        // ------------------------------------------------------------
        private static List<string> BrowserCacheDirs()
        {
            List<string> dirs = new List<string>();
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string[] bases = new string[] {
                Path.Combine(local, @"Microsoft\Edge\User Data"),
                Path.Combine(local, @"Google\Chrome\User Data"),
                Path.Combine(local, @"Tencent\QQBrowser\User Data"),
                Path.Combine(local, @"360Chrome\Chrome\User Data")
            };
            foreach (string b in bases)
            {
                if (!Directory.Exists(b)) continue;
                try
                {
                    foreach (string prof in Directory.GetDirectories(b))
                    {
                        string name = Path.GetFileName(prof);
                        if (name.Equals("Crashpad", StringComparison.OrdinalIgnoreCase)) continue;
                        foreach (string sub in new string[] { "Cache", "Code Cache", "GPUCache", @"Service Worker\CacheStorage" })
                        {
                            string d = Path.Combine(prof, sub);
                            if (Directory.Exists(d)) dirs.Add(d);
                        }
                    }
                }
                catch (Exception) { }
            }
            return dirs;
        }

        // ------------------------------------------------------------
        // 安全校验
        // ------------------------------------------------------------
        private static bool InRoot(string file, string root)
        {
            try
            {
                string f = Path.GetFullPath(file).TrimEnd('\\').ToLower();
                string r = Path.GetFullPath(root).TrimEnd('\\').ToLower();
                if (r.Length <= 3) return false;                 // 拒绝整盘根目录
                return f.StartsWith(r + "\\", StringComparison.Ordinal);
            }
            catch (Exception) { return false; }
        }

        private static bool IsSafeRoot(string root)
        {
            try
            {
                string r = Path.GetFullPath(root).TrimEnd('\\').ToLower();
                if (r.Length <= 3) return false;
                string[] deny = new string[] {
                    @"c:\windows", @"c:\windows\system32", @"c:\program files",
                    @"c:\program files (x86)", @"c:\users", @"c:\programdata"
                };
                foreach (string d in deny) if (r == d) return false;
                return true;
            }
            catch (Exception) { return false; }
        }

        // ------------------------------------------------------------
        // 日志
        // ------------------------------------------------------------
        public static void LogLine(string s)
        {
            _log.Add(s);
        }

        public static string WriteLog(string header)
        {
            try
            {
                if (!Directory.Exists(LogDir)) Directory.CreateDirectory(LogDir);
                string path = Path.Combine(LogDir, "清理日志_" + SafeName(Environment.MachineName) + "_" + _stamp + ".txt");
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("================================================================");
                sb.AppendLine("66系统清理 操作日志");
                sb.AppendLine("================================================================");
                sb.AppendLine(header);
                sb.AppendLine("时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine("备份目录：" + BackupDir);
                sb.AppendLine("----------------------------------------------------------------");
                foreach (string s in _log) sb.AppendLine(s);
                sb.AppendLine("================================================================");
                sb.AppendLine("回滚说明：");
                sb.AppendLine("  注册表：双击“备份”目录里的 .reg 文件可还原被删的启动项；");
                sb.AppendLine("  快捷方式：备份\\快捷方式 里的 .lnk 拖回桌面/开始菜单即可；");
                sb.AppendLine("  临时文件、缓存、回收站：不可恢复（本来就不建议留）。");
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
                return path;
            }
            catch (Exception) { return ""; }
        }

        public static string SafeName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "未命名";
            StringBuilder sb = new StringBuilder();
            foreach (char c in s)
                sb.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
            return sb.ToString();
        }

        private static void RunCmd(string exe, string args)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = exe;
                psi.Arguments = args;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                using (Process p = new Process())
                {
                    p.StartInfo = psi;
                    p.Start();
                    p.StandardOutput.ReadToEnd();
                    p.StandardError.ReadToEnd();
                    p.WaitForExit(10000);
                }
            }
            catch (Exception) { }
        }

        // ------------------------------------------------------------
        // 自检：在沙箱目录里验证删除引擎与白名单，不碰真实文件
        // ------------------------------------------------------------
        public static bool SelfTest(out string report)
        {
            StringBuilder sb = new StringBuilder();
            bool ok = true;
            string root = Path.Combine(Path.GetTempPath(), "66clean_selftest");
            try
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
                Directory.CreateDirectory(Path.Combine(root, "a"));
                Directory.CreateDirectory(Path.Combine(root, "a", "b"));
                for (int i = 0; i < 5; i++) File.WriteAllText(Path.Combine(root, "a", "f" + i + ".tmp"), new string('x', 1000));
                for (int i = 0; i < 3; i++) File.WriteAllText(Path.Combine(root, "a", "b", "g" + i + ".tmp"), new string('y', 2000));
                // 把时间调旧，绕过"30 分钟内不删"的保护
                DateTime old = DateTime.Now.AddDays(-2);
                foreach (string f in Directory.GetFiles(root, "*", SearchOption.AllDirectories)) File.SetLastWriteTime(f, old);

                string outside = Path.Combine(Path.GetTempPath(), "66clean_selftest_outside.txt");
                File.WriteAllText(outside, "must not be deleted");

                CleanJob j = new CleanJob();
                j.Id = 99; j.Name = "自检"; j.Kind = CleanKind.TempFiles;
                j.Roots = new string[] { root };

                Execute(j);
                int left = Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length;

                bool deletedAll = j.DoneFiles == 8 && left == 0;
                bool outsideSafe = File.Exists(outside);
                bool blocked = !InRoot(outside, root);
                sb.AppendLine("删除文件数：" + j.DoneFiles + "（期望 8），剩余：" + left + "（期望 0）");
                sb.AppendLine("白名单拦截：" + (blocked ? "生效" : "失效"));
                sb.AppendLine("根目录外文件：" + (outsideSafe ? "未被误删" : "被误删！"));
                if (!deletedAll) { ok = false; sb.AppendLine("FAIL：删除数量不符"); }
                if (!outsideSafe || !blocked) { ok = false; sb.AppendLine("FAIL：白名单未生效"); }

                try { Directory.Delete(root, true); } catch (Exception) { }

                // 日志与备份目录的写入链路也要验证（写到临时目录，测完删掉）
                string logTest = Path.Combine(Path.GetTempPath(), "66clean_logtest");
                LogDir = logTest;
                BackupDir = logTest;
                try { Directory.CreateDirectory(logTest); } catch (Exception) { }
                LogLine("[selftest] 日志写入测试");
                string lp = WriteLog("自检");
                bool logOk = File.Exists(lp);
                sb.AppendLine("日志写入：" + (logOk ? "正常" : "失败"));
                if (!logOk) { ok = false; sb.AppendLine("FAIL：日志没写出来"); }
                try { Directory.Delete(logTest, true); } catch (Exception) { }
                try { File.Delete(outside); } catch (Exception) { }
            }
            catch (Exception ex)
            {
                ok = false;
                sb.AppendLine("异常：" + ex.Message);
            }
            report = sb.ToString();
            return ok;
        }
    }
}
