using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;

namespace StoreCheck
{
    /// 报价参考（按自家门店改这里的数字即可，结论单"参考价"列只取这里）
    public static class Price
    {
        public const string Clean = "80";        // 系统盘瘦身与垃圾清理
        public const string ReplaceDisk = "260"; // 换盘 + 数据迁移 + 装系统（不含硬盘钱）
        public const string Backup = "150";      // 重要数据备份方案
        public const string BootOpt = "80";      // 开机优化
        public const string NetFix = "100";      // 网络排查
        public const string PathFix = "120";     // 桌面/文档路径修复
        public const string SysFix = "150";      // 系统文件修复
        public const string Year1 = "360";       // 年度维护（第 1 台/年）
        public const string YearN = "300";       // 年度维护（每增加 1 台/年）
    }

    public static class Report
    {
        private static string _dir = "";

        public static string Dir { get { return _dir; } }

        // ---------------- 输出目录 ----------------
        public static string PrepareDir()
        {
            string exeDir = "";
            try { exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location); }
            catch (Exception) { }
            if (string.IsNullOrEmpty(exeDir)) exeDir = AppDomain.CurrentDomain.BaseDirectory;
            if (string.IsNullOrEmpty(exeDir)) exeDir = Environment.CurrentDirectory;

            string want = "";
            try { want = Path.Combine(exeDir, "报告"); }
            catch (Exception) { }
            if (TryUse(want)) { _dir = want; return _dir; }

            string tmp = "";
            try { tmp = Path.Combine(Path.GetTempPath(), "66check"); }
            catch (Exception) { }
            if (TryUse(tmp)) { _dir = tmp; return _dir; }

            _dir = exeDir;
            return _dir;
        }

        private static bool TryUse(string dir)
        {
            try
            {
                if (string.IsNullOrEmpty(dir)) return false;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string probe = Path.Combine(dir, ".w.tmp");
                File.WriteAllText(probe, "x", new UTF8Encoding(false));
                File.Delete(probe);
                return true;
            }
            catch (Exception) { return false; }
        }

        public static string SafeName(string s)
        {
            string t = s == null ? "" : s.Trim();
            if (t.Length == 0) t = "未命名";
            StringBuilder sb = new StringBuilder();
            foreach (char c in t)
            {
                if (Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0) sb.Append('_');
                else sb.Append(c);
            }
            string r = sb.ToString().Trim();
            if (r.Length == 0) r = "未命名";
            if (r.Length > 40) r = r.Substring(0, 40);
            return r;
        }

        // ---------------- 综合判定 ----------------
        public static void Decide(Snapshot s)
        {
            int bad = 0, warn = 0;
            bool diskBad = false, cBad = false;
            foreach (Block b in s.Blocks)
            {
                if (b.Item.V == Verdict.Bad) bad++;
                if (b.Item.V == Verdict.Warn) warn++;
                if (b.Item.Key == "DiskHealth" && b.Item.V == Verdict.Bad) diskBad = true;
                if (b.Item.Key == "DiskSpace" && b.Item.V == Verdict.Bad) cBad = true;
            }
            int age = s.Inv.AgeYears();
            double ram = s.Inv.RamGB;

            string overall;
            if (diskBad && age >= 6) overall = "该换";
            else if (bad >= 2 && age >= 8) overall = "该换";
            else if (diskBad && ram > 0 && ram <= 4) overall = "该换";
            else if (bad >= 1 || cBad) overall = "该修";
            else if (warn >= 2) overall = "该修";
            else overall = "不用换";

            string note;
            if (overall == "该换") note = "关键部件已到年限，继续用有丢数据风险，建议更换，先把重要数据备份出来";
            else if (overall == "该修") note = "有几项需要处理，处理完这台机器还能正常用几年";
            else note = "目前状态正常，做好数据备份就能继续用，不需要换机";

            s.Overall = overall;
            s.OverallNote = note;
        }

        /// 给店主看的白话总结：现在怎么样 / 要不要花钱 / 最该先做
        private static string[] PlainSummary(Snapshot s, int deviceCount)
        {
            int bad = 0, warn = 0;
            CheckItem top = null;
            foreach (Block b in s.Blocks)
            {
                if (b.Item.V == Verdict.Bad) bad++;
                if (b.Item.V == Verdict.Warn) warn++;
                if (top == null && b.Item.TopPriority) top = b.Item;
            }
            if (top == null)
            {
                foreach (Block b in s.Blocks)
                {
                    if (b.Item.V == Verdict.Bad) { top = b.Item; break; }
                }
            }
            if (top == null)
            {
                foreach (Block b in s.Blocks)
                {
                    if (b.Item.V == Verdict.Warn) { top = b.Item; break; }
                }
            }

            string now;
            if (s.Overall == "该换") now = "这台电脑年头到了，继续用有丢数据的风险，建议换新的，先把账目、会员、监控这些数据备份出来";
            else if (s.Overall == "该修") now = "这台电脑还能继续用，但有 " + (bad + warn) + " 处要处理，处理完之后还能正常干几年活";
            else now = "这台电脑目前状态正常，不用换机也不用修";

            string money;
            List<string[]> rows = QuoteRows(s, deviceCount);
            if (rows.Count == 0) money = "这次不用花钱，平时每月备份一次数据就行";
            else money = "建议做 " + rows.Count + " 项处理，每一项都写明原因和费用（见下面的表）；不着急的可以往后放";

            string first = top != null
                ? "先处理「" + top.Name + "」——" + FirstSentence(top.Meaning)
                : "继续正常用就行，但请把重要数据定期备份到移动硬盘，这是最省心的一件事";

            return new string[] { now, money, first };
        }

