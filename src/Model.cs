using System;
using System.Collections.Generic;
using System.Globalization;
using System.Management;
using System.Text;
using Microsoft.Win32;

namespace StoreCheck
{
    /// 判定档位：正常 / 注意 / 该处理 / 未检测
    public enum Verdict { Normal = 0, Warn = 1, Bad = 2, Unknown = 3 }

    /// 结论单里的一行
    public class CheckItem
    {
        public string Key;           // 内部键
        public string Name;          // 检测项名称
        public string Measured;      // 实测值（原始数据，可多行）
        public string ReportValue;   // 实测值（结论单用紧凑版）
        public string PrevMeasured;  // 上次（历史对比）
        public Verdict V;
        public string Meaning;       // 意味着什么（店主能懂）
        public bool TopPriority;     // 结论单置顶
        public string ConsoleLine;   // 屏幕一行结论

        public CheckItem()
        {
            Key = ""; Name = ""; Measured = ""; ReportValue = "";
            PrevMeasured = ""; V = Verdict.Unknown; Meaning = "";
            TopPriority = false; ConsoleLine = "";
        }
    }

    /// 一个检测项的完整产出
    public class Block
    {
        public CheckItem Item;
        public List<string> Lines;                  // 原始数据明细
        public Dictionary<string, string> Keys;     // 历史对比键值

        public Block()
        {
            Item = new CheckItem();
            Lines = new List<string>();
            Keys = new Dictionary<string, string>();
        }
    }

    /// 整机资产
    public class Inventory
    {
        public string Vendor = "";
        public string Model = "";
        public string Cpu = "";
        public string Gpu = "";
        public string Board = "";
        public string OsCaption = "";
        public string OsVersion = "";
        public string OsBuild = "";
        public string InstallDate = "";
        public string BiosDate = "";
        public double RamGB = 0;
        public int RamSticks = 0;
        public int MonitorCount = -1;
        public int FactoryYear = 0;
        public List<string> Disks = new List<string>();
        public int SwCount = 0;
        public List<string> SwList = new List<string>();
        public List<string> Bundles = new List<string>();

        public string DeviceModel()
        {
            string v = Vendor == null ? "" : Vendor.Trim();
            string m = Model == null ? "" : Model.Trim();
            if (v.Equals("To be filled by O.E.M.", StringComparison.OrdinalIgnoreCase)) v = "";
            if (m.Equals("To be filled by O.E.M.", StringComparison.OrdinalIgnoreCase)) m = "";
            if (v.Equals("Default string", StringComparison.OrdinalIgnoreCase)) v = "";
            if (m.Equals("Default string", StringComparison.OrdinalIgnoreCase)) m = "";
            if (m.Equals("System Product Name", StringComparison.OrdinalIgnoreCase)) m = "";
            if (v.Length > 0 && m.Length > 0)
            {
                if (m.StartsWith(v, StringComparison.OrdinalIgnoreCase)) return m;
                return v + " " + m;
            }
            if (m.Length > 0) return m;
            if (v.Length > 0) return v;
            if (Board != null && Board.Trim().Length > 0) return "组装机（主板 " + Board.Trim() + "）";
            return "";
        }

        public int AgeYears()
        {
            if (FactoryYear <= 1970) return -1;
            int age = DateTime.Now.Year - FactoryYear;
            if (age < 0) age = 0;
            return age;
        }

        public string AgeText()
        {
            if (FactoryYear <= 1970) return "未检测";
            return FactoryYear + " 年（机龄约 " + AgeYears() + " 年）";
        }
    }

    /// 一台设备一次巡检
    public class Snapshot
    {
        public string StoreName = "";
        public string Inspector = "";
        public string DeviceName = "";
        public DateTime StartTime;
        public DateTime EndTime;
        public Inventory Inv = new Inventory();
        public List<Block> Blocks = new List<Block>();
        public string Overall = "";
        public string OverallNote = "";
        public string SheetNo = "";
    }

    public static class Vs
    {
        public static string Sym(Verdict v)
        {
            switch (v)
            {
                case Verdict.Normal: return "■";
                case Verdict.Warn: return "▲";
                case Verdict.Bad: return "●";
                default: return "○";
            }
        }

        public static string Txt(Verdict v)
        {
            switch (v)
            {
                case Verdict.Normal: return "正常";
                case Verdict.Warn: return "注意";
                case Verdict.Bad: return "该处理";
                default: return "未检测";
            }
        }

        public static string Css(Verdict v)
        {
            switch (v)
            {
                case Verdict.Normal: return "ok";
                case Verdict.Warn: return "wn";
                case Verdict.Bad: return "bd";
                default: return "uk";
            }
        }

        public static ConsoleColor Color(Verdict v)
        {
            switch (v)
            {
                case Verdict.Normal: return ConsoleColor.Green;
                case Verdict.Warn: return ConsoleColor.Yellow;
                case Verdict.Bad: return ConsoleColor.Red;
                default: return ConsoleColor.DarkGray;
            }
        }
    }

    /// WMI 只读封装（全部吞异常，读不到即视为不支持）
    public static class Wmi
    {
        /// WMI 在多线程并发查询时偶尔会互相干扰，统一串行化（单次查询通常很快）
        private static readonly object Gate = new object();

