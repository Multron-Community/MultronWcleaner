using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;

namespace MultronWinCleaner
{
    internal enum TrayBadge
    {
        Info,
        Warning,
        Danger,
        Success
    }

    internal static class ScanTrayIcons
    {
        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr handle);

        private static int Size => Math.Max(16, System.Windows.Forms.SystemInformation.SmallIconSize.Width);

        public static Icon[] CreateScanningFrames(int count) =>
            Enumerable.Range(0, count).Select(i => Create(DrawTile, i * 360f / count, TrayBadge.Info)).ToArray();

        public static Icon CreateIdle(TrayBadge badge = TrayBadge.Info) => Create(DrawTile, null, badge);

        public static Icon[] CreateSpinnerFrames(Bitmap baseIcon, int count) =>
            Enumerable.Range(0, count).Select(i => Create((g, r) => g.DrawImage(baseIcon, r), i * 360f / count, TrayBadge.Info)).ToArray();

        public static Icon CreateWithBadge(Bitmap baseIcon, TrayBadge? badge) =>
            Create((g, r) => g.DrawImage(baseIcon, r), null, badge);

        public static Bitmap LoadBase(string packUri)
        {
            var info = System.Windows.Application.GetResourceStream(new Uri(packUri, UriKind.Absolute));
            using (info.Stream)
            using (var icon = new Icon(info.Stream, Size, Size))
                return icon.ToBitmap();
        }

        private static Icon Finish(Bitmap bitmap)
        {
            IntPtr handle = bitmap.GetHicon();
            try
            {
                using var temporary = Icon.FromHandle(handle);
                return (Icon)temporary.Clone();
            }
            finally
            {
                DestroyIcon(handle);
            }
        }

        private static GraphicsPath RoundedSquare(RectangleF r, float radius)
        {
            float d = radius * 2f;
            var path = new GraphicsPath();
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static void DrawTile(Graphics g, RectangleF tile)
        {
            using (var square = RoundedSquare(tile, tile.Width * 0.24f))
            using (var back = new LinearGradientBrush(tile, Color.FromArgb(255, 32, 140, 152), Color.FromArgb(255, 3, 46, 56), LinearGradientMode.Vertical))
                g.FillPath(back, square);

            float height = tile.Height * 0.74f;
            float width = height * 18f / 22f;
            float left = tile.Left + (tile.Width - width) / 2f, top = tile.Top + (tile.Height - height) / 2f + tile.Height * 0.01f;
            PointF P(float x, float y) => new PointF(left + (x - 3f) / 18f * width, top + (y - 1f) / 22f * height);

            using var shield = new GraphicsPath();
            shield.AddLine(P(12, 1), P(3, 4.6f));
            shield.AddLine(P(3, 4.6f), P(3, 11));
            shield.AddBezier(P(3, 11), P(3, 16.55f), P(6.84f, 21.74f), P(12, 23));
            shield.AddBezier(P(12, 23), P(17.16f, 21.74f), P(21, 16.55f), P(21, 11));
            shield.AddLine(P(21, 11), P(21, 4.6f));
            shield.CloseFigure();
            using (var fill = new LinearGradientBrush(new RectangleF(left, top, width, height), Color.White, Color.FromArgb(255, 150, 232, 242), LinearGradientMode.Vertical))
                g.FillPath(fill, shield);

            using var check = new Pen(Color.FromArgb(255, 10, 92, 104), Math.Max(1.6f, tile.Width / 9f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            g.DrawLines(check, new[] { P(7.6f, 12.2f), P(10.9f, 15.4f), P(16.6f, 9f) });
        }

        private static Icon Create(Action<Graphics, RectangleF> drawBase, float? spinnerAngle, TrayBadge? badgeKind)
        {
            int size = Size;
            using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);

                bool scanning = spinnerAngle.HasValue;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                if (!scanning && badgeKind == null)
                {
                    drawBase(g, new RectangleF(0, 0, size, size));
                    return Finish(bitmap);
                }
                drawBase(g, new RectangleF(0, 0, size * 0.86f, size * 0.86f));

                float badge = size * 0.56f;
                var disc = new RectangleF(size - badge, size - badge, badge, badge);
                var cut = RectangleF.Inflate(disc, size / 16f, size / 16f);
                g.CompositingMode = CompositingMode.SourceCopy;
                using (var clear = new SolidBrush(Color.Transparent))
                    g.FillEllipse(clear, cut);
                g.CompositingMode = CompositingMode.SourceOver;

                if (!scanning)
                {
                    var tone = badgeKind switch
                    {
                        TrayBadge.Danger => Color.FromArgb(255, 229, 57, 53),
                        TrayBadge.Warning => Color.FromArgb(255, 253, 202, 40),
                        TrayBadge.Success => Color.FromArgb(255, 46, 184, 114),
                        _ => Color.FromArgb(255, 30, 136, 229)
                    };
                    using (var back = new SolidBrush(tone))
                        g.FillEllipse(back, disc);
                    float cx = disc.Left + disc.Width / 2f;
                    float stem = Math.Max(1.6f, badge / 6f);
                    var mark = badgeKind == TrayBadge.Warning ? Color.FromArgb(255, 60, 42, 0) : Color.White;
                    using var pen = new Pen(mark, stem) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                    using var dot = new SolidBrush(mark);
                    float r = stem * 0.62f;
                    if (badgeKind == TrayBadge.Success)
                    {
                        using var tick = new Pen(Color.White, stem) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
                        g.DrawLines(tick, new[]
                        {
                            new PointF(disc.Left + badge * 0.28f, disc.Top + badge * 0.52f),
                            new PointF(disc.Left + badge * 0.44f, disc.Top + badge * 0.68f),
                            new PointF(disc.Left + badge * 0.73f, disc.Top + badge * 0.36f)
                        });
                    }
                    else if (badgeKind == TrayBadge.Info)
                    {
                        g.FillEllipse(dot, cx - r, disc.Top + badge * 0.27f - r, r * 2f, r * 2f);
                        g.DrawLine(pen, cx, disc.Top + badge * 0.47f, cx, disc.Top + badge * 0.74f);
                    }
                    else
                    {
                        g.DrawLine(pen, cx, disc.Top + badge * 0.26f, cx, disc.Top + badge * 0.53f);
                        g.FillEllipse(dot, cx - r, disc.Top + badge * 0.73f - r, r * 2f, r * 2f);
                    }
                }
                else
                {
                    using (var back = new SolidBrush(Color.FromArgb(255, 24, 28, 33)))
                        g.FillEllipse(back, disc);
                    float stroke = Math.Max(1.5f, badge / 6.5f);
                    float inset = stroke / 2f + badge * 0.12f;
                    var ring = RectangleF.Inflate(disc, -inset, -inset);
                    using var track = new Pen(Color.FromArgb(70, 255, 255, 255), stroke);
                    g.DrawEllipse(track, ring);
                    using var arc = new Pen(Color.FromArgb(255, 66, 165, 245), stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                    g.DrawArc(arc, ring, spinnerAngle!.Value, 120f);
                }
            }

            return Finish(bitmap);
        }
    }
}