        /// 只取第一句，避免总结太长
        private static string FirstSentence(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string x = s.Replace("\r\n", "\n").Split('。')[0].Trim();
            return x.Length > 0 ? x + "。" : s;
        }

        public static string NextVisit(Snapshot s)
        {
            bool diskIssue = false;
            int bad = 0, warn = 0;
            foreach (Block b in s.Blocks)
            {
                if (b.Item.V == Verdict.Bad) bad++;
                if (b.Item.V == Verdict.Warn) warn++;
                if (b.Item.Key == "DiskHealth" && (b.Item.V == Verdict.Bad || b.Item.V == Verdict.Warn)) diskIssue = true;
            }
            if (diskIssue) return "建议 1 个月后复检，同时尽快做一次完整数据备份";
            if (bad > 0) return "建议 2 个月后复检";
            if (warn > 0) return "建议 3 个月后复检";
            return "建议 6 个月后复检（每季度自己确认一次备份是否正常）";
        }

        private static List<string[]> QuoteRows(Snapshot s, int deviceCount)
        {
            List<string[]> q = new List<string[]>();
            Verdict dh = VOf(s, "DiskHealth"), ds = VOf(s, "DiskSpace"), bt = VOf(s, "Boot"),
                     sf = VOf(s, "ShellFolders"), nw = VOf(s, "Network"), sc = VOf(s, "Sfc");

            if (dh == Verdict.Bad)
                q.Add(new string[] { "换硬盘 + 数据迁移 + 装系统", "先把账目/会员/监控数据搬出来，再换新盘（硬盘按实价另计）", Price.ReplaceDisk });
            else if (dh == Verdict.Warn)
                q.Add(new string[] { "重要数据备份方案", "把账目、会员、监控数据备份到移动硬盘，先保证丢不了", Price.Backup });
            if (ds == Verdict.Bad || ds == Verdict.Warn)
                q.Add(new string[] { "系统盘瘦身与垃圾清理", "清出 C 盘空间，保证系统更新和收银软件能正常存数据", Price.Clean });
            if (bt == Verdict.Bad || bt == Verdict.Warn)
                q.Add(new string[] { "开机优化（清理无效启动项）", "去掉没用的开机启动项，缩短开机和结账等待", Price.BootOpt });
            if (sf == Verdict.Bad || sf == Verdict.Warn)
                q.Add(new string[] { "桌面/文档路径修复", "把桌面和文档改回默认位置，找回'消失'的文件", Price.PathFix });
            if (nw == Verdict.Bad)
                q.Add(new string[] { "网络排查（网线/水晶头/路由器）", "找出掉线原因，避免收银和监控频繁断网（不含硬件）", Price.NetFix });
            if (sc == Verdict.Bad)
                q.Add(new string[] { "系统文件修复", "修复损坏的系统文件，减少报错、卡死和蓝屏", Price.SysFix });

            if (q.Count > 4) q.RemoveRange(4, q.Count - 4);
            q.Add(new string[] { "年度维护服务（第 1 台/年）", "含 4 次上门巡检 + 远程支持 + 备份提醒", Price.Year1 });
            if (deviceCount > 1)
                q.Add(new string[] { "年度维护服务（每增加 1 台）", "服务内容同上，按台计价", Price.YearN });
            return q;
        }

        /// 每一项都要给店主一个能照着做的办法（白话，一句话）
        private static string ActionText(string key, Verdict v)
        {
            if (key == "DiskSpace")
            {
                if (v == Verdict.Bad) return "清缓存和回收站；不够就加硬盘或挪走大文件";
                if (v == Verdict.Warn) return "清一次缓存、临时文件和回收站";
                return "够用，每季度看一眼";
            }
            if (key == "DiskHealth")
            {
                if (v == Verdict.Bad) return "今天先备份，再换新硬盘";
                if (v == Verdict.Warn) return "两天内备份数据，再安排换盘";
                if (v == Verdict.Unknown) return "用厂家工具复测一次再定";
                return "每年看一眼健康数据";
            }
            if (key == "Boot")
            {
                if (v == Verdict.Bad) return "关掉没用的启动项，开机能快一半";
                if (v == Verdict.Warn) return "不认识的先禁用，观察一周";
                return "不用动";
            }
            if (key == "Sfc")
            {
                if (v == Verdict.Bad) return "管理员身份跑 sfc /scannow，完事重启";
                if (v == Verdict.Unknown) return "换管理员身份重跑一次";
                return "不用动";
            }
            if (key == "Network")
            {
                if (v == Verdict.Bad) return "换网线、重压水晶头，再重启路由器";
                if (v == Verdict.Warn) return "查网线松紧和路由器散热";
                return "不用动";
            }
            if (key == "ShellFolders")
            {
                if (v == Verdict.Bad) return "把桌面和文档改回默认位置";
                if (v == Verdict.Warn) return "先问清是不是故意搬到网盘";
                return "不用动";
            }
            if (key == "Inventory")
            {
                if (v == Verdict.Bad) return "6-12 个月内规划换机";
                return "记进设备台账，下次对比";
            }
            if (key == "Experience")
            {
                if (v == Verdict.Bad) return "卸掉多余的管家，只留一个";
                if (v == Verdict.Warn) return "看看弹窗是谁加的，不用的卸载";
                return "不用动";
            }
            if (key == "Peripherals")
            {
                if (v == Verdict.Bad) return "重启打印机，换一根 USB 线再打一张测试小票";
                if (v == Verdict.Warn) return "确认打印机电源和线都插好，再打一张小票试试";
                return "不用动";
            }
            if (key == "PosSoftware")
            {
                if (v == Verdict.Normal) return "不用动";
                return "开店前先打开收银软件试结一单";
            }
            return "";
        }

