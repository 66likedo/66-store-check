using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;

namespace StoreCheck
{
    /// 专业版报告：技术版（可多页 A4）+ 原始数据 + 处理清单
    public static class ProReport
    {
        private static string _dir = "";

        public static string Dir { get { return _dir; } }

        public static string PrepareDir()
        {
            string exeDir = "";
            try { exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location); }
            catch (Exception) { }
            if (string.IsNullOrEmpty(exeDir)) exeDir = AppDomain.CurrentDomain.BaseDirectory;
            if (string.IsNullOrEmpty(exeDir)) exeDir = Environment.CurrentDirectory;

            string want = "";
            try { want = Path.Combine(exeDir, "专业报告"); }
            catch (Exception) { }
            if (TryUse(want)) { _dir = want; return _dir; }

            string tmp = "";
            try { tmp = Path.Combine(Path.GetTempPath(), "66check_pro"); }
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
            return r.Length > 40 ? r.Substring(0, 40) : r;
        }

        private static string E(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }

        private static string Br(string s)
        {
            return E(s).Replace("\r\n", "<br/>").Replace("\n", "<br/>");
        }

        private static int CountOf(Snapshot s, Verdict v)
        {
            int n = 0;
            foreach (Block b in s.Blocks) if (b.Item.V == v) n++;
            return n;
        }

        // ------------------------------------------------------------
        // 建议动作（技术员看的，不是店主看的）
        // ------------------------------------------------------------
        /// 给店主的三句话（白话）：现状 / 花不花钱 / 先做哪件
        private static string[] PlainSummary(Snapshot s, int bad, int warn)
        {
            CheckItem top = null;
            foreach (Block b in s.Blocks) if (b.Item.V == Verdict.Bad && b.Item.TopPriority) { top = b.Item; break; }
            if (top == null) foreach (Block b in s.Blocks) if (b.Item.V == Verdict.Bad) { top = b.Item; break; }
            if (top == null) foreach (Block b in s.Blocks) if (b.Item.V == Verdict.Warn) { top = b.Item; break; }

            string now = bad > 0
                ? "这台电脑有 " + bad + " 处必须处理的问题，现在还能用，但拖久了可能影响收银"
                : (warn > 0 ? "这台电脑整体能用，有 " + warn + " 处建议尽快处理" : "这台电脑目前状态正常，这次不用修也不用换");
            string money = bad > 0
                ? "先把 P1 的应急项目做掉，其余不着急，具体费用看第 2 节"
                : (warn > 0 ? "做几项常规维护即可，费用看第 2 节，不是非花不可" : "这次不用花钱，每月把账目和会员数据备份一次就行");
            string first = top != null
                ? "先处理「" + top.Name + "」：" + FirstSentence(top.Meaning)
                : "继续正常用，把账目、会员、监控数据每月备份到移动硬盘";
            return new string[] { now, money, first };
        }

        private static string FirstSentence(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string x = s.Replace("\r\n", "\n").Split('。')[0].Trim();
            return x.Length > 0 ? x + "。" : s;
        }

