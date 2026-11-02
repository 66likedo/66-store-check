using System;
using System.Runtime.InteropServices;

namespace StoreCheck
{
    /// 只做显示层的小调用：调大控制台字号、数显示器数量。不改任何系统设置。
    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct COORD
        {
            public short X;
            public short Y;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct CONSOLE_FONT_INFO_EX
        {
            public int cbSize;
            public uint nFont;
            public COORD dwFontSize;
            public uint FontFamily;
            public uint FontWeight;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string FaceName;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string DeviceKey;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(int nStdHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetCurrentConsoleFontEx(IntPtr h, bool bMax, ref CONSOLE_FONT_INFO_EX f);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool EnumDisplayDevices(string lpDevice, uint iDevNum, ref DISPLAY_DEVICE d, uint dwFlags);

        /// 只影响当前控制台窗口，适合店主站在旁边看
        public static bool TryBigFont(int height)
        {
            try
            {
                // 等宽优先：雅黑是比例字体，"MB" 比数字宽，列会歪
                string[] faces = new string[] { "NSimSun", "新宋体", "SimSun", "宋体", "Consolas", "Microsoft YaHei" };
                IntPtr h = GetStdHandle(-11);
                if (h == IntPtr.Zero || h == new IntPtr(-1)) return false;
                foreach (string face in faces)
                {
                    try
                    {
                        CONSOLE_FONT_INFO_EX f = new CONSOLE_FONT_INFO_EX();
                        f.cbSize = Marshal.SizeOf(typeof(CONSOLE_FONT_INFO_EX));
                        f.nFont = 0;
                        f.dwFontSize.X = 0;
                        f.dwFontSize.Y = (short)height;
                        f.FontFamily = 0;
                        f.FontWeight = 400;
                        f.FaceName = face;
                        if (SetCurrentConsoleFontEx(h, false, ref f)) return true;
                    }
                    catch (Exception) { }
                }
            }
            catch (Exception) { }
            return false;
        }

        /// 实际接了几台显示器；读不到返回 -1
        public static int MonitorCount()
        {
            int count = 0;
            try
            {
                for (uint a = 0; a < 8; a++)
                {
                    DISPLAY_DEVICE ad = new DISPLAY_DEVICE();
                    ad.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
                    if (!EnumDisplayDevices(null, a, ref ad, 0)) break;
                    for (uint m = 0; m < 8; m++)
                    {
                        DISPLAY_DEVICE md = new DISPLAY_DEVICE();
                        md.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
                        if (!EnumDisplayDevices(ad.DeviceName, m, ref md, 0)) break;
                        if ((md.StateFlags & 0x1) != 0) count++;   // DISPLAY_DEVICE_ATTACHED_TO_DESKTOP
                    }
                }
                return count;
            }
            catch (Exception)
            {
                return -1;
            }
        }
    }
}
