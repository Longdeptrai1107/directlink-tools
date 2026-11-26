param([Parameter(Mandatory = $true)][string]$OutputPath, [switch]$Upload)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;

public static class DirectLinkIconBuilder
{
    public static void Save(string output, bool upload)
    {
        int[] sizes = { 16, 32, 48, 64, 128, 256 };
        List<byte[]> images = new List<byte[]>();
        foreach (int size in sizes)
        {
            using (Bitmap bitmap = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.ScaleTransform(size / 256F, size / 256F);
                    using (GraphicsPath path = new GraphicsPath())
                    {
                        path.AddArc(10, 10, 96, 96, 180, 90); path.AddArc(150, 10, 96, 96, 270, 90);
                        path.AddArc(150, 150, 96, 96, 0, 90); path.AddArc(10, 150, 96, 96, 90, 90); path.CloseFigure();
                        using (LinearGradientBrush brush = new LinearGradientBrush(new Rectangle(0, 0, 256, 256), Color.FromArgb(124, 77, 245), Color.FromArgb(42, 139, 250), 45)) g.FillPath(brush, path);
                    }
                    using (Pen p = new Pen(Color.White, 17) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                    {
                        g.DrawLine(p, 128, 58, 128, 148);
                        if (upload) g.DrawLines(p, new Point[] { new Point(89, 97), new Point(128, 58), new Point(167, 97) });
                        else g.DrawLines(p, new Point[] { new Point(89, 111), new Point(128, 150), new Point(167, 111) });
                        g.DrawLines(p, new Point[] { new Point(65, 153), new Point(65, 189), new Point(191, 189), new Point(191, 153) });
                    }
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(67, 236, 209))) g.FillEllipse(b, 189, 28, 31, 31);
                }
                using (MemoryStream stream = new MemoryStream())
                using (BinaryWriter w = new BinaryWriter(stream))
                {
                    int maskStride = ((size + 31) / 32) * 4;
                    w.Write(40); w.Write(size); w.Write(size * 2); w.Write((short)1); w.Write((short)32);
                    w.Write(0); w.Write(size * size * 4); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
                    for (int y = size - 1; y >= 0; y--)
                        for (int x = 0; x < size; x++) { Color c = bitmap.GetPixel(x, y); w.Write(c.B); w.Write(c.G); w.Write(c.R); w.Write(c.A); }
                    for (int y = size - 1; y >= 0; y--)
                    {
                        byte[] row = new byte[maskStride];
                        for (int x = 0; x < size; x++) if (bitmap.GetPixel(x, y).A == 0) row[x / 8] |= (byte)(128 >> (x % 8));
                        w.Write(row);
                    }
                    images.Add(stream.ToArray());
                }
            }
        }
        using (BinaryWriter writer = new BinaryWriter(File.Create(output)))
        {
            writer.Write((short)0); writer.Write((short)1); writer.Write((short)sizes.Length);
            int offset = 6 + sizes.Length * 16;
            for (int i = 0; i < sizes.Length; i++)
            {
                writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
                writer.Write((byte)0); writer.Write((byte)0); writer.Write((short)1); writer.Write((short)32);
                writer.Write(images[i].Length); writer.Write(offset); offset += images[i].Length;
            }
            foreach (byte[] image in images) writer.Write(image);
        }
    }
}
'@
[DirectLinkIconBuilder]::Save($OutputPath, [bool]$Upload)
