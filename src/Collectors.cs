using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.IO;
using System.Management;
using System.Net;
using System.Net.NetworkInformation;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.Win32;

namespace StoreCheck
{
    internal class DirEnt
    {
        public string Path = "";
        public long Size = 0;
        public bool Partial = false;
    }

    internal class SmartAttr
    {
        public int Id;
        public int Value;
        public int Worst;
        public long Raw;
    }

    internal class PhysDisk
    {
        public int Index = -1;
        public string Model = "";
        public string Pnp = "";
        public string MediaType = "";
        public ulong Size = 0;
        public string Serial = "";
        public string MsftMedia = "";
        public string MsftHealth = "";
        public bool HasSmart = false;
        public bool PredictRead = false;
        public bool PredictFailure = false;
        public bool IsOsDisk = false;
        public Dictionary<int, SmartAttr> Attr = new Dictionary<int, SmartAttr>();

        private static long Hours(long raw)
        {
            long v = raw & 0xFFFFFFFFL;
            if (v > 300000) v = (raw >> 16) & 0xFFFFFFL;
            return v;
        }

        public long PowerOnHours()
        {
            SmartAttr a;
            if (Attr.TryGetValue(0x09, out a)) return Hours(a.Raw);
            return -1;
        }

        public long Realloc()
        {
            SmartAttr a;
            if (Attr.TryGetValue(0x05, out a)) return a.Raw & 0xFFFFFFFFL;
            return -1;
        }

        public long Pending()
        {
            SmartAttr a;
            if (Attr.TryGetValue(0xC5, out a)) return a.Raw & 0xFFFFFFFFL;
            return -1;
        }

        public long Uncorrect()
        {
            SmartAttr a;
            if (Attr.TryGetValue(0xC6, out a)) return a.Raw & 0xFFFFFFFFL;
            return -1;
        }

        public long Crc()
        {
            SmartAttr a;
            if (Attr.TryGetValue(0xC7, out a)) return a.Raw & 0xFFFFFFFFL;
            return -1;
        }

        public long LifeLeft()
        {
            SmartAttr a;
            if (Attr.TryGetValue(0xE9, out a)) return a.Value;
            if (Attr.TryGetValue(0xCA, out a)) return a.Value;
            return -1;
        }

        public long BadCount()
        {
            long r = Realloc(), p = Pending(), u = Uncorrect();
            long s = 0;
            if (r > 0) s += r;
            if (p > 0) s += p;
            if (u > 0) s += u;
            return s;
        }
    }

    internal class StartupItem
    {
        public string Name = "";
        public string Command = "";
        public string Location = "";
        public bool Missing = false;
        public bool Updater = false;
    }

    internal class SwInfo
    {
        public string Name = "";
        public string Version = "";
        public string Date = "";
    }

    public static class Collect
    {
        private const double GB = 1073741824.0;
        private static double _dnsFirst = -1;   // 本次会话第一次实测到的 DNS 耗时

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

        // ==========================================================
        // 1. C 盘与磁盘占用
        // ==========================================================
        public static Block DiskSpace()
        {
            Block b = new Block();
            CheckItem it = b.Item;
            it.Key = "DiskSpace";
            it.Name = "C 盘空间够不够";

            try
            {
                string sys = Environment.GetEnvironmentVariable("SystemDrive");
                if (string.IsNullOrEmpty(sys)) sys = "C:";
                if (sys.Length == 1) sys = sys + ":";
                string root = sys + "\\";

                List<string> parts = new List<string>();
                double cFree = 0, cTotal = 0, cPct = 0;
                bool cFound = false;
                try
                {
                    foreach (DriveInfo d in DriveInfo.GetDrives())
                    {
                        try
                        {
                            if (d.DriveType != DriveType.Fixed) continue;
                            if (!d.IsReady) { parts.Add(d.Name + " 未就绪"); continue; }
                            double total = d.TotalSize;
                            double free = d.AvailableFreeSpace;
                            double pct = total > 0 ? free * 100.0 / total : 0;
                            parts.Add(d.Name + " 共 " + Fmt.G0(total) + "，剩余 " + Fmt.G(free) + "（" + Fmt.Pct(pct) + "）");
                            if (d.Name.StartsWith(sys, StringComparison.OrdinalIgnoreCase))
                            { cFree = free; cTotal = total; cPct = pct; cFound = true; }
                        }
                        catch (Exception) { }
                    }
                }
                catch (Exception) { }

                DateTime deadline = DateTime.UtcNow.AddSeconds(62);
                List<DirEnt> scan = ScanChildren(root, deadline, 68000);

                // 对最大的两个目录再展开一层，让"占地方"的大头看得见
                scan.Sort(delegate (DirEnt x, DirEnt y) { return y.Size.CompareTo(x.Size); });
                if (scan.Count > 0 && DateTime.UtcNow < deadline.AddSeconds(-8))
                {
                    DateTime dl2 = DateTime.UtcNow.AddSeconds(16);
                    List<DirEnt> merged = new List<DirEnt>();
                    int expand = Math.Min(2, scan.Count);
                    for (int i = 0; i < scan.Count; i++)
                    {
                        if (i < expand)
                        {
                            List<DirEnt> kids = ScanChildren(scan[i].Path, dl2, 17000);
                            if (kids.Count > 0)
                            {
                                long sum = 0;
                                foreach (DirEnt k in kids) sum += k.Size;
                                if (sum < scan[i].Size / 2) merged.Add(scan[i]);
                                else merged.AddRange(kids);
                                continue;
                            }
                        }
                        merged.Add(scan[i]);
                    }
                    scan = merged;
                }
                scan.Sort(delegate (DirEnt x, DirEnt y) { return y.Size.CompareTo(x.Size); });

                List<string> dirs = new List<string>();
                string top3 = "";
                for (int i = 0; i < scan.Count && i < 8; i++)
                {
                    string rel = scan[i].Path;
                    try { if (rel.StartsWith(root, StringComparison.OrdinalIgnoreCase)) rel = rel.Substring(root.Length); } catch (Exception) { }
                    string one = rel + "  " + Fmt.G0(scan[i].Size) + (scan[i].Partial ? "（未扫完）" : "");
                    dirs.Add(one);
                    if (i < 3)
                    {
                        if (top3.Length > 0) top3 += "、";
                        string leaf = rel;
                        int sp = leaf.LastIndexOf('\\');
                        if (sp >= 0 && sp + 1 < leaf.Length) leaf = leaf.Substring(sp + 1);
                        top3 += Fmt.Cut(leaf, 13) + " " + Fmt.G0(scan[i].Size);
                    }
                }
                b.Lines.Add("统计方式：C 盘一级目录按大小排序，最大的两个目录再展开到二级；只统计大小，不读文件名。");

                it.Measured = "系统盘 " + root + " 剩余 " + Fmt.G(cFree) + "（" + Fmt.Pct(cPct) + "），总 " + Fmt.G0(cTotal)
                    + Environment.NewLine + "占用最多的 8 个目录："
                    + (dirs.Count > 0 ? Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", dirs.ToArray()) : " 未能统计")
                    + Environment.NewLine + "分区：" + string.Join("；", parts.ToArray());
                it.ReportValue = "剩余 " + Fmt.G(cFree) + "（" + Fmt.Pct(cPct) + "）" + Environment.NewLine
                    + (top3.Length > 0 ? "最大：" + top3 : "目录统计未完成");
                it.ConsoleLine = root + " 剩余 " + Fmt.G(cFree) + "（" + Fmt.Pct(cPct) + "）";

                double freeG = cFree / GB;
                if (!cFound)
                {
                    it.V = Verdict.Unknown;
                    it.ReportValue = "未检测到系统盘";
                    it.ConsoleLine = "未检测到系统盘";
                    it.Meaning = "没读到系统盘容量，请确认硬盘是否正常";
                }
                else if (cPct < 5.0 || freeG < 10) it.V = Verdict.Bad;
                else if (cPct < 15.0 || freeG < 20) it.V = Verdict.Warn;
                else it.V = Verdict.Normal;

                if (it.V == Verdict.Bad) it.Meaning = "C 盘快满了，系统会变慢、Windows 更新会失败，收银软件可能存不进单据";
                else if (it.V == Verdict.Warn) it.Meaning = "空间偏紧，再掉下去就会卡、更新失败，建议近期清理一次";
                else if (it.V == Verdict.Normal) it.Meaning = "空间够用，不用清理，这一项不用花钱";

                b.Keys["C盘剩余GB"] = (cFree / GB).ToString("0.0");
                b.Keys["C盘剩余百分比"] = cPct.ToString("0.0");
            }
            catch (Exception ex)
            {
                it.V = Verdict.Unknown;
                it.Measured = "未检测（" + ex.Message + "）";
                it.ReportValue = "未检测";
                it.ConsoleLine = "未检测";
                it.Meaning = "没能读到磁盘容量，请检查硬盘";
            }
            return b;
        }

        private static List<DirEnt> ScanChildren(string parent, DateTime deadline, int waitMs)
        {
            List<DirEnt> res = new List<DirEnt>();
            string[] dirs;
            try { dirs = Directory.GetDirectories(parent); }
            catch (Exception) { return res; }
            List<Task> tasks = new List<Task>();
            foreach (string d in dirs)
            {
                DirEnt e = new DirEnt();
                e.Path = d;
                res.Add(e);
                string dd = d;
                DateTime dl = deadline;
                tasks.Add(Task.Factory.StartNew(delegate ()
                {
                    bool part = false;
                    e.Size = DirSize(dd, dl, out part);
                    e.Partial = part;
                }));
            }
            try { Task.WaitAll(tasks.ToArray(), waitMs); }
            catch (Exception) { }
            return res;
        }

        private static long DirSize(string root, DateTime deadline, out bool partial)
        {
            partial = false;
            long total = 0;
            Stack<string> stack = new Stack<string>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                if (DateTime.UtcNow > deadline) { partial = true; return total; }
                string dir = stack.Pop();
                try
                {
                    DirectoryInfo di = new DirectoryInfo(dir);
                    foreach (FileSystemInfo fsi in di.EnumerateFileSystemInfos("*", SearchOption.TopDirectoryOnly))
                    {
                        try
                        {
                            FileAttributes at;
                            try { at = fsi.Attributes; }
                            catch (Exception) { continue; }
                            if ((at & FileAttributes.ReparsePoint) != 0) continue;
                            if ((at & FileAttributes.Directory) != 0) stack.Push(fsi.FullName);
                            else
                            {
                                FileInfo fi = fsi as FileInfo;
                                if (fi != null) total += fi.Length;
                            }
                        }
                        catch (Exception) { }
                    }
                }
                catch (Exception) { }
            }
            return total;
        }