        private static string[] Advice(string key, Verdict v)
        {
            bool bad = v == Verdict.Bad;
            switch (key)
            {
                case "SysLicense":
                    return bad
                        ? new string[] { "系统未激活", "先激活再交付，否则后续更新/个性化里会反复弹提示", "slmgr /dlv 查看授权详情（只读）；有正版密钥再 slui 4" }
                        : new string[] { "系统与授权有缺口", "补丁断更或安全开关被关，按需补一次", "设置 → Windows 更新；确认 UAC 未被安全软件关闭" };
                case "DiskSpace":
                    return new string[] { "系统盘空间不足", "先清系统盘，再考虑把 Users 大目录迁到数据盘", "cleanmgr /sageset:11；%TEMP%、SoftwareDistribution\\Download、浏览器缓存；大目录用 robocopy /MIR 迁移后再改路径" };
                case "DiskHealth":
                    return bad
                        ? new string[] { "硬盘已出现故障预警或坏道", "先整盘备份，再换盘迁移，别在原盘上反复读写", "备份：robocopy /E /R:1 /W:1；换盘后用品牌工具克隆或 DISM /Capture-Image" }
                        : new string[] { "硬盘开始老化", "先把重要数据备份出来，安排时间换盘", "备份到移动硬盘/网络盘；换盘后恢复" };
                case "Boot":
                    return new string[] { "开机慢 / 自启项多", "精简开机自启，保留输入法、杀毒、收银软件", "任务管理器 → 启动；msconfig；删除失效的 Run 项前先记录" };
                case "Sfc":
                    return new string[] { "系统文件有损坏", "修复系统文件，必要时补系统组件", "sfc /scannow；DISM /Online /Cleanup-Image /RestoreHealth（需联网）" };
                case "EventLog":
                    return new string[] { "系统在频繁报错", "按 Top 来源逐个定位：磁盘/驱动/服务/软件崩溃", "事件查看器 → Windows 日志 → 系统；按来源和 ID 搜索；先导出日志再动" };
                case "Services":
                    return new string[] { "服务或设备异常", "先修驱动/设备，再看服务启动失败原因", "设备管理器更新驱动；services.msc 查看依赖；事件 7000/7009 记录失败模块" };
                case "Tasks":
                    return new string[] { "非微软计划任务偏多", "逐条核对来源，删掉不用的更新器和推广任务", "taskschd.msc 查看；删除前先导出任务 XML 备份" };
                case "Network":
                    return new string[] { "网络质量或配置有问题", "先查物理层（网线/水晶头/交换机口），再看 DNS 与代理", "测线仪压线；ipconfig /flushdns；DNS 改 223.5.5.5 / 119.29.29.29；去掉不明代理" };
                case "Security":
                    return new string[] { "安全开关或远程入口偏松", "按现状收紧：不需要的远控卸掉，远程桌面不用就关，防火墙保持全开", "防火墙：wf.msc；远控在控制面板卸载；关闭 RDP：系统属性 → 远程 → 不允许连接" };
                case "ShellFolders":
                    return new string[] { "桌面/文档路径异常", "把路径改回默认并搬回文件", "右键桌面 → 属性 → 位置 → 还原默认值；搬回前先备份" };
                case "Inventory":
                    return new string[] { "资产已登记", "无", "无" };
                case "Experience":
                    return new string[] { "常驻软件/右键菜单太乱", "卸掉多余的管家、助手、看图、压缩类软件，只留一套", "控制面板卸载；右键菜单用 shell 菜单管理工具删除" };
                default:
                    return new string[] { "需要关注", "按实测值逐项确认", "无" };
            }
        }

