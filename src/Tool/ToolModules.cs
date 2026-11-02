using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace StoreCheck
{
    internal static class Fs
    {
        public static void Walk(string root, Action<string> onFile)
        {
            Stack<string> st = new Stack<string>();
            st.Push(root);
            int guard = 0;
            while (st.Count > 0)
            {
                if (guard++ > 300000) break;
                string d = st.Pop();
                string[] fs;
                try { fs = Directory.GetFiles(d); }
                catch (Exception) { continue; }
                foreach (string f in fs) { try { onFile(f); } catch (Exception) { } }
                string[] ds;
                try { ds = Directory.GetDirectories(d); }
                catch (Exception) { ds = new string[0]; }
                foreach (string x in ds)
                {
                    try
                    {
                        if ((File.GetAttributes(x) & FileAttributes.ReparsePoint) != 0) continue;
                    }
                    catch (Exception) { continue; }
                    st.Push(x);
                }
            }
        }

        public static void Measure(string root, out long files, out long bytes, out int longPaths)
        {
            long f = 0, b = 0;
            int lp = 0;
            Walk(root, delegate (string p)
            {
                try
                {
                    FileInfo fi = new FileInfo(p);
                    f++;
                    b += fi.Length;
                    if (p.Length > 240) lp++;
                }
                catch (Exception) { }
            });
            files = f; bytes = b; longPaths = lp;
        }
    }

    // ============================================================
    // 1. 数据迁移
    // ============================================================
    internal static class ModuleMigrate
    {
        public static void Run()
        {
            Tool.ResetLog();
            Tool.Head("数据迁移（把源目录搬到目标盘；源目录不会被删除）");

            List<string> cand = new List<string>();
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string[] names = new string[] { "桌面", "文档", "下载", "图片", "视频" };
            string[] paths = new string[] {
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Path.Combine(profile, "Downloads"),
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
            };
            Ui.L("  常用源目录：", ConsoleColor.DarkGray);
            for (int i = 0; i < paths.Length; i++)
            {
                if (string.IsNullOrEmpty(paths[i]) || !Directory.Exists(paths[i])) continue;
                long f, b; int lp;
                Fs.Measure(paths[i], out f, out b, out lp);
                cand.Add(paths[i]);
                Ui.L("    [" + cand.Count + "] " + Ui.Pad(names[i], 8) + Ui.PadLeft(Tool.Sz(b), 9)
                    + Ui.PadLeft(Tool.Num(f), 9) + " 个    " + paths[i], ConsoleColor.Gray);
            }
            Ui.L("    [0] 自己输入路径", ConsoleColor.DarkGray);

            string src = Tool.Ask("源目录（编号或路径）", "0");
            int idx;
            if (int.TryParse(src, out idx) && idx > 0 && idx <= cand.Count) src = cand[idx - 1];
            else if (src == "0") src = Tool.Ask("请输入完整路径", "");
            if (!Directory.Exists(src))
            {
                Tool.PauseExit("源目录不存在：" + src, ConsoleColor.Red);
                return;
            }
            src = Path.GetFullPath(src).TrimEnd('\\');

            Ui.L("");
            Ui.L("  目标盘剩余空间：", ConsoleColor.DarkGray);
            foreach (DriveInfo d in Tool.FixedDrives())
                Ui.L("    " + d.Name + "  剩余 " + Tool.Sz(d.AvailableFreeSpace) + "  共 " + Tool.Sz(d.TotalSize), ConsoleColor.Gray);

            string dstRoot = Tool.Ask("目标目录（例如 D:\\迁移；回车自动用剩余最大的盘）", "");
            if (dstRoot.Length == 0)
            {
                long best = -1; string bestName = "";
                foreach (DriveInfo d in Tool.FixedDrives())
                    if (d.AvailableFreeSpace > best) { best = d.AvailableFreeSpace; bestName = d.Name; }
                if (bestName.Length == 0) { Tool.PauseExit("没有可用目标盘", ConsoleColor.Red); return; }
                dstRoot = Path.Combine(bestName, "66迁移");
            }
            try { if (!Directory.Exists(dstRoot)) Directory.CreateDirectory(dstRoot); }
            catch (Exception ex) { Tool.PauseExit("目标目录建不出来：" + ex.Message, ConsoleColor.Red); return; }

            string leaf = new DirectoryInfo(src).Name;
            string dst = Path.Combine(dstRoot, leaf);
            if (Directory.Exists(dst) && Directory.GetFileSystemEntries(dst).Length > 0)
            {
                dst = Path.Combine(dstRoot, leaf + "_" + DateTime.Now.ToString("MMddHHmm"));
                Ui.L("  目标已存在，改用：" + dst, ConsoleColor.Yellow);
            }

            Ui.L("");
            Ui.L("  预检中……", ConsoleColor.DarkGray);
            long files, bytes; int longPaths;
            Fs.Measure(src, out files, out bytes, out longPaths);
            long free = Tool.FreeBytes(dst);
            Ui.L("  源目录：" + src, ConsoleColor.Gray);
            Ui.L("  目标：" + dst, ConsoleColor.Gray);
            Ui.L("  待迁移：" + Tool.Sz(bytes) + "，" + Tool.Num(files) + " 个文件", ConsoleColor.Gray);
            Ui.L("  目标盘剩余：" + Tool.Sz(free), free > bytes * 1.1 ? ConsoleColor.Green : ConsoleColor.Red);

            if (free >= 0 && free < (long)(bytes * 1.1))
            {
                Tool.PauseExit("目标盘空间不足（需要源大小的 1.1 倍），已停止。", ConsoleColor.Red);
                return;
            }
            if (longPaths > 0)
                Ui.L("  提示：有 " + longPaths + " 个文件路径超过 240 字符，robocopy 一般能处理，但个别软件可能读不到。", ConsoleColor.Yellow);

            Ui.L("");
            Ui.L("  迁移方式：robocopy 复制（保留时间戳/属性），完成后自动校验；", ConsoleColor.Gray);
            Ui.L("  源目录保持原样，确认无误后再由你手动删除。", ConsoleColor.Gray);

            if (!Tool.Confirm("即将开始复制 " + Tool.Sz(bytes) + " 到 " + dst))
            {
                Tool.PauseExit("已取消，什么都没有做。", ConsoleColor.Gray);
                return;
            }

            Tool.Log("迁移： " + src + "  →  " + dst);
            Tool.Log("预检： " + files + " 个文件 / " + bytes + " 字节；目标剩余 " + free);
            string logFile = Path.Combine(Tool.EnsureBackup("迁移日志"), "robocopy_" + DateTime.Now.ToString("HHmmss") + ".log");

            Ui.L("");
            Ui.L("  正在复制，请勿关闭……", ConsoleColor.Yellow);
            string args = "\"" + src + "\" \"" + dst + "\" /E /COPY:DAT /DCOPY:T /R:1 /W:1 /MT:8 /XJ /NFL /NDL /NP /LOG:\"" + logFile + "\"";
            string outp;
            int rc = Tool.Run("robocopy.exe", args, out outp, 7200000);
            Ui.L("  robocopy 退出码：" + rc + "（0-7 为正常）", rc <= 7 ? ConsoleColor.Green : ConsoleColor.Red);
            if (rc > 7) Ui.L("  " + Fmt.Cut(Fmt.Clean(outp), 300), ConsoleColor.DarkGray);

            Ui.L("");
            Ui.L("  正在校验……", ConsoleColor.DarkGray);
            long dfiles, dbytes;
            Compare(src, dst, out dfiles, out dbytes, null, 0);
            Ui.L("  源：" + Tool.Num(files) + " 个 / " + Tool.Sz(bytes), ConsoleColor.Gray);
            Ui.L("  目标：" + Tool.Num(dfiles) + " 个 / " + Tool.Sz(dbytes), ConsoleColor.Gray);

            bool ok = dfiles == files;
            if (ok) Ui.L("  文件数一致，迁移完成。源目录未删除。", ConsoleColor.Green);
            else Ui.L("  文件数不一致，请看校验明细（[2] 迁移校验）。", ConsoleColor.Red);

            Tool.Log("结果：源 " + files + " 个，目标 " + dfiles + " 个；一致=" + ok);
            Tool.Log("robocopy 日志：" + logFile);
            Tool.Log("注意：源目录未删除，请人工确认后再处理。");
            string p = Tool.SaveLog("迁移_" + leaf, "源：" + src + "  目标：" + dst);
            if (p.Length > 0) Ui.L("  操作记录：" + p, ConsoleColor.Cyan);
            Tool.PauseExit("", ConsoleColor.Gray);
        }

        /// 比对目录：文件数/大小；最多收集 20 条差异
        public static void Compare(string src, string dst, out long dfiles, out long dbytes, List<string> diffs, int maxDiff)
        {
            long n = 0, b = 0;
            Dictionary<string, long> map = new Dictionary<string, long>();
            Fs.Walk(src, delegate (string f)
            {
                try
                {
                    string rel = f.Substring(src.Length).TrimStart('\\');
                    map[rel.ToLower()] = new FileInfo(f).Length;
                }
                catch (Exception) { }
            });
            Fs.Walk(dst, delegate (string f)
            {
                try
                {
                    n++;
                    FileInfo fi = new FileInfo(f);
                    b += fi.Length;
                    string rel = f.Substring(dst.Length).TrimStart('\\').ToLower();
                    if (diffs != null)
                    {
                        if (!map.ContainsKey(rel)) { if (diffs.Count < maxDiff) diffs.Add("多出：" + rel); }
                        else if (map[rel] != fi.Length) { if (diffs.Count < maxDiff) diffs.Add("大小不一致：" + rel + " " + map[rel] + " → " + fi.Length); }
                    }
                }
                catch (Exception) { }
            });
            if (diffs != null)
            {
                foreach (KeyValuePair<string, long> kv in map)
                {
                    if (diffs.Count >= maxDiff) break;
                    string full = Path.Combine(dst, kv.Key);
                    if (!File.Exists(full)) diffs.Add("缺失：" + kv.Key);
                }
            }
            dfiles = n; dbytes = b;
        }
    }

    // ============================================================
    // 2. 迁移校验
    // ============================================================
    internal static class ModuleVerify
    {
        public static void Run()
        {
            Tool.ResetLog();
            Tool.Head("迁移校验（对比源目录和目标目录）");
            string src = Tool.Ask("源目录", "");
            string dst = Tool.Ask("目标目录", "");
            if (!Directory.Exists(src) || !Directory.Exists(dst))
            {
                Tool.PauseExit("源或目标目录不存在。", ConsoleColor.Red);
                return;
            }
            Ui.L("");
            Ui.L("  对比中……", ConsoleColor.DarkGray);
            long sf, sb, df, db;
            int lp;
            Fs.Measure(src, out sf, out sb, out lp);
            List<string> diffs = new List<string>();
            ModuleMigrate.Compare(src, dst, out df, out db, diffs, 20);

            Ui.L("  源：" + Tool.Num(sf) + " 个 / " + Tool.Sz(sb), ConsoleColor.Gray);
            Ui.L("  目标：" + Tool.Num(df) + " 个 / " + Tool.Sz(db), ConsoleColor.Gray);
            bool ok = sf == df && diffs.Count == 0;
            Ui.L(ok ? "  一致" : "  有差异：", ok ? ConsoleColor.Green : ConsoleColor.Red);
            foreach (string d in diffs) Ui.L("    · " + d, ConsoleColor.Yellow);

            Tool.Log("校验 " + src + " → " + dst + "：源 " + sf + "/" + sb + "，目标 " + df + "/" + db);
            foreach (string d in diffs) Tool.Log("  差异：" + d);
            string p = Tool.SaveLog("迁移校验", "");
            if (p.Length > 0) Ui.L("  报告：" + p, ConsoleColor.Cyan);
            Tool.PauseExit("", ConsoleColor.Gray);
        }
    }

    // ============================================================
    // 3. 启动项管理
    // ============================================================
    internal static class ModuleStartup
    {
        private class Item
        {
            public string Where = "";      // HKLM/HKCU/启动文件夹
            public string Key = "";        // 注册表路径
            public string Name = "";
            public string Cmd = "";
            public bool Disabled = false;
            public bool FromFolder = false;
            public string File = "";
        }

        private static readonly string[] RunPaths = new string[] {
            @"Software\Microsoft\Windows\CurrentVersion\Run",
            @"Software\Microsoft\Windows\CurrentVersion\RunOnce",
            @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run"
        };

        public static void Run()
        {
            Tool.ResetLog();
            Tool.Head("启动项管理（禁用 = 挪到 _66Disabled 键；可随时启用回来）");

            while (true)
            {
                List<Item> list = Load();
                Ui.Blank();
                Ui.L("      状态  名称                        位置            命令", ConsoleColor.DarkGray);
                Ui.L("      " + new string('-', 92), ConsoleColor.DarkGray);
                for (int i = 0; i < list.Count; i++)
                {
                    Item it = list[i];
                    Ui.C("  [" + Ui.PadLeft((i + 1).ToString(), 2) + "] ", ConsoleColor.Yellow);
                    Ui.C(it.Disabled ? "已禁用 " : "启用中 ", it.Disabled ? ConsoleColor.DarkGray : ConsoleColor.Green);
                    Ui.C(Ui.Pad(Fmt.Cut(it.Name, 26), 28), ConsoleColor.Gray);
                    Ui.C(Ui.Pad(it.Where, 12), ConsoleColor.DarkGray);
                    Ui.L(Fmt.Cut(Fmt.Clean(it.Cmd), 46), ConsoleColor.DarkGray);
                }
                Ui.Blank();
                Ui.L("  用法：编号 + 动作，例如  3 d（禁用）  3 e（启用）  3 x（删除）  q 返回", ConsoleColor.Cyan);
                string line = Tool.Ask("请输入", "q").Trim().ToLower();
                if (line == "q" || line == "") return;

                string[] part = line.Split(' ');
                int no;
                if (part.Length < 2 || !int.TryParse(part[0], out no) || no < 1 || no > list.Count)
                {
                    Ui.Err("格式不对，应为：编号 空格 动作（d/e/x）");
                    Ui.Pause(800);
                    continue;
                }
                string act = part[1];
                Item target = list[no - 1];

                if (act == "d")
                {
                    if (target.Disabled) { Ui.Err("这项已经是禁用状态。"); Ui.Pause(800); continue; }
                    if (!Tool.Confirm("禁用启动项：" + target.Name)) { Ui.L("  已取消。", ConsoleColor.Gray); Tool.Pause(800); continue; }
                    string err = Disable(target);
                    Ui.L(err.Length == 0 ? "  已禁用：" + target.Name : "  失败：" + err, err.Length == 0 ? ConsoleColor.Green : ConsoleColor.Red);
                    Tool.Log("禁用启动项：" + target.Name + "  [" + target.Where + "]");
                }
                else if (act == "e")
                {
                    if (!target.Disabled) { Ui.Err("这项是启用状态，不需要启用。"); Ui.Pause(800); continue; }
                    if (!Tool.Confirm("重新启用：" + target.Name)) { Ui.L("  已取消。", ConsoleColor.Gray); Tool.Pause(800); continue; }
                    string err = Enable(target);
                    Ui.L(err.Length == 0 ? "  已启用：" + target.Name : "  失败：" + err, err.Length == 0 ? ConsoleColor.Green : ConsoleColor.Red);
                    Tool.Log("启用启动项：" + target.Name);
                }
                else if (act == "x")
                {
                    if (!Tool.Confirm("删除启动项（会先导出 .reg 备份）：" + target.Name)) { Ui.L("  已取消。", ConsoleColor.Gray); Tool.Pause(800); continue; }
                    string err = Remove(target);
                    Ui.L(err.Length == 0 ? "  已删除：" + target.Name : "  失败：" + err, err.Length == 0 ? ConsoleColor.Green : ConsoleColor.Red);
                    Tool.Log("删除启动项：" + target.Name);
                }
                else
                {
                    Ui.Err("动作只有 d / e / x");
                    Ui.Pause(800);
                }
            }
        }

        private static List<Item> Load()
        {
            List<Item> list = new List<Item>();
            RegistryHive[] hives = new RegistryHive[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser };
            foreach (RegistryHive h in hives)
            {
                foreach (string p in RunPaths)
                {
                    Read(h, p, false, list);
                    Read(h, p + "_66Disabled", true, list);
                }
            }
            // 启动文件夹
            string[] folders = new string[] {
                Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup)
            };
            foreach (string f in folders)
            {
                if (string.IsNullOrEmpty(f) || !Directory.Exists(f)) continue;
                try
                {
                    foreach (string lnk in Directory.GetFiles(f, "*.lnk"))
                    {
                        Item it = new Item();
                        it.Where = "启动文件夹";
                        it.Name = Path.GetFileNameWithoutExtension(lnk);
                        it.Cmd = lnk;
                        it.FromFolder = true;
                        it.File = lnk;
                        list.Add(it);
                    }
                }
                catch (Exception) { }
            }
            // 被本工具禁用的快捷方式
            string disDir = Path.Combine(Tool.LogDir, "已禁用启动项");
            if (Directory.Exists(disDir))
            {
                try
                {
                    foreach (string lnk in Directory.GetFiles(disDir, "*.lnk"))
                    {
                        Item it = new Item();
                        it.Where = "启动文件夹";
                        it.Name = "（已禁用）" + Path.GetFileNameWithoutExtension(lnk);
                        it.Cmd = lnk;
                        it.FromFolder = true;
                        it.File = lnk;
                        it.Disabled = true;
                        list.Add(it);
                    }
                }
                catch (Exception) { }
            }
            return list;
        }

        private static void Read(RegistryHive hive, string path, bool disabled, List<Item> list)
        {
            try
            {
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64))
                using (RegistryKey k = baseKey.OpenSubKey(path, false))
                {
                    if (k == null) return;
                    foreach (string n in k.GetValueNames())
                    {
                        Item it = new Item();
                        it.Where = (hive == RegistryHive.LocalMachine ? "HKLM" : "HKCU");
                        it.Key = path;
                        it.Name = n;
                        it.Cmd = Convert.ToString(k.GetValue(n));
                        it.Disabled = disabled;
                        it.FromFolder = false;
                        list.Add(it);
                    }
                }
            }
            catch (Exception) { }
        }

        private static string Disable(Item it)
        {
            if (it.FromFolder) return MoveLnk(it, true);
            try
            {
                ExportKey(it);
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey k = baseKey.OpenSubKey(it.Key, true))
                {
                    if (k == null) return "读不到注册表键";
                    object v = k.GetValue(it.Name);
                    using (RegistryKey dk = baseKey.CreateSubKey(it.Key + "_66Disabled"))
                        dk.SetValue(it.Name, v);
                    k.DeleteValue(it.Name, false);
                }
                return "";
            }
            catch (Exception ex) { return Fmt.Cut(Fmt.Clean(ex.Message), 60); }
        }

        private static string Enable(Item it)
        {
            if (it.FromFolder) return MoveLnk(it, false);
            try
            {
                string hk = it.Key;
                int cut = hk.IndexOf("_66Disabled", StringComparison.OrdinalIgnoreCase);
                if (cut < 0) return "这不是本工具禁用的项";
                string src = hk;
                string dst = hk.Remove(cut, "_66Disabled".Length);
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                {
                    using (RegistryKey sk = baseKey.OpenSubKey(src, true))
                    {
                        if (sk == null) return "找不到禁用区";
                        object v = sk.GetValue(it.Name);
                        using (RegistryKey dk = baseKey.CreateSubKey(dst))
                            dk.SetValue(it.Name, v);
                        sk.DeleteValue(it.Name, false);
                    }
                }
                return "";
            }
            catch (Exception ex) { return Fmt.Cut(Fmt.Clean(ex.Message), 60); }
        }

        private static string Remove(Item it)
        {
            if (it.FromFolder)
            {
                try
                {
                    string dir = Tool.EnsureBackup("启动项快捷方式");
                    File.Move(it.File, Path.Combine(dir, Path.GetFileName(it.File)));
                    return "";
                }
                catch (Exception ex) { return Fmt.Cut(Fmt.Clean(ex.Message), 60); }
            }
            try
            {
                ExportKey(it);
                string key = it.Key;
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey k = baseKey.OpenSubKey(key, true))
                {
                    if (k == null) return "读不到注册表键";
                    k.DeleteValue(it.Name, false);
                }
                return "";
            }
            catch (Exception ex) { return Fmt.Cut(Fmt.Clean(ex.Message), 60); }
        }

        private static void ExportKey(Item it)
        {
            try
            {
                string hive = it.Where == "HKLM" ? "HKLM" : "HKCU";
                string full = hive + "\\" + it.Key;
                string dir = Tool.EnsureBackup("注册表");
                string file = Path.Combine(dir, DateTime.Now.ToString("HHmmss") + "_" + Tool.SafeName(it.Key) + ".reg");
                string o;
                Tool.Run("reg.exe", "export \"" + full + "\" \"" + file + "\" /y", out o, 5000);
                if (File.Exists(file)) Tool.Log("  备份注册表：" + file);
            }
            catch (Exception) { }
        }

        private static string MoveLnk(Item it, bool toDisabled)
        {
            try
            {
                if (toDisabled)
                {
                    string d = Path.Combine(Tool.LogDir, "已禁用启动项");
                    if (!Directory.Exists(d)) Directory.CreateDirectory(d);
                    string target = Path.Combine(d, Path.GetFileName(it.File));
                    File.Move(it.File, target);
                    File.WriteAllText(target + ".原路径.txt", it.File, Encoding.UTF8);
                    return "";
                }
                string side = it.File + ".原路径.txt";
                if (!File.Exists(side)) return "缺少还原信息";
                string orig = File.ReadAllText(side, Encoding.UTF8).Trim();
                string dir = Path.GetDirectoryName(orig);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.Move(it.File, orig);
                try { File.Delete(side); } catch (Exception) { }
                return "";
            }
            catch (Exception ex) { return Fmt.Cut(Fmt.Clean(ex.Message), 60); }
        }
    }

    // ============================================================
    // 4. 目录修复
    // ============================================================
    internal static class ModuleFolders
    {
        public static void Run()
        {
            Tool.ResetLog();
            Tool.Head("目录修复（把桌面/文档/图片改回默认位置）");
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string key = @"Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders";
            string[] names = new string[] { "Desktop", "Personal", "My Pictures", "My Video" };
            string[] cn = new string[] { "桌面", "文档", "图片", "视频" };
            string[] defs = new string[] { "Desktop", "Documents", "Pictures", "Videos" };

            List<string> bad = new List<string>();
            Ui.Blank();
            Ui.L("      项目  当前路径                                          默认路径", ConsoleColor.DarkGray);
            Ui.L("      " + new string('-', 96), ConsoleColor.DarkGray);
            for (int i = 0; i < names.Length; i++)
            {
                string cur = RegValue(RegistryHive.CurrentUser, key, names[i]);
                string def = Path.Combine(profile, defs[i]);
                bool same = string.Equals((cur ?? "").TrimEnd('\\'), def.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
                Ui.C("  ", ConsoleColor.Gray);
                Ui.C(Ui.Pad(cn[i], 6), ConsoleColor.Gray);
                Ui.C(Ui.Pad(Fmt.Cut(cur.Length > 0 ? cur : "（空）", 46), 48), same ? ConsoleColor.Gray : ConsoleColor.Yellow);
                Ui.L(Fmt.Cut(def, 46), ConsoleColor.DarkGray);
                if (!same) bad.Add(names[i] + "|" + cn[i] + "|" + cur + "|" + def);
            }

            if (bad.Count == 0)
            {
                Tool.PauseExit("四项都在默认位置，不需要修复。", ConsoleColor.Green);
                return;
            }

            Ui.Blank();
            Ui.L("  发现 " + bad.Count + " 项不在默认位置。改成默认后，旧位置的文件不会自动搬回来。", ConsoleColor.Yellow);
            if (!Tool.Confirm("是否把这 " + bad.Count + " 项改回默认位置（会先导出注册表备份）"))
            {
                Tool.PauseExit("已取消。", ConsoleColor.Gray);
                return;
            }

            string dir = Tool.EnsureBackup("注册表");
            string reg = Path.Combine(dir, "UserShellFolders.reg");
            string o;
            Tool.Run("reg.exe", "export \"HKCU\\" + key + "\" \"" + reg + "\" /y", out o, 8000);
            Tool.Log("备份：" + reg);

            int ok = 0;
            for (int i = 0; i < names.Length; i++)
            {
                string cur = RegValue(RegistryHive.CurrentUser, key, names[i]);
                string def = Path.Combine(profile, defs[i]);
                if (string.Equals((cur ?? "").TrimEnd('\\'), def.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    using (RegistryKey k = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64).OpenSubKey(key, true))
                    {
                        if (k == null) continue;
                        string expand = "%USERPROFILE%\\" + defs[i];
                        k.SetValue(names[i], expand, RegistryValueKind.ExpandString);
                        ok++;
                        Tool.Log("改回默认：" + cn[i] + "  " + cur + "  →  " + expand);
                    }
                }
                catch (Exception ex) { Ui.Err(cn[i] + " 修改失败：" + ex.Message); }
            }
            Ui.L("  已改回 " + ok + " 项。", ConsoleColor.Green);
            Ui.L("  生效方式：注销一次或重启资源管理器；旧目录里的文件可以手工搬回，", ConsoleColor.Gray);
            Ui.L("  也可以用 [1] 数据迁移 把旧目录复制到新默认位置（源保留）。", ConsoleColor.Gray);

            string p = Tool.SaveLog("目录修复", "用户：" + profile);
            if (p.Length > 0) Ui.L("  记录：" + p, ConsoleColor.Cyan);
            Tool.PauseExit("", ConsoleColor.Gray);
        }

        private static string RegValue(RegistryHive hive, string path, string name)
        {
            try
            {
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64))
                using (RegistryKey k = baseKey.OpenSubKey(path, false))
                {
                    if (k == null) return "";
                    object v = k.GetValue(name);
                    if (v == null) return "";
                    string s = Convert.ToString(v);
                    return Environment.ExpandEnvironmentVariables(s);
                }
            }
            catch (Exception) { return ""; }
        }
    }

    // ============================================================
    // 5. 重复文件扫描
    // ============================================================
    internal static class ModuleDup
    {
        public static void Run()
        {
            Tool.ResetLog();
            Tool.Head("重复文件扫描（按内容哈希比对，只报告不删除）");
            string def = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string root = Tool.Ask("要扫描的目录", def);
            if (!Directory.Exists(root))
            {
                Tool.PauseExit("目录不存在。", ConsoleColor.Red);
                return;
            }
            long minMB;
            string s = Tool.Ask("最小文件大小（MB，小于这个不扫）", "1");
            if (!long.TryParse(s, out minMB) || minMB < 0) minMB = 1;

            Ui.L("");
            Ui.L("  正在枚举……", ConsoleColor.DarkGray);
            Dictionary<long, List<string>> bySize = new Dictionary<long, List<string>>();
            long total = 0;
            Fs.Walk(root, delegate (string f)
            {
                try
                {
                    FileInfo fi = new FileInfo(f);
                    if (fi.Length < minMB * 1024 * 1024) return;
                    if (fi.Length > 500L * 1024 * 1024) return;
                    total++;
                    if (!bySize.ContainsKey(fi.Length)) bySize[fi.Length] = new List<string>();
                    bySize[fi.Length].Add(f);
                }
                catch (Exception) { }
            });

            List<List<string>> groups = new List<List<string>>();
            long wasted = 0;
            int hashed = 0;
            Ui.L("  按大小分组后需要哈希的文件：" + total + " 个，正在计算……", ConsoleColor.DarkGray);
            foreach (KeyValuePair<long, List<string>> kv in bySize)
            {
                if (kv.Value.Count < 2) continue;
                Dictionary<string, List<string>> byHash = new Dictionary<string, List<string>>();
                foreach (string f in kv.Value)
                {
                    if (hashed > 4000) break;
                    hashed++;
                    try
                    {
                        string h = Hash(f);
                        if (h == null) continue;
                        if (!byHash.ContainsKey(h)) byHash[h] = new List<string>();
                        byHash[h].Add(f);
                    }
                    catch (Exception) { }
                }
                foreach (KeyValuePair<string, List<string>> g in byHash)
                {
                    if (g.Value.Count < 2) continue;
                    groups.Add(g.Value);
                    wasted += (g.Value.Count - 1) * kv.Key;
                }
            }

            groups.Sort(delegate (List<string> a, List<string> b) { return b.Count.CompareTo(a.Count); });
            Ui.Blank();
            Ui.C("  重复组：", ConsoleColor.DarkGray);
            Ui.C(groups.Count + " 组", ConsoleColor.Yellow);
            Ui.C("    可省空间：", ConsoleColor.DarkGray);
            Ui.L(Tool.Sz(wasted), ConsoleColor.Green);
            Ui.Blank();
            for (int i = 0; i < groups.Count && i < 15; i++)
            {
                List<string> g = groups[i];
                long sz = 0;
                try { sz = new FileInfo(g[0]).Length; } catch (Exception) { }
                Ui.L("  [" + (i + 1) + "] " + Tool.Sz(sz) + " × " + g.Count + " 份", ConsoleColor.Yellow);
                for (int j = 0; j < g.Count && j < 4; j++)
                    Ui.L("      " + Fmt.Cut(g[j], 82), ConsoleColor.DarkGray);
                if (g.Count > 4) Ui.L("      …… 还有 " + (g.Count - 4) + " 份", ConsoleColor.DarkGray);
            }
            if (groups.Count > 15) Ui.L("  …… 另有 " + (groups.Count - 15) + " 组，见记录文件", ConsoleColor.DarkGray);

            foreach (List<string> g in groups)
            {
                Tool.Log("重复组（" + g.Count + " 份）：");
                foreach (string f in g) Tool.Log("  " + f);
            }
            string p = Tool.SaveLog("重复文件扫描", "目录：" + root + "  最小 " + minMB + "MB");
            if (p.Length > 0) Ui.L("  记录：" + p, ConsoleColor.Cyan);
            Ui.L("  本功能只报告，不删除任何文件。", ConsoleColor.DarkGray);
            Tool.PauseExit("", ConsoleColor.Gray);
        }

        private static string Hash(string path)
        {
            try
            {
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536))
                using (SHA256 sha = SHA256.Create())
                {
                    byte[] h = sha.ComputeHash(fs);
                    StringBuilder sb = new StringBuilder();
                    foreach (byte b in h) sb.Append(b.ToString("x2"));
                    return sb.ToString();
                }
            }
            catch (Exception) { return null; }
        }
    }

    // ============================================================
    // 6. 乱码名清理
    // ============================================================
    internal static class ModuleMojibake
    {
        private class Fix
        {
            public string Full = "";
            public string Old = "";
            public string New = "";
            public bool Sure = false;   // 高置信
        }

        public static void Run()
        {
            Tool.ResetLog();
            Tool.Head("乱码名清理（修复编码错乱的文件名，先预览再改）");
            string def = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string root = Tool.Ask("要扫描的目录", def);
            if (!Directory.Exists(root))
            {
                Tool.PauseExit("目录不存在。", ConsoleColor.Red);
                return;
            }

            List<Fix> fixes = new List<Fix>();
            Ui.L("");
            Ui.L("  正在扫描文件名……", ConsoleColor.DarkGray);
            Collect(root, fixes);

            if (fixes.Count == 0)
            {
                Tool.PauseExit("没有发现可修复的乱码文件名。", ConsoleColor.Green);
                return;
            }
            int sure = 0;
            foreach (Fix f in fixes) if (f.Sure) sure++;

            Ui.Blank();
            Ui.L("  发现 " + fixes.Count + " 个疑似乱码名（其中高置信 " + sure + " 个）：", ConsoleColor.Yellow);
            for (int i = 0; i < fixes.Count && i < 25; i++)
            {
                Ui.C("  " + (fixes[i].Sure ? "● " : "○ "), fixes[i].Sure ? ConsoleColor.Green : ConsoleColor.DarkYellow);
                Ui.C(Fmt.Cut(fixes[i].Old, 34), ConsoleColor.Gray);
                Ui.C("  →  ", ConsoleColor.DarkGray);
                Ui.L(Fmt.Cut(fixes[i].New, 34), ConsoleColor.Cyan);
            }
            if (fixes.Count > 25) Ui.L("  …… 另有 " + (fixes.Count - 25) + " 个", ConsoleColor.DarkGray);
            Ui.L("  ● = 高置信（基本可以确定）  ○ = 低置信（可能改错，建议先看预览）", ConsoleColor.DarkGray);

            Ui.Blank();
            Ui.L("  输入 1 只改高置信；输入 2 全部改；直接回车不改。", ConsoleColor.Cyan);
            string c = Tool.ReadLine().Trim();
            if (c != "1" && c != "2")
            {
                Tool.PauseExit("已取消，文件名没有任何改动。", ConsoleColor.Gray);
                return;
            }
            bool onlySure = c == "1";
            if (!Tool.Confirm(onlySure ? "只重命名高置信项的 " + sure + " 个文件" : "重命名全部 " + fixes.Count + " 个文件"))
            {
                Tool.PauseExit("已取消。", ConsoleColor.Gray);
                return;
            }

            int done = 0, skip = 0;
            foreach (Fix f in fixes)
            {
                if (onlySure && !f.Sure) continue;
                try
                {
                    if (!Tool.InRoot(f.Full, root)) { skip++; continue; }
                    string dir = Path.GetDirectoryName(f.Full);
                    string target = Path.Combine(dir, f.New);
                    if (File.Exists(target) || Directory.Exists(target)) { skip++; Tool.Log("跳过（目标已存在）：" + f.Full); continue; }
                    Tool.Log(f.Old + "  →  " + f.New);
                    if (Directory.Exists(f.Full)) Directory.Move(f.Full, target);
                    else File.Move(f.Full, target);
                    done++;
                }
                catch (Exception ex)
                {
                    skip++;
                    Tool.Log("失败：" + f.Full + "  " + Fmt.Clean(ex.Message));
                }
            }
            Ui.L("  已重命名 " + done + " 个，跳过 " + skip + " 个。", ConsoleColor.Green);
            string p = Tool.SaveLog("乱码名清理", "目录：" + root);
            if (p.Length > 0) Ui.L("  记录：" + p, ConsoleColor.Cyan);
            Tool.PauseExit("", ConsoleColor.Gray);
        }

        private static void Collect(string root, List<Fix> fixes)
        {
            // 先收集所有文件和目录（深度最后改，避免路径变化）
            List<string> all = new List<string>();
            Fs.Walk(root, delegate (string f) { all.Add(f); });
            Stack<string> st = new Stack<string>();
            st.Push(root);
            while (st.Count > 0)
            {
                string d = st.Pop();
                string[] ds;
                try { ds = Directory.GetDirectories(d); } catch (Exception) { continue; }
                foreach (string x in ds)
                {
                    try
                    {
                        if ((File.GetAttributes(x) & FileAttributes.ReparsePoint) != 0) continue;
                    }
                    catch (Exception) { continue; }
                    all.Add(x);
                    st.Push(x);
                }
            }
            foreach (string full in all)
            {
                string name = Path.GetFileName(full);
                if (string.IsNullOrEmpty(name)) continue;
                string fixedName; bool sure;
                if (!TryFix(name, out fixedName, out sure)) continue;
                Fix f = new Fix();
                f.Full = full;
                f.Old = name;
                f.New = fixedName;
                f.Sure = sure;
                fixes.Add(f);
                if (fixes.Count > 2000) break;
            }
        }

        /// 两种常见编码错乱：UTF-8 被当 Latin-1 解；GBK 与 UTF-8 互相解错
        private static bool TryFix(string name, out string result, out bool sure)
        {
            result = ""; sure = false;

            // 情形一：大量 0x80-0xFF 的西欧字符（"æ–‡ä»¶" 这种）
            int latin = 0;
            foreach (char c in name) if (c >= 0x80 && c <= 0xFF) latin++;
            if (latin >= name.Length * 0.5 && latin >= 2)
            {
                // 1252 优先（西欧码页把 UTF-8 字节解成 æ–‡ä»¶ 这类），再退回 28591
                string t = DecodeChain(name, 1252, new UTF8Encoding(false, true));
                if (t == null || !HasCjk(t)) t = DecodeChain(name, 28591, new UTF8Encoding(false, true));
                if (t != null && t != name && HasCjk(t))
                {
                    result = t; sure = true; return true;
                }
            }

            // 情形二：UTF-8 字节被按 GBK 解（"鏂囦欢" 这种），低置信
            string g = DecodeChain(name, 65001, Encoding.GetEncoding(936));
            if (g != null && g != name && HasCjk(g) && !HasCjk(name))
            {
                result = g; sure = false; return true;
            }
            return false;
        }

        private static string DecodeChain(string s, int fromCp, Encoding to)
        {
            try
            {
                Encoding from = fromCp == 65001 ? new UTF8Encoding(false, true) : Encoding.GetEncoding(fromCp);
                byte[] b = from.GetBytes(s);
                string r = to.GetString(b);
                if (r.IndexOf('\uFFFD') >= 0) return null;
                if (r.IndexOf('\0') >= 0) return null;
                foreach (char c in r)
                    if (Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0) return null;
                return r.Trim();
            }
            catch (Exception) { return null; }
        }

        private static bool HasCjk(string s)
        {
            int n = 0;
            foreach (char c in s) if (c >= 0x4E00 && c <= 0x9FFF) n++;
            return n > 0;
        }
    }

    // ============================================================
    // 7. 卸载建议
    // ============================================================
    internal static class ModuleUninstall
    {
        public static void Run()
        {
            Tool.ResetLog();
            Tool.Head("卸载建议（只给建议，不执行卸载）");
            List<string[]> sw = Installed();
            Ui.L("  已装软件 " + sw.Count + " 个，正在评估……", ConsoleColor.DarkGray);

            List<string[]> sug = new List<string[]>();
            string[] kw = new string[] {
                "360", "腾讯电脑管家", "QQ电脑管家", "金山毒霸", "2345", "鲁大师",
                "驱动精灵", "驱动人生", "快压", "好压", "百度卫士", "百度杀毒", "瑞星",
                "搜狗浏览器", "猎豹浏览器", "优化大师", "桌面整理", "智能云输入法"
            };
            foreach (string[] s in sw)
            {
                string name = s[0];
                foreach (string k in kw)
                {
                    if (name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        sug.Add(new string[] { name, "装机捆绑/助手类，功能与系统自带重复，卸载不影响系统" });
                        break;
                    }
                }
            }

            // 同类重复
            string[][] families = new string[][] {
                new string[] { "压缩", "WinRAR", "7-Zip", "好压", "快压", "Bandizip", "360压缩" },
                new string[] { "看图", "2345看图", "光影看图", "ACDSee", "美图看看" },
                new string[] { "浏览器", "360浏览器", "QQ浏览器", "搜狗浏览器", "猎豹浏览器", "2345加速浏览器" }
            };
            foreach (string[] fam in families)
            {
                List<string> hit = new List<string>();
                foreach (string[] s in sw)
                    foreach (string k in fam)
                        if (k.Length > 0 && s[0].IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) { if (!hit.Contains(s[0])) hit.Add(s[0]); break; }
                if (hit.Count >= 2)
                    sug.Add(new string[] { string.Join(" / ", hit.ToArray()), fam[0] + "类装了 " + hit.Count + " 个，留一个常用的就够" });
            }

            Ui.Blank();
            if (sug.Count == 0)
            {
                Tool.PauseExit("没有明显建议卸载的软件。", ConsoleColor.Green);
                return;
            }
            Ui.L("  建议关注 " + sug.Count + " 项：", ConsoleColor.Yellow);
            foreach (string[] s in sug)
            {
                Ui.C("  · ", ConsoleColor.DarkYellow);
                Ui.C(Ui.Pad(Fmt.Cut(s[0], 40), 42), ConsoleColor.Gray);
                Ui.L(Fmt.Cut(s[1], 40), ConsoleColor.DarkGray);
                Tool.Log("建议：" + s[0] + "  —— " + s[1]);
            }
            Ui.L("");
            Ui.L("  卸载入口：控制面板 → 程序和功能（或设置 → 应用）。", ConsoleColor.DarkGray);
            Ui.L("  卸载前建议先用 [1] 数据迁移 备份对应软件的配置/数据目录。", ConsoleColor.DarkGray);
            string p = Tool.SaveLog("卸载建议", "");
            if (p.Length > 0) Ui.L("  记录：" + p, ConsoleColor.Cyan);
            Tool.PauseExit("", ConsoleColor.Gray);
        }

        private static List<string[]> Installed()
        {
            List<string[]> list = new List<string[]>();
            Dictionary<string, bool> seen = new Dictionary<string, bool>();
            string[] roots = new string[] {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };
            RegistryHive[] hives = new RegistryHive[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser };
            foreach (RegistryHive h in hives)
            {
                foreach (string r in roots)
                {
                    try
                    {
                        using (RegistryKey baseKey = RegistryKey.OpenBaseKey(h, RegistryView.Registry64))
                        using (RegistryKey k = baseKey.OpenSubKey(r, false))
                        {
                            if (k == null) continue;
                            foreach (string sub in k.GetSubKeyNames())
                            {
                                using (RegistryKey sk = k.OpenSubKey(sub, false))
                                {
                                    if (sk == null) continue;
                                    string name = Convert.ToString(sk.GetValue("DisplayName"));
                                    if (string.IsNullOrEmpty(name)) continue;
                                    if (Convert.ToString(sk.GetValue("SystemComponent")) == "1") continue;
                                    string key = name.ToLower();
                                    if (seen.ContainsKey(key)) continue;
                                    seen[key] = true;
                                    list.Add(new string[] { name.Trim(), Convert.ToString(sk.GetValue("InstallDate")) });
                                }
                            }
                        }
                    }
                    catch (Exception) { }
                }
            }
            return list;
        }
    }

    // ============================================================
    // 8. 桌面图标整理
    // ============================================================
    internal static class ModuleTidy
    {
        public static void Run()
        {
            Tool.ResetLog();
            Tool.Head("桌面图标整理（把快捷方式归类到子文件夹，可一键还原）");
            Ui.L("  [1] 整理桌面快捷方式", ConsoleColor.Gray);
            Ui.L("  [2] 还原上次整理", ConsoleColor.Gray);
            Ui.L("  [0] 返回", ConsoleColor.Gray);
            string c = Tool.Ask("请选择", "0");
            if (c == "1") DoTidy();
            else if (c == "2") DoRestore();
        }

        private static void DoTidy()
        {
            List<string> roots = new List<string>();
            string ud = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string cd = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
            if (!string.IsNullOrEmpty(ud) && Directory.Exists(ud)) roots.Add(ud);
            if (!string.IsNullOrEmpty(cd) && Directory.Exists(cd)) roots.Add(cd);

            List<string> files = new List<string>();
            foreach (string r in roots)
            {
                try
                {
                    foreach (string f in Directory.GetFiles(r))
                    {
                        string ext = Path.GetExtension(f).ToLower();
                        if (ext == ".lnk" || ext == ".url") files.Add(f);
                    }
                }
                catch (Exception) { }
            }
            if (files.Count == 0)
            {
                Tool.PauseExit("桌面上没有快捷方式。", ConsoleColor.Green);
                return;
            }

            Dictionary<string, List<string>> plan = new Dictionary<string, List<string>>();
            foreach (string f in files)
            {
                string cat = Category(Path.GetFileNameWithoutExtension(f));
                if (!plan.ContainsKey(cat)) plan[cat] = new List<string>();
                plan[cat].Add(f);
            }

            Ui.Blank();
            Ui.L("  计划（只在用户桌面下建文件夹，系统桌面不动）：", ConsoleColor.Yellow);
            List<string> cats = new List<string>(plan.Keys);
            cats.Sort();
            foreach (string cat in cats)
                Ui.L("    " + Ui.Pad(cat, 10) + plan[cat].Count + " 个", ConsoleColor.Gray);
            Ui.L("    共 " + files.Count + " 个快捷方式", ConsoleColor.DarkGray);

            if (!Tool.Confirm("按上面的分类移动这 " + files.Count + " 个快捷方式"))
            {
                Tool.PauseExit("已取消。", ConsoleColor.Gray);
                return;
            }

            string manifest = Path.Combine(Tool.EnsureBackup("桌面整理"), "manifest.txt");
            StringBuilder sb = new StringBuilder();
            int moved = 0, skip = 0;
            // 每个来源桌面各自建子文件夹（公共桌面不动到用户桌面里去）
            foreach (string root in roots)
            {
                foreach (string cat in cats)
                {
                    List<string> inRoot = plan[cat].FindAll(delegate (string f)
                    {
                        return f.StartsWith(root, StringComparison.OrdinalIgnoreCase);
                    });
                    if (inRoot.Count == 0) continue;
                    string dir = Path.Combine(root, "快捷方式", cat);
                    try { if (!Directory.Exists(dir)) Directory.CreateDirectory(dir); }
                    catch (Exception) { skip += inRoot.Count; continue; }
                    foreach (string f in inRoot)
                    {
                        try
                        {
                            string target = Path.Combine(dir, Path.GetFileName(f));
                            int n = 2;
                            while (File.Exists(target))
                            {
                                target = Path.Combine(dir, Path.GetFileNameWithoutExtension(f) + "_" + n + Path.GetExtension(f));
                                n++;
                            }
                            File.Move(f, target);
                            sb.AppendLine(f + "\t" + target);
                            moved++;
                        }
                        catch (Exception ex) { skip++; Tool.Log("移动失败：" + f + "  " + Fmt.Clean(ex.Message)); }
                    }
                }
            }
            try { File.WriteAllText(manifest, sb.ToString(), Encoding.UTF8); }
            catch (Exception) { }

            Ui.L("  已移动 " + moved + " 个，跳过 " + skip + " 个。", ConsoleColor.Green);
            Ui.L("  还原方式：菜单 [8] → [2]，会读取 " + Path.GetFileName(manifest), ConsoleColor.DarkGray);
            string p = Tool.SaveLog("桌面整理", "源：" + roots[0]);
            if (p.Length > 0) Ui.L("  记录：" + p, ConsoleColor.Cyan);
            Tool.PauseExit("", ConsoleColor.Gray);
        }

        private static void DoRestore()
        {
            string dir = Path.Combine(Tool.LogDir, "备份_" + Tool.Stamp, "桌面整理");
            string manifest = "";
            // 找最近一次的 manifest
            try
            {
                string[] dirs = Directory.GetDirectories(Tool.LogDir, "备份_*");
                Array.Sort(dirs);
                for (int i = dirs.Length - 1; i >= 0; i--)
                {
                    string m = Path.Combine(dirs[i], "桌面整理", "manifest.txt");
                    if (File.Exists(m)) { manifest = m; break; }
                }
            }
            catch (Exception) { }
            if (manifest.Length == 0)
            {
                Tool.PauseExit("没有找到上次整理的记录。", ConsoleColor.Red);
                return;
            }
            int ok = 0, skip = 0;
            Ui.L("  使用记录：" + manifest, ConsoleColor.DarkGray);
            foreach (string line in File.ReadAllLines(manifest, Encoding.UTF8))
            {
                if (line.Trim().Length == 0) continue;
                string[] p = line.Split('\t');
                if (p.Length < 2) continue;
                try
                {
                    if (!File.Exists(p[1])) { skip++; continue; }
                    string dirName = Path.GetDirectoryName(p[0]);
                    if (!Directory.Exists(dirName)) Directory.CreateDirectory(dirName);
                    File.Move(p[1], p[0]);
                    ok++;
                }
                catch (Exception) { skip++; }
            }
            Ui.L("  已还原 " + ok + " 个，跳过 " + skip + " 个。", ConsoleColor.Green);
            Tool.PauseExit("", ConsoleColor.Gray);
        }

        private static string Category(string name)
        {
            string n = name.ToLower();
            string[] office = new string[] { "word", "excel", "ppt", "wps", "office", "pdf", "钉钉", "微信", "qq", "企业微信", "mail", "foxmail", "记事本", "便签" };
            string[] media = new string[] { "音乐", "视频", "播放", "影音", "爱奇艺", "优酷", "腾讯视频", "网易云", "qq音乐", "酷狗", "剪辑", "剪映" };
            string[] game = new string[] { "游戏", "game", "steam", "wegame", "英雄联盟", "王者" };
            string[] tool = new string[] { "工具", "tool", "压缩", "看图", "截图", "远程", "向日葵", "todesk", "teamviewer", "驱动", "清理", "杀毒", "管家" };
            foreach (string k in game) if (n.IndexOf(k) >= 0) return "游戏";
            foreach (string k in media) if (n.IndexOf(k) >= 0) return "影音";
            foreach (string k in office) if (n.IndexOf(k) >= 0) return "办公";
            foreach (string k in tool) if (n.IndexOf(k) >= 0) return "工具";
            return "其他";
        }
    }

    // ============================================================
    // 9. 个性化修复
    // ============================================================
    internal static class ModulePersonal
    {
        public static void Run()
        {
            Tool.ResetLog();
            Tool.Head("个性化修复");
            Ui.L("  [1] 重建图标/缩略图缓存（删缓存文件，之后需重启资源管理器）", ConsoleColor.Gray);
            Ui.L("  [2] 清理右键菜单第三方项", ConsoleColor.Gray);
            Ui.L("  [3] 恢复系统外壳为 explorer.exe（若被改成别的）", ConsoleColor.Gray);
            Ui.L("  [0] 返回", ConsoleColor.Gray);
            string c = Tool.Ask("请选择", "0");
            if (c == "1") IconCache();
            else if (c == "2") ContextMenu();
            else if (c == "3") ShellFix();
        }

        private static void IconCache()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string explorer = Path.Combine(local, @"Microsoft\Windows\Explorer");
            List<string> targets = new List<string>();
            try
            {
                if (Directory.Exists(explorer))
                {
                    foreach (string f in Directory.GetFiles(explorer, "iconcache_*.db")) targets.Add(f);
                    foreach (string f in Directory.GetFiles(explorer, "thumbcache_*.db")) targets.Add(f);
                }
            }
            catch (Exception) { }
            string rootIcon = Path.Combine(local, "IconCache.db");
            if (File.Exists(rootIcon)) targets.Add(rootIcon);

            if (targets.Count == 0)
            {
                Tool.PauseExit("没找到缓存文件。", ConsoleColor.Green);
                return;
            }
            long sz = 0;
            foreach (string f in targets) { try { sz += new FileInfo(f).Length; } catch (Exception) { } }
            Ui.L("  缓存文件 " + targets.Count + " 个，共 " + Tool.Sz(sz), ConsoleColor.Gray);
            if (!Tool.Confirm("删除这些缓存文件（Windows 会自动重建）"))
            {
                Tool.PauseExit("已取消。", ConsoleColor.Gray);
                return;
            }
            int ok = 0, fail = 0;
            foreach (string f in targets)
            {
                try { File.Delete(f); ok++; }
                catch (Exception) { fail++; }
            }
            Ui.L("  已删 " + ok + " 个，占用中的 " + fail + " 个（重启资源管理器后再删即可）。", ConsoleColor.Green);
            Ui.L("  重启资源管理器：任务管理器 → Windows 资源管理器 → 重新启动。", ConsoleColor.DarkGray);
            Tool.Log("图标缓存：删除 " + ok + " 个，失败 " + fail + " 个");
            Tool.SaveLog("个性化_图标缓存", "");
            Tool.PauseExit("", ConsoleColor.Gray);
        }

        private static void ContextMenu()
        {
            string[] keys = new string[] {
                @"*\shellex\ContextMenuHandlers",
                @"Directory\shellex\ContextMenuHandlers",
                @"Directory\Background\shellex\ContextMenuHandlers"
            };
            List<string> names = new List<string>();
            List<string> paths = new List<string>();   // 相对 HKCU 的键路径
            foreach (string k in keys)
            {
                string full = @"Software\Classes\" + k;
                try
                {
                    using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64))
                    using (RegistryKey rk = baseKey.OpenSubKey(full, false))
                    {
                        if (rk == null) continue;
                        foreach (string sub in rk.GetSubKeyNames())
                        {
                            if (names.Contains(sub)) continue;
                            names.Add(sub);
                            paths.Add(full + "\\" + sub);
                        }
                    }
                }
                catch (Exception) { }
            }
            if (names.Count == 0)
            {
                Tool.PauseExit("用户级右键菜单里没有第三方项。", ConsoleColor.Green);
                return;
            }
            Ui.Blank();
            for (int i = 0; i < names.Count; i++)
                Ui.L("  [" + Ui.PadLeft((i + 1).ToString(), 2) + "] " + names[i], ConsoleColor.Gray);
            Ui.L("  说明：只列 HKCU（当前用户）项，删掉不影响其他账户；会先导出 .reg。", ConsoleColor.DarkGray);
            string sel = Tool.Ask("要删除的编号（多个用逗号分隔，回车返回）", "");
            if (sel.Trim().Length == 0) return;

            string dir = Tool.EnsureBackup("注册表");
            int done = 0;
            foreach (string one in sel.Split(','))
            {
                int n;
                if (!int.TryParse(one.Trim(), out n) || n < 1 || n > names.Count) continue;
                string rel = paths[n - 1];
                try
                {
                    string file = Path.Combine(dir, "菜单_" + Tool.SafeName(names[n - 1]) + ".reg");
                    string o;
                    Tool.Run("reg.exe", "export \"HKCU\\" + rel + "\" \"" + file + "\" /y", out o, 5000);
                    if (File.Exists(file)) Tool.Log("  备份：" + file);
                    DeleteKeyTree(RegistryHive.CurrentUser, rel);
                    done++;
                    Tool.Log("删除菜单项：" + names[n - 1] + "   (" + rel + ")");
                }
                catch (Exception ex) { Ui.Err("删除失败：" + Fmt.Cut(Fmt.Clean(ex.Message), 40)); }
            }
            Ui.L("  已删除 " + done + " 项，备份在 " + dir, ConsoleColor.Green);
            Tool.SaveLog("个性化_右键菜单", "");
            Tool.PauseExit("", ConsoleColor.Gray);
        }

        private static void DeleteKeyTree(RegistryHive hive, string path)
        {
            using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64))
                baseKey.DeleteSubKeyTree(path, false);
        }

        private static void ShellFix()
        {
            string key = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon";
            string cur = "";
            try
            {
                using (RegistryKey k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(key, false))
                {
                    if (k != null) cur = Convert.ToString(k.GetValue("Shell"));
                }
            }
            catch (Exception) { }
            Ui.L("  当前 Shell = " + (cur.Length > 0 ? cur : "（未设置，默认 explorer.exe）"), ConsoleColor.Gray);
            if (cur.Length == 0 || cur.ToLower().IndexOf("explorer.exe", StringComparison.Ordinal) >= 0)
            {
                Tool.PauseExit("外壳正常，不需要修复。", ConsoleColor.Green);
                return;
            }
            if (!Tool.Confirm("把 Shell 改回 explorer.exe"))
            {
                Tool.PauseExit("已取消。", ConsoleColor.Gray);
                return;
            }
            try
            {
                string dir = Tool.EnsureBackup("注册表");
                string file = Path.Combine(dir, "Winlogon_Shell.reg");
                string o;
                Tool.Run("reg.exe", "export \"HKLM\\" + key + "\" \"" + file + "\" /y", out o, 8000);
                using (RegistryKey k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(key, true))
                    k.SetValue("Shell", "explorer.exe", RegistryValueKind.String);
                Ui.L("  已改回 explorer.exe，重启后生效。", ConsoleColor.Green);
                Tool.Log("Shell：" + cur + " → explorer.exe");
                Tool.SaveLog("个性化_外壳", "");
            }
            catch (Exception ex) { Ui.Err("修改失败：" + ex.Message); }
            Tool.PauseExit("", ConsoleColor.Gray);
        }
    }

    // ============================================================
    // 10. 环境自检
    // ============================================================
    internal static class ModuleEnv
    {
        public static void Run()
        {
            Tool.ResetLog();
            Tool.Head("环境自检（只读）");
            Ui.L("  操作系统：" + Environment.OSVersion.VersionString + "（" + (Environment.Is64BitOperatingSystem ? "64 位" : "32 位") + "）", ConsoleColor.Gray);
            Ui.L("  .NET 运行时：" + Environment.Version, ConsoleColor.Gray);
            Ui.L("  管理员权限：" + (Tool.IsAdmin() ? "有" : "没有（66专业检测/迁移的部分功能用不了）"), Tool.IsAdmin() ? ConsoleColor.Green : ConsoleColor.Yellow);
            Ui.L("  计算机名：" + Environment.MachineName + "    用户：" + Environment.UserName, ConsoleColor.Gray);
            Ui.L("  系统时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), ConsoleColor.Gray);

            foreach (DriveInfo d in Tool.FixedDrives())
                Ui.L("  磁盘 " + d.Name + " 剩余 " + Tool.Sz(d.AvailableFreeSpace) + " / " + Tool.Sz(d.TotalSize), ConsoleColor.Gray);

            string o;
            int rc = Tool.Run("robocopy.exe", "", out o, 3000);
            Ui.L("  robocopy：" + (rc == -2 ? "不可用（迁移功能会受影响）" : "可用"), rc == -2 ? ConsoleColor.Red : ConsoleColor.Green);

            rc = Tool.Run("cmd.exe", "/c ver", out o, 3000);
            Ui.L("  cmd：" + (rc == -2 ? "不可用" : "可用"), rc == -2 ? ConsoleColor.Red : ConsoleColor.Green);

            bool wmi = false;
            try
            {
                using (System.Management.ManagementObjectSearcher s = new System.Management.ManagementObjectSearcher("root\\cimv2", "SELECT Name FROM Win32_OperatingSystem"))
                {
                    foreach (object x in s.Get()) { wmi = true; break; }
                }
            }
            catch (Exception) { }
            Ui.L("  WMI：" + (wmi ? "可用" : "不可用（66专业检测会缺数据）"), wmi ? ConsoleColor.Green : ConsoleColor.Red);

            string hosts = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"drivers\etc\hosts");
            Ui.L("  hosts 可读：" + (File.Exists(hosts) ? "是" : "否"), ConsoleColor.Gray);
            Ui.L("  记录目录：" + Tool.LogDir, ConsoleColor.DarkGray);
            Tool.PauseExit("", ConsoleColor.Gray);
        }
    }
}
