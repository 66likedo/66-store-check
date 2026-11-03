using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Text;

namespace StoreCheck
{
    public static class CleanProgram
    {
        private const string Ver = "1.1";

        public static int Main(string[] args)
        {
            bool plan = false, yes = false, degraded = false, selftest = false, restore = false;
            string apply = "";
            for (int i = 0; i < args.Length; i++)
            {
                string x = args[i] == null ? "" : args[i].Trim();
                string low = x.ToLower();
                if (low == "--plan" || low == "/plan") plan = true;
                else if (low == "--yes" || low == "/yes") yes = true;
                else if (low == "--degraded") degraded = true;
                else if (low == "--selftest") selftest = true;
                else if (low == "--restore") restore = true;
                else if (low == "--apply")
                {
                    if (i + 1 < args.Length) { apply = args[i + 1]; i++; }
                }
                else if (low.StartsWith("--apply=") || low.StartsWith("--apply:"))
                {
                    apply = low.Substring(8);
                }
            }

            try { Console.Title = "66系统清理 v" + Ver; } catch (Exception) { }
            Ui.Setup();

            if (selftest)
            {
                string rep;
                bool ok = Cleaner.SelfTest(out rep);
                Ui.L("清理引擎自检（沙箱，不碰真实文件）：", ConsoleColor.Cyan);
                Ui.L(rep.TrimEnd(), ConsoleColor.Gray);
                Ui.L(ok ? "RESULT: PASS" : "RESULT: FAIL", ok ? ConsoleColor.Green : ConsoleColor.Red);
                return ok ? 0 : 1;
            }

            bool admin = IsAdmin();

            if (!admin && !degraded && !plan)
            {
                if (!TryElevate()) return 0;
            }

            PrintBanner(admin);

            string who = "（本机）";
            if (!plan && apply.Length == 0)
            {
                Ui.Blank();
                Ui.C("  操作人（写进日志，可直接回车跳过）：", ConsoleColor.Cyan);
                string w = ReadLine().Trim();
                if (w.Length > 0) who = w;
            }

            string exeDir = "";
            try { exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location); }
            catch (Exception) { }
            if (string.IsNullOrEmpty(exeDir)) exeDir = Environment.CurrentDirectory;

            // --restore：命令行直接还原上次清理（便于自动化/沙箱验证）
            if (restore)
            {
                Cleaner.PrepareDirs(exeDir);
                string rep;
                bool ok = Cleaner.RestoreLast(out rep);
                Ui.Blank();
                Ui.L(rep.TrimEnd(), ok ? ConsoleColor.Green : ConsoleColor.Yellow);
                return PauseExit("还原结束。", ok ? ConsoleColor.Green : ConsoleColor.Yellow);
            }

            // 交互模式先问一句：扫描清理，还是还原上次清理
            if (!plan && apply.Length == 0)
            {
                int choice = MainMenu();
                if (choice == 0) return 0;
                if (choice == 2)
                {
                    Cleaner.PrepareDirs(exeDir);
                    string rep;
                    bool ok = Cleaner.RestoreLast(out rep);
                    Ui.Blank();
                    Ui.L(rep.TrimEnd(), ok ? ConsoleColor.Green : ConsoleColor.Yellow);
                    Ui.L("  说明：还原只能把上次移走的快捷方式放回去；已删除的临时文件/缓存/回收站找不回来。", ConsoleColor.DarkGray);
                    return PauseExit("还原结束。", ok ? ConsoleColor.Green : ConsoleColor.Yellow);
                }
            }

            if (!plan) Cleaner.PrepareDirs(exeDir);   // 只预演时不建任何目录

            // ---------------- 扫描 ----------------
            List<CleanJob> jobs = Cleaner.BuildJobs();
            Ui.Blank();
            Ui.L("正在扫描（只读，不动任何文件）……", ConsoleColor.DarkGray);
            Ui.Blank();
            PrintHeader();
            string freeBefore = FreeSpace("C:");
            foreach (CleanJob j in jobs)
            {
                Cleaner.Scan(j);
                PrintRow(j);
            }
            long totalBytes = 0;
            int totalFiles = 0;
            foreach (CleanJob j in jobs)
            {
                totalBytes += j.Bytes;
                totalFiles += j.Files;
            }
            Ui.Blank();
            Ui.C("  合计可清理：", ConsoleColor.DarkGray);
            Ui.C(Sz(totalBytes), ConsoleColor.Green);
            Ui.L("，共 " + Num(totalFiles) + " 个文件", ConsoleColor.DarkGray);
            Ui.L("  （标“需管理员”的项目在提权后才能处理；带“备份”的项可还原）", ConsoleColor.DarkGray);

