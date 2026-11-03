using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;

namespace StoreCheck
{
    public static class ProProgram
    {
        private static bool _admin = false;
        private static bool _degraded = false;
        private const int Total = 13;

        public static int Main(string[] args)
        {
            foreach (string a in args)
            {
                string x = a == null ? "" : a.Trim().ToLower();
                if (x == "--degraded" || x == "/degraded") _degraded = true;
            }

            try { Console.Title = "66专业检测 v1.2（只读深检）"; }
            catch (Exception) { }

            Ui.Setup();
            _admin = Collect.IsAdmin();

            if (!_admin && !_degraded)
            {
                if (!TryElevate()) return 0;
            }

            string dir = ProReport.PrepareDir();

            Ui.L("共 " + Total + " 项深度检测，预计 6–10 分钟；全程只读，不修改系统", ConsoleColor.Cyan);
            string store = AskRequired("门店名称：", "康泰药店 / 如家宾馆 / 老李汽修");
            string inspector = AskRequired("检测人姓名：", "六六 / 王师傅");

            Ui.Blank();
            Ui.L("66专业检测 v1.2     只读深检，不修改系统", ConsoleColor.Cyan);
            Ui.C("权限：", ConsoleColor.DarkGray);
            Ui.L(_admin ? "管理员（可读事件日志 / SFC / 服务 / 防火墙）" : "受限（未提权，部分项会标未检测）",
                _admin ? ConsoleColor.Green : ConsoleColor.Yellow);
            Ui.L("门店：" + store + "    检测人：" + inspector + "    设备：" + Environment.MachineName, ConsoleColor.DarkGray);
            Ui.Blank();

            Snapshot s = RunOnce(store, inspector);

            Ui.Big("检测完成，正在生成报告");

            string html = "", txt = "", plan = "";
            try { ProReport.WriteAll(s, _admin, out html, out txt, out plan); }
            catch (Exception ex) { Ui.Err("报告写入失败：" + ex.Message); }

            Ui.Blank();
            Ui.L("报告目录：" + ProReport.Dir, ConsoleColor.Cyan);
            if (html.Length > 0) Ui.L("技术报告：" + Path.GetFileName(html), ConsoleColor.Green);
            if (txt.Length > 0) Ui.L("原始数据：" + Path.GetFileName(txt), ConsoleColor.Green);
            if (plan.Length > 0) Ui.L("处理清单：" + Path.GetFileName(plan), ConsoleColor.Green);

            int bad = 0, warn = 0, unk = 0;
            foreach (Block b in s.Blocks)
            {
                if (b.Item.V == Verdict.Bad) bad++;
                else if (b.Item.V == Verdict.Warn) warn++;
                else if (b.Item.V == Verdict.Unknown) unk++;
            }
            Ui.Blank();
            Ui.C("  严重 ", ConsoleColor.DarkGray);
            Ui.C(bad + " 项", bad > 0 ? ConsoleColor.Red : ConsoleColor.Green);
            Ui.C("    中等 ", ConsoleColor.DarkGray);
            Ui.C(warn + " 项", warn > 0 ? ConsoleColor.Yellow : ConsoleColor.Green);
            Ui.C("    未检测 ", ConsoleColor.DarkGray);
            Ui.L(unk + " 项", ConsoleColor.DarkGray);
            if (!_admin) Ui.L("  （未检测项需要管理员权限，右键以管理员身份再跑一次可补齐）", ConsoleColor.DarkGray);

            if (html.Length > 0) ProReport.Open(html);

            Ui.Blank();
            Ui.C("  按任意键退出", ConsoleColor.Green);
            Console.WriteLine();
            try { if (!Console.IsInputRedirected) Console.ReadKey(true); }
            catch (Exception) { }
            return 0;
        }

        // ---------------- 提权 ----------------
        private static bool TryElevate()
        {
            Ui.Blank();
            Ui.L("本工具需要管理员权限，只为了“读得到”，不是为了改：", ConsoleColor.Cyan);
            Ui.L("  1) 开机耗时（性能事件日志）", ConsoleColor.Gray);
            Ui.L("  2) 系统文件校验（sfc /verifyonly，只校验不修复）", ConsoleColor.Gray);
            Ui.L("  3) 服务启动失败、磁盘/系统错误日志", ConsoleColor.Gray);
            Ui.L("  4) 防火墙状态、带感叹号的设备", ConsoleColor.Gray);
            Ui.Blank();
            Ui.L("只读承诺：不删文件、不改注册表/服务/启动项、不装软件、不上传数据。", ConsoleColor.Green);
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
                Ui.L("如果刚点了“否”，重新打开同意即可；", ConsoleColor.Gray);
                Ui.L("也可以先用只读方式跑：66专业检测.exe --degraded （部分项会标“未检测”）", ConsoleColor.Gray);
                Ui.Blank();
                Ui.C("  按任意键退出", ConsoleColor.Green);
                Console.WriteLine();
                try { if (!Console.IsInputRedirected) Console.ReadKey(true); }
                catch (Exception) { }
                return false;
            }
        }