        // ==========================================================
        // 2. 硬盘健康（SMART）
        // ==========================================================
        public static Block DiskHealth()
        {
            Block b = new Block();
            CheckItem it = b.Item;
            it.Key = "DiskHealth";
            it.Name = "硬盘状况";

            List<PhysDisk> disks = new List<PhysDisk>();
            try
            {
                List<ManagementObject> wd = Wmi.Q(@"root\cimv2",
                    "SELECT DeviceID,Model,Size,PNPDeviceID,MediaType,SerialNumber FROM Win32_DiskDrive WHERE MediaType LIKE '%Fixed%'");
                if (wd.Count == 0)
                    wd = Wmi.Q(@"root\cimv2", "SELECT DeviceID,Model,Size,PNPDeviceID,MediaType,SerialNumber FROM Win32_DiskDrive");
                foreach (ManagementObject o in wd)
                {
                    PhysDisk d = new PhysDisk();
                    d.Index = ParseDiskIndex(Wmi.S(o, "DeviceID"));
                    d.Model = Wmi.S(o, "Model");
                    d.Pnp = Wmi.S(o, "PNPDeviceID");
                    d.Size = Wmi.N(o, "Size");
                    d.Serial = Wmi.S(o, "SerialNumber");
                    d.MediaType = Wmi.S(o, "MediaType");
                    disks.Add(d);
                }

                int osIndex = OsDiskIndex();
                if (osIndex >= 0)
                    foreach (PhysDisk d in disks) d.IsOsDisk = d.Index == osIndex;
                if (disks.Count > 0)
                {
                    bool any = false;
                    foreach (PhysDisk d in disks) if (d.IsOsDisk) any = true;
                    if (!any) disks[0].IsOsDisk = true;
                }

                // 介质类型/健康状态：Win8+ 的存储 WMI（读不到就留空，绝不猜）
                foreach (ManagementObject o in Wmi.Q(@"root\Microsoft\Windows\Storage",
                    "SELECT DeviceId,FriendlyName,MediaType,HealthStatus,Size,SerialNumber FROM MSFT_PhysicalDisk"))
                {
                    string id = Wmi.S(o, "DeviceId");
                    string friendly = Wmi.S(o, "FriendlyName");
                    string serial = Wmi.S(o, "SerialNumber");
                    ulong size = Wmi.N(o, "Size");
                    int mt = 0, hs = -1;
                    try { mt = Convert.ToInt32(o["MediaType"], CultureInfo.InvariantCulture); } catch (Exception) { }
                    try { hs = Convert.ToInt32(o["HealthStatus"], CultureInfo.InvariantCulture); } catch (Exception) { }
                    foreach (PhysDisk d in disks)
                    {
                        bool match = false;
                        if (id.Length > 0 && d.Index >= 0 && id.Trim() == d.Index.ToString()) match = true;
                        if (!match && serial.Length > 3 && d.Serial.Length > 3 && serial.Trim() == d.Serial.Trim()) match = true;
                        if (!match && friendly.Length > 3 && d.Model.Length > 3 && d.Model.IndexOf(friendly, StringComparison.OrdinalIgnoreCase) >= 0) match = true;
                        if (!match && size > 0 && d.Size > 0 && Math.Abs((double)size - (double)d.Size) < 200.0 * 1024 * 1024) match = true;
                        if (!match) continue;
                        d.MsftMedia = mt == 4 ? "SSD" : (mt == 3 ? "HDD" : "");
                        d.MsftHealth = hs == 0 ? "健康" : (hs == 1 ? "警告" : (hs == 2 ? "不健康" : ""));
                        break;
                    }
                }

                List<ManagementObject> smart = Wmi.Q(@"root\wmi", "SELECT InstanceName,Active,VendorSpecific FROM MSStorageDriver_ATAPISmartData");
                List<ManagementObject> pred = Wmi.Q(@"root\wmi", "SELECT InstanceName,Active,PredictFailure,Reason FROM MSStorageDriver_FailurePredictStatus");

                foreach (PhysDisk d in disks)
                {
                    foreach (ManagementObject s in smart)
                    {
                        if (!MatchSmart(Wmi.S(s, "InstanceName"), d)) continue;
                        byte[] vs = null;
                        try { vs = s["VendorSpecific"] as byte[]; } catch (Exception) { }
                        if (vs == null || vs.Length < 200) continue;
                        d.Attr = ParseSmart(vs);
                        d.HasSmart = true;
                        break;
                    }
                    if (!d.HasSmart && smart.Count == 1 && disks.Count == 1)
                    {
                        byte[] vs = null;
                        try { vs = smart[0]["VendorSpecific"] as byte[]; } catch (Exception) { }
                        if (vs != null && vs.Length >= 200) { d.Attr = ParseSmart(vs); d.HasSmart = true; }
                    }
                    foreach (ManagementObject p in pred)
                    {
                        if (!MatchSmart(Wmi.S(p, "InstanceName"), d)) continue;
                        d.PredictRead = true;
                        try { d.PredictFailure = Convert.ToBoolean(p["PredictFailure"], CultureInfo.InvariantCulture); } catch (Exception) { }
                        break;
                    }
                    if (!d.PredictRead && pred.Count == 1 && disks.Count == 1)
                    {
                        d.PredictRead = true;
                        try { d.PredictFailure = Convert.ToBoolean(pred[0]["PredictFailure"], CultureInfo.InvariantCulture); } catch (Exception) { }
                    }
                }
            }
            catch (Exception ex)
            {
                b.Lines.Add("读取异常：" + ex.Message);
            }

            if (disks.Count == 0)
            {
                it.V = Verdict.Unknown;
                it.Measured = "未检测到硬盘";
                it.ReportValue = "未检测到硬盘";
                it.ConsoleLine = "未检测到硬盘";
                it.Meaning = "没读到硬盘信息，请检查硬盘连接";
                return b;
            }

            PhysDisk os = null;
            foreach (PhysDisk d in disks) if (d.IsOsDisk) { os = d; break; }
            if (os == null) os = disks[0];

            foreach (PhysDisk d in disks)
            {
                string media = d.MsftMedia.Length > 0 ? d.MsftMedia : "未检测";
                b.Lines.Add("硬盘 " + (d.IsOsDisk ? "【系统盘】" : "") + d.Model + "  " + Fmt.G0(d.Size) + "  介质 " + media);
                b.Lines.Add("  系统健康状态：" + (d.MsftHealth.Length > 0 ? d.MsftHealth : "未检测"));
                b.Lines.Add("  SMART：" + (d.HasSmart ? "可读" : "不可读"));
                if (d.HasSmart)
                {
                    b.Lines.Add("  通电小时：" + (d.PowerOnHours() >= 0 ? d.PowerOnHours().ToString() : "不支持"));
                    b.Lines.Add("  重映射扇区(05)：" + (d.Realloc() >= 0 ? d.Realloc().ToString() : "不支持"));
                    b.Lines.Add("  待映射扇区(C5)：" + (d.Pending() >= 0 ? d.Pending().ToString() : "不支持"));
                    b.Lines.Add("  不可修复(C6)：" + (d.Uncorrect() >= 0 ? d.Uncorrect().ToString() : "不支持"));
                    b.Lines.Add("  接口CRC(C7)：" + (d.Crc() >= 0 ? d.Crc().ToString() : "不支持"));
                    b.Lines.Add("  剩余寿命：" + (d.LifeLeft() >= 0 ? d.LifeLeft() + "%" : "不支持"));
                }
                if (d.PredictRead) b.Lines.Add("  故障预警：" + (d.PredictFailure ? "有（建议马上备份换盘）" : "无"));
            }

            string mediaText = os.MsftMedia.Length > 0 ? os.MsftMedia : "介质未检测";
            string shortName = Fmt.Cut(os.Model, 12);
            string head = mediaText + " " + shortName + " " + Fmt.G0(os.Size);
            long hours = os.PowerOnHours();
            long bad = os.BadCount();
            long life = os.LifeLeft();
            bool noData = !os.HasSmart && !os.PredictRead && os.MsftHealth.Length == 0;

            if (noData)
            {
                it.V = Verdict.Unknown;
                it.Measured = head + Environment.NewLine + "健康数据：这块盘不上报（老硬盘或外接盘常见）";
                it.ReportValue = head + Environment.NewLine + "这块盘不上报健康数据";
                it.ConsoleLine = "读不到自检数据";
                it.Meaning = "这块盘读不到自检数据，建议当面再看一眼，或用硬盘厂家自己的检测工具复测一次，别急着下结论";
                b.Keys["硬盘通电小时"] = "";
                b.Keys["硬盘坏道计数"] = "";
                return b;
            }

            string health = os.PredictFailure ? "故障预警：有" : (os.PredictRead ? "故障预警：无" : (os.MsftHealth.Length > 0 ? "系统报告：" + os.MsftHealth : "健康状态：未检测"));
            string line2 = health;
            if (hours >= 0) line2 += "；通电 " + hours + " 小时";
            if (life >= 0) line2 += "；剩余寿命约 " + life + "%";
            if (os.HasSmart) line2 += "；坏道/错误计数 " + bad;

            string rv2 = "";
            if (hours >= 0) rv2 = "用了 " + hours + " 小时";
            if (bad > 0) rv2 += (rv2.Length > 0 ? "；" : "") + "有 " + bad + " 处损伤";
            if (life >= 0) rv2 += (rv2.Length > 0 ? "；" : "") + "剩余寿命 " + life + "%";
            if (rv2.Length == 0) rv2 = os.PredictFailure ? "已经出现故障预警" : "暂无预警";

            it.Measured = head + Environment.NewLine + line2;
            it.ReportValue = head + Environment.NewLine + rv2;
            it.ConsoleLine = hours >= 0
                ? ("通电 " + hours + " 小时，" + (bad > 0 ? "有坏道" : "无坏道"))
                : (os.PredictFailure ? "已出现故障预警" : "无预警");

            bool ssd = mediaText == "SSD";
            Verdict v;
            if (os.PredictFailure) v = Verdict.Bad;
            else if (os.MsftHealth == "不健康") v = Verdict.Bad;
            else if (os.Pending() > 0 || os.Uncorrect() > 0 || os.Realloc() >= 10) v = Verdict.Bad;
            else if (os.Realloc() > 0) v = Verdict.Warn;
            else if (ssd && life >= 0 && life < 10) v = Verdict.Bad;
            else if (ssd && life >= 0 && life < 20) v = Verdict.Warn;
            else if (!ssd && hours >= 50000) v = Verdict.Bad;
            else if (!ssd && hours >= 30000) v = Verdict.Warn;
            else if (hours >= 60000) v = Verdict.Bad;
            else if (hours >= 40000) v = Verdict.Warn;
            else if (os.MsftHealth == "警告") v = Verdict.Warn;
            else v = Verdict.Normal;
            it.V = v;

            if (v == Verdict.Bad)
            {
                it.Meaning = "硬盘随时可能坏，请先备份重要数据，再尽快换盘";
                it.TopPriority = true;
            }
            else if (v == Verdict.Warn) it.Meaning = "硬盘开始老化，先把重要数据备份出来，还能再用一阵";
            else it.Meaning = "短期内不会坏，这一项不用花钱";

            b.Keys["硬盘通电小时"] = hours >= 0 ? hours.ToString() : "";
            b.Keys["硬盘坏道计数"] = os.HasSmart ? bad.ToString() : "";
            b.Keys["硬盘剩余寿命百分比"] = life >= 0 ? life.ToString() : "";
            return b;
        }