            if (plan)
                return PauseExit("--plan 模式：只扫描，不执行。", ConsoleColor.Yellow);

            // ---------------- 选择 ----------------
            List<CleanJob> picked = new List<CleanJob>();
            if (apply.Length > 0)
            {
                picked = SelectByIds(jobs, apply);
                if (picked.Count == 0) return PauseExit("--apply 里的编号都不对，什么都没做。", ConsoleColor.Yellow);
            }
            else
            {
                while (true)
                {
                    Ui.Blank();
                    Ui.C("  >>> 输入要处理的编号（例：1,3,5），all = 全部低风险项，q = 退出：", ConsoleColor.Cyan);
                    string sel = ReadLine().Trim();
                    if (sel.Length == 0 || sel.ToLower() == "q" || sel == "退出")
                    {
                        Ui.Blank();
                        return PauseExit("已退出，什么都没有改。", ConsoleColor.Gray);
                    }
                    if (sel.ToLower() == "all")
                    {
                        foreach (CleanJob j in jobs) if (j.Risk == Risk.Low && j.Files > 0) picked.Add(j);
                    }
                    else
                    {
                        picked = SelectByIds(jobs, sel);
                    }
                    if (picked.Count > 0) break;
                    Ui.Blank();
                    Ui.Err("没有匹配的编号（可用编号：1-" + jobs.Count + "），请重新输入，或输入 q 退出。");
                }
            }

            if (picked.Count == 0)
                return PauseExit("没有选中任何可处理的项（可能都已经干净了）。", ConsoleColor.Yellow);

            long pickBytes = 0;
            foreach (CleanJob j in picked) pickBytes += j.Bytes;

            Ui.Blank();
            Ui.L("  将要处理：", ConsoleColor.Cyan);
            foreach (CleanJob j in picked)
                Ui.L("    · " + Ui.Pad(j.Name, 16) + (j.Files > 0 ? Ui.PadLeft(Sz(j.Bytes), 8) + Ui.PadLeft(Num(j.Files) + " 个", 10) : "按实际执行") + (NeedsBackup(j) ? "  （先备份）" : ""), ConsoleColor.Gray);
            Ui.C("  合计约 ", ConsoleColor.DarkGray);
            Ui.C(Sz(pickBytes), ConsoleColor.Yellow);
            Ui.Blank();
            Ui.L("  这一步会真正改动系统：删除的临时文件/缓存/回收站不可恢复；", ConsoleColor.Yellow);
            Ui.L("  注册表与快捷方式会先备份到“清理记录\\备份_时间”里，可以还原。", ConsoleColor.Yellow);

            if (!yes)
            {
                Ui.Blank();
                Ui.C("  确认执行请输入 YES（其他任何输入都取消）：", ConsoleColor.Cyan);
                string ok = ReadLine().Trim();
                if (ok != "YES" && ok != "yes" && ok != "Yes")
                {
                    Ui.Blank();
                    return PauseExit("已取消，什么都没有改。", ConsoleColor.Gray);
                }
            }

            // ---------------- 执行 ----------------
            Ui.Blank();
            Ui.L("开始执行……", ConsoleColor.Cyan);
            Cleaner.LogLine("操作模式：" + (yes ? "命令行 --apply（免确认）" : "交互确认"));
            Cleaner.LogLine("执行前 C 盘可用：" + freeBefore);
            foreach (CleanJob j in picked)
            {
                Ui.C("  [" + j.Id + "] " + j.Name + " ", ConsoleColor.Gray);
                Cleaner.LogLine("");
                Cleaner.LogLine("[" + j.Id + "] " + j.Name + "（" + j.Desc + "）");
                Cleaner.LogLine("  范围：" + j.RootLabel);

                Cleaner.Execute(j);

                string head = j.Result.Length > 0 ? j.Result
                    : ("删除 " + j.DoneFiles + " 个，释放 " + Sz(j.DoneBytes) + (j.FailFiles > 0 ? "，跳过/失败 " + j.FailFiles : ""));
                Ui.L(head, j.FailFiles > 0 && j.DoneFiles == 0 ? ConsoleColor.Red : ConsoleColor.Green);

                Cleaner.LogLine("  结果：" + head);
                foreach (string s in j.Removed) Cleaner.LogLine("  " + s);
                foreach (string s in j.FailSample) Cleaner.LogLine("  失败示例：" + s);
            }

