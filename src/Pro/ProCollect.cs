using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.IO;
using System.Management;
using System.Net.NetworkInformation;
using System.Text;
using Microsoft.Win32;

namespace StoreCheck
{
    internal class SvcInfo
    {
        public string Name = "";
        public string Display = "";
        public string State = "";
        public string StartMode = "";
        public string PathName = "";
        public bool ThirdParty = false;
    }

    /// 专业版专属的深度只读采集（都只读，不改系统）
    public static class ProCollect
    {
        // ==========================================================
        // 01 系统与授权
        // ==========================================================
        public static Block SystemLicense()
        {
            Block b = new Block();
            CheckItem it = b.Item;
            it.Key = "SysLicense";
            it.Name = "系统与授权";

            string caption = "", ver = "", build = "", arch = "", install = "";
            try
            {
                foreach (ManagementObject o in Wmi.Q(@"root\cimv2", "SELECT Caption,Version,BuildNumber,OSArchitecture,InstallDate FROM Win32_OperatingSystem"))
                {
                    caption = Fmt.Clean(Wmi.S(o, "Caption"));
                    ver = Wmi.S(o, "Version");
                    build = Wmi.S(o, "BuildNumber");
                    arch = Wmi.S(o, "OSArchitecture");
                    DateTime d = Wmi.D(o, "InstallDate");
                    install = d == DateTime.MinValue ? "" : d.ToString("yyyy-MM-dd");
                    break;
                }
            }
            catch (Exception) { }

            string licStatus = "";
            try
            {
                foreach (ManagementObject o in Wmi.Q(@"root\cimv2",
                    "SELECT Name,LicenseStatus,PartialProductKey FROM SoftwareLicensingProduct WHERE PartialProductKey IS NOT NULL AND Name LIKE 'Windows%'"))
                {
                    int st = -1;
                    try { st = Convert.ToInt32(o["LicenseStatus"], CultureInfo.InvariantCulture); } catch (Exception) { }
                    licStatus = st == 1 ? "已激活" : (st == 0 ? "未激活" : "非正常状态(" + st + ")");
                    break;
                }
            }
            catch (Exception) { }
            if (licStatus.Length == 0) licStatus = "未检测";

            int patchCount = 0;
            string lastPatch = "";
            try
            {
                foreach (ManagementObject o in Wmi.Q(@"root\cimv2", "SELECT HotFixID,InstalledOn FROM Win32_QuickFixEngineering"))
                {
                    patchCount++;
                    string s = Wmi.S(o, "InstalledOn");
                    if (s.Length >= 8)
                    {
                        DateTime d;
                        if (DateTime.TryParse(s, out d))
                        {
                            if (lastPatch.Length == 0 || string.Compare(d.ToString("yyyy-MM-dd"), lastPatch, StringComparison.Ordinal) > 0)
                                lastPatch = d.ToString("yyyy-MM-dd");
                        }
                    }
                }
            }
            catch (Exception) { }

            string uac = Reg.Get(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "EnableLUA");
            bool uacOn = uac != "0";

            string guest = "";
            try
            {
                foreach (ManagementObject o in Wmi.Q(@"root\cimv2", "SELECT Name,Disabled FROM Win32_UserAccount WHERE LocalAccount=True AND Name='Guest'"))
                {
                    bool dis = true;
                    try { dis = Convert.ToBoolean(o["Disabled"], CultureInfo.InvariantCulture); } catch (Exception) { }
                    guest = dis ? "已禁用" : "已启用";
                    break;
                }
            }
            catch (Exception) { }
            if (guest.Length == 0) guest = "未检测";

            int admins = 0;
            try
            {
                foreach (ManagementObject o in Wmi.Q(@"root\cimv2", "SELECT PartComponent FROM Win32_GroupUser"))
                {
                    string g = Wmi.S(o, "GroupComponent");
                    if (g.Length == 0) g = "";
                    if (g.IndexOf("S-1-5-32-544", StringComparison.OrdinalIgnoreCase) >= 0) admins++;
                }
            }
            catch (Exception) { }

            b.Lines.Add("系统：" + caption + " " + ver + "（内部版本 " + build + "，" + arch + "）");
            b.Lines.Add("安装日期：" + (install.Length > 0 ? install : "未检测"));
            b.Lines.Add("激活状态：" + licStatus);
            b.Lines.Add("已装补丁数：" + patchCount + "，最近补丁日期：" + (lastPatch.Length > 0 ? lastPatch : "未检测"));
            b.Lines.Add("UAC：" + (uacOn ? "开启" : "已关闭（EnableLUA=0）"));
            b.Lines.Add("Guest 账户：" + guest);
            b.Lines.Add("管理员组成员数：" + admins);

            long daysOld = -1;
            if (install.Length == 10)
            {
                DateTime d2;
                if (DateTime.TryParse(install, out d2)) daysOld = (long)(DateTime.Now - d2).TotalDays;
            }
            long patchDays = -1;
            if (lastPatch.Length == 10)
            {
                DateTime d3;
                if (DateTime.TryParse(lastPatch, out d3)) patchDays = (long)(DateTime.Now - d3).TotalDays;
            }

            it.Measured = caption + "（" + build + "）｜" + licStatus
                + "｜补丁 " + patchCount + " 个" + (lastPatch.Length > 0 ? "，最近 " + lastPatch : "")
                + "｜UAC " + (uacOn ? "开启" : "已关闭") + "｜Guest " + guest;
            it.ReportValue = Fmt.Cut(caption, 22) + " " + build + " / " + licStatus
                + Environment.NewLine + "补丁 " + patchCount + " 个" + (patchDays >= 0 ? "，最近 " + patchDays + " 天前" : "")
                + " / UAC " + (uacOn ? "开" : "关") + " / Guest " + guest;

            Verdict v = Verdict.Normal;
            if (licStatus == "未激活") v = Verdict.Bad;
            else if (!uacOn || guest == "已启用") v = Verdict.Warn;
            else if (patchDays > 180) v = Verdict.Warn;
            else if (patchCount == 0) v = Verdict.Warn;
            it.V = v;
            it.ConsoleLine = licStatus + "，补丁 " + patchCount + " 个";

            if (v == Verdict.Bad) it.Meaning = "系统未激活，可能被限制部分功能或频繁提示，需处理";
            else if (v == Verdict.Warn) it.Meaning = "系统可用，但补丁或安全开关有缺口，建议补齐";
            else it.Meaning = "系统与授权正常，补丁和安全开关都在位";

            b.Keys["补丁数"] = patchCount.ToString();
            b.Keys["开机秒数"] = "";
            return b;
        }

