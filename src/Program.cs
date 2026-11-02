using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace StoreCheck
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            try { Console.Title = "66门店巡检 v1.0（只读检测，不修改系统）"; }
            catch (Exception) { }

            bool assetOnly = false;
            foreach (string a in args)
            {
                string x = a == null ? "" : a.Trim().ToLower();
                if (x == "--asset" || x == "/asset" || x == "-a") assetOnly = true;
            }

            Ui.Setup();
            string dir = Report.PrepareDir();

            if (assetOnly) return AssetMode();

            // ---------------- 首屏：只三行 ----------------
            Ui.L("共 8 项检测，预计 4–6 分钟（只读检测，不修改、不安装、不联网）", ConsoleColor.Cyan);
            string store = AskRequired("门店名称：", "康泰药店 / 如家宾馆 / 老李汽修");
            string inspector = AskRequired("检测人姓名：", "六六 / 王师傅");

            Ui.Blank();
            Ui.L("66门店巡检 v1.0     只读检测，不修改系统", ConsoleColor.Cyan);
            Ui.L("门店：" + store + "    检测人：" + inspector + "    设备：" + Environment.MachineName, ConsoleColor.DarkGray);
            Ui.Blank();

            List<Snapshot> all = new List<Snapshot>();
            while (true)
            {
                Snapshot s = RunOnce(store, inspector, dir);
                all.Add(s);

                Ui.Big("检测完成，正在生成报告");

                string html = "", txt = "";
                try { Report.WriteDevice(s, all.Count, out html, out txt); }
                catch (Exception ex) { Ui.Err("报告写入失败：" + ex.Message); }

                Ui.Blank();
                Ui.L("报告目录：" + Report.Dir, ConsoleColor.Cyan);
                if (html.Length > 0) Ui.L("结论单：" + Path.GetFileName(html), ConsoleColor.Green);
                if (txt.Length > 0) Ui.L("原始数据：" + Path.GetFileName(txt), ConsoleColor.Green);

                string oc = s.Overall == "该换" ? "● 该换" : (s.Overall == "该修" ? "▲ 该修" : "■ 不用换");
                ConsoleColor occ = s.Overall == "该换" ? ConsoleColor.Red : (s.Overall == "该修" ? ConsoleColor.Yellow : ConsoleColor.Green);
                Ui.Blank();
                Ui.C("  综合结论：" + oc, occ);
                Ui.L("    " + s.OverallNote, ConsoleColor.Gray);

                if (html.Length > 0) Report.Open(html);

                if (!AskNext()) break;
                Ui.Blank();
                Ui.L("请把下一台设备的检测准备好，按回车开始……", ConsoleColor.Cyan);
                ReadLineSafe();
                Ui.Blank();
            }

            if (all.Count > 1)
            {
                Ui.Blank();
                foreach (Snapshot s in all)
                {
                    try
                    {
                        string h2 = "", t2 = "";
                        Report.WriteDevice(s, all.Count, out h2, out t2);
                    }
                    catch (Exception) { }
                }
                try
                {
                    string ov = Report.WriteOverview(all, store);
                    Ui.L("本店设备总览：" + Path.GetFileName(ov), ConsoleColor.Cyan);
                    Report.Open(ov);
                }
                catch (Exception ex) { Ui.Err("总览生成失败：" + ex.Message); }
            }

            Ui.Blank();
            Ui.C("  按任意键退出", ConsoleColor.Green);
            Console.WriteLine();
            try { if (!Console.IsInputRedirected) Console.ReadKey(true); }
            catch (Exception) { }
            return 0;
        }

        private static int AssetMode()
        {
            Ui.L("66门店巡检 · 资产清单单独导出（只读采集）", ConsoleColor.Cyan);
            Inventory inv = new Inventory();
            Block b = Collect.Inventory(inv);
            Snapshot s = new Snapshot();
            s.DeviceName = Environment.MachineName;
            s.StartTime = DateTime.Now;
            s.EndTime = DateTime.Now;
            s.Inv = inv;
            s.Blocks.Add(b);
            s.SheetNo = SheetNo(s.DeviceName);
            try
            {
                string p = Report.WriteAsset(s);
                Ui.L("已导出：" + p, ConsoleColor.Green);
                Report.Open(p);
            }
            catch (Exception ex) { Ui.Err("导出失败：" + ex.Message); }
            Ui.C("  按任意键退出", ConsoleColor.Green);
            Console.WriteLine();
            try { if (!Console.IsInputRedirected) Console.ReadKey(true); }
            catch (Exception) { }
            return 0;
        }

        // ---------------- 一次巡检 ----------------
        private static Snapshot RunOnce(string store, string inspector, string dir)
        {
            Snapshot s = new Snapshot();
            s.StoreName = store;
            s.Inspector = inspector;
            s.DeviceName = Environment.MachineName;
            s.StartTime = DateTime.Now;
            s.SheetNo = SheetNo(s.DeviceName);

            Inventory inv = new Inventory();

            // 后台并行采集，屏幕上按顺序逐项显示
            Task<Block> t1 = Task.Factory.StartNew(delegate { return Collect.DiskSpace(); });
            Task<Block> t2 = Task.Factory.StartNew(delegate { return Collect.DiskHealth(); });
            Task<Block> t3 = Task.Factory.StartNew(delegate { return Collect.Boot(); });
            Task<Block> t4 = Task.Factory.StartNew(delegate { return Collect.Sfc(); });
            Task<Block> t5 = Task.Factory.StartNew(delegate { return Collect.Network(); });
            Task<Block> t6 = Task.Factory.StartNew(delegate { return Collect.ShellFolders(); });
            Task<Block> t7 = Task.Factory.StartNew(delegate { return Collect.Inventory(inv); });
            Task<Block> t8 = t7.ContinueWith<Block>(delegate (Task<Block> prev) { return Collect.Experience(inv); });

            s.Blocks.Add(Show(1, "C 盘空间够不够", "DiskSpace", t1));
            s.Blocks.Add(Show(2, "硬盘状况", "DiskHealth", t2));
            s.Blocks.Add(Show(3, "开机快不快", "Boot", t3));
            s.Blocks.Add(Show(4, "系统文件有没有坏", "Sfc", t4));
            s.Blocks.Add(Show(5, "上网稳不稳", "Network", t5));
            s.Blocks.Add(Show(6, "文件存在哪儿", "ShellFolders", t6));
            s.Blocks.Add(Show(7, "电脑配置", "Inventory", t7));
            s.Blocks.Add(Show(8, "用着顺不顺手", "Experience", t8));

            s.Inv = inv;
            s.EndTime = DateTime.Now;

            try
            {
                Dictionary<string, string> prev = Report.LoadPrev(dir, s.DeviceName, null);
                Report.ApplyHistory(s, prev);
            }
            catch (Exception) { }

            Report.Decide(s);
            return s;
        }

        private static Block Show(int idx, string name, string key, Task<Block> t)
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
                b.Item.Name = name;
                b.Item.V = Verdict.Unknown;
                b.Item.Measured = "未检测（采集异常：" + ex.Message + "）";
                b.Item.ReportValue = "未检测（采集异常）";
                b.Item.ConsoleLine = "未检测";
                b.Item.Meaning = "这项没能读出来，不影响其他项，可下次再测一次";
            }
            if (b.Item.Name.Length == 0) b.Item.Name = name;
            Ui.Item(idx, 8, name, b.Item.ConsoleLine, b.Item.V);
            Ui.Pause(400);
            return b;
        }

        // ---------------- 输入 ----------------
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

        private static bool AskNext()
        {
            try
            {
                if (!Console.IsInputRedirected)
                {
                    Ui.Blank();
                    Ui.C("  继续检测本店下一台设备吗？输入 Y 继续，直接回车结束：", ConsoleColor.Cyan);
                }
                string r = ReadLineSafe().Trim().ToLower();
                return r == "y" || r == "yes" || r == "是";
            }
            catch (Exception) { return false; }
        }

        private static string SheetNo(string device)
        {
            try
            {
                // 用 MD5 取前两字节做后缀：不同机器/不同时间稳定可复现，不受 .NET 版本影响
                string seed = (device == null ? "" : device) + "|" + DateTime.Now.ToString("yyyyMMddHHmmssfff");
                byte[] hash;
                using (MD5 md5 = MD5.Create())
                {
                    hash = md5.ComputeHash(Encoding.UTF8.GetBytes(seed));
                }
                int h = ((hash[0] << 8) | hash[1]) & 0xFFFF;
                return "SC-" + DateTime.Now.ToString("yyyyMMdd") + "-" + h.ToString("X4");
            }
            catch (Exception) { return "SC-" + DateTime.Now.ToString("yyyyMMdd") + "-0000"; }
        }
    }
}