        // ------------------------------------------------------------
        // 技术报告 HTML
        // ------------------------------------------------------------
        public static string BuildHtml(Snapshot s, bool admin)
        {
            StringBuilder h = new StringBuilder();
            TimeSpan dur = s.EndTime - s.StartTime;
            string durText = ((int)dur.TotalMinutes) + " 分 " + dur.Seconds.ToString("00") + " 秒";
            int bad = CountOf(s, Verdict.Bad);
            int warn = CountOf(s, Verdict.Warn);
            int unk = CountOf(s, Verdict.Unknown);

            h.Append("<!DOCTYPE html><html lang=\"zh-CN\"><head><meta charset=\"utf-8\"/>");
            h.Append("<title>专业巡检报告 - ").Append(E(s.DeviceName)).Append("</title><style>");
            h.Append("@page{size:A4 portrait;margin:14mm;}");
            h.Append("html,body{margin:0;padding:0;background:#fff;color:#000;}");
            h.Append("body{font-family:\"Microsoft YaHei\",\"微软雅黑\",SimSun,sans-serif;font-size:10pt;line-height:1.4;}");
            h.Append("h1{font-size:16pt;margin:0 0 1mm 0;letter-spacing:1px;}");
            h.Append("h2{font-size:12pt;margin:5mm 0 2mm 0;padding-bottom:1mm;border-bottom:1.2px solid #000;}");
            h.Append("h2.newpage{page-break-before:always;}");
            h.Append(".sub{font-size:9pt;color:#333;}");
            h.Append(".no{text-align:right;font-size:8.8pt;white-space:nowrap;vertical-align:bottom;}");
            h.Append(".bar{border:0.7px solid #000;padding:1.6mm 2mm;margin:2mm 0;font-size:9pt;}");
            h.Append(".bar span{margin-right:5mm;white-space:nowrap;}");
            h.Append(".sum{border:1.2px solid #000;padding:2mm;margin:2mm 0;font-size:10pt;}");
            h.Append("table{width:100%;border-collapse:collapse;font-size:9pt;}");
            h.Append("th,td{border:0.6px solid #000;padding:1mm 1.4mm;text-align:left;vertical-align:top;}");
            h.Append("th{background:#efefef;font-weight:bold;}");
            h.Append("tr{page-break-inside:avoid;}");
            h.Append(".ok{color:#0a7a2c;font-weight:bold;}.wn{color:#b06a00;font-weight:bold;}");
            h.Append(".bd{color:#c00000;font-weight:bold;}.uk{color:#666;font-weight:bold;}");
            h.Append("ul{margin:1mm 0 0 4mm;padding:0;}li{margin:0.4mm 0;font-size:9pt;}");
            h.Append(".ft{font-size:7.6pt;color:#444;margin-top:3mm;border-top:0.6px solid #666;padding-top:1mm;}");
            h.Append("@media print{body{-webkit-print-color-adjust:exact;}}");
            h.Append("</style></head><body>");

            h.Append("<table style=\"width:100%;border-collapse:collapse;border:0;\"><tr>");
            h.Append("<td style=\"border:0;padding:0;vertical-align:bottom;\"><h1>专业巡检报告</h1>");
            h.Append("<div class=\"sub\">六六电脑服务 · 66专业检测 · 只读深检，不修改系统</div></td>");
            h.Append("<td class=\"no\" style=\"border:0;\">单据编号 ").Append(E(s.SheetNo));
            h.Append("<br/>").Append(E(s.StartTime.ToString("yyyy-MM-dd HH:mm")));
            h.Append("<br/>权限 ").Append(admin ? "管理员（深检已启用）" : "受限（部分项未检测）").Append("</td></tr></table>");

            h.Append("<div class=\"bar\">");
            h.Append("<span>门店：<b>").Append(E(s.StoreName)).Append("</b></span>");
            h.Append("<span>设备名：<b>").Append(E(s.DeviceName)).Append("</b></span>");
            h.Append("<span>检测人：").Append(E(s.Inspector)).Append("</span><br/>");
            h.Append("<span>型号：").Append(E(s.Inv.DeviceModel())).Append("</span>");
            h.Append("<span>出厂年份（机龄）：").Append(E(s.Inv.AgeText())).Append("</span>");
            h.Append("<span>检测项：").Append(s.Blocks.Count).Append(" 项</span>");
            h.Append("<span>耗时：").Append(E(durText)).Append("</span>");
            h.Append("</div>");

            h.Append("<div class=\"sum\"><b>结论摘要：</b>严重 <span class=\"bd\">").Append(bad).Append("</span> 项，中等 <span class=\"wn\">")
                .Append(warn).Append("</span> 项，未检测 <span class=\"uk\">").Append(unk).Append("</span> 项。");
            h.Append(bad > 0 ? "存在必须先处理的问题（见第 2 节 P1）。" : (warn > 0 ? "无致命问题，有若干项建议处理。" : "整体健康，无必须处理的项。"));
            h.Append("</div>");

            // 给店主的三句话：先用大白话说清现状、花不花钱、先做哪件
            string[] plain = PlainSummary(s, bad, warn);
            h.Append("<div class=\"sum\"><b>给店主的三句话</b><br/>");
            h.Append("① 现在怎么样：").Append(E(plain[0])).Append("<br/>");
            h.Append("② 要不要花钱：").Append(E(plain[1])).Append("<br/>");
            h.Append("③ 最该先做：").Append(E(plain[2]));
            h.Append("</div>");

            // 1. 检测结果总表
            h.Append("<h2>1. 检测结果</h2><table><thead><tr>");
            h.Append("<th style=\"width:30mm\">检测项</th><th style=\"width:18mm\">结果</th><th>关键证据</th></tr></thead><tbody>");
            foreach (Block b in s.Blocks)
            {
                CheckItem it = b.Item;
                h.Append("<tr><td>").Append(E(it.Name)).Append("</td><td><span class=\"").Append(Vs.Css(it.V))
                    .Append("\">").Append(Vs.Sym(it.V)).Append("</span> ").Append(Vs.Txt(it.V)).Append("</td><td>")
                    .Append(Br(it.ReportValue.Length > 0 ? it.ReportValue : it.Measured)).Append("</td></tr>");
            }
            h.Append("</tbody></table>");

            // 2. 问题清单
            h.Append("<h2 class=\"newpage\">2. 问题清单与处理建议</h2>");
            h.Append("<table><thead><tr><th style=\"width:12mm\">优先级</th><th style=\"width:26mm\">项目</th>");
            h.Append("<th style=\"width:46mm\">现象</th><th>建议动作</th></tr></thead><tbody>");
            int p1 = 0, p2 = 0;
            foreach (Block b in s.Blocks)
            {
                if (b.Item.V != Verdict.Bad && b.Item.V != Verdict.Warn) continue;
                if (b.Item.Key == "Inventory") continue;
                string[] ad = Advice(b.Item.Key, b.Item.V);
                string pri = b.Item.V == Verdict.Bad ? "P1 严重" : "P2 中等";
                if (b.Item.V == Verdict.Bad) p1++; else p2++;
                h.Append("<tr><td><span class=\"").Append(b.Item.V == Verdict.Bad ? "bd" : "wn").Append("\">")
                    .Append(pri).Append("</span></td><td>").Append(E(b.Item.Name)).Append("</td><td>")
                    .Append(E(ad[0])).Append("<br/>").Append(Br(b.Item.ReportValue)).Append("</td><td>")
                    .Append(E(ad[1])).Append("<br/><span style=\"color:#444\">参考：").Append(E(ad[2])).Append("</span></td></tr>");
            }
            if (p1 + p2 == 0)
                h.Append("<tr><td colspan=\"4\">没有需要处理的项。</td></tr>");
            h.Append("</tbody></table>");

            foreach (Block b in s.Blocks)
            {
                if (b.Item.V != Verdict.Unknown) continue;
                string[] ad = Advice(b.Item.Key, b.Item.V);
                h.Append("<div style=\"margin-top:2mm;font-size:9pt\"><span class=\"uk\">○ 未检测：</span>")
                    .Append(E(b.Item.Name)).Append(" —— ").Append(E(b.Item.Meaning)).Append("</div>");
            }

            // 3. 常驻项
            h.Append("<h2 class=\"newpage\">3. 常驻项与网络面（逐条核对用）</h2>");
            AppendLines(h, s, "问题设备：", "带感叹号的设备", 20);
            AppendLines(h, s, "  未运行：", "自动启动但未运行", 20);
            AppendStartup(h, s);
            AppendLines(h, s, "第三方服务：", "第三方服务", 22);
            AppendLines(h, s, "非微软任务：", "非微软计划任务", 22);
            AppendLines(h, s, "远程控制软件：", "远程控制软件", 12);
            AppendLines(h, s, "共享目录：", "共享目录", 12);
            AppendLines(h, s, "系统代理：", "系统代理", 6);
            AppendLines(h, s, "TCP 监听端口：", "网络端口", 6);
            AppendLines(h, s, "hosts 有效条目：", "hosts", 6);
            AppendLines(h, s, "右键菜单第三方项：", "右键菜单", 6);

            // 4. 资产
            h.Append("<h2 class=\"newpage\">4. 设备资产与已装软件</h2>");
            h.Append("<table><tbody>");
            h.Append(Row("设备型号", s.Inv.DeviceModel()));
            h.Append(Row("出厂年份（机龄）", s.Inv.AgeText()));
            h.Append(Row("CPU", s.Inv.Cpu));
            h.Append(Row("内存", s.Inv.RamGB.ToString("0.0") + "G（" + s.Inv.RamSticks + " 条）"));
            h.Append(Row("显卡", s.Inv.Gpu));
            h.Append(Row("主板", s.Inv.Board));
            h.Append(Row("显示器", s.Inv.MonitorCount >= 0 ? s.Inv.MonitorCount + " 台" : "未检测"));
            h.Append(Row("硬盘", string.Join("；", s.Inv.Disks.ToArray())));
            h.Append(Row("系统", s.Inv.OsCaption + " " + s.Inv.OsVersion + "（" + s.Inv.OsBuild + "）"));
            h.Append(Row("系统安装日期", s.Inv.InstallDate));
            h.Append(Row("BIOS 日期", s.Inv.BiosDate));
            h.Append(Row("已装软件数量", s.Inv.SwCount + " 个"));
            h.Append("</tbody></table>");
            List<string> sw = SoftwareLines(s);
            h.Append("<div style=\"margin-top:2mm;font-size:9pt\"><b>已装软件清单（").Append(sw.Count).Append(" 个）：</b></div>");
            h.Append("<div style=\"column-count:2;column-gap:6mm;font-size:8.2pt;line-height:1.45;margin-top:1mm\">");
            foreach (string one in sw) h.Append("<div>").Append(E(Fmt.Cut(one, 46))).Append("</div>");
            h.Append("</div>");

            h.Append("<div class=\"ft\">本报告由只读巡检生成：即使以管理员权限运行，也未删除文件、未修改注册表/服务/启动项、未安装软件、未联网上传。处理建议需人工确认后执行。</div>");
            h.Append("</body></html>");
            return h.ToString();
        }

