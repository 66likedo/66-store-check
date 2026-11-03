using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace StoreCheck
{
    public static class ToolProgram
    {
        private const string Ver = "1.1";

        public static int Main(string[] args)
        {
            bool degraded = false;
            string direct = "";
            for (int i = 0; i < args.Length; i++)
            {
                string low = args[i].Trim().ToLower();
                if (low == "--degraded") degraded = true;
                else if (low == "--run" && i + 1 < args.Length) { direct = args[i + 1]; i++; }
            }

            try { Console.Title = "66维护工具 v" + Ver; } catch (Exception) { }
            Ui.Setup();

            bool admin = Tool.IsAdmin();
            string exeDir = "";
            try { exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location); }
            catch (Exception) { }
            if (string.IsNullOrEmpty(exeDir)) exeDir = Environment.CurrentDirectory;
            Tool.Init(exeDir);

            if (!admin && !degraded)
            {
                if (!TryElevate()) return 0;
            }

            if (direct.Length > 0) return Dispatch(direct);

            while (true)
            {
                PrintMenu(admin);
                string c = Tool.ReadLine().Trim().ToLower();
                if (c.Length == 0) continue;
                if (c == "q") return 0;
                Dispatch(c);
            }
        }

        private static void PrintMenu(bool admin)
        {
            Ui.Blank();
            Ui.L("  " + new string('=', 70), ConsoleColor.DarkCyan);
            Ui.L("  66维护工具  v" + Ver + "   六六电脑服务", ConsoleColor.Cyan);
            Ui.L("  会改系统 / 搬文件：每一步都先预演、要确认、写记录", ConsoleColor.Yellow);
            Ui.L("  " + new string('=', 70), ConsoleColor.DarkCyan);
            Ui.C("  权限：", ConsoleColor.DarkGray);
            Ui.L(admin ? "管理员" : "受限（启动项/外壳/迁移的部分功能用不了，可用 --degraded 强制跑）",
                admin ? ConsoleColor.Green : ConsoleColor.Yellow);
            Ui.L("  记录目录：" + Tool.LogDir, ConsoleColor.DarkGray);
            Ui.Blank();

            Row("1", "数据迁移", "整目录搬到目标盘（源目录不删，自动校验）");
            Row("2", "迁移校验", "对比源和目标，列差异清单");
            Row("3", "启动项管理", "禁用 / 启用 / 删除自启项（先导出 .reg）");
            Row("4", "目录修复", "桌面/文档/图片改回默认位置");
            Row("5", "重复文件扫描", "按哈希找重复，只报告不删");
            Row("6", "乱码名清理", "修复编码错乱的文件名，先预览再改");
            Row("7", "卸载建议", "按装机捆绑/重复功能给建议，不执行卸载");
            Row("8", "桌面图标整理", "快捷方式归类，可一键还原");
            Row("9", "个性化修复", "图标缓存 / 右键菜单 / 外壳");
            Row("0", "环境自检", "只读检查运行环境");
            Ui.Blank();
            Ui.L("      输入编号回车；q 退出", ConsoleColor.Cyan);
            Console.Write("  > ");
        }

        private static void Row(string no, string name, string desc)
        {
            Ui.C("  [" + no + "] ", ConsoleColor.Yellow);
            Ui.C(Ui.Pad(name, 14), ConsoleColor.Gray);
            Ui.L(desc, ConsoleColor.DarkGray);
        }

        private static int Dispatch(string cmd)
        {
            switch (cmd)
            {
                case "1": ModuleMigrate.Run(); return 0;
                case "2": ModuleVerify.Run(); return 0;
                case "3": ModuleStartup.Run(); return 0;
                case "4": ModuleFolders.Run(); return 0;
                case "5": ModuleDup.Run(); return 0;
                case "6": ModuleMojibake.Run(); return 0;
                case "7": ModuleUninstall.Run(); return 0;
                case "8": ModuleTidy.Run(); return 0;
                case "9": ModulePersonal.Run(); return 0;
                case "0": ModuleEnv.Run(); return 0;
                case "q": return -1;
                default:
                    Ui.Blank();
                    Ui.Err("没有这个编号：" + cmd);
                    Ui.Pause(700);
                    return 0;
            }
        }

        private static bool TryElevate()
        {
            Ui.Blank();
            Ui.L("66维护工具需要管理员权限：", ConsoleColor.Cyan);
            Ui.L("  1) 启动项管理要写 HKLM", ConsoleColor.Gray);
            Ui.L("  2) 外壳修复要写 HKLM\\Winlogon", ConsoleColor.Gray);
            Ui.L("  3) 迁移大盘时常遇到系统目录权限", ConsoleColor.Gray);
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
                Ui.Err("没有获得管理员权限：" + Fmt.Cut(Fmt.Clean(ex.Message), 50));
                Ui.L("可以先用只读/受限方式跑：66维护工具.exe --degraded", ConsoleColor.Gray);
                Ui.Blank();
                Ui.C("  按任意键退出", ConsoleColor.Green);
                Console.WriteLine();
                try { if (!Console.IsInputRedirected) Console.ReadKey(true); }
                catch (Exception) { }
                return false;
            }
        }
    }
}
