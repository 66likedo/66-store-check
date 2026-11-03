using System;
using System.Text;
using System.Threading;

namespace StoreCheck
{
    /// 控制台外壳：深色底、青绿标题、黄色序号、绿/黄/红结论
    public static class Ui
    {
        public const int Cols = 78;

        public static void Setup()
        {
            try
            {
                Console.BackgroundColor = ConsoleColor.Black;
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.Clear();
            }
            catch (Exception) { }

            Native.TryBigFont(20);

            try
            {
                if (Console.BufferWidth < 100) Console.BufferWidth = 100;
            }
            catch (Exception) { }
            try
            {
                if (Console.WindowWidth < 100) Console.WindowWidth = 100;
            }
            catch (Exception) { }
        }

        // ---------------- 颜色 ----------------
        public static void C(string s, ConsoleColor color)
        {
            try
            {
                ConsoleColor old = Console.ForegroundColor;
                Console.ForegroundColor = color;
                Console.Write(s);
                Console.ForegroundColor = old;
            }
            catch (Exception)
            {
                Console.Write(s);
            }
        }

        public static void L(string s, ConsoleColor color)
        {
            C(s, color);
            Console.WriteLine();
        }

        public static void L(string s)
        {
            Console.WriteLine(s);
        }

        public static void Plain(string s)
        {
            Console.WriteLine(s);
        }

        public static void Blank()
        {
            Console.WriteLine();
        }

        public static void Pause(int ms)
        {
            try { Thread.Sleep(ms); }
            catch (Exception) { }
        }

        // ---------------- 中文宽字符对齐 ----------------
        public static int W(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            int w = 0;
            foreach (char c in s)
            {
                if (c >= 0x1100 && c <= 0x115F) w += 2;
                else if (c >= 0x2E80 && c <= 0xA4CF) w += 2;
                else if (c >= 0xAC00 && c <= 0xD7A3) w += 2;
                else if (c >= 0xF900 && c <= 0xFAFF) w += 2;
                else if (c >= 0xFE30 && c <= 0xFE6F) w += 2;
                else if (c >= 0xFF00 && c <= 0xFF60) w += 2;
                else if (c >= 0xFFE0 && c <= 0xFFE6) w += 2;
                else w += 1;
            }
            return w;
        }

        public static string Pad(string s, int cols)
        {
            s = s == null ? "" : s;
            int pad = cols - W(s);
            if (pad < 1) pad = 1;
            return s + new string(' ', pad);
        }

        /// 右对齐（数字列用，保证 G/MB/KB 的小数点对齐）
        public static string PadLeft(string s, int cols)
        {
            s = s == null ? "" : s;
            int pad = cols - W(s);
            if (pad < 1) pad = 1;
            return new string(' ', pad) + s;
        }

        public static string Clip(string s, int cols)
        {
            s = Fmt.Clean(s);
            if (W(s) <= cols) return s;
            StringBuilder sb = new StringBuilder();
            int w = 0;
            foreach (char c in s)
            {
                int cw = W(c.ToString());
                if (w + cw > cols - 3) break;
                sb.Append(c);
                w += cw;
            }
            return sb.ToString() + "...";
        }

        public static void Center(string s, ConsoleColor color)
        {
            int width = 78;
            try
            {
                int cw = Console.WindowWidth;
                if (cw > 20) width = cw;
            }
            catch (Exception) { }
            int left = (width - W(s)) / 2;
            if (left < 0) left = 0;
            C(new string(' ', left) + s, color);
            Console.WriteLine();
        }

        // ---------------- 版式 ----------------
        public static void TitleBar(string subtitle)
        {
            ConsoleColor old = Console.ForegroundColor;
            Plain("");
            L("  " + new string('=', Cols), ConsoleColor.DarkCyan);
            C("  66门店巡检  ", ConsoleColor.Cyan);
            L("v1.2   六六电脑服务", ConsoleColor.DarkGray);
            L("  " + subtitle, ConsoleColor.Cyan);
            L("  " + new string('=', Cols), ConsoleColor.DarkCyan);
            Plain("");
        }

        /// [3/8] 硬盘健康      良好，无预警              ■ 正常
        public static void Item(int idx, int total, string name, string summary, Verdict v)
        {
            C("[", ConsoleColor.DarkGray);
            C(idx + "/" + total, ConsoleColor.Yellow);
            C("] ", ConsoleColor.DarkGray);
            C(Pad(name, 16), ConsoleColor.Gray);
            C(Pad(Clip(summary, 32), 32), Vs.Color(v) == ConsoleColor.DarkGray ? ConsoleColor.Gray : Vs.Color(v));
            C(Vs.Sym(v) + " " + Vs.Txt(v), Vs.Color(v));
            Console.WriteLine();
        }

        public static void Extra(string name, string summary, Verdict v)
        {
            C("[", ConsoleColor.DarkGray);
            C("附", ConsoleColor.DarkYellow);
            C("] ", ConsoleColor.DarkGray);
            C(Pad(name, 16), ConsoleColor.Gray);
            C(Pad(Clip(summary, 32), 32), Vs.Color(v) == ConsoleColor.DarkGray ? ConsoleColor.Gray : Vs.Color(v));
            C(Vs.Sym(v) + " " + Vs.Txt(v), Vs.Color(v));
            Console.WriteLine();
        }

        /// 屏幕中央大字
        public static void Big(string text)
        {
            try
            {
                int h = 20;
                try { h = Console.WindowHeight; } catch (Exception) { }
                int blank = h / 2 - 3;
                if (blank < 1) blank = 1;
                if (blank > 10) blank = 10;
                Blank();
                for (int i = 0; i < blank; i++) Console.WriteLine();
                Center(new string('=', 56), ConsoleColor.DarkCyan);
                Blank();
                Center(text, ConsoleColor.Green);
                Blank();
                Center(new string('=', 56), ConsoleColor.DarkCyan);
                Blank();
            }
            catch (Exception)
            {
                Console.WriteLine(text);
            }
        }

        public static void KeyHint(string text)
        {
            Blank();
            C("  " + text, ConsoleColor.DarkGray);
            Console.WriteLine();
        }

        public static void OK(string text)
        {
            C("  √ ", ConsoleColor.Green);
            L(text, ConsoleColor.Gray);
        }

        public static void Err(string text)
        {
            C("  × ", ConsoleColor.Red);
            L(text, ConsoleColor.Gray);
        }

        public static void Info(string text)
        {
            C("  · ", ConsoleColor.DarkGray);
            L(text, ConsoleColor.Gray);
        }
    }
}