        // ==========================================================
        // 06 事件日志体检
        // ==========================================================
        public static Block EventLogs()
        {
            Block b = new Block();
            CheckItem it = b.Item;
            it.Key = "EventLog";
            it.Name = "事件日志体检";

            DateTime since = DateTime.Now.AddDays(-7);
            string sinceStr = since.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
            string xpath = "*[System[(Level=1 or Level=2) and TimeCreated[@SystemTime>='" + sinceStr + "']]]";

            int total = 0;
            bool truncated = false;
            Dictionary<string, int> byProvider = new Dictionary<string, int>();
            List<string> recent = new List<string>();
            string err = "";

            string[] channels = new string[] { "System", "Application" };
            foreach (string ch in channels)
            {
                int n = 0;
                try
                {
                    EventLogQuery q = new EventLogQuery(ch, PathType.LogName, xpath);
                    q.ReverseDirection = false;
                    using (EventLogReader r = new EventLogReader(q))
                    {
                        while (true)
                        {
                            EventRecord e = null;
                            try { e = r.ReadEvent(); }
                            catch (Exception) { break; }
                            if (e == null) break;
                            n++;
                            total++;
                            string prov = "?";
                            try { prov = e.ProviderName; } catch (Exception) { }
                            if (prov == null || prov.Length == 0) prov = "?";
                            int c;
                            if (byProvider.TryGetValue(prov, out c)) byProvider[prov] = c + 1;
                            else byProvider[prov] = 1;

                            if (recent.Count < 8)
                            {
                                string msg = "";
                                try { msg = e.FormatDescription(); } catch (Exception) { }
                                if (string.IsNullOrEmpty(msg)) msg = "(无描述)";
                                string t = "";
                                try
                                {
                                    DateTime tc = e.TimeCreated.GetValueOrDefault(DateTime.MinValue);
                                    t = tc == DateTime.MinValue ? "" : tc.ToString("MM-dd HH:mm");
                                }
                                catch (Exception) { }
                                recent.Add(t + " [" + ch + "] " + Fmt.Cut(prov, 22) + " #" + e.Id + "  " + Fmt.Cut(Fmt.Clean(msg), 60));
                            }
                            try { e.Dispose(); } catch (Exception) { }
                            if (n > 4000) { truncated = true; break; }
                        }
                    }
                }
                catch (Exception ex)
                {
                    err = ex.Message;
                }
                b.Lines.Add("日志 " + ch + "：近 7 天 严重/错误 共 " + n + " 条" + (truncated ? "（超过 4000 条后截断）" : ""));
            }
            if (err.Length > 0) b.Lines.Add("读取异常：" + err);

            List<KeyValuePair<string, int>> top = new List<KeyValuePair<string, int>>(byProvider);
            top.Sort(delegate (KeyValuePair<string, int> x, KeyValuePair<string, int> y) { return y.Value.CompareTo(x.Value); });
            b.Lines.Add("Top 来源：");
            string topText = "";
            for (int i = 0; i < top.Count && i < 5; i++)
            {
                b.Lines.Add("  " + top[i].Key + "：" + top[i].Value + " 条");
                if (topText.Length > 0) topText += "、";
                topText += Fmt.Cut(top[i].Key, 14) + " " + top[i].Value;
            }
            foreach (string r in recent) b.Lines.Add("  最近：" + r);

            it.Measured = "近 7 天 严重/错误 共 " + total + " 条" + Environment.NewLine
                + "Top 来源：" + (topText.Length > 0 ? topText : "无")
                + (err.Length > 0 ? Environment.NewLine + "（部分日志读不到：" + Fmt.Cut(err, 40) + "）" : "");
            it.ReportValue = total + " 条（近 7 天）" + Environment.NewLine + Fmt.Cut(topText, 40);
            it.ConsoleLine = "近 7 天错误 " + total + " 条";

            Verdict v;
            if (err.Length > 0 && total == 0) v = Verdict.Unknown;
            else if (total >= 200) v = Verdict.Bad;
            else if (total >= 50) v = Verdict.Warn;
            else v = Verdict.Normal;
            it.V = v;

            if (v == Verdict.Bad) it.Meaning = "系统在频繁报错，说明有硬件或驱动/软件故障，需按来源逐个查";
            else if (v == Verdict.Warn) it.Meaning = "有一些报错，暂不影响使用，建议按 Top 来源看一眼";
            else if (v == Verdict.Unknown) it.Meaning = "事件日志读不到（通常需要管理员权限），这项未检测";
            else it.Meaning = "近期没有成规模的报错，系统运行干净";

            b.Keys["事件错误数"] = total.ToString();
            return b;
        }