        private static int ParseDiskIndex(string deviceId)
        {
            try
            {
                if (string.IsNullOrEmpty(deviceId)) return -1;
                int p = deviceId.ToLower().LastIndexOf("physicaldrive", StringComparison.Ordinal);
                if (p < 0) return -1;
                string rest = deviceId.Substring(p + 13);
                StringBuilder sb = new StringBuilder();
                foreach (char c in rest)
                {
                    if (char.IsDigit(c)) sb.Append(c);
                    else break;
                }
                if (sb.Length == 0) return -1;
                return int.Parse(sb.ToString(), CultureInfo.InvariantCulture);
            }
            catch (Exception) { return -1; }
        }

        private static bool MatchSmart(string instanceName, PhysDisk d)
        {
            string dn = instanceName == null ? "" : instanceName.Trim();
            if (dn.Length == 0 || d.Index < 0) return false;
            if (dn.EndsWith("_" + d.Index.ToString(), StringComparison.Ordinal)) return true;
            if (dn.ToLower().IndexOf("physicaldrive" + d.Index.ToString(), StringComparison.Ordinal) >= 0) return true;
            string pnp = d.Pnp == null ? "" : d.Pnp.ToLower().Replace('\\', '#');
            if (pnp.Length > 10)
            {
                int take = Math.Min(40, pnp.Length);
                if (dn.ToLower().IndexOf(pnp.Substring(0, take), StringComparison.Ordinal) >= 0) return true;
            }
            return false;
        }

        private static int OsDiskIndex()
        {
            try
            {
                string sys = Environment.GetEnvironmentVariable("SystemDrive");
                if (string.IsNullOrEmpty(sys)) sys = "C:";
                foreach (ManagementObject p in Wmi.Q(@"root\cimv2",
                    "ASSOCIATORS OF {Win32_LogicalDisk.DeviceID=\"" + sys + "\"} WHERE AssocClass=Win32_LogicalDiskToPartition"))
                {
                    string pid = Wmi.S(p, "DeviceID");
                    if (pid.Length == 0) continue;
                    foreach (ManagementObject d in Wmi.Q(@"root\cimv2",
                        "ASSOCIATORS OF {Win32_DiskPartition.DeviceID=\"" + pid + "\"} WHERE AssocClass=Win32_DiskDriveToDiskPartition"))
                    {
                        int idx = ParseDiskIndex(Wmi.S(d, "DeviceID"));
                        if (idx >= 0) return idx;
                    }
                }
            }
            catch (Exception) { }
            return -1;
        }

        private static Dictionary<int, SmartAttr> ParseSmart(byte[] vs)
        {
            Dictionary<int, SmartAttr> map = new Dictionary<int, SmartAttr>();
            try
            {
                for (int i = 0; i < 30; i++)
                {
                    int b = 2 + i * 12;
                    if (b + 12 > vs.Length) break;
                    int id = vs[b];
                    if (id == 0) continue;
                    SmartAttr a = new SmartAttr();
                    a.Id = id;
                    a.Value = vs[b + 3];
                    a.Worst = vs[b + 4];
                    long raw = 0;
                    for (int k = 0; k < 6; k++) raw |= ((long)vs[b + 5 + k]) << (8 * k);
                    a.Raw = raw;
                    map[id] = a;
                }
            }
            catch (Exception) { }
            return map;
        }

        // ==========================================================
        // 3. 开机速度与启动项
        // ==========================================================
        public static Block Boot()
        {
            Block b = new Block();
            CheckItem it = b.Item;
            it.Key = "Boot";
            it.Name = "开机快不快";

            List<long> boots = new List<long>();
            string bootErr = "";
            try
            {
                EventLogQuery q = new EventLogQuery("Microsoft-Windows-Diagnostics-Performance/Operational",
                    PathType.LogName, "*[System[EventID=100]]");
                q.ReverseDirection = true;
                using (EventLogReader r = new EventLogReader(q))
                {
                    for (int i = 0; i < 7; i++)
                    {
                        EventRecord rec = null;
                        try { rec = r.ReadEvent(); }
                        catch (Exception) { break; }
                        if (rec == null) break;
                        long ms = 0;
                        try { ms = BootMsFromXml(rec.ToXml()); }
                        catch (Exception) { }
                        try { rec.Dispose(); } catch (Exception) { }
                        if (ms > 0) boots.Add(ms);
                    }
                }
            }
            catch (Exception ex)
            {
                bootErr = ex.Message;
            }

            long median = 0;
            if (boots.Count > 0)
            {
                boots.Sort();
                median = boots[boots.Count / 2];
                if (boots.Count % 2 == 0 && boots.Count >= 2)
                    median = (boots[boots.Count / 2 - 1] + boots[boots.Count / 2]) / 2;
            }
            b.Lines.Add("开机耗时样本(ms)：" + string.Join(", ", ToStrArray(boots)));
            if (bootErr.Length > 0) b.Lines.Add("事件日志读取失败：" + bootErr);
            b.Lines.Add("开机耗时中位数：" + (median > 0 ? Fmt.Sec(median) : "未检测（需管理员）"));

            // 系统日志里的“实际开机耗时”（Kernel-Boot 100），作为旁证
            long realBoot = KernelBootMs();
            if (realBoot > 0) b.Lines.Add("系统日志记录的实际开机：" + Fmt.Sec(realBoot) + "（Kernel-Boot）");
            else b.Lines.Add("系统日志记录的实际开机：未读到（需管理员或该机器未记录）");

            long up = 0;
            try { up = Environment.TickCount; } catch (Exception) { }
            if (up > 0) b.Lines.Add("本次已连续运行：" + Fmt.Uptime(up) + "（未经重启）");

            List<StartupItem> items = new List<StartupItem>();
            try
            {
                foreach (ManagementObject o in Wmi.Q(@"root\cimv2", "SELECT Name,Command,Location FROM Win32_StartupCommand"))
                {
                    StartupItem si = new StartupItem();
                    si.Name = Wmi.S(o, "Name");
                    si.Command = Wmi.S(o, "Command");
                    si.Location = Wmi.S(o, "Location");
                    if (si.Name.Length == 0) si.Name = "(未命名)";
                    items.Add(si);
                }
            }
            catch (Exception) { }

            Dictionary<string, bool> seen = new Dictionary<string, bool>();
            foreach (StartupItem si in items) seen[(si.Name + "|" + si.Command).ToLower()] = true;

            string[] runKeys = new string[] {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run"
            };
            RegistryHive[] hives = new RegistryHive[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser };
            foreach (RegistryHive h in hives)
            {
                foreach (string rk in runKeys)
                {
                    foreach (string vn in Reg.Names(h, rk))
                    {
                        if (string.IsNullOrEmpty(vn)) continue;
                        string cmd = Reg.Get(h, rk, vn);
                        string k = (vn + "|" + cmd).ToLower();
                        if (seen.ContainsKey(k)) continue;
                        seen[k] = true;
                        StartupItem si = new StartupItem();
                        si.Name = vn;
                        si.Command = cmd;
                        si.Location = rk;
                        items.Add(si);
                    }
                }
            }

            foreach (StartupItem si in items)
                b.Lines.Add("启动项：" + si.Name + "  [" + si.Location + "]  " + Fmt.Clean(si.Command));

            // Edge 每次升级都会新增一条 MicrosoftEdgeAutoLaunch_xxx，属同一功能，合并计数
            List<StartupItem> eff = new List<StartupItem>();
            int dupEdge = 0;
            StartupItem edgeRep = null;
            foreach (StartupItem si in items)
            {
                if (si.Name.IndexOf("MicrosoftEdgeAutoLaunch_", StringComparison.OrdinalIgnoreCase) == 0)
                {
                    dupEdge++;
                    if (edgeRep == null) { edgeRep = si; eff.Add(si); }
                    continue;
                }
                eff.Add(si);
            }
            if (dupEdge > 1 && edgeRep != null) edgeRep.Name = "Microsoft Edge 开机自启（重复登记 " + dupEdge + " 条）";

            int safe = 0;
            List<string> safeNames = new List<string>();
            List<string> invalidNames = new List<string>();
            foreach (StartupItem si in eff)
            {
                string exe = ExtractExe(si.Command);
                if (exe.Length > 0 && !File.Exists(exe)) { si.Missing = true; invalidNames.Add(si.Name); }
                else if (IsUpdater(si.Name, exe)) si.Updater = true;
                if (si.Missing || si.Updater)
                {
                    safe++;
                    if (safeNames.Count < 6) safeNames.Add(si.Name);
                }
                b.Lines.Add("判定：" + si.Name + " → " + (si.Missing ? "指向的程序已不存在" : (si.Updater ? "升级/助手类，可关" : "保留")));
            }

            string bootText = median > 0 ? Fmt.Sec(median) + "（近 " + boots.Count + " 次中位数）"
                : (realBoot > 0 ? Fmt.Sec(realBoot) + "（系统日志记录的实际开机）" : "未检测（需管理员）");
            if (median > 0 && realBoot > 0)
                bootText += "；系统日志实际 " + Fmt.Sec(realBoot);
            it.Measured = "开机耗时：" + bootText + Environment.NewLine
                + "启动项：有效 " + eff.Count + " 项（注册表登记 " + items.Count + " 条"
                + (dupEdge > 1 ? "，Edge 重复 " + (dupEdge - 1) + " 条" : "") + "），可安全关闭 " + safe + " 项"
                + (safeNames.Count > 0 ? Environment.NewLine + "可关：" + string.Join("、", safeNames.ToArray()) : "")
                + Environment.NewLine + "已连续运行：" + Fmt.Uptime(up);
            it.ReportValue = "开机 " + (median > 0 ? Fmt.Sec(median) : (realBoot > 0 ? Fmt.Sec(realBoot) + "（系统日志）" : "这次没测到，下次用管理员身份补测")) + Environment.NewLine
                + "开机自动启动的程序 " + eff.Count + " 个，可关 " + safe + " 个";
            it.ConsoleLine = "开机 " + (median > 0 ? Fmt.Sec(median) : (realBoot > 0 ? Fmt.Sec(realBoot) : "未检测")) + "，自启程序 " + eff.Count + " 个";

            Verdict vBoot = Verdict.Unknown;
            if (median > 0)
            {
                double s = median / 1000.0;
                if (s <= 45) vBoot = Verdict.Normal;
                else if (s <= 90) vBoot = Verdict.Warn;
                else vBoot = Verdict.Bad;
            }
            Verdict vStart = Verdict.Normal;
            if (eff.Count > 25 || invalidNames.Count >= 5) vStart = Verdict.Bad;
            else if (eff.Count > 15 || invalidNames.Count >= 2) vStart = Verdict.Warn;

            Verdict v;
            if (vBoot != Verdict.Unknown)
                v = (vBoot == Verdict.Bad || vStart == Verdict.Bad) ? Verdict.Bad
                    : ((vBoot == Verdict.Warn || vStart == Verdict.Warn) ? Verdict.Warn : Verdict.Normal);
            else if (vStart == Verdict.Bad || vStart == Verdict.Warn)
                v = Verdict.Warn;
            else
                v = Verdict.Unknown;
            it.V = v;

            if (v == Verdict.Bad) it.Meaning = "开机太慢、开机自启太多，客人结账等不起，建议做一次开机优化";
            else if (v == Verdict.Warn)
            {
                if (median > 0) it.Meaning = "开机偏慢，清掉没用的启动项能快一些，不影响正常使用";
                else it.Meaning = "开机时间这次没测到（要管理员权限才能读）；开机自动启动的程序偏多，下次开权限再测一次";
            }
            else if (v == Verdict.Unknown) it.Meaning = "开机时间这次没测到（要管理员权限才能读）；开机自动启动的程序已登记，下次补测";
            else it.Meaning = "开机速度正常，不用优化，这一项不用花钱";

            b.Keys["开机秒数"] = median > 0 ? (median / 1000.0).ToString("0") : "";
            b.Keys["实际开机秒数"] = realBoot > 0 ? (realBoot / 1000.0).ToString("0") : "";
            b.Keys["启动项数"] = eff.Count.ToString();
            b.Keys["启动项可安全关闭数"] = safe.ToString();
            return b;
        }

