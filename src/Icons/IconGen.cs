using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace IconStudio
{
    internal enum Glyph { Check, Magnifier, Bin, Gear }

    /// 生成多尺寸 .ico（16/24/32/48/64/128/256），并输出一张预览图
    internal static class IconGen
    {
        private static readonly int[] Sizes = new int[] { 16, 24, 32, 48, 64, 128, 256 };

        private static int Main(string[] args)
        {
            string outDir = args.Length > 0 ? args[0] : ".";
            try { Directory.CreateDirectory(outDir); }
            catch (Exception ex) { Console.WriteLine("无法创建目录：" + ex.Message); return 1; }

            WriteIco(Path.Combine(outDir, "store.ico"), Glyph.Check);
            WriteIco(Path.Combine(outDir, "pro.ico"), Glyph.Magnifier);
            WriteIco(Path.Combine(outDir, "clean.ico"), Glyph.Bin);
            WriteIco(Path.Combine(outDir, "tool.ico"), Glyph.Gear);
            MakePreview(Path.Combine(outDir, "preview.png"));
            Console.WriteLine("icons written to " + Path.GetFullPath(outDir));
            return 0;
        }

        // ---------------- 绘制 ----------------
        private static Color Accent(Glyph g)
        {
            switch (g)
            {
                case Glyph.Magnifier: return Color.FromArgb(255, 56, 176, 232);   // 青蓝
                case Glyph.Bin: return Color.FromArgb(255, 240, 158, 60);         // 橙
                case Glyph.Gear: return Color.FromArgb(255, 170, 126, 255);       // 紫
                default: return Color.FromArgb(255, 47, 211, 122);                // 青绿
            }
        }

        private static Bitmap Draw(Glyph g, int size)
        {
            Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics gfx = Graphics.FromImage(bmp))
            {
                gfx.SmoothingMode = SmoothingMode.AntiAlias;
                gfx.InterpolationMode = InterpolationMode.HighQualityBicubic;
                gfx.Clear(Color.Transparent);
                float s = size / 256f;
                Color ac = Accent(g);

                // 底板
                RectangleF bg = new RectangleF(6 * s, 6 * s, 244 * s, 244 * s);
                using (GraphicsPath p = Round(bg, 54 * s))
                {
                    using (LinearGradientBrush br = new LinearGradientBrush(
                        new RectangleF(0, 0, size, size),
                        Color.FromArgb(255, 27, 37, 47),
                        Color.FromArgb(255, 8, 12, 16),
                        90f))
                    {
                        gfx.FillPath(br, p);
                    }
                    using (Pen pen = new Pen(Color.FromArgb(110, ac), Math.Max(1f, 3 * s)))
                        gfx.DrawPath(pen, p);
                }

                switch (g)
                {
                    case Glyph.Check: DrawCheck(gfx, s); break;
                    case Glyph.Magnifier: DrawMagnifier(gfx, s, ac); break;
                    case Glyph.Bin: DrawBin(gfx, s, ac); break;
                    default: DrawGear(gfx, s, ac); break;
                }
            }
            return bmp;
        }

        private static void DrawCheck(Graphics gfx, float s)
        {
            using (Pen pen = new Pen(Color.FromArgb(255, 47, 211, 122), 30 * s))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                gfx.DrawLines(pen, new PointF[] {
                    new PointF(68 * s, 132 * s),
                    new PointF(112 * s, 178 * s),
                    new PointF(190 * s, 78 * s)
                });
            }
            using (Pen pen2 = new Pen(Color.FromArgb(90, 47, 211, 122), 8 * s))
            {
                pen2.StartCap = LineCap.Round;
                pen2.EndCap = LineCap.Round;
                gfx.DrawLine(pen2, 66 * s, 206 * s, 190 * s, 206 * s);
            }
        }

        private static void DrawMagnifier(Graphics gfx, float s, Color ac)
        {
            using (Pen ring = new Pen(ac, 26 * s))
                gfx.DrawEllipse(ring, 52 * s, 52 * s, 118 * s, 118 * s);
            using (Pen hand = new Pen(ac, 30 * s))
            {
                hand.StartCap = LineCap.Round;
                hand.EndCap = LineCap.Round;
                gfx.DrawLine(hand, 158 * s, 158 * s, 204 * s, 204 * s);
            }
            using (Pen chk = new Pen(Color.FromArgb(255, 245, 250, 252), 20 * s))
            {
                chk.StartCap = LineCap.Round;
                chk.EndCap = LineCap.Round;
                chk.LineJoin = LineJoin.Round;
                gfx.DrawLines(chk, new PointF[] {
                    new PointF(84 * s, 112 * s),
                    new PointF(104 * s, 134 * s),
                    new PointF(142 * s, 86 * s)
                });
            }
        }

        private static void DrawBin(Graphics gfx, float s, Color ac)
        {
            using (SolidBrush brush = new SolidBrush(ac))
            {
                // 桶盖
                using (GraphicsPath lid = Round(new RectangleF(62 * s, 78 * s, 132 * s, 22 * s), 10 * s))
                    gfx.FillPath(brush, lid);
                // 提手
                using (GraphicsPath hd = Round(new RectangleF(106 * s, 54 * s, 44 * s, 22 * s), 9 * s))
                    gfx.FillPath(brush, hd);
                // 桶身
                PointF[] body = new PointF[] {
                    new PointF(72 * s, 112 * s),
                    new PointF(184 * s, 112 * s),
                    new PointF(168 * s, 202 * s),
                    new PointF(88 * s, 202 * s)
                };
                gfx.FillPolygon(brush, body);
            }
            // 桶身两道浅色分隔
            using (Pen pen = new Pen(Color.FromArgb(150, 20, 28, 36), 10 * s))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                gfx.DrawLine(pen, 108 * s, 130 * s, 112 * s, 182 * s);
                gfx.DrawLine(pen, 148 * s, 130 * s, 144 * s, 182 * s);
            }
        }

        private static void DrawGear(Graphics gfx, float s, Color ac)
        {
            for (int i = 0; i < 8; i++)
            {
                GraphicsState st = gfx.Save();
                gfx.TranslateTransform(128 * s, 128 * s);
                gfx.RotateTransform(i * 45f);
                using (GraphicsPath tooth = Round(new RectangleF(-16 * s, -106 * s, 32 * s, 44 * s), 11 * s))
                using (SolidBrush br = new SolidBrush(ac))
                    gfx.FillPath(br, tooth);
                gfx.Restore(st);
            }
            using (Pen ring = new Pen(ac, 30 * s))
                gfx.DrawEllipse(ring, 62 * s, 62 * s, 132 * s, 132 * s);
            using (SolidBrush hole = new SolidBrush(Color.FromArgb(255, 18, 24, 30)))
                gfx.FillEllipse(hole, 104 * s, 104 * s, 48 * s, 48 * s);
        }

        private static GraphicsPath Round(RectangleF r, float radius)
        {
            GraphicsPath p = new GraphicsPath();
            float d = radius * 2;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // ---------------- 写 ICO ----------------
        private static void WriteIco(string path, Glyph g)
        {
            List<byte[]> images = new List<byte[]>();
            foreach (int sz in Sizes)
            {
                using (Bitmap bmp = Draw(g, sz))
                {
                    // 小尺寸用传统 DIB（兼容性最好），大尺寸用 PNG 压缩（体积小很多）
                    if (sz <= 64) images.Add(ToDib(bmp));
                    else
                    {
                        using (MemoryStream ms = new MemoryStream())
                        {
                            bmp.Save(ms, ImageFormat.Png);
                            images.Add(ms.ToArray());
                        }
                    }
                }
            }

            using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (BinaryWriter w = new BinaryWriter(fs))
            {
                w.Write((ushort)0);            // reserved
                w.Write((ushort)1);            // type = icon
                w.Write((ushort)Sizes.Length); // count

                int offset = 6 + 16 * Sizes.Length;
                for (int i = 0; i < Sizes.Length; i++)
                {
                    int sz = Sizes[i];
                    w.Write((byte)(sz >= 256 ? 0 : sz));
                    w.Write((byte)(sz >= 256 ? 0 : sz));
                    w.Write((byte)0);          // colors
                    w.Write((byte)0);          // reserved
                    w.Write((ushort)1);        // planes
                    w.Write((ushort)32);       // bpp
                    w.Write((uint)images[i].Length);
                    w.Write((uint)offset);
                    offset += images[i].Length;
                }
                foreach (byte[] img in images) w.Write(img);
            }
        }

        private static byte[] ToDib(Bitmap bmp)
        {
            int w = bmp.Width, h = bmp.Height;
            int maskStride = ((w + 31) / 32) * 4;
            int maskSize = maskStride * h;
            byte[] data = new byte[40 + w * h * 4 + maskSize];

            PutInt(data, 0, 40);
            PutInt(data, 4, w);
            PutInt(data, 8, h * 2);
            PutShort(data, 12, 1);
            PutShort(data, 14, 32);
            PutInt(data, 16, 0);
            PutInt(data, 20, w * h * 4 + maskSize);
            PutInt(data, 24, 0);
            PutInt(data, 28, 0);
            PutInt(data, 32, 0);
            PutInt(data, 36, 0);

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    Color c = bmp.GetPixel(x, h - 1 - y);
                    int o = 40 + (y * w + x) * 4;
                    data[o] = c.B;
                    data[o + 1] = c.G;
                    data[o + 2] = c.R;
                    data[o + 3] = c.A;
                }
            }
            return data;
        }

        private static void PutInt(byte[] b, int o, int v)
        {
            b[o] = (byte)(v & 0xFF);
            b[o + 1] = (byte)((v >> 8) & 0xFF);
            b[o + 2] = (byte)((v >> 16) & 0xFF);
            b[o + 3] = (byte)((v >> 24) & 0xFF);
        }

        private static void PutShort(byte[] b, int o, int v)
        {
            b[o] = (byte)(v & 0xFF);
            b[o + 1] = (byte)((v >> 8) & 0xFF);
        }

        // ---------------- 预览图 ----------------
        private static void MakePreview(string path)
        {
            int big = 180, small = 48;
            int width = (big + small + 20) * 4 + 20;
            int height = big + 90;
            using (Bitmap sheet = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(sheet))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.FromArgb(255, 245, 246, 248));
                Glyph[] list = new Glyph[] { Glyph.Check, Glyph.Magnifier, Glyph.Bin, Glyph.Gear };
                string[] names = new string[] { "store", "pro", "clean", "tool" };
                using (Font f = new Font("Consolas", 11))
                using (SolidBrush br = new SolidBrush(Color.FromArgb(255, 60, 66, 74)))
                {
                    for (int i = 0; i < list.Length; i++)
                    {
                        int x = 20 + i * (big + small + 20);
                        using (Bitmap b = Draw(list[i], big)) g.DrawImage(b, x, 20, big, big);
                        using (Bitmap b32 = Draw(list[i], 32)) g.DrawImage(b32, x, 20 + big + 8, 32, 32);
                        using (Bitmap b16 = Draw(list[i], 16)) g.DrawImage(b16, x + 44, 20 + big + 16, 16, 16);
                        g.DrawString(names[i], f, br, x, 20 + big + 44);
                    }
                }
                sheet.Save(path, ImageFormat.Png);
            }
        }
    }
}