        // ==========================================================
        // 07 服务与驱动
        // ==========================================================
        public static Block ServicesDrivers()
        {
            Block b = new Block();
            CheckItem it = b.Item;
            it.Key = "Services";
            it.Name = "服务与驱动";

            List<SvcInfo> svcs = new List<SvcInfo>();
            try
            {
                foreach (ManagementObject o in Wmi.Q(@"root\cimv2", "SELECT Name,DisplayName,State,StartMode,PathName FROM Win32_Service"))
                {
                    SvcInfo s = new SvcInfo();
                    s.Name = Wmi.S(o, "Name");
                    s.Display = Wmi.S(o, "DisplayName");
                    s.State = Wmi.S(o, "State");
                    s.StartMode = Wmi.S(o, "StartMode");
                    s.PathName = Wmi.S(o, "PathName");
                    string p = s.PathName.ToLower();
                    s.ThirdParty = p.Length > 0 && p.IndexOf("\\windows\\") < 0;
                    svcs.Add(s);
                }
            }
            catch (Exception) { }

            List<SvcInfo> autoDown = new List<SvcInfo>();
            List<SvcInfo> thirdParty = new List<SvcInfo>();
            foreach (SvcInfo s in svcs)
            {
                if (s.StartMode == "Auto" && s.State != "Running") autoDown.Add(s);
                if (s.ThirdParty) thirdParty.Add(s);
            }

            // 启动失败类事件
            int failEvents = 0;
            try
            {
                DateTime since = DateTime.Now.AddDays(-7);
                string ss = since.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
                string xp = "*[System[(EventID=7000 or EventID=7009 or EventID=7011 or EventID=7031 or EventID=7034) and TimeCreated[@SystemTime>='" + ss + "']]]";
                EventLogQuery q = new EventLogQuery("System", PathType.LogName, xp);
                q.ReverseDirection = true;
                using (EventLogReader r = new EventLogReader(q))
                {
                    while (true)
                    {
                        EventRecord e = null;
                        try { e = r.ReadEvent(); } catch (Exception) { break; }
                        if (e == null) break;
                        failEvents++;
                        try { e.Dispose(); } catch (Exception) { }
                        if (failEvents > 300) break;
                    }
                }
            }
            catch (Exception) { }

            List<string> badDevices = new List<string>();
            try
            {
                foreach (ManagementObject o in Wmi.Q(@"root\cimv2", "SELECT Name,DeviceID,ConfigManagerErrorCode,Status FROM Win32_PnPEntity"))
                {
                    int code = 0;
                    try { code = Convert.ToInt32(o["ConfigManagerErrorCode"], CultureInfo.InvariantCulture); } catch (Exception) { }
                    if (code == 0) continue;
                    string nm = Wmi.S(o, "Name");
                    if (nm.Length == 0) nm = "(未命名设备)";
                    badDevices.Add(nm + "（错误码 " + code + "）");
                    b.Lines.Add("问题设备：" + nm + "  错误码 " + code + "  DeviceID " + Wmi.S(o, "DeviceID"));
                    if (badDevices.Count >= 40) break;
                }
            }
            catch (Exception) { }

            b.Lines.Add("服务总数：" + svcs.Count + "，第三方服务：" + thirdParty.Count);
            b.Lines.Add("自动启动但未运行：" + autoDown.Count);
            foreach (SvcInfo s in autoDown)
            {
                if (b.Lines.Count > 400) break;
                b.Lines.Add("  未运行：" + s.Display + "  [" + s.Name + "]  路径 " + Fmt.Clean(s.PathName));
            }
            b.Lines.Add("近 7 天服务启动失败类事件：" + failEvents + " 条");
            foreach (SvcInfo s in thirdParty)
            {
                if (b.Lines.Count > 500) break;
                b.Lines.Add("第三方服务：" + s.Display + "  [" + s.State + "]  " + Fmt.Clean(s.PathName));
            }

            string firstDown = autoDown.Count > 0 ? autoDown[0].Display : "";
            it.Measured = "服务 " + svcs.Count + " 个（第三方 " + thirdParty.Count + "）；自动但未运行 " + autoDown.Count + " 个；启动失败事件 " + failEvents + " 条"
                + Environment.NewLine + "问题设备 " + badDevices.Count + " 个"
                + (badDevices.Count > 0 ? "：" + string.Join("；", Fmt.Cut(string.Join("、", badDevices.ToArray()), 46)) : "");
            it.ReportValue = "未运行服务 " + autoDown.Count + " 个 / 问题设备 " + badDevices.Count + " 个" + Environment.NewLine
                + "第三方服务 " + thirdParty.Count + " 个，启动失败 " + failEvents + " 次";
            it.ConsoleLine = "问题设备 " + badDevices.Count + "，未运行服务 " + autoDown.Count;

            Verdict v = Verdict.Normal;
            if (badDevices.Count > 0 || failEvents >= 10) v = Verdict.Bad;
            else if (autoDown.Count >= 2 || failEvents > 0) v = Verdict.Warn;
            it.V = v;

            if (v == Verdict.Bad) it.Meaning = "有设备带感叹号或服务反复启动失败，可能导致外设不工作、开机报错";
            else if (v == Verdict.Warn) it.Meaning = "有服务没起来或有零星失败，个别功能可能时好时坏";
            else it.Meaning = "服务和设备都正常，没有掉线和反复失败";

            b.Keys["未运行服务数"] = autoDown.Count.ToString();
            b.Keys["问题设备数"] = badDevices.Count.ToString();
            return b;
        }