        private static void AppendLines(StringBuilder h, Snapshot s, string prefix, string title, int max)
        {
            List<string> hit = new List<string>();
            foreach (Block b in s.Blocks)
            {
                foreach (string ln in b.Lines)
                {
                    if (ln.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        string t = ln.Substring(prefix.Length).Trim();
                        if (t.Length > 0 && !hit.Contains(t)) hit.Add(t);
                    }
                }
            }
            RenderList(h, title, hit, max);
        }

        /// 启动项：合并 Edge 那些同名重复登记，避免报告里刷屏
        private static void AppendStartup(StringBuilder h, Snapshot s)
        {
            List<string> hit = new List<string>();
            int edge = 0;
            foreach (Block b in s.Blocks)
            {
                if (b.Item.Key != "Boot") continue;
                foreach (string ln in b.Lines)
                {
                    if (!ln.StartsWith("启动项：", StringComparison.Ordinal)) continue;
                    string rest = ln.Substring(4).Trim();
                    if (rest.StartsWith("MicrosoftEdgeAutoLaunch_", StringComparison.OrdinalIgnoreCase)) { edge++; continue; }
                    if (rest.Length > 0 && !hit.Contains(rest)) hit.Add(rest);
                }
            }
            if (edge > 0) hit.Add("Microsoft Edge 开机自启（重复登记 " + edge + " 条，计 1 项）");
            RenderList(h, "开机自启", hit, 22);
        }