            string freeAfter = FreeSpace("C:");
            Ui.Blank();
            Ui.C("  C 盘可用空间：", ConsoleColor.DarkGray);
            Ui.C(freeBefore + " → " + freeAfter, ConsoleColor.Green);
            Ui.Blank();

            Cleaner.LogLine("");
            Cleaner.LogLine("执行后 C 盘可用：" + freeAfter);
            string logPath = Cleaner.WriteLog("操作人：" + who + "    权限：" + (admin ? "管理员" : "受限") + "    C 盘：" + freeBefore + " → " + freeAfter);
            if (logPath.Length > 0) Ui.L("  操作日志：" + logPath, ConsoleColor.Cyan);
            string csv = Cleaner.ExportCsv(picked);
            if (csv.Length > 0) Ui.L("  结果表格：" + csv + "（Excel 可直接打开）", ConsoleColor.Cyan);
            if (NeedsAnyBackup(picked)) Ui.L("  备份目录：" + Cleaner.BackupDir, ConsoleColor.Cyan);
            Ui.L("  回滚：下次启动选 [2] 一键还原上次清理；注册表 .reg 双击导入。", ConsoleColor.DarkGray);

            Ui.Blank();
            Ui.C("  按任意键退出", ConsoleColor.Green);
            Console.WriteLine();
            try { if (!Console.IsInputRedirected) Console.ReadKey(true); }
            catch (Exception) { }
            return 0;
        }

        /// 主菜单：1=扫描清理，2=一键还原上次清理，0=退出
        private static int MainMenu()
        {
            if (Console.IsInputRedirected) return 1;
            Ui.Blank();
            Ui.C("  请选择要做什么：", ConsoleColor.Cyan);
            Ui.L("    [1] 扫描并清理（先看清单，再输入 YES 才动手）", ConsoleColor.Gray);
            Ui.L("    [2] 一键还原上次清理（快捷方式自动放回，注册表 .reg 双击导入）", ConsoleColor.Gray);
            Ui.L("    [0] 退出", ConsoleColor.Gray);
            Ui.C("  >>> ", ConsoleColor.Cyan);
            string s = ReadLine().Trim().ToLower();
            if (s == "0" || s == "q") return 0;
            if (s == "2") return 2;
            return 1;
        }

        private static string Sz(long b)
        {
            if (b <= 0) return "0";
            if (b < 1024L * 1024) return (b / 1024) + " KB";
            double mb = b / 1048576.0;
            if (mb < 1024) return (mb < 10 ? mb.ToString("0.0") : mb.ToString("0")) + " MB";
            double gb = mb / 1024.0;
            return (gb < 10 ? gb.ToString("0.0") : gb.ToString("0")) + " GB";
        }

        /// 千分位
        private static string Num(int n)
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

        /// 任何提前结束的路径都停一下，别让窗口一闪就没
        private static int PauseExit(string msg, ConsoleColor color)
        {
            Ui.Blank();
            if (!string.IsNullOrEmpty(msg)) Ui.L("  " + msg, color);
            Ui.C("  按任意键退出", ConsoleColor.Green);
            Console.WriteLine();
            try { if (!Console.IsInputRedirected) Console.ReadKey(true); }
            catch (Exception) { }
            return 0;
        }

        private static bool NeedsBackup(CleanJob j)
        {
            return j.Kind == CleanKind.RegRun || j.Kind == CleanKind.Shortcut;
        }

        private static bool NeedsAnyBackup(List<CleanJob> list)
        {
            foreach (CleanJob j in list) if (NeedsBackup(j)) return true;
            return false;
        }

        private static void PrintBanner(bool admin)
        {
            Ui.Blank();
            Ui.L("  " + new string('=', 70), ConsoleColor.DarkCyan);
            Ui.L("  66系统清理  v" + Ver + "   六六电脑服务", ConsoleColor.Cyan);
            Ui.L("  注意：这一步会真正删除/修改文件，不属于只读巡检", ConsoleColor.Yellow);
            Ui.L("  " + new string('=', 70), ConsoleColor.DarkCyan);
            Ui.C("  权限：", ConsoleColor.DarkGray);
            Ui.L(admin ? "管理员" : "受限（标“需管理员”的项会被跳过或失败）",
                admin ? ConsoleColor.Green : ConsoleColor.Yellow);
        }

