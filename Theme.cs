using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DirectLinkDownloader
{
    internal enum Glyph { Download, Link, Folder, Bolt, Clipboard, Check, Stop, Clock, File, Shield, Help, Arrow, Upload, Cloud }

    internal static class Palette
    {
        public static readonly Color Ink = Color.FromArgb(28, 35, 58);
        public static readonly Color Muted = Color.FromArgb(126, 135, 157);
        public static readonly Color Purple = Color.FromArgb(113, 81, 240);
        public static readonly Color Blue = Color.FromArgb(63, 134, 250);
        public static readonly Color Cyan = Color.FromArgb(67, 221, 207);
        public static readonly Color Green = Color.FromArgb(23, 169, 119);
        public static readonly Color Background = Color.FromArgb(245, 247, 252);
        public static readonly Color Line = Color.FromArgb(231, 235, 244);

        public static GraphicsPath Round(RectangleF r, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0) { path.AddRectangle(r); return path; }
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void Icon(Graphics g, Glyph glyph, RectangleF r, Color color)
        {
            GraphicsState state = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TranslateTransform(r.X, r.Y);
            g.ScaleTransform(r.Width / 24, r.Height / 24);
            using (Pen p = new Pen(color, 1.8F) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                switch (glyph)
                {
                    case Glyph.Download:
                        g.DrawLine(p, 12, 3, 12, 15); g.DrawLines(p, new PointF[] { new PointF(7, 10), new PointF(12, 15), new PointF(17, 10) });
                        g.DrawLines(p, new PointF[] { new PointF(4, 15), new PointF(4, 20), new PointF(20, 20), new PointF(20, 15) }); break;
                    case Glyph.Upload:
                        g.DrawLine(p, 12, 3, 12, 15); g.DrawLines(p, new PointF[] { new PointF(7, 8), new PointF(12, 3), new PointF(17, 8) });
                        g.DrawLines(p, new PointF[] { new PointF(4, 15), new PointF(4, 20), new PointF(20, 20), new PointF(20, 15) }); break;
                    case Glyph.Cloud:
                        g.DrawArc(p, 2, 10, 9, 10, 90, 180); g.DrawArc(p, 7, 3, 12, 14, 180, 190);
                        g.DrawArc(p, 15, 11, 7, 9, 260, 190); g.DrawLine(p, 6, 20, 18, 20); break;
                    case Glyph.Link:
                        g.DrawArc(p, 3, 10, 11, 10, 45, 270); g.DrawArc(p, 10, 3, 11, 10, 225, 270); g.DrawLine(p, 8, 16, 16, 8); break;
                    case Glyph.Folder:
                        g.DrawLines(p, new PointF[] { new PointF(3, 8), new PointF(3, 5), new PointF(9, 5), new PointF(11, 8), new PointF(21, 8), new PointF(21, 19), new PointF(3, 19), new PointF(3, 8), new PointF(21, 8) }); break;
                    case Glyph.Bolt:
                        g.DrawPolygon(p, new PointF[] { new PointF(14, 2), new PointF(5, 13), new PointF(11, 13), new PointF(10, 22), new PointF(20, 10), new PointF(13, 10) }); break;
                    case Glyph.Clipboard:
                        g.DrawLines(p, new PointF[] { new PointF(8, 5), new PointF(5, 5), new PointF(5, 21), new PointF(19, 21), new PointF(19, 5), new PointF(16, 5) });
                        g.DrawRectangle(p, 8, 3, 8, 4); g.DrawLine(p, 9, 12, 15, 12); g.DrawLine(p, 9, 16, 15, 16); break;
                    case Glyph.Check:
                        g.DrawLines(p, new PointF[] { new PointF(5, 12), new PointF(10, 17), new PointF(20, 6) }); break;
                    case Glyph.Stop: g.DrawRectangle(p, 6, 6, 12, 12); break;
                    case Glyph.Clock:
                        g.DrawEllipse(p, 3, 3, 18, 18); g.DrawLines(p, new PointF[] { new PointF(12, 7), new PointF(12, 12), new PointF(16, 14) }); break;
                    case Glyph.File:
                        g.DrawLines(p, new PointF[] { new PointF(13, 3), new PointF(5, 3), new PointF(5, 21), new PointF(19, 21), new PointF(19, 9), new PointF(13, 3), new PointF(13, 9), new PointF(19, 9) });
                        g.DrawLine(p, 9, 14, 15, 14); g.DrawLine(p, 9, 17, 14, 17); break;
                    case Glyph.Shield:
                        g.DrawLines(p, new PointF[] { new PointF(12, 2), new PointF(20, 6), new PointF(19, 15), new PointF(16, 19), new PointF(12, 22), new PointF(8, 19), new PointF(5, 15), new PointF(4, 6), new PointF(12, 2) });
                        g.DrawLines(p, new PointF[] { new PointF(8, 11), new PointF(11, 14), new PointF(16, 9) }); break;
                    case Glyph.Help:
                        g.DrawEllipse(p, 3, 3, 18, 18); g.DrawArc(p, 9, 7, 6, 6, 180, 240); g.DrawLine(p, 12, 12, 12, 14); g.DrawEllipse(p, 11.6F, 17, 0.8F, 0.8F); break;
                    case Glyph.Arrow:
                        g.DrawLine(p, 4, 12, 20, 12); g.DrawLines(p, new PointF[] { new PointF(15, 7), new PointF(20, 12), new PointF(15, 17) }); break;
                }
            }
            g.Restore(state);
        }
    }

    internal sealed class Card : Panel
    {
        public Color Fill = Color.White;
        public Color Border = Palette.Line;
        public int Radius = 16;
        public Card() { DoubleBuffered = true; BackColor = Color.Transparent; Padding = new Padding(20); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = Palette.Round(new RectangleF(0.5F, 0.5F, Width - 1.5F, Height - 1.5F), Radius))
            using (SolidBrush brush = new SolidBrush(Fill))
            using (Pen pen = new Pen(Border))
            { e.Graphics.FillPath(brush, path); e.Graphics.DrawPath(pen, path); }
            base.OnPaint(e);
        }
    }

    internal sealed class IconView : Control
    {
        public Glyph Glyph;
        public Color Tint = Palette.Purple;
        public Color Tile = Color.Transparent;
        public bool Gradient;
        public IconView() { SetStyle(ControlStyles.SupportsTransparentBackColor | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true); BackColor = Color.Transparent; }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF r = new RectangleF(0, 0, Width - 1, Height - 1);
            if (Gradient || Tile != Color.Transparent)
                using (GraphicsPath path = Palette.Round(r, Math.Min(13, Height / 3)))
                {
                    using (Brush brush = Gradient ? (Brush)new LinearGradientBrush(r, Palette.Purple, Palette.Blue, 45) : new SolidBrush(Tile)) e.Graphics.FillPath(brush, path);
                }
            float inset = Width >= 36 ? Width * 0.24F : 1;
            Palette.Icon(e.Graphics, Glyph, new RectangleF(inset, inset, Width - inset * 2, Height - inset * 2), Tint);
        }
    }

    internal sealed class ModernButton : Button
    {
        public Glyph Glyph = Glyph.Download;
        public bool Primary;
        public bool Dark;
        public bool Quiet;
        private bool hovered;
        private bool pressed;
        public ModernButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent; FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand; Font = new Font("Segoe UI", 10F, FontStyle.Bold); Height = 44;
        }
        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF r = new RectangleF(0.5F, 0.5F, Width - 1.5F, Height - 1.5F);
            Color color = !Enabled ? Color.FromArgb(170, 177, 195) : Primary ? Color.White : Dark ? Color.FromArgb(190, 200, 224) : Palette.Ink;
            Color fill = !Enabled ? Color.FromArgb(237, 240, 247) : Dark ? (hovered ? Color.FromArgb(43, 51, 82) : Color.FromArgb(26, 34, 58))
                : Quiet ? (hovered ? Color.FromArgb(231, 226, 253) : Color.FromArgb(241, 238, 255)) : hovered ? Color.FromArgb(242, 244, 251) : Color.White;
            using (GraphicsPath path = Palette.Round(r, 10))
            {
                using (Brush brush = Primary && Enabled ? (Brush)new LinearGradientBrush(r,
                    pressed ? Color.FromArgb(91, 62, 209) : Palette.Purple, Palette.Blue, 20) : new SolidBrush(fill)) e.Graphics.FillPath(brush, path);
                if (!Primary && !Dark && !Quiet) using (Pen pen = new Pen(Palette.Line)) e.Graphics.DrawPath(pen, path);
                if (Focused && ShowFocusCues) using (Pen pen = new Pen(Primary ? Color.White : Palette.Purple) { DashStyle = DashStyle.Dot })
                    e.Graphics.DrawRectangle(pen, 5, 5, Width - 11, Height - 11);
            }
            if (Quiet && Enabled) color = Palette.Purple;
            Size text = TextRenderer.MeasureText(Text, Font, new Size(Width, Height), TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            int combined = text.Width + 28;
            int left = Dark ? 16 : Math.Max(10, (Width - combined) / 2);
            Palette.Icon(e.Graphics, Glyph, new RectangleF(left, (Height - 20) / 2F, 20, 20), color);
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(left + 29, 0, Width - left - 33, Height), color,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }
    }

    internal sealed class ProgressTrack : Control
    {
        public double Value;
        public bool Indeterminate;
        public int Phase;
        public ProgressTrack() { DoubleBuffered = true; Height = 10; }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF r = new RectangleF(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = Palette.Round(r, Height / 2F))
            {
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(237, 240, 248))) e.Graphics.FillPath(brush, path);
                e.Graphics.SetClip(path);
                float width = Indeterminate ? Width * 0.24F : (float)(Width * Math.Max(0, Math.Min(1, Value)));
                float x = Indeterminate ? ((Phase % 30) / 29F) * (Width + width) - width : 0;
                if (width > 0)
                    using (LinearGradientBrush brush = new LinearGradientBrush(new RectangleF(x, 0, Math.Max(1, width), Height), Palette.Purple, Palette.Cyan, 0F))
                    using (GraphicsPath fill = Palette.Round(new RectangleF(x, 0, width, Height - 1), Height / 2F)) e.Graphics.FillPath(brush, fill);
                e.Graphics.ResetClip();
            }
        }
    }

    internal sealed class SpeedChart : Control
    {
        private readonly List<double> samples = new List<double>();
        public SpeedChart() { DoubleBuffered = true; BackColor = Color.White; }
        public void Reset() { samples.Clear(); Invalidate(); }
        public void Add(double value) { samples.Add(Math.Max(0, value)); if (samples.Count > 80) samples.RemoveAt(0); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen grid = new Pen(Color.FromArgb(238, 241, 248))) e.Graphics.DrawLine(grid, 0, Height - 2, Width, Height - 2);
            if (samples.Count < 2) return;
            double max = 1;
            foreach (double value in samples) max = Math.Max(max, value);
            PointF[] points = new PointF[samples.Count];
            for (int i = 0; i < points.Length; i++) points[i] = new PointF(i * (Width - 2F) / 79, Height - 3 - (float)(samples[i] / max * (Height - 7)));
            using (GraphicsPath area = new GraphicsPath())
            {
                area.AddLines(points); area.AddLine(points[points.Length - 1], new PointF(points[points.Length - 1].X, Height));
                area.AddLine(new PointF(points[points.Length - 1].X, Height), new PointF(0, Height)); area.CloseFigure();
                using (LinearGradientBrush brush = new LinearGradientBrush(ClientRectangle, Color.FromArgb(65, Palette.Purple), Color.FromArgb(5, Palette.Purple), 90)) e.Graphics.FillPath(brush, area);
            }
            using (Pen pen = new Pen(Palette.Purple, 1.8F)) e.Graphics.DrawLines(pen, points);
        }
    }
}