        private static string[] ToStrArray(List<long> list)
        {
            string[] arr = new string[list.Count];
            for (int i = 0; i < list.Count; i++) arr[i] = list[i].ToString();
            return arr;
        }

        /// 读最近一次 Kernel-Boot 事件 100 的 BootTime（单位通常是 100 纳秒，换算成毫秒）
        private static long KernelBootMs()
        {
            try
            {
                EventLogQuery q = new EventLogQuery("Microsoft-Windows-Kernel-Boot/Operational",
                    PathType.LogName, "*[System[EventID=100]]");
                q.ReverseDirection = true;
                using (EventLogReader r = new EventLogReader(q))
                {
                    EventRecord rec = null;
                    try { rec = r.ReadEvent(); } catch (Exception) { }
                    if (rec == null) return 0;
                    string xml = "";
                    try { xml = rec.ToXml(); } finally { try { rec.Dispose(); } catch (Exception) { } }
                    if (xml.Length == 0) return 0;
                    XmlDocument doc = new XmlDocument();
                    doc.LoadXml(xml);
                    XmlNode n = doc.SelectSingleNode("//*[local-name()='Data'][@Name='BootTime']");
                    if (n == null) return 0;
                    long v = 0;
                    long.TryParse(n.InnerText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v);
                    if (v <= 0) return 0;
                    // 100ns 单位：真实开机一般 1e8~1e10；小于 1e6 的值按毫秒处理
                    return v > 1000000 ? v / 10000 : v;
                }
            }
            catch (Exception) { return 0; }
        }