        // ---------------- 一次深检 ----------------
        private static Snapshot RunOnce(string store, string inspector)
        {
            Snapshot s = new Snapshot();
            s.StoreName = store;
            s.Inspector = inspector;
            s.DeviceName = Environment.MachineName;
            s.StartTime = DateTime.Now;
            s.SheetNo = "PRO-" + DateTime.Now.ToString("yyyyMMdd") + "-" + (DateTime.Now.Millisecond % 10000).ToString("D4");

            Inventory inv = new Inventory();

            Task<Block> t01 = Task.Factory.StartNew(delegate { return ProCollect.SystemLicense(); });
            Task<Block> t02 = Task.Factory.StartNew(delegate { return Collect.DiskSpace(); });
            Task<Block> t03 = Task.Factory.StartNew(delegate { return Collect.DiskHealth(); });
            Task<Block> t04 = Task.Factory.StartNew(delegate { return Collect.Boot(); });
            Task<Block> t05 = Task.Factory.StartNew(delegate { return Collect.Sfc(); });
            Task<Block> t06 = Task.Factory.StartNew(delegate { return ProCollect.EventLogs(); });
            Task<Block> t07 = Task.Factory.StartNew(delegate { return ProCollect.ServicesDrivers(); });
            Task<Block> t08 = Task.Factory.StartNew(delegate { return ProCollect.Tasks(); });
            Task<Block> t09 = Task.Factory.StartNew(delegate { return ProCollect.ProNetwork(); });
            Task<Block> t10 = Task.Factory.StartNew(delegate { return ProCollect.Security(); });
            Task<Block> t11 = Task.Factory.StartNew(delegate { return Collect.ShellFolders(); });
            Task<Block> t12 = Task.Factory.StartNew(delegate { return Collect.Inventory(inv); });
            Task<Block> t13 = t12.ContinueWith<Block>(delegate (Task<Block> prev) { return Collect.Experience(inv); });

            s.Blocks.Add(Show(1, "系统与授权", "SysLicense", t01, "系统与授权"));
            s.Blocks.Add(Show(2, "磁盘与空间", "DiskSpace", t02, "磁盘与空间占用"));
            s.Blocks.Add(Show(3, "硬盘健康", "DiskHealth", t03, "硬盘健康"));
            s.Blocks.Add(Show(4, "开机与启动项", "Boot", t04, "开机速度与启动项"));
            s.Blocks.Add(Show(5, "系统文件", "Sfc", t05, "系统文件完整性"));
            s.Blocks.Add(Show(6, "事件日志", "EventLog", t06, "事件日志体检"));
            s.Blocks.Add(Show(7, "服务与驱动", "Services", t07, "服务与驱动"));
            s.Blocks.Add(Show(8, "计划任务", "Tasks", t08, "计划任务"));
            s.Blocks.Add(Show(9, "网络深检", "Network", t09, "网络深检"));
            s.Blocks.Add(Show(10, "安全面", "Security", t10, "安全面"));
            s.Blocks.Add(Show(11, "桌面文档路径", "ShellFolders", t11, "桌面与文档路径"));
            s.Blocks.Add(Show(12, "整机资产", "Inventory", t12, "整机资产清单"));
            s.Blocks.Add(Show(13, "使用体验", "Experience", t13, "使用体验"));

            s.Inv = inv;
            s.EndTime = DateTime.Now;
            return s;
        }

        private static Block Show(int idx, string showName, string key, Task<Block> t, string officialName)
        {
            Block b;
            try
            {
                t.Wait();
                b = t.Result;
            }
            catch (Exception ex)
            {
                b = new Block();
                b.Item.Key = key;
                b.Item.Name = officialName;
                b.Item.V = Verdict.Unknown;
                b.Item.Measured = "未检测（采集异常：" + ex.Message + "）";
                b.Item.ReportValue = "未检测（采集异常）";
                b.Item.ConsoleLine = "未检测";
                b.Item.Meaning = "这项没读出来，可重新跑一次";
            }
            if (b.Item.Name.Length == 0) b.Item.Name = officialName;

            ConsoleColor cc = Vs.Color(b.Item.V);
            Ui.C("[", ConsoleColor.DarkGray);
            Ui.C((idx < 10 ? "0" : "") + idx + "/" + Total, ConsoleColor.Yellow);
            Ui.C("] ", ConsoleColor.DarkGray);
            Ui.C(Ui.Pad(showName, 16), ConsoleColor.Gray);
            Ui.C(Ui.Pad(Ui.Clip(b.Item.ConsoleLine, 30), 30), cc == ConsoleColor.DarkGray ? ConsoleColor.Gray : cc);
            Ui.C(Vs.Sym(b.Item.V) + " " + Vs.Txt(b.Item.V), cc);
            Console.WriteLine();
            Ui.Pause(400);
            return b;
        }

        private static string AskRequired(string label, string example)
        {
            while (true)
            {
                Console.Write(label);
                string v = ReadLineSafe().Trim();
                if (v.Length > 0) return Fmt.Cut(v, 30);
                if (Console.IsInputRedirected) return "未填写";
                Ui.Err("这一项必须填，不能直接回车（例如：" + example + "）");
            }
        }

        private static string ReadLineSafe()
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