        private static void RenderList(StringBuilder h, string title, List<string> hit, int max)
        {
            if (hit.Count == 0) return;
            h.Append("<div style=\"margin-top:1.5mm;font-size:8.6pt\"><b>").Append(E(title)).Append("（").Append(hit.Count).Append("）：</b></div>");
            h.Append("<div style=\"column-count:2;column-gap:5mm;font-size:8.2pt;line-height:1.4\">");
            for (int i = 0; i < hit.Count && i < max; i++)
                h.Append("<div>· ").Append(E(Fmt.Cut(hit[i], 58))).Append("</div>");
            if (hit.Count > max) h.Append("<div>· …… 还有 ").Append(hit.Count - max).Append(" 项（见原始数据）</div>");
            h.Append("</div>");
        }

        private static List<string> SoftwareLines(Snapshot s)
        {
            List<string> sw = new List<string>();
            foreach (Block b in s.Blocks)
            {
                if (b.Item.Key != "Inventory") continue;
                foreach (string ln in b.Lines)
                {
                    if (ln.StartsWith("  软件：", StringComparison.Ordinal))
                        sw.Add(ln.Substring(6).Trim());
                }
            }
            if (sw.Count == 0) sw.AddRange(s.Inv.SwList);
            return sw;
        }

        private static string Row(string k, string v)
        {
            return "<tr><th style=\"width:34mm\">" + E(k) + "</th><td>" + E(v) + "</td></tr>";
        }