        private static long BootMsFromXml(string xml)
        {
            try
            {
                XmlDocument doc = new XmlDocument();
                doc.LoadXml(xml);
                XmlNamespaceManager mgr = new XmlNamespaceManager(doc.NameTable);
                mgr.AddNamespace("e", "http://schemas.microsoft.com/win/2004/08/events/event");
                XmlNode n = doc.SelectSingleNode("//e:Data[@Name='BootTime']", mgr);
                if (n == null) n = doc.SelectSingleNode("//*[local-name()='Data'][@Name='BootTime']");
                if (n == null) return 0;
                long v = 0;
                long.TryParse(n.InnerText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v);
                return v;
            }
            catch (Exception) { return 0; }
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

        private static bool IsUpdater(string name, string exe)
        {
            string[] kw = new string[] {
                "SunJavaUpdateSched", "AdobeAAMUpdater", "AdobeGCInvoker", "AdobeUpdater",
                "Acrobat Assistant", "Creative Cloud", "CCXProcess", "iTunesHelper", "QuickTime"
            };
            foreach (string k in kw)
                if (name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (exe.Length > 0)
            {
                string e = exe.ToLower();
                if (e.IndexOf("jusched.exe") >= 0) return true;
                if (e.IndexOf("adobe updater") >= 0) return true;
            }
            return false;
        }

        // ==========================================================
        // 4. 系统文件完整性（需管理员，绝不提权）
        // ==========================================================
        public static Block Sfc()
        {
            Block b = new Block();
            CheckItem it = b.Item;
            it.Key = "Sfc";
            it.Name = "系统文件有没有坏";
            bool admin = IsAdmin();
            b.Lines.Add("当前权限：" + (admin ? "管理员" : "标准用户（非管理员）"));

            if (!admin)
            {
                it.V = Verdict.Unknown;
                it.Measured = "未检测（需管理员）" + Environment.NewLine + "本次未运行任何修复命令";
                it.ReportValue = "未检测（需管理员）";
                it.ConsoleLine = "未检测（需管理员）";
                it.Meaning = "这一项要管理员权限才能查，这次先跳过；下次用管理员身份跑一次就行";
                b.Keys["系统文件"] = "";
                return b;
            }

            string output = "";
            bool timeout = false;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = "sfc.exe";
                psi.Arguments = "/verifyonly";
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                StringBuilder sb = new StringBuilder();
                using (Process p = new Process())
                {
                    p.StartInfo = psi;
                    p.OutputDataReceived += delegate (object s, DataReceivedEventArgs e) { if (e.Data != null) sb.AppendLine(e.Data); };
                    p.ErrorDataReceived += delegate (object s, DataReceivedEventArgs e) { if (e.Data != null) sb.AppendLine(e.Data); };
                    p.Start();
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                    if (!p.WaitForExit(180000))
                    {
                        timeout = true;
                        try { p.Kill(); } catch (Exception) { }
                    }
                    else
                    {
                        try { p.WaitForExit(); } catch (Exception) { }
                    }
                }
                output = sb.ToString();
            }
            catch (Exception ex)
            {
                output = "运行失败：" + ex.Message;
            }

            b.Lines.Add("sfc /verifyonly 输出：");
            foreach (string ln in output.Replace("\r", "").Split('\n'))
            {
                string t = ln.Trim();
                if (t.Length > 0) b.Lines.Add("  " + t);
            }

            if (timeout)
            {
                it.V = Verdict.Unknown;
                it.Measured = "超时未完成（等待超过 3 分钟）";
                it.ReportValue = "超时未完成（>3 分钟）";
                it.ConsoleLine = "超时未完成";
                it.Meaning = "系统文件检查没跑完，不影响其他项结论，可下次再查";
                b.Keys["系统文件"] = "";
                return b;
            }

            string low = output.ToLower();
            if (output.IndexOf("未找到任何完整性冲突", StringComparison.Ordinal) >= 0
                || output.IndexOf("没有找到任何完整性冲突", StringComparison.Ordinal) >= 0
                || low.IndexOf("did not find any integrity violations") >= 0)
            {
                it.V = Verdict.Normal;
                it.Measured = "检查完成：没发现损坏的系统文件";
                it.ReportValue = "检查完成，没发现问题";
                it.ConsoleLine = "没问题";
                it.Meaning = "系统文件完好，不用修复，这一项不用花钱";
                b.Keys["系统文件"] = "正常";
            }
            else if (output.IndexOf("完整性冲突", StringComparison.Ordinal) >= 0 || low.IndexOf("integrity violations") >= 0)
            {
                it.V = Verdict.Bad;
                it.Measured = "检查完成：发现损坏的系统文件，需要修复";
                it.ReportValue = "查出损坏，需要修";
                it.ConsoleLine = "有损坏，要修";
                it.Meaning = "系统文件有损坏，可能频繁报错、蓝屏，建议安排一次修复";
                b.Keys["系统文件"] = "有损坏";
            }
            else
            {
                it.V = Verdict.Unknown;
                it.Measured = "未判定（sfc 输出无法识别）";
                it.ReportValue = "未判定（输出无法识别）";
                it.ConsoleLine = "未能判定";
                it.Meaning = "系统文件检查没出结果，建议换个时间用管理员身份再查一次";
                b.Keys["系统文件"] = "";
            }
            return b;
        }

        // ==========================================================
        // 5. 网络连通与质量（只测延迟/丢包/DNS，不测速）
        // ==========================================================
        public static Block Network()
        {
            Block b = new Block();
            CheckItem it = b.Item;
            it.Key = "Network";
            it.Name = "上网稳不稳";

            IPAddress gw = null;
            List<string> dns = new List<string>();
            string adapter = "";
            try
            {
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    IPInterfaceProperties ip = ni.GetIPProperties();
                    if (ip == null) continue;
                    GatewayIPAddressInformationCollection gws = ip.GatewayAddresses;
                    if (gws == null || gws.Count == 0) continue;
                    foreach (GatewayIPAddressInformation g in gws)
                    {
                        if (g.Address != null && g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        { gw = g.Address; break; }
                    }
                    if (gw == null) continue;
                    adapter = ni.Name + "（" + ni.NetworkInterfaceType + "）";
                    foreach (IPAddress d in ip.DnsAddresses)
                        if (d.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) dns.Add(d.ToString());
                    break;
                }
            }
            catch (Exception) { }

            if (dns.Count == 0)
            {
                foreach (ManagementObject o in Wmi.Q(@"root\cimv2", "SELECT DNSServerSearchOrder,Description FROM Win32_NetworkAdapterConfiguration WHERE IPEnabled=True"))
                {
                    string[] arr = null;
                    try { arr = o["DNSServerSearchOrder"] as string[]; } catch (Exception) { }
                    if (arr == null) continue;
                    foreach (string s in arr) dns.Add(s);
                    if (dns.Count > 0 && adapter.Length == 0) adapter = Wmi.S(o, "Description");
                    if (dns.Count > 0) break;
                }
            }

            b.Lines.Add("网卡：" + (adapter.Length > 0 ? adapter : "未检测到"));
            b.Lines.Add("默认网关：" + (gw == null ? "未检测到" : gw.ToString()));
            b.Lines.Add("DNS 服务器：" + (dns.Count > 0 ? string.Join(" / ", dns.ToArray()) : "未检测到"));

            if (gw == null)
            {
                it.V = Verdict.Unknown;
                it.Measured = "未检测到默认网关（网线未插 / Wi-Fi 未连 / 网卡被禁用）";
                it.ReportValue = "未检测到网关（当前未联网）";
                it.ConsoleLine = "未检测到网关";
                it.Meaning = "这台机器现在没连上网，请先确认网线或 Wi-Fi，再看收银和监控";
                b.Keys["网关延迟毫秒"] = "";
                b.Keys["丢包百分比"] = "";
                b.Keys["DNS解析毫秒"] = "";
                return b;
            }

            int sent = 0, recv = 0;
            long sum = 0;
            try
            {
                using (Ping p = new Ping())
                {
                    for (int i = 0; i < 20; i++)
                    {
                        sent++;
                        try
                        {
                            PingReply r = p.Send(gw, 800);
                            if (r != null && r.Status == IPStatus.Success) { recv++; sum += r.RoundtripTime; }
                        }
                        catch (Exception) { }
                    }
                }
            }
            catch (Exception) { }
            double loss = sent > 0 ? (sent - recv) * 100.0 / sent : 100;
            double avg = recv > 0 ? (double)sum / recv : 0;
            b.Lines.Add("到网关 " + gw.ToString() + "：发 " + sent + " 包 / 回 " + recv + " 包，丢包 "
                + loss.ToString("0.0") + "%，平均延迟 " + avg.ToString("0.0") + "ms");

            string[] hosts = new string[] { "www.baidu.com", "www.qq.com", "www.taobao.com" };
            long dnsSum = 0;
            int dnsOk = 0;
            List<string> logs = new List<string>();
            foreach (string h in hosts)
            {
                try
                {
                    Stopwatch sw = Stopwatch.StartNew();
                    IPAddress[] addrs = Dns.GetHostAddresses(h);
                    sw.Stop();
                    if (addrs != null && addrs.Length > 0) { dnsOk++; dnsSum += sw.ElapsedMilliseconds; logs.Add(h + " " + sw.ElapsedMilliseconds + "ms"); }
                    else logs.Add(h + " 未解析到地址");
                }
                catch (Exception ex)
                {
                    logs.Add(h + " 解析失败（" + Fmt.Cut(Fmt.Clean(ex.Message), 28) + "）");
                }
            }
            double dnsAvg = dnsOk > 0 ? (double)dnsSum / dnsOk : -1;
            string dnsNote = "";
            if (dnsAvg > 0)
            {
                if (_dnsFirst <= 0) _dnsFirst = dnsAvg;
            }
            else if (dnsAvg == 0 && _dnsFirst > 0)
            {
                // 第二次跑同一会话时域名已在本地缓存里，0ms 不是真实解析耗时
                dnsAvg = _dnsFirst;
                dnsNote = "（本次命中本地缓存，取本会话首次实测）";
            }
            b.Lines.Add("DNS 解析：" + string.Join("，", logs.ToArray()) + dnsNote);

            // 局域网可达：从 ARP 邻居里挑几台（排除网关）实际 ping 一遍
            int lanProbes = 0, lanOk = 0;
            List<string> lanLogs = new List<string>();
            try
            {
                List<string> neighbors = ArpNeighbors(gw.ToString());
                using (Ping p2 = new Ping())
                {
                    int max = neighbors.Count > 5 ? 5 : neighbors.Count;
                    for (int i = 0; i < max; i++)
                    {
                        lanProbes++;
                        try
                        {
                            PingReply r2 = p2.Send(neighbors[i], 600);
                            bool ok = r2 != null && r2.Status == IPStatus.Success;
                            if (ok) { lanOk++; lanLogs.Add(neighbors[i] + " 通 " + r2.RoundtripTime + "ms"); }
                            else lanLogs.Add(neighbors[i] + " 不通");
                        }
                        catch (Exception) { lanLogs.Add(neighbors[i] + " 不通"); }
                    }
                }
            }
            catch (Exception) { }
            if (lanProbes > 0)
                b.Lines.Add("局域网其他设备：探测 " + lanProbes + " 台，可达 " + lanOk + " 台（" + string.Join("，", lanLogs.ToArray()) + "）");
            else
                b.Lines.Add("局域网其他设备：ARP 表里只有网关，暂无其他设备（门店只有一台电脑属正常）");

            string meas = "网关 " + gw.ToString() + "：平均延迟 " + avg.ToString("0.0") + "ms，丢包 " + loss.ToString("0.0") + "%（20 包）"
                + Environment.NewLine
                + (dnsAvg >= 0 ? "DNS 解析平均 " + dnsAvg.ToString("0") + "ms（" + dnsOk + "/3 个域名成功）" + dnsNote : "DNS 解析：未检测（域名解析失败）");
            if (dns.Count > 0) meas += Environment.NewLine + "DNS 服务器：" + string.Join(" / ", dns.ToArray());
            if (lanProbes > 0) meas += Environment.NewLine + "局域网邻居可达 " + lanOk + "/" + lanProbes + " 台";
            it.Measured = meas;
            it.ReportValue = "反应 " + avg.ToString("0") + " 毫秒（越小越顺）；掉包 " + loss.ToString("0") + "%" + Environment.NewLine
                + (dnsAvg >= 0 ? "打开网页等待 " + dnsAvg.ToString("0") + " 毫秒" : "网页测试未完成");
            it.ConsoleLine = "反应 " + avg.ToString("0") + " 毫秒，掉包 " + loss.ToString("0") + "%";

            Verdict v = Verdict.Normal;
            if (loss > 5.0 || avg > 50) v = Verdict.Bad;
            else if (loss > 0 || avg > 20) v = Verdict.Warn;
            if (dnsAvg >= 0)
            {
                if (dnsAvg > 300 && v != Verdict.Bad) v = Verdict.Warn;
            }
            else if (v == Verdict.Normal) v = Verdict.Warn;
            it.V = v;

            if (v == Verdict.Bad) it.Meaning = "网络不稳，收银和监控会掉线、会漏单，建议查网线头和路由器";
            else if (v == Verdict.Warn) it.Meaning = "网络有点卡或域名解析慢，忙的时候可能掉单，建议留意";
            else it.Meaning = "内网和上网都正常，不用花钱";

            b.Keys["网关延迟毫秒"] = recv > 0 ? avg.ToString("0.0") : "";
            b.Keys["丢包百分比"] = loss.ToString("0.0");
            b.Keys["DNS解析毫秒"] = dnsAvg >= 0 ? dnsAvg.ToString("0") : "";
            return b;
        }

        // ==========================================================
        // 6. 用户目录与桌面路径
        // ==========================================================
        public static Block ShellFolders()
        {
            Block b = new Block();
            CheckItem it = b.Item;
            it.Key = "ShellFolders";
            it.Name = "文件存在哪儿";

            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(profile)) profile = Environment.GetEnvironmentVariable("USERPROFILE");
            if (profile == null) profile = "";

            string key = @"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders";
            string[] names = new string[] { "Desktop", "Personal", "My Pictures" };
            string[] cn = new string[] { "桌面", "文档", "图片" };
            string[] defs = new string[] { "Desktop", "Documents", "Pictures" };

            int bad = 0, moved = 0;
            string movedTo = "";
            string badName = "";
            for (int i = 0; i < names.Length; i++)
            {
                string cur = Reg.Get(RegistryHive.CurrentUser, key, names[i]);
                string def = profile.Length > 0 ? Path.Combine(profile, defs[i]) : "";
                if (cur.Length == 0) cur = def;
                bool exist = false;
                try { exist = Directory.Exists(cur); } catch (Exception) { }

                string state;
                if (cur.Length == 0)
                {
                    state = "未读到";
                }
                else if (!exist)
                {
                    state = "路径已失效（文件夹不存在）";
                    bad++;
                    if (badName.Length == 0) badName = cn[i];
                }
                else if (!SameDrive(cur, def))
                {
                    string drv = "";
                    try { drv = Path.GetPathRoot(Path.GetFullPath(cur)).Replace("\\", "").Replace(":", ""); } catch (Exception) { }
                    state = "已被改到另一块盘";
                    moved++;
                    if (movedTo.Length == 0) movedTo = drv;
                }
                else if (cur.IndexOf("OneDrive", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    state = "被挪到 OneDrive（可能是云端占位）";
                    moved++;
                    if (movedTo.Length == 0) movedTo = "OneDrive";
                }
                else if (!SamePath(cur, def))
                {
                    state = "路径被改过（同盘）";
                }
                else
                {
                    state = "默认位置";
                }

                b.Lines.Add(cn[i] + "（" + names[i] + "）：" + (cur.Length > 0 ? cur : "空")
                    + "  默认应为 " + def + "  判定：" + state + "  文件夹存在：" + (exist ? "是" : "否"));
            }

            it.Measured = string.Join(Environment.NewLine, b.Lines.ToArray());
            if (bad > 0) it.ReportValue = badName + "路径已失效，需修复";
            else if (moved > 0) it.ReportValue = "桌面/文档 已搬到 " + movedTo + " 盘";
            else it.ReportValue = "桌面/文档/图片 都在默认位置";
            it.ConsoleLine = bad > 0 ? "有路径已失效" : (moved > 0 ? "桌面/文档被搬走" : "路径正常");

            if (bad > 0)
            {
                it.V = Verdict.Bad;
                it.Meaning = "桌面/文档路径已失效，存进去的文件可能看不见、也容易丢，需尽快修";
            }
            else if (moved > 0)
            {
                it.V = Verdict.Warn;
                it.Meaning = "桌面/文档被搬到别的盘或云端，文件不会丢但容易找不着，建议改回默认";
            }
            else
            {
                it.V = Verdict.Normal;
                it.Meaning = "桌面和文档都在老位置，文件不会莫名消失，这一项不用花钱";
            }
            b.Keys["路径异常数"] = (bad + moved).ToString();
            return b;
        }

        private static bool SamePath(string a, string b)
        {
            try
            {
                if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
                return Path.GetFullPath(a).TrimEnd('\\').Equals(Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception) { return false; }
        }

        private static bool SameDrive(string a, string b)
        {
            try
            {
                if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return true;
                string ra = Path.GetPathRoot(Path.GetFullPath(a));
                string rb = Path.GetPathRoot(Path.GetFullPath(b));
                if (string.IsNullOrEmpty(ra) || string.IsNullOrEmpty(rb)) return true;
                return ra.Equals(rb, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception) { return true; }
        }

        // ==========================================================
        // 7. 整机资产清单
        // ==========================================================
        public static Block Inventory(Inventory inv)
        {
            Block b = new Block();
            CheckItem it = b.Item;
            it.Key = "Inventory";
            it.Name = "电脑配置";

            try
            {
                foreach (ManagementObject o in Wmi.Q(@"root\cimv2", "SELECT Manufacturer,Model FROM Win32_ComputerSystem"))
                { inv.Vendor = Wmi.S(o, "Manufacturer"); inv.Model = Wmi.S(o, "Model"); break; }

                foreach (ManagementObject o in Wmi.Q(@"root\cimv2", "SELECT Name FROM Win32_Processor"))
                { inv.Cpu = Fmt.Clean(Wmi.S(o, "Name")); break; }

                List<string> gpus = new List<string>();
                foreach (ManagementObject o in Wmi.Q(@"root\cimv2", "SELECT Name FROM Win32_VideoController"))
                {
                    string g = Fmt.Clean(Wmi.S(o, "Name"));
                    if (g.Length > 0 && !gpus.Contains(g)) gpus.Add(g);
                }
                foreach (string g in gpus)
                {
                    if (IsVirtualGpu(g)) continue;
                    inv.Gpu = g;
                    break;
                }
                if (inv.Gpu.Length == 0 && gpus.Count > 0) inv.Gpu = gpus[0];
                if (gpus.Count > 1) inv.Gpu += "（共 " + gpus.Count + " 个显示设备）";

                foreach (ManagementObject o in Wmi.Q(@"root\cimv2", "SELECT Manufacturer,Product FROM Win32_BaseBoard"))
                { inv.Board = Fmt.Clean(Wmi.S(o, "Manufacturer") + " " + Wmi.S(o, "Product")); break; }

                ulong ram = 0;
                int sticks = 0;
                foreach (ManagementObject o in Wmi.Q(@"root\cimv2", "SELECT Capacity FROM Win32_PhysicalMemory"))
                {
                    ulong c = Wmi.N(o, "Capacity");
                    if (c > 0) { sticks++; ram += c; }
                }
                inv.RamGB = Math.Round(ram / GB, 1);
                inv.RamSticks = sticks;
                if (ram == 0)
                {
                    foreach (ManagementObject o in Wmi.Q(@"root\cimv2", "SELECT TotalPhysicalMemory FROM Win32_ComputerSystem"))
                    { inv.RamGB = Math.Round(Wmi.N(o, "TotalPhysicalMemory") / GB, 1); break; }
                }

                inv.Disks.Clear();
                foreach (ManagementObject o in Wmi.Q(@"root\cimv2", "SELECT Model,Size FROM Win32_DiskDrive"))
                {
                    string m = Fmt.Clean(Wmi.S(o, "Model"));
                    ulong sz = Wmi.N(o, "Size");
                    if (m.Length == 0 && sz == 0) continue;
                    inv.Disks.Add(m + " " + Fmt.G0(sz));
                }

                inv.MonitorCount = Native.MonitorCount();

                foreach (ManagementObject o in Wmi.Q(@"root\cimv2", "SELECT Caption,Version,BuildNumber,InstallDate FROM Win32_OperatingSystem"))
                {
                    inv.OsCaption = Fmt.Clean(Wmi.S(o, "Caption"));
                    inv.OsVersion = Wmi.S(o, "Version");
                    inv.OsBuild = Wmi.S(o, "BuildNumber");
                    DateTime dt = Wmi.D(o, "InstallDate");
                    inv.InstallDate = dt == DateTime.MinValue ? "" : dt.ToString("yyyy-MM-dd");
                    break;
                }

                foreach (ManagementObject o in Wmi.Q(@"root\cimv2", "SELECT ReleaseDate FROM Win32_BIOS"))
                {
                    DateTime dt = Wmi.D(o, "ReleaseDate");
                    inv.BiosDate = dt == DateTime.MinValue ? "" : dt.ToString("yyyy-MM-dd");
                    if (dt != DateTime.MinValue && dt.Year > 1990) inv.FactoryYear = dt.Year;
                    break;
                }

                List<SwInfo> sw = ReadSoftware();
                inv.SwCount = sw.Count;
                inv.SwList.Clear();
                for (int i = 0; i < sw.Count && i < 15; i++)
                    inv.SwList.Add(sw[i].Name + (sw[i].Version.Length > 0 ? "  " + sw[i].Version : ""));
                inv.Bundles = FindBundles(sw);
            }
            catch (Exception ex)
            {
                b.Lines.Add("资产采集异常：" + ex.Message);
            }

            b.Lines.Add("设备型号：" + inv.DeviceModel());
            b.Lines.Add("出厂年份（按 BIOS 日期估算）：" + inv.AgeText());
            b.Lines.Add("CPU：" + inv.Cpu);
            b.Lines.Add("内存：" + inv.RamGB.ToString("0.0") + "G（" + inv.RamSticks + " 条）");
            b.Lines.Add("显卡：" + inv.Gpu);
            b.Lines.Add("主板：" + inv.Board);
            b.Lines.Add("显示器：" + (inv.MonitorCount >= 0 ? inv.MonitorCount + " 台" : "未检测"));
            b.Lines.Add("硬盘：" + string.Join("；", inv.Disks.ToArray()));
            b.Lines.Add("系统：" + inv.OsCaption + " " + inv.OsVersion + "（内部版本 " + inv.OsBuild + "）");
            b.Lines.Add("系统安装日期：" + (inv.InstallDate.Length > 0 ? inv.InstallDate : "未检测"));
            b.Lines.Add("BIOS 日期：" + (inv.BiosDate.Length > 0 ? inv.BiosDate : "未检测"));
            b.Lines.Add("已装软件数量：" + inv.SwCount);
            foreach (string s in inv.SwList) b.Lines.Add("  软件：" + s);
            b.Lines.Add("管家/助手类软件：" + (inv.Bundles.Count > 0 ? string.Join("、", inv.Bundles.ToArray()) : "无"));

            string mdl = Fmt.Cut(inv.DeviceModel(), 18);
            if (mdl.Length == 0) mdl = "型号未检测";
            string cpu = inv.Cpu;
            int wpos = cpu.IndexOf(" with ", StringComparison.OrdinalIgnoreCase);
            if (wpos > 0) cpu = cpu.Substring(0, wpos);
            if (cpu.StartsWith("AMD ", StringComparison.OrdinalIgnoreCase)) cpu = cpu.Substring(4);
            if (cpu.StartsWith("Intel(R) ", StringComparison.OrdinalIgnoreCase)) cpu = cpu.Substring(10);
            if (cpu.StartsWith("Intel ", StringComparison.OrdinalIgnoreCase)) cpu = cpu.Substring(6);
            cpu = Fmt.Cut(cpu, 16);
            if (cpu.Length == 0) cpu = "CPU 未检测";
            string os = inv.OsCaption;
            if (os.IndexOf("Windows 11") >= 0) os = "Win11";
            else if (os.IndexOf("Windows 10") >= 0) os = "Win10";
            else if (os.IndexOf("Windows 7") >= 0) os = "Win7";
            else os = Fmt.Cut(os, 10);
            if (os.Length == 0) os = "系统未检测";

            it.Measured = "设备型号 " + inv.DeviceModel() + "；出厂年份 " + inv.AgeText()
                + Environment.NewLine + "CPU " + inv.Cpu + "；内存 " + inv.RamGB.ToString("0.0") + "G（" + inv.RamSticks + " 条）"
                + Environment.NewLine + "硬盘 " + string.Join("；", inv.Disks.ToArray())
                + "；显示器 " + (inv.MonitorCount >= 0 ? inv.MonitorCount + " 台" : "未检测")
                + Environment.NewLine + "系统 " + inv.OsCaption + "，已装软件 " + inv.SwCount + " 个";
            it.ReportValue = mdl + " / " + cpu + Environment.NewLine
                + "内存 " + inv.RamGB.ToString("0.0") + "G / " + os + " / 软件 " + inv.SwCount + " 个";
            it.ConsoleLine = "已登记配置与软件清单";
            it.V = Verdict.Normal;
            it.Meaning = "这台机器的配置和已装软件已登记在册，是年度维护服务的设备凭据";

            b.Keys["内存G"] = inv.RamGB.ToString("0.0");
            b.Keys["已装软件数"] = inv.SwCount.ToString();
            b.Keys["机龄年"] = inv.AgeYears().ToString();
            return b;
        }

        private static bool IsVirtualGpu(string name)
        {
            string[] kw = new string[] { "Oray", "Idd", "Virtual", "Mirror", "RDP", "Remote", "Basic Render", "Basic Display" };
            foreach (string k in kw)
                if (name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        internal static List<SwInfo> ReadSoftware()
        {
            List<SwInfo> list = new List<SwInfo>();
            Dictionary<string, bool> seen = new Dictionary<string, bool>();
            string[] roots = new string[] {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };
            RegistryHive[] hives = new RegistryHive[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser };
            foreach (RegistryHive h in hives)
            {
                foreach (string root in roots)
                {
                    foreach (string sub in Reg.Subs(h, root))
                    {
                        string path = root + "\\" + sub;
                        string name = Reg.Get(h, path, "DisplayName");
                        if (name.Length == 0) continue;
                        if (Reg.Get(h, path, "SystemComponent") == "1") continue;
                        if (Reg.Get(h, path, "ParentDisplayName").Length > 0) continue;
                        string rt = Reg.Get(h, path, "ReleaseType");
                        if (rt.IndexOf("Hotfix", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                        if (rt.IndexOf("Security Update", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                        if (rt.IndexOf("Update Rollup", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                        string k = name.ToLower();
                        if (seen.ContainsKey(k)) continue;
                        seen[k] = true;
                        SwInfo si = new SwInfo();
                        si.Name = Fmt.Clean(name);
                        si.Version = Reg.Get(h, path, "DisplayVersion");
                        si.Date = Reg.Get(h, path, "InstallDate");
                        list.Add(si);
                    }
                }
            }
            list.Sort(delegate (SwInfo a, SwInfo b)
            {
                bool da = a.Date.Length == 8, db = b.Date.Length == 8;
                if (da && db)
                {
                    int c = string.Compare(b.Date, a.Date, StringComparison.Ordinal);
                    if (c != 0) return c;
                }
                else if (da) return -1;
                else if (db) return 1;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            return list;
        }

        private static List<string> FindBundles(List<SwInfo> sw)
        {
            List<string> hit = new List<string>();
            string[] kw = new string[] {
                "360安全卫士", "360杀毒", "360手机助手", "360驱动大师", "360压缩",
                "腾讯电脑管家", "QQ电脑管家", "金山毒霸", "金山卫士",
                "2345加速", "2345安全卫士", "2345好压", "2345王牌", "2345看图",
                "鲁大师", "驱动精灵", "驱动人生", "瑞星", "百度卫士", "百度杀毒",
                "快压", "好压", "搜狗高速浏览器"
            };
            foreach (SwInfo s in sw)
            {
                foreach (string k in kw)
                {
                    if (s.Name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (!hit.Contains(s.Name)) hit.Add(s.Name);
                        break;
                    }
                }
            }
            return hit;
        }

        // ==========================================================
        // 8. 使用体验（附加项：桌面/任务栏/右键菜单/全家桶）
        // ==========================================================
        public static Block Experience(Inventory inv)
        {
            Block b = new Block();
            CheckItem it = b.Item;
            it.Key = "Experience";
            it.Name = "用着顺不顺手";

            List<string> issues = new List<string>();

            string hideVal = Reg.Get(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "HideIcons");
            b.Lines.Add("桌面图标开关 HideIcons=" + (hideVal.Length > 0 ? hideVal : "未设置"));
            if (hideVal == "1") issues.Add("桌面图标被隐藏，客人会以为文件丢了");

            string shell = Reg.Get(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon", "Shell");
            b.Lines.Add("Windows 外壳 Shell=" + (shell.Length > 0 ? shell : "未读到（默认 explorer.exe）"));
            if (shell.Length > 0 && shell.ToLower().IndexOf("explorer.exe", StringComparison.Ordinal) < 0)
                issues.Add("系统外壳被改成 " + Fmt.Cut(Fmt.Clean(shell), 20));

            int thirdMenu = 0;
            List<string> menuNames = new List<string>();
            string[] menuKeys = new string[] {
                @"*\shellex\ContextMenuHandlers",
                @"Directory\shellex\ContextMenuHandlers",
                @"Directory\Background\shellex\ContextMenuHandlers"
            };
            RegistryHive[] mh = new RegistryHive[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine };
            Dictionary<string, bool> seen = new Dictionary<string, bool>();
            foreach (RegistryHive h in mh)
            {
                string prefix = h == RegistryHive.CurrentUser ? @"Software\Classes\" : @"SOFTWARE\Classes\";
                foreach (string mk in menuKeys)
                {
                    foreach (string sub in Reg.Subs(h, prefix + mk))
                    {
                        if (IsMsMenu(sub)) continue;
                        if (seen.ContainsKey(sub.ToLower())) continue;
                        seen[sub.ToLower()] = true;
                        thirdMenu++;
                        if (menuNames.Count < 6) menuNames.Add(sub);
                    }
                }
            }
            b.Lines.Add("右键菜单第三方项：" + thirdMenu + (menuNames.Count > 0 ? "（" + string.Join("、", menuNames.ToArray()) + "）" : ""));
            if (thirdMenu >= 8) issues.Add("右键菜单里堆了 " + thirdMenu + " 个第三方项，菜单很长");

            b.Lines.Add("管家/助手类软件：" + (inv.Bundles.Count > 0 ? string.Join("、", inv.Bundles.ToArray()) : "无"));
            if (inv.Bundles.Count >= 2) issues.Add("同时装了 " + inv.Bundles.Count + " 个管家/助手类软件");

            if (issues.Count == 0)
            {
                it.V = Verdict.Normal;
                it.Measured = "桌面图标正常显示；系统外壳正常；右键菜单第三方项 " + thirdMenu + " 个；未发现多个管家并存";
                it.ReportValue = "桌面正常，右键菜单里多了 " + thirdMenu + " 个附加项";
                it.ConsoleLine = "桌面/右键菜单干净";
                it.Meaning = "桌面和右键菜单都干净，用着不会别扭，这一项不用花钱";
            }
            else
            {
                it.V = Verdict.Warn;
                it.Measured = string.Join("；", issues.ToArray());
                string s;
                if (inv.Bundles.Count >= 2) s = "装了 " + inv.Bundles.Count + " 个管家类软件";
                else if (thirdMenu >= 8) s = "右键菜单多了 " + thirdMenu + " 个附加项";
                else s = Fmt.Cut(issues[0], 18);
                it.ReportValue = s;
                it.ConsoleLine = "发现 " + issues.Count + " 处不顺手的地方";
                it.Meaning = "装了多个管家/助手或插件，会拖慢开机、弹窗多、菜单乱，建议清理";
            }
            b.Keys["右键菜单第三方项"] = thirdMenu.ToString();
            b.Keys["管家类软件数"] = inv.Bundles.Count.ToString();
            return b;
        }

        private static bool IsMsMenu(string name)
        {
            string[] ms = new string[] {
                "WorkFolders", "Sharing", "ModernSharing", "LibraryLocation", "PinToStartScreen",
                "SendTo", "OpenWith", "Encrypt", "BitLocker", "PreviousVersions", "BriefcaseMenu",
                "CopyAsPathMenu", "Compatibility", "GraphicsOptions", "PlayTo"
            };
            foreach (string m in ms)
                if (name.IndexOf(m, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        /// 从 ARP 表里取同网段邻居（排除网关、本机地址、广播/组播）
        private static List<string> ArpNeighbors(string gateway)
        {
            List<string> list = new List<string>();
            List<string> locals = new List<string>();
            try
            {
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (UnicastIPAddressInformation u in ni.GetIPProperties().UnicastAddresses)
                        if (u.Address != null && u.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                            locals.Add(u.Address.ToString());
                }
            }
            catch (Exception) { }

            string outp = CmdOut("arp.exe", "-a", 4000);
            if (outp.Length == 0) return list;
            string[] lines = outp.Replace("\r", "").Split('\n');
            foreach (string line in lines)
            {
                string[] t = line.Trim().Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (t.Length < 2) continue;
                string ip = t[0];
                IPAddress addr = null;
                if (!IPAddress.TryParse(ip, out addr)) continue;
                if (ip.StartsWith("127.") || ip.StartsWith("255.") || ip == "0.0.0.0") continue;
                // 组播 224–239 / 广播 / 网络地址（常见 /24 网段）
                if (addr.GetAddressBytes()[0] >= 224 && addr.GetAddressBytes()[0] <= 239) continue;
                if (ip.EndsWith(".255") || ip.EndsWith(".0")) continue;
                if (ip == gateway) continue;
                if (locals.Contains(ip)) continue;
                // 第二列必须是 MAC 地址（含 - 或 :），否则不是有效邻居行
                string mac = t[1];
                if (mac.IndexOf('-') < 0 && mac.IndexOf(':') < 0) continue;
                if (!list.Contains(ip)) list.Add(ip);
            }
            return list;
        }

        // ==========================================================
        // 9. 打印机 / 扫码枪 / 收银外设
        // ==========================================================
        public static Block Peripherals()
        {
            Block b = new Block();
            CheckItem it = b.Item;
            it.Key = "Peripherals";
            it.Name = "打印机/扫码枪";

            List<string> printers = new List<string>();
            List<string> pnpPrinters = new List<string>();
            List<string> scannerDevices = new List<string>();
            List<string> serialPorts = new List<string>();
            int offline = 0, errors = 0;
            bool hasDefault = false;

            try
            {
                foreach (ManagementObject o in Wmi.Q(@"root\cimv2",
                    "SELECT Name,Default,WorkOffline,PrinterStatus,PortName FROM Win32_Printer"))
                {
                    string name = Fmt.Clean(Wmi.S(o, "Name"));
                    if (name.Length == 0) continue;
                    if (IsVirtualPrinter(name)) continue;

                    bool def = false, off = false;
                    int st = 0;
                    try { def = Convert.ToBoolean(o["Default"], CultureInfo.InvariantCulture); } catch (Exception) { }
                    try { off = Convert.ToBoolean(o["WorkOffline"], CultureInfo.InvariantCulture); } catch (Exception) { }
                    try { st = Convert.ToInt32(o["PrinterStatus"], CultureInfo.InvariantCulture); } catch (Exception) { }

                    // PrinterStatus：7=离线，6=已停止，0/2=未知，其余按状态文字判断
                    string state = off ? "脱机" : (st == 7 ? "离线" : (st == 6 ? "已停止" : (st == 4 ? "正在打印" : "就绪")));
                    if (off || st == 7) offline++;
                    if (st == 6) errors++;
                    if (def) hasDefault = true;
                    printers.Add(name + (def ? "（默认）" : "") + "  " + state);
                    b.Lines.Add("打印机：" + name + (def ? "  [默认]" : "") + "  状态：" + state
                        + "  端口：" + Wmi.S(o, "PortName"));
                }

                foreach (ManagementObject o in Wmi.Q(@"root\cimv2", "SELECT DeviceID,Description FROM Win32_SerialPort"))
                {
                    string id = Wmi.S(o, "DeviceID");
                    string desc = Fmt.Clean(Wmi.S(o, "Description"));
                    if (id.Length == 0) continue;
                    serialPorts.Add(id + (desc.Length > 0 ? "（" + desc + "）" : ""));
                    b.Lines.Add("串口：" + id + "  " + desc);
                }

                string[] kw = new string[] {
                    "print", "cash", "scanner", "scan", "barcode", "bar code",
                    "扫码", "收银", "打印", "小票", "标签", "钱箱", "读卡", "刷卡",
                    "thermal", "receipt", "ch340", "cp210", "ftdi", "prolific", "usb-serial"
                };
                foreach (ManagementObject o in Wmi.Q(@"root\cimv2",
                    "SELECT Name,PNPClass,Status,ConfigManagerErrorCode,Manufacturer FROM Win32_PnPEntity WHERE PNPClass='USB' OR PNPClass='Ports' OR PNPClass='Printer'"))
                {
                    string name = Fmt.Clean(Wmi.S(o, "Name"));
                    if (name.Length == 0) continue;
                    string cls = Wmi.S(o, "PNPClass");
                    bool printer = cls == "Printer" || name.IndexOf("打印", StringComparison.Ordinal) >= 0
                        || name.IndexOf("printer", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (printer && IsVirtualPrinter(name)) continue;
                    bool hit = printer;
                    foreach (string k in kw)
                        if (name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) { hit = true; break; }
                    if (!hit) continue;
                    int code = 0;
                    try { code = Convert.ToInt32(o["ConfigManagerErrorCode"], CultureInfo.InvariantCulture); } catch (Exception) { }
                    string st = Wmi.S(o, "Status");
                    if (printer)
                    {
                        string one = name + (code == 0 ? "  正常" : "  异常（代码 " + code + "）");
                        if (!pnpPrinters.Contains(one)) pnpPrinters.Add(one);
                        b.Lines.Add("打印设备：" + name + "  状态：" + (st.Length > 0 ? st : "未上报")
                            + (code != 0 ? "  错误代码 " + code : ""));
                        if (code != 0) errors++;
                    }
                    else if (!scannerDevices.Contains(name))
                    {
                        scannerDevices.Add(name);
                        b.Lines.Add("外设：" + name + "  " + (cls.Length > 0 ? cls + "  " : "") + (st.Length > 0 ? st : ""));
                        if (code != 0) errors++;
                    }
                }
            }
            catch (Exception ex)
            {
                b.Lines.Add("外设读取异常：" + Fmt.Clean(ex.Message));
                it.V = Verdict.Unknown;
                it.Measured = "未检测（" + Fmt.Clean(ex.Message) + "）";
                it.ReportValue = "未检测";
                it.ConsoleLine = "未检测";
                it.Meaning = "这一项没读出来，不影响其他项";
                return b;
            }

            int realPrinters = printers.Count;
            if (realPrinters == 0) realPrinters = pnpPrinters.Count;
            if (realPrinters > 0 && !hasDefault && printers.Count > 0) errors++;

            string rv = realPrinters > 0 ? "打印机 " + realPrinters + " 台" + (hasDefault ? "（有默认）" : (printers.Count > 0 ? "（无默认）" : ""))
                : "没检测到打印机";
            if (scannerDevices.Count > 0) rv += "；扫码/收银外设 " + scannerDevices.Count + " 个";
            if (serialPorts.Count > 0) rv += "；串口 " + serialPorts.Count + " 个";

            it.Measured = "打印机：" + (printers.Count > 0 ? string.Join("；", printers.ToArray())
                    : (pnpPrinters.Count > 0 ? string.Join("；", pnpPrinters.ToArray()) : "未检测到"))
                + (scannerDevices.Count > 0 ? Environment.NewLine + "扫码/收银外设：" + string.Join("、", scannerDevices.ToArray()) : "")
                + (serialPorts.Count > 0 ? Environment.NewLine + "串口：" + string.Join("、", serialPorts.ToArray()) : "");
            it.ReportValue = rv;
            it.ConsoleLine = rv;

            if (realPrinters == 0 && scannerDevices.Count == 0 && serialPorts.Count == 0)
            {
                it.V = Verdict.Warn;
                it.Meaning = "没找到打印机、扫码枪或串口设备。如果店里本来有小票/标签打印机，请检查电源、USB 线和驱动";
            }
            else if (offline > 0 || errors > 0)
            {
                it.V = Verdict.Bad;
                it.Meaning = "打印机脱机或设备异常，收银时可能打不出小票、扫不了码，建议先重启打印机并检查 USB 线";
                it.TopPriority = true;
            }
            else
            {
                it.V = Verdict.Normal;
                it.Meaning = "打印机和收银外设都能正常识别，这一项不用花钱";
            }

            b.Keys["打印机数"] = realPrinters.ToString();
            b.Keys["打印机脱机数"] = offline.ToString();
            b.Keys["外设异常数"] = errors.ToString();
            return b;
        }

        private static bool IsVirtualPrinter(string name)
        {
            string[] kw = new string[] {
                "Microsoft Print to PDF", "Microsoft XPS", "Fax", "OneNote",
                "Send To OneNote", "虚拟", "Virtual", "WPS PDF", "Foxit PDF",
                "Adobe PDF", "PDFCreator", "CutePDF", "IPP Class Driver", "Universal Print"
            };
            foreach (string k in kw)
                if (name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        // ==========================================================
        // 10. 收银软件 / ERP 是否在运行
        // ==========================================================
        public static Block PosSoftware()
        {
            Block b = new Block();
            CheckItem it = b.Item;
            it.Key = "PosSoftware";
            it.Name = "收银软件在不在跑";

            List<string> procs = new List<string>();
            List<string> services = new List<string>();
            try
            {
                string[] kw = PosKeywords();
                foreach (Process p in Process.GetProcesses())
                {
                    try
                    {
                        string pn = p.ProcessName == null ? "" : p.ProcessName;
                        if (pn.Length == 0) continue;
                        foreach (string k in kw)
                        {
                            if (k.Length == 0) continue;
                            if (PosHit(pn, k))
                            {
                                if (!procs.Contains(pn)) procs.Add(pn);
                                break;
                            }
                        }
                    }
                    catch (Exception) { }
                }

                foreach (ManagementObject o in Wmi.Q(@"root\cimv2", "SELECT Name,DisplayName,State FROM Win32_Service"))
                {
                    string nm = Wmi.S(o, "Name");
                    string dn = Fmt.Clean(Wmi.S(o, "DisplayName"));
                    if (Wmi.S(o, "State") != "Running") continue;
                    foreach (string k in kw)
                    {
                        if (k.Length == 0) continue;
                        if (PosHit(nm + " " + dn, k))
                        {
                            string label = dn.Length > 0 ? dn : nm;
                            if (!services.Contains(label)) services.Add(label);
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                b.Lines.Add("收银软件检测异常：" + Fmt.Clean(ex.Message));
            }

            b.Lines.Add("识别到正在运行的收银/ERP 进程：" + (procs.Count > 0 ? string.Join("、", procs.ToArray()) : "无"));
            b.Lines.Add("识别到正在运行的收银相关服务：" + (services.Count > 0 ? string.Join("、", services.ToArray()) : "无"));
            b.Lines.Add("说明：可在程序目录放“收银软件清单.txt”，每行写一个进程/服务关键词，下次就会按清单识别。");

            if (procs.Count > 0 || services.Count > 0)
            {
                it.V = Verdict.Normal;
                it.ReportValue = "正在运行：" + Fmt.Cut(procs.Count > 0 ? string.Join("、", procs.ToArray()) : string.Join("、", services.ToArray()), 40);
                it.ConsoleLine = "收银软件在运行";
                it.Meaning = "收银/ERP 软件正在运行，结账流程不受影响，这一项不用花钱";
            }
            else
            {
                it.V = Verdict.Warn;
                it.ReportValue = "没发现收银软件在运行";
                it.ConsoleLine = "没发现收银软件";
                it.Meaning = "没发现收银软件在跑。若现在不营业或用网页收银可忽略；否则先打开它确认能结账";
            }
            it.Measured = it.ReportValue;
            b.Keys["收银软件运行数"] = (procs.Count + services.Count).ToString();
            return b;
        }

        /// 关键词匹配：pos 太短，只认开头/结尾，避免把 repository/composite 当成收银软件
        private static bool PosHit(string text, string keyword)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(keyword)) return false;
            if (keyword.Equals("pos", StringComparison.OrdinalIgnoreCase))
                return text.StartsWith("pos", StringComparison.OrdinalIgnoreCase)
                    || text.EndsWith("pos", StringComparison.OrdinalIgnoreCase);
            return text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// 默认关键词 + 程序目录下“收银软件清单.txt”的自定义关键词（每行一个）
        private static string[] PosKeywords()
        {
            List<string> list = new List<string>();
            string[] def = new string[] {
                "收银", "pos", "cashier", "零售", "管家婆", "思迅", "科脉", "海鼎",
                "银豹", "客如云", "美团收银", "二维火", "哗啦啦", "天店", "千里马",
                "中崎", "佳博", "商米", "银收", "erp", "进销存"
            };
            foreach (string d in def) list.Add(d);
            try
            {
                string file = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "收银软件清单.txt");
                if (File.Exists(file))
                {
                    foreach (string line in File.ReadAllLines(file, Encoding.UTF8))
                    {
                        string t = line.Trim();
                        if (t.Length == 0 || t.StartsWith("#")) continue;
                        if (!list.Contains(t)) list.Add(t);
                    }
                }
            }
            catch (Exception) { }
            return list.ToArray();
        }

        // ==========================================================
        // 网络辅助：局域网邻居探测
        // ==========================================================
        private static string CmdOut(string exe, string args, int timeoutMs)
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
                    string o = p.StandardOutput.ReadToEnd();
                    p.StandardError.ReadToEnd();
                    p.WaitForExit(timeoutMs);
                    return o;
                }
            }
            catch (Exception) { return ""; }
        }
    }
}