        // ==========================================================
        // 08 计划任务
        // ==========================================================
        public static Block Tasks()
        {
            Block b = new Block();
            CheckItem it = b.Item;
            it.Key = "Tasks";
            it.Name = "计划任务";

            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "Tasks");
            int total = 0;
            List<string> list = new List<string>();
            try
            {
                if (Directory.Exists(root))
                {
                    Stack<string> stack = new Stack<string>();
                    foreach (string d in Directory.GetDirectories(root)) stack.Push(d);
                    while (stack.Count > 0)
                    {
                        string dir = stack.Pop();
                        string rel = dir.Substring(root.Length).TrimStart('\\');
                        foreach (string sub in Directory.GetDirectories(dir)) stack.Push(sub);
                        foreach (string f in Directory.GetFiles(dir))
                        {
                            if (rel.Equals("Microsoft", StringComparison.OrdinalIgnoreCase)) continue;
                            if (rel.StartsWith("Microsoft\\", StringComparison.OrdinalIgnoreCase)) continue;
                            total++;
                            string name = rel + "\\" + Path.GetFileName(f);
                            if (list.Count < 12) list.Add(name);
                        }
                    }
                }
            }
            catch (Exception) { }

            foreach (string s in list) b.Lines.Add("非微软任务：" + s);
            it.Measured = "非微软计划任务 " + total + " 个" + (list.Count > 0 ? Environment.NewLine + "示例：" + string.Join("；", list.ToArray()) : "");
            it.ReportValue = total + " 个非微软任务" + (list.Count > 0 ? Environment.NewLine + Fmt.Cut(string.Join("、", list.ToArray()), 40) : "");
            it.ConsoleLine = "非微软任务 " + total + " 个";

