using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Text;

namespace StoreCheck
{
    /// 维护模块的公共设施：目录、备份、日志、确认、命令调用
    internal static class Tool
    {
        public static string Stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        public static string LogDir = "";
        public static string BackupDir = "";
        private static List<string> _log = new List<string>();

        public static void Init(string exeDir)
        {
            string want = "";
            try { want = Path.Combine(exeDir, "维护记录"); } catch (Exception) { }
            if (!Writable(exeDir))
            {
                try { want = Path.Combine(Path.GetTempPath(), "66check_tool"); } catch (Exception) { }
                if (!Writable(Path.GetTempPath())) want = Path.GetTempPath();
            }
            LogDir = want;
            try { BackupDir = Path.Combine(LogDir, "备份_" + Stamp); } catch (Exception) { BackupDir = LogDir; }
        }

        public static string EnsureBackup(string sub)
        {
            try
            {
                if (string.IsNullOrEmpty(BackupDir)) BackupDir = LogDir;
                string d = string.IsNullOrEmpty(sub) ? BackupDir : Path.Combine(BackupDir, sub);
                if (!Directory.Exists(d)) Directory.CreateDirectory(d);
                return d;
            }
            catch (Exception) { return BackupDir; }
        }

        private static bool Writable(string dir)
        {
            try
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return false;
                string p = Path.Combine(dir, ".w.tmp");
                File.WriteAllText(p, "x");
                File.Delete(p);
                return true;
            }
            catch (Exception) { return false; }
        }

        public static void Log(string s)
        {
            _log.Add(s);
        }

        public static string SaveLog(string title, string header)
        {
            try
            {
                if (!Directory.Exists(LogDir)) Directory.CreateDirectory(LogDir);
                string p = Path.Combine(LogDir, SafeName(title) + "_" + Stamp + ".txt");
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("================================================================");
                sb.AppendLine("66维护工具 操作记录");
                sb.AppendLine("================================================================");
                sb.AppendLine("操作：" + title);
                sb.AppendLine("时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                if (!string.IsNullOrEmpty(header)) sb.AppendLine(header);
                sb.AppendLine("备份目录：" + BackupDir);
                sb.AppendLine("----------------------------------------------------------------");
                foreach (string s in _log) sb.AppendLine(s);
                sb.AppendLine("================================================================");
                File.WriteAllText(p, sb.ToString(), new UTF8Encoding(false));
                return p;
            }
            catch (Exception) { return ""; }
        }

        public static void ResetLog()
        {
            _log.Clear();
        }

        public static string SafeName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "未命名";
            StringBuilder sb = new StringBuilder();
            foreach (char c in s) sb.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
            string r = sb.ToString().Trim();
            return r.Length > 60 ? r.Substring(0, 60) : r;
        }

        // ---------------- 交互 ----------------
        public static string Ask(string prompt, string def)
        {
            Console.Write("  " + prompt);
            if (def != null && def.Length > 0) Console.Write("[" + def + "]");
            Console.Write("：");
            string s = ReadLine().Trim();
            if (s.Length == 0) return def == null ? "" : def;
            return s;
        }

        public static string ReadLine()
        {
            try
            {
                string s = Console.ReadLine();
                return s == null ? "" : s;
            }
            catch (Exception) { return ""; }
        }

        /// 必须输入 YES
        public static bool Confirm(string what)
        {
            Ui.Blank();
            Ui.L("  " + what, ConsoleColor.Yellow);
            Ui.C("  确认请输入 YES（其他任何输入都取消）：", ConsoleColor.Cyan);
            string s = ReadLine().Trim();
            return s == "YES" || s == "yes" || s == "Yes";
        }

        public static void Pause(int ms)
        {
            try { System.Threading.Thread.Sleep(ms); }
            catch (Exception) { }
        }

        public static void Head(string title)
        {
            Ui.Blank();
            Ui.L("  " + new string('-', 66), ConsoleColor.DarkCyan);
            Ui.L("  " + title, ConsoleColor.Cyan);
            Ui.L("  " + new string('-', 66), ConsoleColor.DarkCyan);
        }

        public static int PauseExit(string msg, ConsoleColor color)
        {
            Ui.Blank();
            if (!string.IsNullOrEmpty(msg)) Ui.L("  " + msg, color);
            Ui.C("  按任意键返回菜单", ConsoleColor.Green);
            Console.WriteLine();
            try { if (!Console.IsInputRedirected) Console.ReadKey(true); }
            catch (Exception) { }
            return 0;
        }

        public static string Back()
        {
            Ui.Blank();
            Ui.C("  按回车返回菜单……", ConsoleColor.DarkGray);
            ReadLine();
            return "";
        }

        // ---------------- 校验 ----------------
        public static bool InRoot(string file, string root)
        {
            try
            {
                string f = Path.GetFullPath(file).TrimEnd('\\').ToLower();
                string r = Path.GetFullPath(root).TrimEnd('\\').ToLower();
                if (r.Length <= 3) return false;
                return f.StartsWith(r + "\\", StringComparison.Ordinal) || f == r;
            }
            catch (Exception) { return false; }
        }

        public static bool IsAdmin()
        {
            try
            {
                WindowsIdentity id = WindowsIdentity.GetCurrent();
                if (id == null) return false;
                return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch (Exception) { return false; }
        }

        public static int Run(string exe, string args, out string output, int timeoutMs)
        {
            output = "";
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
                    string o = p.StandardOutput.ReadToEnd();
                    string e = p.StandardError.ReadToEnd();
                    if (!p.WaitForExit(timeoutMs))
                    {
                        try { p.Kill(); } catch (Exception) { }
                        output = o + e;
                        return -1;
                    }
                    output = o + e;
                    return p.ExitCode;
                }
            }
            catch (Exception ex)
            {
                output = ex.Message;
                return -2;
            }
        }

        public static long FreeBytes(string path)
        {
            try
            {
                string root = Path.GetPathRoot(Path.GetFullPath(path));
                return new DriveInfo(root).AvailableFreeSpace;
            }
            catch (Exception) { return -1; }
        }

        public static string Sz(long b)
        {
            if (b <= 0) return "0";
            if (b < 1024L * 1024) return (b / 1024) + " KB";
            double mb = b / 1048576.0;
            if (mb < 1024) return (mb < 10 ? mb.ToString("0.0") : mb.ToString("0")) + " MB";
            double gb = mb / 1024.0;
            return (gb < 10 ? gb.ToString("0.0") : gb.ToString("0")) + " GB";
        }

        public static string Num(long n)
        {
            string s = n.ToString();
            StringBuilder sb = new StringBuilder();
            int c = 0;
            for (int i = s.Length - 1; i >= 0; i--)
            {
                sb.Insert(0, s[i]);
                c++;
                if (c % 3 == 0 && i > 0) sb.Insert(0, ',');
            }
            return sb.ToString();
        }

        /// 列出所有固定磁盘及剩余空间
        public static List<DriveInfo> FixedDrives()
        {
            List<DriveInfo> list = new List<DriveInfo>();
            try
            {
                foreach (DriveInfo d in DriveInfo.GetDrives())
                {
                    try
                    {
                        if (d.DriveType != DriveType.Fixed) continue;
                        if (!d.IsReady) continue;
                        list.Add(d);
                    }
                    catch (Exception) { }
                }
            }
            catch (Exception) { }
            return list;
        }
    }
}