        // ------------------------------------------------------------
        // 原始数据
        // ------------------------------------------------------------
        public static string BuildTxt(Snapshot s, bool admin)
        {
            StringBuilder t = new StringBuilder();
            TimeSpan dur = s.EndTime - s.StartTime;
            t.AppendLine("================================================================");
            t.AppendLine("66专业检测 · 原始数据（只读深检，不修改系统）");
            t.AppendLine("================================================================");
            t.AppendLine("门店名称：" + s.StoreName);
            t.AppendLine("检测人：" + s.Inspector);
            t.AppendLine("设备名：" + s.DeviceName);
            t.AppendLine("设备型号：" + s.Inv.DeviceModel());
            t.AppendLine("权限：" + (admin ? "管理员" : "受限（未提权）"));
            t.AppendLine("开始：" + s.StartTime.ToString("yyyy-MM-dd HH:mm:ss"));
            t.AppendLine("结束：" + s.EndTime.ToString("yyyy-MM-dd HH:mm:ss"));
            t.AppendLine("耗时：" + ((int)dur.TotalMinutes) + " 分 " + dur.Seconds.ToString("00") + " 秒");
            t.AppendLine("单据编号：" + s.SheetNo);
            t.AppendLine("检测项数：" + s.Blocks.Count);
            t.AppendLine("");

            for (int i = 0; i < s.Blocks.Count; i++)
            {
                Block b = s.Blocks[i];
                t.AppendLine("----------------------------------------------------------------");
                t.AppendLine("[" + (i + 1) + "] " + b.Item.Name);
                t.AppendLine("实测：");
                foreach (string ln in b.Item.Measured.Replace("\r\n", "\n").Split('\n'))
                    t.AppendLine("  " + ln);
                t.AppendLine("判定：" + Vs.Sym(b.Item.V) + " " + Vs.Txt(b.Item.V));
                t.AppendLine("意味着什么：" + b.Item.Meaning);
                foreach (string ln in b.Lines) t.AppendLine("  " + ln);
                t.AppendLine("");
            }

            t.AppendLine("----------------------------------------------------------------");
            t.AppendLine("整机资产");
            t.AppendLine("设备型号：" + s.Inv.DeviceModel());
            t.AppendLine("出厂年份（机龄）：" + s.Inv.AgeText());
            t.AppendLine("CPU：" + s.Inv.Cpu);
            t.AppendLine("内存：" + s.Inv.RamGB.ToString("0.0") + "G（" + s.Inv.RamSticks + " 条）");
            t.AppendLine("显卡：" + s.Inv.Gpu);
            t.AppendLine("主板：" + s.Inv.Board);
            t.AppendLine("显示器：" + (s.Inv.MonitorCount >= 0 ? s.Inv.MonitorCount + " 台" : "未检测"));
            t.AppendLine("硬盘：" + string.Join("；", s.Inv.Disks.ToArray()));
            t.AppendLine("系统：" + s.Inv.OsCaption + " " + s.Inv.OsVersion + "（" + s.Inv.OsBuild + "）");
            t.AppendLine("系统安装日期：" + s.Inv.InstallDate);
            t.AppendLine("BIOS 日期：" + s.Inv.BiosDate);
            t.AppendLine("已装软件数量：" + s.Inv.SwCount);
            foreach (string x in SoftwareLines(s)) t.AppendLine("  " + x);
            t.AppendLine("");
            t.AppendLine("[KEYS]");
            foreach (Block b in s.Blocks)
                foreach (KeyValuePair<string, string> kv in b.Keys)
                    if (!string.IsNullOrEmpty(kv.Value)) t.AppendLine(kv.Key + "=" + kv.Value);
            t.AppendLine("[/KEYS]");
            return t.ToString();
        }