            Verdict v = Verdict.Normal;
            if (total > 40) v = Verdict.Bad;
            else if (total > 20) v = Verdict.Warn;
            it.V = v;
            if (v == Verdict.Bad) it.Meaning = "计划任务太多，会偷偷更新/弹窗/占用后台，建议逐条核对来源";
            else if (v == Verdict.Warn) it.Meaning = "非微软计划任务偏多，多是更新器，建议核对不用的关掉";
            else it.Meaning = "计划任务不多，都是必要的常驻任务";

            b.Keys["计划任务数"] = total.ToString();
            return b;
        }

        // ==========================================================
        // 09 网络深检
        // ==========================================================
        public static Block ProNetwork()
        {
            Block b = Collect.Network();
            b.Item.Name = "网络深检";

            int listen = 0, established = 0;
            try
            {
                IPGlobalProperties ip = IPGlobalProperties.GetIPGlobalProperties();
                listen = ip.GetActiveTcpListeners().Length;
                foreach (TcpConnectionInformation c in ip.GetActiveTcpConnections())
                    if (c.State == TcpState.Established) established++;
            }
            catch (Exception) { }
            b.Lines.Add("TCP 监听端口：" + listen + " 个，已建立连接：" + established + " 个");

            string proxy = "";
            try
            {
                string en = Reg.Get(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Internet Settings", "ProxyEnable");
                string sv = Reg.Get(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Internet Settings", "ProxyServer");
                if (en == "1" && sv.Length > 0) proxy = sv;
            }
            catch (Exception) { }
            b.Lines.Add("系统代理：" + (proxy.Length > 0 ? proxy + "（已开启）" : "未开启或未设置"));

            List<string> shares = new List<string>();
            try
            {
                foreach (ManagementObject o in Wmi.Q(@"root\cimv2", "SELECT Name,Path FROM Win32_Share"))
                {
                    string n = Wmi.S(o, "Name");
                    string p = Wmi.S(o, "Path");
                    shares.Add(n + (p.Length > 0 ? "→" + p : ""));
                }
            }
            catch (Exception) { }
            b.Lines.Add("共享目录：" + (shares.Count > 0 ? string.Join("；", shares.ToArray()) : "无"));

            int hostLines = 0;
            List<string> hostSample = new List<string>();
            try
            {
                string hp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"drivers\etc\hosts");
                if (File.Exists(hp))
                {
                    foreach (string ln in File.ReadAllLines(hp))
                    {
                        string t = ln.Trim();
                        if (t.Length == 0 || t.StartsWith("#")) continue;
                        hostLines++;
                        if (hostSample.Count < 5) hostSample.Add(t);
                    }
                }
            }
            catch (Exception) { }
            b.Lines.Add("hosts 有效条目：" + hostLines + (hostSample.Count > 0 ? "（" + string.Join("；", hostSample.ToArray()) + "）" : ""));

            b.Item.Measured = b.Item.Measured + Environment.NewLine
                + "监听端口 " + listen + " 个 / 已建立 " + established + " 个；共享 " + shares.Count + " 个；代理 "
                + (proxy.Length > 0 ? "已开启" : "未开启") + "；hosts " + hostLines + " 条";
            b.Item.ReportValue = b.Item.ReportValue + Environment.NewLine
                + "端口 " + listen + " / 共享 " + shares.Count + " / hosts " + hostLines;

            if (hostLines > 20 && b.Item.V == Verdict.Normal) b.Item.V = Verdict.Warn;
            b.Keys["监听端口数"] = listen.ToString();
            return b;
        }

        // ==========================================================
        // 10 安全面
        // ==========================================================
        public static Block Security()
        {
            Block b = new Block();
            CheckItem it = b.Item;
            it.Key = "Security";
            it.Name = "安全面";

            string fw = "未检测";
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "netsh.exe";
                psi.Arguments = "advfirewall show allprofiles state";
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                using (Process p = new Process())
                {
                    p.StartInfo = psi;
                    p.Start();
                    string outp = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(8000);
                    string low = outp.ToLower();
                    int on = 0, off = 0;
                    foreach (string ln in outp.Replace("\r", "").Split('\n'))
                    {
                        string t = ln.Trim().ToLower();
                        if (t.IndexOf("状态") == 0 || t.IndexOf("state") == 0)
                        {
                            if (t.IndexOf("on") >= 0 || t.IndexOf("打开") >= 0 || t.IndexOf("启用") >= 0) on++;
                            else if (t.IndexOf("off") >= 0 || t.IndexOf("关闭") >= 0) off++;
                        }
                    }
                    if (on + off > 0) fw = (off == 0 ? "全部开启" : (on == 0 ? "全部关闭" : "部分关闭（开 " + on + " / 关 " + off + "）"));
                    else if (low.IndexOf("requires elevation") >= 0 || low.IndexOf("需要提升") >= 0) fw = "未检测（需管理员）";
                }
            }
            catch (Exception) { }
            b.Lines.Add("防火墙：" + fw);

            List<string> av = new List<string>();
            try
            {
                foreach (ManagementObject o in Wmi.Q(@"root\SecurityCenter2", "SELECT displayName,productState FROM AntiVirusProduct"))
                {
                    string n = Wmi.S(o, "displayName");
                    string st = Wmi.S(o, "productState");
                    av.Add(n + (st.Length > 0 ? "（state=" + st + "）" : ""));
                }
            }
            catch (Exception) { }
            b.Lines.Add("杀毒软件：" + (av.Count > 0 ? string.Join("；", av.ToArray()) : "未检测到（或系统未提供 SecurityCenter2）"));

            string rdp = Reg.Get(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Terminal Server", "fDenyTSConnections");
            bool rdpOpen = rdp == "0";
            b.Lines.Add("远程桌面：" + (rdp.Length == 0 ? "未检测" : (rdpOpen ? "已允许（3389）" : "已禁止")));

            string smb1 = Reg.Get(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters", "SMB1");
            b.Lines.Add("SMB1：" + (smb1 == "1" ? "已启用（建议关闭）" : "未启用"));

            List<string> remote = new List<string>();
            try
            {
                List<SwInfo> sw = Collect.ReadSoftware();
                string[] kw = new string[] { "SunloginClient", "向日葵", "ToDesk", "AnyDesk", "TeamViewer", "RustDesk", "网络人", "Radmin", "AweSun" };
                foreach (SwInfo s in sw)
                {
                    foreach (string k in kw)
                    {
                        if (s.Name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            if (!remote.Contains(s.Name)) remote.Add(s.Name);
                            break;
                        }
                    }
                }
            }
            catch (Exception) { }
            b.Lines.Add("远程控制软件：" + (remote.Count > 0 ? string.Join("、", remote.ToArray()) : "未安装"));

            it.Measured = "防火墙：" + fw + Environment.NewLine
                + "杀毒：" + (av.Count > 0 ? string.Join("、", av.ToArray()) : "未检测") + Environment.NewLine
                + "远程桌面：" + (rdpOpen ? "已允许" : "已禁止") + "；远控软件：" + (remote.Count > 0 ? string.Join("、", remote.ToArray()) : "无");
            it.ReportValue = "防火墙 " + fw + Environment.NewLine
                + "远控 " + (remote.Count > 0 ? remote.Count + " 个" : "无") + " / 远程桌面 " + (rdpOpen ? "开" : "关");
            it.ConsoleLine = "防火墙 " + fw + "，远控 " + remote.Count + " 个";

            Verdict v = Verdict.Normal;
            if (fw.IndexOf("全部关闭") >= 0 && av.Count == 0) v = Verdict.Bad;
            else if (fw.IndexOf("关闭") >= 0 || rdpOpen || remote.Count >= 2) v = Verdict.Warn;
            if (fw == "未检测") v = v == Verdict.Bad ? Verdict.Bad : Verdict.Warn;
            it.V = v;

            if (v == Verdict.Bad) it.Meaning = "防火墙和杀毒都没开，收银机最容易中招，必须处理";
            else if (v == Verdict.Warn) it.Meaning = "有安全开关没到位（防火墙/远程桌面/远控软件），建议按需收紧";
            else it.Meaning = "防火墙、杀毒、远程入口都在合理状态";

            b.Keys["远控软件数"] = remote.Count.ToString();
            return b;
        }
    }
}
