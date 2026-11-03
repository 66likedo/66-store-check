using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace StoreCheck
{
    public static class Program
    {
        private const string Ver = "1.2";

        public static int Main(string[] args)
        {
            try { Console.Title = "66门店巡检 v" + Ver + "（只读检测，不修改系统）"; }
            catch (Exception) { }

            // 检查更新放到后台：网络慢/失败都不影响巡检，结束前只顺手看一眼结果
            Task<string> upd = null;
            try { upd = Task.Factory.StartNew<string>(delegate { return Update.Check(Ver); }); }
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
            Ui.L("共 10 项检测，预计 5–7 分钟（只读检测，不修改、不安装、不联网）", ConsoleColor.Cyan);
            string store = AskRequired("门店名称：", "康泰药店 / 如家宾馆 / 老李汽修");
            string inspector = AskRequired("检测人姓名：", "六六 / 王师傅");

            Ui.Blank();
            Ui.L("66门店巡检 v1.2     只读检测，不修改系统", ConsoleColor.Cyan);
            Ui.L("门店：" + store + "    检测人：" + inspector + "    设备：" + Environment.MachineName, ConsoleColor.DarkGray);
            Ui.Blank();

            List<Snapshot> all = new List<Snapshot>();
            while (true)
            {
                Snapshot s = RunOnce(store, inspector, dir);
                all.Add(s);

                // 现场照片（可选）：先问，再写报告，照片会内嵌进结论单
                try { Report.AskPhotos(s); } catch (Exception) { }

                Ui.Big("检测完成，正在生成报告");

                string html = "", txt = "";
                try { Report.WriteDevice(s, all.Count, out html, out txt); }
                catch (Exception ex) { Ui.Err("报告写入失败：" + ex.Message); }

                Ui.Blank();
                Ui.L("报告目录：" + Report.Dir, ConsoleColor.Cyan);
                if (html.Length > 0) Ui.L("结论单：" + Path.GetFileName(html), ConsoleColor.Green);
                if (txt.Length > 0) Ui.L("原始数据：" + Path.GetFileName(txt), ConsoleColor.Green);

                // 一键导出同目录同名 PDF（Edge 无头打印；没装就静默跳过）
                if (html.Length > 0)
                {
                    string pdf = Report.ExportPdf(html);
                    if (pdf.Length > 0) Ui.L("PDF 版：" + Path.GetFileName(pdf), ConsoleColor.Green);
                    else Ui.L("PDF 未生成（本机没找到 Edge）；可打开结论单后按 Ctrl+P 另存为 PDF", ConsoleColor.DarkGray);
                }

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
                    string ovPdf = Report.ExportPdf(ov);
                    if (ovPdf.Length > 0) Ui.L("总览 PDF：" + Path.GetFileName(ovPdf), ConsoleColor.Green);
                    Report.Open(ov);
                }
                catch (Exception ex) { Ui.Err("总览生成失败：" + ex.Message); }
            }

            // 后台检查更新：已经返回就顺手提示，没返回就完全不打扰
            try
            {
                if (upd != null && upd.IsCompleted)
                {
                    string tip = upd.Result;
                    if (!string.IsNullOrEmpty(tip))
                        Ui.L("  版本提示：" + tip, ConsoleColor.DarkCyan);
                }
            }
            catch (Exception) { }

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
            Task<Block> t9 = Task.Factory.StartNew(delegate { return Collect.Peripherals(); });
            Task<Block> t10 = Task.Factory.StartNew(delegate { return Collect.PosSoftware(); });

            s.Blocks.Add(Show(1, "C 盘空间够不够", "DiskSpace", t1));
            s.Blocks.Add(Show(2, "硬盘状况", "DiskHealth", t2));
            s.Blocks.Add(Show(3, "开机快不快", "Boot", t3));
            s.Blocks.Add(Show(4, "系统文件有没有坏", "Sfc", t4));
            s.Blocks.Add(Show(5, "上网稳不稳", "Network", t5));
            s.Blocks.Add(Show(6, "文件存在哪儿", "ShellFolders", t6));
            s.Blocks.Add(Show(7, "电脑配置", "Inventory", t7));
            s.Blocks.Add(Show(8, "用着顺不顺手", "Experience", t8));
            s.Blocks.Add(Show(9, "打印机/扫码枪", "Peripherals", t9));
            s.Blocks.Add(Show(10, "收银软件在不在跑", "PosSoftware", t10));

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
            Ui.Item(idx, 10, name, b.Item.ConsoleLine, b.Item.V);
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

    /// 检查更新：只读 GitHub 最新 release 的 tag，网络失败一律静默
    internal static class Update
    {
        private const string Repo = "66likedo/66-store-check";

        public static string Check(string localVer)
        {
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create("https://api.github.com/repos/" + Repo + "/releases/latest");
                req.UserAgent = "66store-check/" + localVer;
                req.Accept = "application/vnd.github+json";
                req.Timeout = 4000;
                req.ReadWriteTimeout = 4000;
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    string json = sr.ReadToEnd();
                    Match m = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"([^\"]+)\"");
                    if (!m.Success) return "";
                    string tag = m.Groups[1].Value.Trim();
                    if (Compare(tag, localVer) > 0)
                        return "发现新版 " + tag + "（当前 v" + localVer + "），可到 GitHub 下载";
                }
            }
            catch (Exception) { }
            return "";
        }

        /// 只比较数字段：v1.2.0 / 1.2 都认，认不出就当没更新
        private static int Compare(string a, string b)
        {
            int[] x = Parts(a), y = Parts(b);
            int n = Math.Max(x.Length, y.Length);
            for (int i = 0; i < n; i++)
            {
                int p = i < x.Length ? x[i] : 0;
                int q = i < y.Length ? y[i] : 0;
                if (p != q) return p > q ? 1 : -1;
            }
            return 0;
        }

        private static int[] Parts(string s)
        {
            try
            {
                string t = (s == null ? "" : s.Trim()).TrimStart('v', 'V');
                string[] seg = t.Split('.', '-', '_');
                List<int> nums = new List<int>();
                foreach (string one in seg)
                {
                    int v;
                    if (int.TryParse(one, out v)) nums.Add(v);
                    else break;
                }
                return nums.ToArray();
            }
            catch (Exception) { return new int[0]; }
        }
    }
}