        private static void PrintHeader()
        {
            Ui.L("      " + Ui.Pad("检测项", 16) + Ui.PadLeft("可清理", 8) + Ui.PadLeft("文件数", 8) + "  结论",
                ConsoleColor.DarkGray);
            Ui.L("      " + new string('-', 44), ConsoleColor.DarkGray);
        }

        private static void PrintRow(CleanJob j)
        {
            ConsoleColor c = j.Files == 0 ? ConsoleColor.DarkGray : ConsoleColor.Gray;
            Ui.C("  [" + j.Id + "] ", ConsoleColor.Yellow);
            Ui.C(Ui.Pad(j.Name, 16), c);
            Ui.C(Ui.PadLeft(j.Files > 0 ? Sz(j.Bytes) : "-", 8), c);
            Ui.C(Ui.PadLeft(j.Files > 0 ? Num(j.Files) : "-", 8), c);
            Console.Write("  ");
            string risk = j.Risk == Risk.Low ? "■ 低" : (j.Risk == Risk.Mid ? "▲ 中" : "● 高");
            Ui.C(risk, j.Risk == Risk.Low ? ConsoleColor.Green : (j.Risk == Risk.Mid ? ConsoleColor.Yellow : ConsoleColor.Red));
            if (j.NeedsAdmin) Ui.C(" ·需管理员", ConsoleColor.DarkYellow);
            if (NeedsBackup(j)) Ui.C(" ·先备份", ConsoleColor.DarkCyan);
            Console.WriteLine();
            Ui.L("        " + j.Desc + (j.Note.Length > 0 ? "  〔" + j.Note + "〕" : ""), ConsoleColor.DarkGray);
        }

        private static List<CleanJob> SelectByIds(List<CleanJob> jobs, string s)
        {
            List<CleanJob> picked = new List<CleanJob>();
            foreach (string part in s.Split(new char[] { ',', '，', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int id;
                if (!int.TryParse(part.Trim(), out id)) continue;
                foreach (CleanJob j in jobs)
                {
                    if (j.Id != id) continue;
                    if (!picked.Contains(j)) picked.Add(j);
                }
            }
            return picked;
        }

        private static string FreeSpace(string drive)
        {
            try
            {
                DriveInfo d = new DriveInfo(drive);
                return Fmt.G(d.AvailableFreeSpace);
            }
            catch (Exception) { return "未检测"; }
        }

        private static bool IsAdmin()
        {
            try
            {
                WindowsIdentity id = WindowsIdentity.GetCurrent();
                if (id == null) return false;
                return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch (Exception) { return false; }
        }

        private static bool TryElevate()
        {
            Ui.Blank();
            Ui.L("清理需要管理员权限，原因：", ConsoleColor.Cyan);
            Ui.L("  1) Windows\\Temp、更新缓存、崩溃转储都在系统目录", ConsoleColor.Gray);
            Ui.L("  2) 回收站清空、HKLM 启动项备份与删除", ConsoleColor.Gray);
            Ui.Blank();
            Ui.L("提醒：这一步不是只读，会真删文件。执行前会先扫描并要你确认。", ConsoleColor.Yellow);
            Ui.Blank();
            Ui.L("正在申请管理员权限……", ConsoleColor.Yellow);
            try
            {
                string exe = Assembly.GetExecutingAssembly().Location;
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = exe;
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                psi.WorkingDirectory = Path.GetDirectoryName(exe);
                Process.Start(psi);
                Ui.L("已在新窗口继续，本窗口即将关闭（3 秒）", ConsoleColor.Cyan);
                Ui.Pause(2500);
                return false;
            }
            catch (Exception ex)
            {
                Ui.Blank();
                Ui.Err("没有获得管理员权限，已停止：" + Fmt.Cut(Fmt.Clean(ex.Message), 50));
                Ui.L("可以先用 --plan 只看不删，或用 --degraded 以受限身份跑（系统目录会失败）。", ConsoleColor.Gray);
                Ui.Blank();
                Ui.C("  按任意键退出", ConsoleColor.Green);
                Console.WriteLine();
                try { if (!Console.IsInputRedirected) Console.ReadKey(true); }
                catch (Exception) { }
                return false;
            }
        }

        private static string ReadLine()
        {
            try
            {
                string s = Console.ReadLine();
                return s == null ? "" : s;
            }
            catch (Exception) { return ""; }
        }
    }
}