        private static Verdict VOf(Snapshot s, string key)
        {
            foreach (Block b in s.Blocks) if (b.Item.Key == key) return b.Item.V;
            return Verdict.Unknown;
        }

        // ---------------- HTML 工具 ----------------
        private static string E(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }

        private static string Br(string s)
        {
            return E(s).Replace("\r\n", "<br/>").Replace("\n", "<br/>");
        }

        /// 没填就留手写空位
        private static string F(string s)
        {
            if (string.IsNullOrEmpty(s)) return "<span class=\"fill\" style=\"min-width:26mm\"></span>";
            return E(s);
        }

        private static string FirstLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return "-";
            string[] p = s.Replace("\r\n", "\n").Split('\n');
            return p[0];
        }

        // ---------------- 结论单 ----------------
        public static string BuildHtml(Snapshot s, int deviceCount)
        {
            StringBuilder h = new StringBuilder();
            TimeSpan dur = s.EndTime - s.StartTime;
            string durText = ((int)dur.TotalMinutes) + " 分 " + dur.Seconds.ToString("00") + " 秒";
            bool hasPrev = false;
            foreach (Block b in s.Blocks) if (!string.IsNullOrEmpty(b.Item.PrevMeasured)) hasPrev = true;

            h.Append("<!DOCTYPE html><html lang=\"zh-CN\"><head><meta charset=\"utf-8\"/>");
            h.Append("<title>电脑体检单 - ").Append(E(s.DeviceName)).Append("</title>");
            h.Append("<style>");
            h.Append("@page{size:A4 portrait;margin:8mm;}");
            h.Append("html,body{margin:0;padding:0;background:#fff;color:#000;}");
            h.Append("body{font-family:\"Microsoft YaHei\",\"微软雅黑\",SimSun,sans-serif;font-size:9.4pt;line-height:1.08;}");
            h.Append(".hdwrap{padding-bottom:1.2mm;}");
            h.Append(".rule{height:0;border-top:1.5px solid #000;margin:0 0 1.6mm 0;}");
            h.Append(".hdwrap h1{font-size:13.2pt;margin:0;letter-spacing:2px;}");
            h.Append(".sub{font-size:8.2pt;color:#333;margin-top:0.5mm;}");
            h.Append(".no{text-align:right;font-size:8.2pt;white-space:nowrap;vertical-align:bottom;}");
            h.Append(".bar{border:0.7px solid #000;padding:0.7mm 1.6mm;margin-bottom:0.9mm;font-size:8.8pt;line-height:1.1;}");
            h.Append(".bar span{margin-right:4mm;white-space:nowrap;}");
            h.Append("table.grid{width:100%;border-collapse:collapse;table-layout:fixed;font-size:8.95pt;line-height:1.1;}");
            h.Append("table.grid th{border:0.7px solid #000;padding:0.5mm 1mm;background:#f0f0f0;text-align:left;font-weight:bold;}");
            h.Append("table.grid td{border:0.7px solid #000;padding:0.2mm 0.9mm;vertical-align:top;}");
            h.Append("td.nm{width:27mm;}td.ms{width:51mm;}td.pv{width:21mm;}td.vd{width:17mm;white-space:nowrap;}");
            h.Append(".ok{color:#0a7a2c;font-weight:bold;}.wn{color:#b06a00;font-weight:bold;}");
            h.Append(".bd{color:#c00000;font-weight:bold;}.uk{color:#666;font-weight:bold;}");
            h.Append(".blk{border:0.7px solid #000;padding:0.5mm 1.4mm;margin-top:0.5mm;}");
            h.Append(".ttl{font-weight:bold;font-size:9.1pt;margin-bottom:0.25mm;}");
            h.Append(".ln{margin:0.15mm 0;font-size:8.2pt;}");
            h.Append(".fill{display:inline-block;border-bottom:0.7px solid #000;height:4mm;min-width:70mm;}");
            h.Append("table.q{width:100%;border-collapse:collapse;font-size:8.7pt;margin-top:0.5mm;}");
            h.Append("table.q th,table.q td{border:0.7px solid #000;padding:0.5mm 1.2mm;text-align:left;}");
            h.Append("table.q th{background:#f0f0f0;}");
            h.Append("table.sg{width:100%;border-collapse:collapse;margin-top:0.7mm;font-size:8.6pt;}");
            h.Append("table.sg td{border:0.7px solid #000;padding:0.3mm 1.8mm;height:4.6mm;vertical-align:bottom;width:50%;}");
            h.Append(".ft{font-size:7pt;color:#444;margin-top:0.5mm;}");
            h.Append(".act{font-size:7.9pt;color:#111;}");
            h.Append(".ph{display:flex;flex-wrap:wrap;gap:1.5mm;margin-top:0.6mm;}");
            h.Append(".ph img{max-width:48%;max-height:34mm;border:0.5px solid #000;}");
            h.Append("@media print{body{-webkit-print-color-adjust:exact;}}");
            h.Append("</style></head><body>");

            // 1. 页头
            h.Append("<div class=\"hdwrap\"><table style=\"width:100%;border-collapse:collapse;\"><tr>");
            h.Append("<td style=\"padding:0;vertical-align:bottom;\">");
            h.Append("<h1>电脑体检单</h1>");
            h.Append("<div class=\"sub\">六六电脑服务 · 66门店巡检 · 只读检测，不修改系统</div></td>");
            h.Append("<td class=\"no\">单据编号 ").Append(E(s.SheetNo));
            h.Append("<br/>日期 ").Append(E(s.StartTime.ToString("yyyy-MM-dd HH:mm"))).Append("</td>");
            h.Append("</tr></table></div><div class=\"rule\"></div>");

            // 2. 设备信息条
            h.Append("<div class=\"bar\">");
            h.Append("<span>门店：<b>").Append(F(s.StoreName)).Append("</b></span>");
            h.Append("<span>设备名：<b>").Append(E(s.DeviceName)).Append("</b></span>");
            h.Append("<span>检测人：").Append(F(s.Inspector)).Append("</span><br/>");
            h.Append("<span>型号：").Append(E(s.Inv.DeviceModel())).Append("</span>");
            h.Append("<span>出厂年份（机龄）：").Append(E(s.Inv.AgeText())).Append("</span>");
            h.Append("<span>检测台数：<b>").Append(deviceCount).Append(" 台</b></span>");
            h.Append("<span>检测耗时：").Append(E(durText)).Append("</span>");
            h.Append("</div>");

            // 2.5 给店主的三句话（全白话，不写术语）
            string[] three = PlainSummary(s, deviceCount);
            h.Append("<div class=\"blk\"><div class=\"ttl\">老板看这三句就够了</div>");
            h.Append("<div class=\"ln\"><b>① 现在怎么样：</b>").Append(E(three[0])).Append("</div>");
            h.Append("<div class=\"ln\"><b>② 要不要花钱：</b>").Append(E(three[1])).Append("</div>");
            h.Append("<div class=\"ln\"><b>③ 最该先做：</b>").Append(E(three[2])).Append("</div>");
            h.Append("</div>");

            // 3. 检测结果表
            h.Append("<table class=\"grid\"><thead><tr>");
            h.Append("<th class=\"nm\">查什么</th><th class=\"ms\">查到的结果</th>");
            if (hasPrev) h.Append("<th class=\"pv\">上次</th>");
            h.Append("<th class=\"vd\">结论</th><th>这是什么意思</th></tr></thead><tbody>");

            List<Block> ordered = new List<Block>();
            foreach (Block b in s.Blocks) if (b.Item.TopPriority) ordered.Add(b);
            foreach (Block b in s.Blocks) if (!b.Item.TopPriority) ordered.Add(b);

            foreach (Block b in ordered)
            {
                CheckItem it = b.Item;
                string rv = it.ReportValue.Length > 0 ? it.ReportValue : it.Measured;
                h.Append("<tr><td class=\"nm\">").Append(E(it.Name));
                if (it.TopPriority) h.Append("<br/><span class=\"bd\">最高优先</span>");
                h.Append("</td><td class=\"ms\">").Append(Br(rv)).Append("</td>");
                if (hasPrev) h.Append("<td class=\"pv\">").Append(Br(it.PrevMeasured)).Append("</td>");
                h.Append("<td class=\"vd\"><span class=\"").Append(Vs.Css(it.V)).Append("\">").Append(Vs.Sym(it.V))
                    .Append("</span> ").Append(Vs.Txt(it.V)).Append("</td>");
                h.Append("<td>").Append(E(it.Meaning));
                string act = ActionText(it.Key, it.V);
                if (act.Length > 0)
                    h.Append(" <span class=\"act\"><b>处理：</b>").Append(E(act)).Append("</span>");
                h.Append("</td></tr>");
            }
            h.Append("</tbody></table>");

            // 4. 需要人工确认的三件事
            h.Append("<div class=\"blk\"><div class=\"ttl\">工具查不到、需要你确认的三件事（请当场问清楚，手写记录）</div>");
            h.Append("<div class=\"ln\">1. 数据有没有备份？备份到哪儿：<span class=\"fill\" style=\"min-width:86mm\"></span></div>");
            h.Append("<div class=\"ln\">2. 监控录像能存几天（回放能往前调几天）：<span class=\"fill\" style=\"min-width:20mm\"></span> 天</div>");
            h.Append("<div class=\"ln\">3. 收银机和办公电脑是不是同一条网线（同一个路由器/交换机）：&nbsp;□ 是 &nbsp;&nbsp; □ 不是</div>");
            h.Append("</div>");

            // 5. 结论 + 报价 + 下次巡检 + 签字
            string cls = s.Overall == "该换" ? "bd" : (s.Overall == "该修" ? "wn" : "ok");
            h.Append("<div class=\"blk\">");
            h.Append("<div class=\"ttl\">综合结论：<span class=\"").Append(cls).Append("\">").Append(E(s.Overall))
                .Append("</span><span style=\"font-weight:normal;font-size:8.8pt\">&nbsp;（只有三档：不用换 / 该修 / 该换）</span></div>");
            h.Append("<div style=\"font-size:9.4pt;margin:0.6mm 0 1mm 0\">").Append(E(s.OverallNote)).Append("</div>");

            h.Append("<table class=\"q\"><thead><tr><th style=\"width:52mm\">建议做</th><th>为什么建议做</th><th style=\"width:20mm\">费用(元)</th></tr></thead><tbody>");
            foreach (string[] row in QuoteRows(s, deviceCount))
                h.Append("<tr><td>").Append(E(row[0])).Append("</td><td>").Append(E(row[1])).Append("</td><td>").Append(E(row[2])).Append("</td></tr>");
            h.Append("</tbody></table>");
            h.Append("<div style=\"font-size:8.4pt;color:#444;margin-top:0.8mm\">以上是上门服务费，不含配件；以现场报价为准。</div>");
            h.Append("<div style=\"font-size:9.4pt;margin-top:1.2mm\">下次巡检建议：").Append(E(NextVisit(s))).Append("</div>");
            h.Append("</div>");

            // 5.5 现场照片（可选，嵌进 HTML，不另存文件）
            if (s.Photos.Count > 0)
            {
                h.Append("<div class=\"blk\"><div class=\"ttl\">现场照片（")
                    .Append(s.Photos.Count).Append(" 张，仅本单存档）</div><div class=\"ph\">");
                foreach (string ph in s.Photos)
                    h.Append("<img src=\"").Append(ph).Append("\"/>");
                h.Append("</div></div>");
            }

            h.Append("<table class=\"sg\"><tr><td>客户确认签字：</td><td>检测人签字：</td></tr>");
            h.Append("<tr><td>日期：&nbsp;&nbsp;&nbsp; 年 &nbsp;&nbsp;&nbsp; 月 &nbsp;&nbsp;&nbsp; 日</td><td>日期：&nbsp;&nbsp;&nbsp; 年 &nbsp;&nbsp;&nbsp; 月 &nbsp;&nbsp;&nbsp; 日</td></tr></table>");
            h.Append("<div class=\"ft\">这份体检单由只读检测生成：没删文件、没改系统设置、没动开机启动项、没装任何软件、没往外传数据。</div>");
            h.Append("</body></html>");
            return h.ToString();
        }

        // ---------------- 原始数据 ----------------
        public static string BuildTxt(Snapshot s, int deviceCount)
        {
            StringBuilder t = new StringBuilder();
            TimeSpan dur = s.EndTime - s.StartTime;
            t.AppendLine("================================================================");
            t.AppendLine("66门店巡检 · 原始数据（只读采集，无一键修复）");
            t.AppendLine("================================================================");
            t.AppendLine("门店名称：" + s.StoreName);
            t.AppendLine("检测人：" + s.Inspector);
            t.AppendLine("设备名：" + s.DeviceName);
            t.AppendLine("设备型号：" + s.Inv.DeviceModel());
            t.AppendLine("出厂年份（机龄）：" + s.Inv.AgeText());
            t.AppendLine("检测台数：" + deviceCount);
            t.AppendLine("开始：" + s.StartTime.ToString("yyyy-MM-dd HH:mm:ss"));
            t.AppendLine("结束：" + s.EndTime.ToString("yyyy-MM-dd HH:mm:ss"));
            t.AppendLine("耗时：" + ((int)dur.TotalMinutes) + " 分 " + dur.Seconds.ToString("00") + " 秒");
            t.AppendLine("单据编号：" + s.SheetNo);
            t.AppendLine("综合结论：" + s.Overall + "（" + s.OverallNote + "）");
            t.AppendLine("");

            for (int i = 0; i < s.Blocks.Count; i++)
            {
                Block b = s.Blocks[i];
                t.AppendLine("----------------------------------------------------------------");
                t.AppendLine("[" + (i + 1) + "] " + b.Item.Name);
                t.AppendLine("实测：");
                foreach (string ln in b.Item.Measured.Replace("\r\n", "\n").Split('\n'))
                    t.AppendLine("  " + ln);
                if (b.Item.PrevMeasured.Length > 0) t.AppendLine("上次：" + Fmt.Clean(b.Item.PrevMeasured));
                t.AppendLine("判定：" + Vs.Sym(b.Item.V) + " " + Vs.Txt(b.Item.V));
                t.AppendLine("意味着什么：" + b.Item.Meaning);
                string act = ActionText(b.Item.Key, b.Item.V);
                if (act.Length > 0) t.AppendLine("处理建议：" + act);
                foreach (string ln in b.Lines) t.AppendLine("  " + ln);
                t.AppendLine("");
            }

            t.AppendLine("----------------------------------------------------------------");
            t.AppendLine("整机资产清单（可作为年度服务合同附件）");
            t.AppendLine("设备型号：" + s.Inv.DeviceModel());
            t.AppendLine("出厂年份（机龄）：" + s.Inv.AgeText());
            t.AppendLine("CPU：" + s.Inv.Cpu);
            t.AppendLine("内存：" + s.Inv.RamGB.ToString("0.0") + "G（" + s.Inv.RamSticks + " 条）");
            t.AppendLine("显卡：" + s.Inv.Gpu);
            t.AppendLine("主板：" + s.Inv.Board);
            t.AppendLine("显示器：" + (s.Inv.MonitorCount >= 0 ? s.Inv.MonitorCount + " 台" : "未检测"));
            t.AppendLine("硬盘：" + string.Join("；", s.Inv.Disks.ToArray()));
            t.AppendLine("系统：" + s.Inv.OsCaption + " " + s.Inv.OsVersion + "（内部版本 " + s.Inv.OsBuild + "）");
            t.AppendLine("系统安装日期：" + (s.Inv.InstallDate.Length > 0 ? s.Inv.InstallDate : "未检测"));
            t.AppendLine("BIOS 日期：" + (s.Inv.BiosDate.Length > 0 ? s.Inv.BiosDate : "未检测"));
            t.AppendLine("已装软件数量：" + s.Inv.SwCount);
            foreach (string x in s.Inv.SwList) t.AppendLine("  " + x);
            t.AppendLine("管家/助手类软件：" + (s.Inv.Bundles.Count > 0 ? string.Join("、", s.Inv.Bundles.ToArray()) : "无"));
            t.AppendLine("");

            t.AppendLine("[KEYS]");
            foreach (Block b in s.Blocks)
            {
                foreach (KeyValuePair<string, string> kv in b.Keys)
                {
                    if (string.IsNullOrEmpty(kv.Value)) continue;
                    t.AppendLine(kv.Key + "=" + kv.Value);
                }
            }
            t.AppendLine("[/KEYS]");
            return t.ToString();
        }

        // ---------------- 历史对比 ----------------
        private static string PrefKey(string itemKey)
        {
            switch (itemKey)
            {
                case "DiskSpace": return "C盘剩余GB";
                case "DiskHealth": return "硬盘通电小时";
                case "Boot": return "开机秒数";
                case "Network": return "网关延迟毫秒";
                case "ShellFolders": return "路径异常数";
                case "Inventory": return "已装软件数";
                case "Experience": return "右键菜单第三方项";
                default: return "";
            }
        }

        private static string UnitOf(string key)
        {
            switch (key)
            {
                case "C盘剩余GB": return "G";
                case "硬盘通电小时": return " 小时";
                case "开机秒数": return " 秒";
                case "网关延迟毫秒": return "ms";
                case "路径异常数": return " 处";
                case "右键菜单第三方项": return " 个";
                default: return "";
            }
        }

        public static Dictionary<string, string> LoadPrev(string dir, string device, string excludeFile)
        {
            Dictionary<string, string> map = new Dictionary<string, string>();
            try
            {
                string dev = SafeName(device);
                List<string> files = new List<string>(Directory.GetFiles(dir, "原始数据_" + dev + "_*.txt"));
                files.Sort();
                string pick = "";
                for (int i = files.Count - 1; i >= 0; i--)
                {
                    if (excludeFile != null && string.Equals(files[i], excludeFile, StringComparison.OrdinalIgnoreCase)) continue;
                    pick = files[i];
                    break;
                }
                if (pick.Length == 0) return map;

                bool inKeys = false;
                foreach (string line in File.ReadAllLines(pick, Encoding.UTF8))
                {
                    string ln = line.Trim();
                    if (ln == "[KEYS]") { inKeys = true; continue; }
                    if (ln == "[/KEYS]") break;
                    if (!inKeys) continue;
                    int p = ln.IndexOf('=');
                    if (p <= 0) continue;
                    map[ln.Substring(0, p).Trim()] = ln.Substring(p + 1).Trim();
                }
            }
            catch (Exception) { }
            return map;
        }

        public static void ApplyHistory(Snapshot s, Dictionary<string, string> prev)
        {
            if (prev == null || prev.Count == 0) return;
            foreach (Block b in s.Blocks)
            {
                string key = PrefKey(b.Item.Key);
                if (key.Length == 0) continue;
                string cur = "";
                if (b.Keys.ContainsKey(key)) cur = b.Keys[key];
                if (string.IsNullOrEmpty(cur)) continue;
                string old = "";
                if (!prev.TryGetValue(key, out old)) continue;
                if (string.IsNullOrEmpty(old)) continue;
                string u = UnitOf(key);
                string nv = Fmt.Cut(cur, 9) + u;
                string ov = Fmt.Cut(old, 9) + u;
                b.Item.PrevMeasured = ov == nv ? nv + "（持平）" : ov + " → " + nv;
            }
        }

        // ---------------- 写盘 ----------------
        public static void WriteDevice(Snapshot s, int deviceCount, out string htmlPath, out string txtPath)
        {
            string dev = SafeName(s.DeviceName);
            string stamp = s.EndTime.ToString("yyyyMMdd_HHmm");
            htmlPath = "";
            txtPath = "";
            // 同一分钟同一台设备重复跑时，加 _2/_3 后缀，绝不覆盖上一份
            for (int n = 1; n < 100; n++)
            {
                string suffix = n == 1 ? "" : "_" + n;
                string h = Path.Combine(_dir, "结论单_" + dev + "_" + stamp + suffix + ".html");
                string t = Path.Combine(_dir, "原始数据_" + dev + "_" + stamp + suffix + ".txt");
                if (File.Exists(h) || File.Exists(t)) continue;
                htmlPath = h;
                txtPath = t;
                break;
            }
            if (htmlPath.Length == 0)
            {
                htmlPath = Path.Combine(_dir, "结论单_" + dev + "_" + stamp + "_dup.html");
                txtPath = Path.Combine(_dir, "原始数据_" + dev + "_" + stamp + "_dup.txt");
            }
            File.WriteAllText(htmlPath, BuildHtml(s, deviceCount), new UTF8Encoding(false));
            File.WriteAllText(txtPath, BuildTxt(s, deviceCount), new UTF8Encoding(false));
        }

        public static string WriteOverview(List<Snapshot> list, string storeName)
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmm");
            string nm = SafeName(string.IsNullOrEmpty(storeName) ? "本店" : storeName);
            string path = Path.Combine(_dir, "总览_" + nm + "_" + stamp + ".html");
            StringBuilder h = new StringBuilder();
            h.Append("<!DOCTYPE html><html lang=\"zh-CN\"><head><meta charset=\"utf-8\"/>");
            h.Append("<title>").Append(E(storeName)).Append(" 设备总览</title><style>");
            h.Append("@page{size:A4 portrait;margin:14mm;}");
            h.Append("body{font-family:\"Microsoft YaHei\",SimSun,sans-serif;font-size:10.5pt;color:#000;margin:0;}");
            h.Append("h1{font-size:15pt;margin:0 0 1mm 0;}");
            h.Append(".sub{font-size:9pt;color:#333;margin-bottom:4mm;}");
            h.Append("table{width:100%;border-collapse:collapse;font-size:9.8pt;}");
            h.Append("th,td{border:0.7px solid #000;padding:1.6mm 2mm;text-align:left;vertical-align:top;}");
            h.Append("th{background:#f0f0f0;}");
            h.Append(".ok{color:#0a7a2c;font-weight:bold;}.wn{color:#b06a00;font-weight:bold;}");
            h.Append(".bd{color:#c00000;font-weight:bold;}.uk{color:#666;font-weight:bold;}");
            h.Append(".ft{font-size:8.6pt;color:#444;margin-top:4mm;}");
            h.Append("</style></head><body>");
            h.Append("<h1>").Append(E(string.IsNullOrEmpty(storeName) ? "本店" : storeName)).Append(" · ").Append(list.Count).Append(" 台设备总览</h1>");
            h.Append("<div class=\"sub\">六六电脑服务 · 66门店巡检 · 只读检测 · 生成时间 ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm")).Append("</div>");
            h.Append("<table><thead><tr><th style=\"width:26mm\">设备名</th><th>型号</th><th style=\"width:24mm\">机龄</th>");
            h.Append("<th style=\"width:26mm\">C 盘剩余</th><th style=\"width:24mm\">开机</th><th style=\"width:22mm\">硬盘健康</th><th style=\"width:16mm\">判定</th></tr></thead><tbody>");
            foreach (Snapshot s in list)
            {
                string cfree = "-", boot = "-", health = "-";
                Verdict hv = Verdict.Unknown;
                foreach (Block b in s.Blocks)
                {
                    if (b.Item.Key == "DiskSpace") cfree = Fmt.Clean(FirstLine(b.Item.ReportValue.Length > 0 ? b.Item.ReportValue : b.Item.Measured)).Replace("剩余 ", "");
                    if (b.Item.Key == "Boot")
                    {
                        string tmp = Fmt.Clean(FirstLine(b.Item.ReportValue)).Replace("开机 ", "");
                        boot = tmp;
                    }
                    if (b.Item.Key == "DiskHealth") { health = Fmt.Clean(FirstLine(b.Item.ReportValue)); hv = b.Item.V; }
                }
                string oc = s.Overall == "该换" ? "bd" : (s.Overall == "该修" ? "wn" : "ok");
                h.Append("<tr><td>").Append(E(s.DeviceName)).Append("</td><td>").Append(E(s.Inv.DeviceModel())).Append("</td>");
                h.Append("<td>").Append(E(s.Inv.AgeYears() >= 0 ? s.Inv.AgeYears() + " 年" : "未检测")).Append("</td>");
                h.Append("<td>").Append(E(cfree)).Append("</td><td>").Append(E(boot)).Append("</td>");
                h.Append("<td><span class=\"").Append(Vs.Css(hv)).Append("\">").Append(Vs.Sym(hv)).Append("</span> ")
                    .Append(E(Vs.Txt(hv))).Append("</td>");
                h.Append("<td><span class=\"").Append(oc).Append("\">").Append(E(s.Overall)).Append("</span></td></tr>");
            }
            h.Append("</tbody></table>");
            h.Append("<div class=\"ft\">结论只有三档：不用换 / 该修 / 该换。本页可作为年度维护服务的设备台账。</div>");
            h.Append("</body></html>");
            File.WriteAllText(path, h.ToString(), new UTF8Encoding(false));
            return path;
        }

        public static string WriteAsset(Snapshot s)
        {
            string dev = SafeName(s.DeviceName);
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmm");
            string path = Path.Combine(_dir, "资产清单_" + dev + "_" + stamp + ".html");
            StringBuilder h = new StringBuilder();
            h.Append("<!DOCTYPE html><html lang=\"zh-CN\"><head><meta charset=\"utf-8\"/>");
            h.Append("<title>资产清单 - ").Append(E(s.DeviceName)).Append("</title><style>");
            h.Append("@page{size:A4 portrait;margin:14mm;}");
            h.Append("body{font-family:\"Microsoft YaHei\",SimSun,sans-serif;font-size:10.5pt;color:#000;margin:0;}");
            h.Append("h1{font-size:14pt;margin:0 0 2mm 0;}");
            h.Append("table{width:100%;border-collapse:collapse;font-size:10pt;}");
            h.Append("th,td{border:0.7px solid #000;padding:1.6mm 2mm;text-align:left;}");
            h.Append("th{background:#f0f0f0;width:30mm;}");
            h.Append(".ft{font-size:8.6pt;color:#444;margin-top:3mm;}");
            h.Append("</style></head><body>");
            h.Append("<h1>设备资产清单</h1><div class=\"ft\" style=\"margin-top:0\">门店：").Append(F(s.StoreName))
                .Append("　检测人：").Append(F(s.Inspector)).Append("　设备名：").Append(E(s.DeviceName))
                .Append("　时间：").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm")).Append("</div><table>");
            h.Append(Row("设备型号", s.Inv.DeviceModel()));
            h.Append(Row("出厂年份（机龄）", s.Inv.AgeText()));
            h.Append(Row("CPU", s.Inv.Cpu));
            h.Append(Row("内存", s.Inv.RamGB.ToString("0.0") + "G（" + s.Inv.RamSticks + " 条）"));
            h.Append(Row("显卡", s.Inv.Gpu));
            h.Append(Row("主板", s.Inv.Board));
            h.Append(Row("显示器", s.Inv.MonitorCount >= 0 ? s.Inv.MonitorCount + " 台" : "未检测"));
            h.Append(Row("硬盘", string.Join("；", s.Inv.Disks.ToArray())));
            h.Append(Row("操作系统", s.Inv.OsCaption + " " + s.Inv.OsVersion + "（内部版本 " + s.Inv.OsBuild + "）"));
            h.Append(Row("系统安装日期", s.Inv.InstallDate));
            h.Append(Row("BIOS 日期", s.Inv.BiosDate));
            h.Append(Row("已装软件数量", s.Inv.SwCount + " 个"));
            h.Append(Row("已装软件（前 15）", string.Join("；", s.Inv.SwList.ToArray())));
            h.Append(Row("管家/助手类软件", s.Inv.Bundles.Count > 0 ? string.Join("、", s.Inv.Bundles.ToArray()) : "无"));
            h.Append("</table><div class=\"ft\">本清单由只读巡检采集，用于年度维护服务的设备台账登记。</div></body></html>");
            File.WriteAllText(path, h.ToString(), new UTF8Encoding(false));
            return path;
        }

        private static string Row(string k, string v)
        {
            return "<tr><th>" + E(k) + "</th><td>" + E(v) + "</td></tr>";
        }

        public static void Open(string path)
        {
            try
            {
                // 自动化测试用：设置 SC_NOOPEN=1 时不弹浏览器
                if (Environment.GetEnvironmentVariable("SC_NOOPEN") == "1") return;
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = path;
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
            catch (Exception) { }
        }

        // ---------------- 一键导出 PDF ----------------
        /// 用本机 Edge 的无头模式把结论单原样打印成 PDF（同目录、同名）
        /// 没装 Edge / 导出超时就返回空字符串，绝不阻塞主流程太久
        public static string ExportPdf(string htmlPath)
        {
            if (string.IsNullOrEmpty(htmlPath) || !File.Exists(htmlPath)) return "";
            string edge = FindEdge();
            if (edge.Length == 0) return "";
            string pdf = Path.ChangeExtension(htmlPath, ".pdf");
            try
            {
                if (File.Exists(pdf)) File.Delete(pdf);
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = edge;
                psi.Arguments = "--headless=new --disable-gpu --no-pdf-header-footer --print-to-pdf=\"" + pdf + "\" \"" + htmlPath + "\"";
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                using (Process p = new Process())
                {
                    p.StartInfo = psi;
                    p.Start();
                    if (!p.WaitForExit(20000))
                    {
                        try { p.Kill(); } catch (Exception) { }
                    }
                }
                return File.Exists(pdf) ? pdf : "";
            }
            catch (Exception) { return ""; }
        }

        private static string FindEdge()
        {
            string[] paths = new string[] {
                @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
                @"C:\Program Files\Microsoft\Edge\Application\msedge.exe"
            };
            foreach (string p in paths)
            {
                try { if (File.Exists(p)) return p; }
                catch (Exception) { }
            }
            return "";
        }

        // ---------------- 现场照片 ----------------
        /// 交互式询问要插入的照片；只接受常见图片格式，单张 ≤2.5MB、总共 ≤5MB
        public static void AskPhotos(Snapshot s)
        {
            if (s == null || Console.IsInputRedirected) return;
            int count = 0;
            long total = 0;
            const long oneMax = 2621440;   // 2.5MB
            const long sumMax = 5242880;   // 5MB
            while (count < 3)
            {
                Ui.Blank();
                Ui.C(count == 0 ? "  要插入现场照片吗？（直接回车跳过，输入图片完整路径）："
                                : "  再加一张？直接回车结束：", ConsoleColor.Cyan);
                string p = ReadLineSafe().Trim().Trim('"');
                if (p.Length == 0) break;
                try
                {
                    if (!File.Exists(p)) { Ui.Err("找不到这个文件：" + p); continue; }
                    string ext = Path.GetExtension(p).ToLower();
                    string mime;
                    if (ext == ".jpg" || ext == ".jpeg") mime = "image/jpeg";
                    else if (ext == ".png") mime = "image/png";
                    else if (ext == ".bmp") mime = "image/bmp";
                    else if (ext == ".gif") mime = "image/gif";
                    else { Ui.Err("只支持 jpg / png / bmp / gif"); continue; }
                    long len = new FileInfo(p).Length;
                    if (len > oneMax) { Ui.Err("这张照片 " + Fmt.G(len) + " 太大，请压到 2.5MB 以内再试"); continue; }
                    if (total + len > sumMax) { Ui.Err("照片总大小超过 5MB，后面的不再插入"); break; }
                    byte[] bytes = File.ReadAllBytes(p);
                    s.Photos.Add("data:" + mime + ";base64," + Convert.ToBase64String(bytes));
                    total += len;
                    count++;
                    Ui.OK("已加入：" + Path.GetFileName(p) + "（" + Fmt.G(len) + "）");
                }
                catch (Exception ex) { Ui.Err("读取失败：" + Fmt.Clean(ex.Message)); }
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