        public static List<ManagementObject> Q(string ns, string wql)
        {
            List<ManagementObject> list = new List<ManagementObject>();
            lock (Gate)
            {
                try
                {
                    EnumerationOptions eo = new EnumerationOptions();
                    eo.ReturnImmediately = true;
                    eo.Timeout = new TimeSpan(0, 0, 45);
                    using (ManagementObjectSearcher s = new ManagementObjectSearcher(ns, wql, eo))
                    {
                        foreach (ManagementObject o in s.Get()) list.Add(o);
                    }
                }
                catch (Exception) { }
            }
            return list;
        }

        public static string S(ManagementBaseObject o, string p)
        {
            try
            {
                object v = o[p];
                if (v == null) return "";
                string[] arr = v as string[];
                if (arr != null) return string.Join("/", arr);
                return Convert.ToString(v, CultureInfo.InvariantCulture).Trim();
            }
            catch (Exception) { return ""; }
        }

        public static ulong N(ManagementBaseObject o, string p)
        {
            try
            {
                object v = o[p];
                if (v == null) return 0;
                return Convert.ToUInt64(v, CultureInfo.InvariantCulture);
            }
            catch (Exception) { return 0; }
        }

        public static DateTime D(ManagementBaseObject o, string p)
        {
            string s = S(o, p);
            if (s.Length < 8) return DateTime.MinValue;
            try { return ManagementDateTimeConverter.ToDateTime(s); }
            catch (Exception) { return DateTime.MinValue; }
        }
    }

    /// 注册表只读封装
    public static class Reg
    {
        public static string Get(RegistryHive hive, string path, string name)
        {
            string[] views = new string[] { "64", "32" };
            foreach (string v in views)
            {
                try
                {
                    RegistryView rv = v == "64" ? RegistryView.Registry64 : RegistryView.Registry32;
                    using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, rv))
                    using (RegistryKey k = baseKey.OpenSubKey(path, false))
                    {
                        if (k == null) continue;
                        object o = k.GetValue(name, null);
                        if (o == null) continue;
                        string s = o as string;
                        if (s != null) return s;
                        byte[] b = o as byte[];
                        if (b != null) return Convert.ToBase64String(b);
                        return Convert.ToString(o, CultureInfo.InvariantCulture);
                    }
                }
                catch (Exception) { }
            }
            return "";
        }

        public static string[] Names(RegistryHive hive, string path)
        {
            try
            {
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64))
                using (RegistryKey k = baseKey.OpenSubKey(path, false))
                {
                    if (k == null) return new string[0];
                    string[] n = k.GetValueNames();
                    return n == null ? new string[0] : n;
                }
            }
            catch (Exception) { return new string[0]; }
        }

        public static string[] Subs(RegistryHive hive, string path)
        {
            try
            {
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64))
                using (RegistryKey k = baseKey.OpenSubKey(path, false))
                {
                    if (k == null) return new string[0];
                    string[] n = k.GetSubKeyNames();
                    return n == null ? new string[0] : n;
                }
            }
            catch (Exception) { return new string[0]; }
        }
    }

    public static class Fmt
    {
        private const double GB = 1073741824.0;

        public static string G(double bytes)
        {
            if (bytes <= 0) return "0G";
            double g = bytes / GB;
            if (g >= 1024) return (g / 1024.0).ToString("0.00") + "T";
            return g.ToString("0.0") + "G";
        }

        public static string G0(double bytes)
        {
            double g = bytes / GB;
            if (g >= 1024) return (g / 1024.0).ToString("0.0") + "T";
            return g.ToString("0") + "G";
        }

        public static string Pct(double v)
        {
            return v.ToString("0.0") + "%";
        }

        public static string Sec(long ms)
        {
            if (ms <= 0) return "未检测";
            double s = ms / 1000.0;
            if (s < 60) return s.ToString("0") + " 秒";
            long m = (long)(s / 60);
            long r = (long)(s - m * 60);
            return m + " 分 " + r.ToString("00") + " 秒";
        }

        public static string Uptime(long ms)
        {
            if (ms <= 0) return "未检测";
            long s = ms / 1000;
            long d = s / 86400;
            long h = s % 86400 / 3600;
            long mi = s % 3600 / 60;
            if (d > 0) return d + " 天 " + h + " 小时";
            if (h > 0) return h + " 小时 " + mi + " 分";
            return mi + " 分";
        }

        /// 去掉换行与表格分隔符，压成一行
        public static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            StringBuilder sb = new StringBuilder();
            foreach (char c in s)
            {
                if (c == '\r' || c == '\n') { sb.Append(' '); continue; }
                if (c == '|') { sb.Append('/'); continue; }
                sb.Append(c);
            }
            string r = sb.ToString();
            while (r.IndexOf("  ") >= 0) r = r.Replace("  ", " ");
            return r.Trim();
        }

        public static string Cut(string s, int max)
        {
            s = s == null ? "" : s.Trim();
            if (s.Length <= max) return s;
            return s.Substring(0, max - 2) + "..";
        }
    }
}
