using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

static class IconBuilder
{
    static void Main(string[] args)
    {
        var images = new List<byte[]>();
        int[] sizes = { 16, 32, 48, 64, 128, 256 };
        foreach (int size in sizes)
        {
            using (var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            using (var graphics = Graphics.FromImage(bitmap))
            using (var memory = new MemoryStream())
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.Clear(Color.Transparent);
                graphics.ScaleTransform(size / 64F, size / 64F);
                using (var path = new GraphicsPath())
                using (var fill = new SolidBrush(Color.FromArgb(36, 99, 235)))
                {
                    path.AddArc(2, 2, 20, 20, 180, 90); path.AddArc(42, 2, 20, 20, 270, 90);
                    path.AddArc(42, 42, 20, 20, 0, 90); path.AddArc(2, 42, 20, 20, 90, 90); path.CloseFigure();
                    graphics.FillPath(fill, path);
                }
                using (var pen = new Pen(Color.White, 4.5F))
                {
                    pen.StartCap = pen.EndCap = LineCap.Round;
                    pen.LineJoin = LineJoin.Round;
                    graphics.DrawLine(pen, 32, 14, 32, 38);
                    graphics.DrawLines(pen, new[] { new Point(23, 29), new Point(32, 38), new Point(41, 29) });
                    graphics.DrawLines(pen, new[] { new Point(17, 40), new Point(17, 49), new Point(47, 49), new Point(47, 40) });
                }
                bitmap.Save(memory, ImageFormat.Png); images.Add(memory.ToArray());
            }
        }
        using (var writer = new BinaryWriter(File.Create(args[0])))
        {
            writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length);
            int offset = 6 + sizes.Length * 16;
            for (int i = 0; i < sizes.Length; i++)
            {
                writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
                writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)1); writer.Write((ushort)32);
                writer.Write(images[i].Length); writer.Write(offset); offset += images[i].Length;
            }
            foreach (var bytes in images) writer.Write(bytes);
        }
    }
}