        // ------------------------------------------------------------
        // 处理清单（人读，工具不执行）
        // ------------------------------------------------------------
        public static string BuildPlan(Snapshot s, bool admin)
        {
            StringBuilder t = new StringBuilder();
            t.AppendLine("================================================================");
            t.AppendLine("66专业检测 · 处理清单（建议，不是自动执行）");
            t.AppendLine("================================================================");
            t.AppendLine("门店：" + s.StoreName + "    检测人：" + s.Inspector + "    设备：" + s.DeviceName);
            t.AppendLine("时间：" + s.StartTime.ToString("yyyy-MM-dd HH:mm"));
            t.AppendLine("权限：" + (admin ? "管理员" : "受限（未提权，带 ※ 的项需管理员身份复检）"));
            t.AppendLine("");
            t.AppendLine("说明：本工具只读。下面每一条都要你自己确认后再动手；");
            t.AppendLine("      改注册表/删文件/换盘前，先备份并留一份操作记录。");
            t.AppendLine("");

            int idx = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                Verdict want = pass == 0 ? Verdict.Bad : Verdict.Warn;
                foreach (Block b in s.Blocks)
                {
                    if (b.Item.V != want) continue;
                    string[] ad = Advice(b.Item.Key, b.Item.V);
                    idx++;
                    t.AppendLine("----------------------------------------------------------------");
                    t.AppendLine("[" + idx + "] " + (want == Verdict.Bad ? "P1 严重" : "P2 中等") + "  " + b.Item.Name);
                    string now = b.Item.ReportValue.Length > 0 ? b.Item.ReportValue : b.Item.Measured;
                    t.AppendLine("  现状：" + now.Replace("\r\n", " | ").Replace("\n", " | "));
                    t.AppendLine("  现象：" + ad[0]);
                    t.AppendLine("  建议：" + ad[1]);
                    t.AppendLine("  参考：" + ad[2]);
                    t.AppendLine("  风险：" + (want == Verdict.Bad ? "不处理会继续恶化，可能影响营业" : "暂时不影响营业，建议 1-2 周内处理"));
                    t.AppendLine("");
                }
            }

            int unk = 0;
            StringBuilder unkList = new StringBuilder();
            foreach (Block b in s.Blocks)
            {
                if (b.Item.V != Verdict.Unknown) continue;
                unk++;
                unkList.AppendLine("  ※ " + b.Item.Name + "：" + b.Item.Meaning);
            }
            if (unk > 0)
            {
                t.AppendLine("----------------------------------------------------------------");
                t.AppendLine("未检测项（需管理员身份复检）");
                t.Append(unkList.ToString());
                t.AppendLine("");
            }

            if (idx == 0 && unk == 0)
                t.AppendLine("没有需要处理的项目。");

            t.AppendLine("================================================================");
            t.AppendLine("交付提醒：改完任何一项，用本工具再跑一遍对比「上次」，确认数值真的变了。");
            return t.ToString();
        }

        public static void WriteAll(Snapshot s, bool admin, out string html, out string txt, out string plan)
        {
            string dev = SafeName(s.DeviceName);
            string stamp = s.EndTime.ToString("yyyyMMdd_HHmm");
            html = ""; txt = ""; plan = "";
            for (int n = 1; n < 100; n++)
            {
                string sfx = n == 1 ? "" : "_" + n;
                string h = Path.Combine(_dir, "专业报告_" + dev + "_" + stamp + sfx + ".html");
                string x = Path.Combine(_dir, "原始数据_" + dev + "_" + stamp + sfx + ".txt");
                string p = Path.Combine(_dir, "处理清单_" + dev + "_" + stamp + sfx + ".txt");
                if (File.Exists(h)) continue;
                html = h; txt = x; plan = p;
                break;
            }
            File.WriteAllText(html, BuildHtml(s, admin), new UTF8Encoding(false));
            File.WriteAllText(txt, BuildTxt(s, admin), new UTF8Encoding(false));
            File.WriteAllText(plan, BuildPlan(s, admin), new UTF8Encoding(false));
        }

        public static void Open(string path)
        {
            try
            {
                if (Environment.GetEnvironmentVariable("SC_NOOPEN") == "1") return;
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = path;
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
            catch (Exception) { }
        }
    }
}
