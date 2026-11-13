using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace MultronWinCleaner
{
    public static class Appearance
    {
        public const string EnabledSettingKey = "transparency";
        public const string ColorSettingKey = "transparencycolor";
        public const string LevelSettingKey = "transparencylevel";

        public static readonly (string Name, Color Color)[] Colors =
        {
            ("Blue", Color.FromRgb(0x1E, 0x88, 0xE5)),
            ("Green", Color.FromRgb(0x2E, 0xB8, 0x5C)),
            ("Orange", Color.FromRgb(0xFF, 0x8C, 0x1A)),
            ("Purple", Color.FromRgb(0x8E, 0x5C, 0xE6)),
            ("Red", Color.FromRgb(0xE5, 0x39, 0x35)),
            ("Pink", Color.FromRgb(0xE8, 0x4A, 0x9B)),
            ("Teal", Color.FromRgb(0x14, 0xB8, 0xA6)),
            ("Gray", Color.FromRgb(0x80, 0x80, 0x80))
        };

        private static readonly (byte WindowAlpha, byte PanelAlpha, double Tint)[] Levels =
        {
            (0xF6, 0xFA, 0.06),
            (0xEA, 0xF3, 0.10),
            (0xD8, 0xE8, 0.16)
        };

        private static readonly string[] Keys = { "WindowBackground", "PanelBackground" };

        public static bool Enabled => ReadSetting(EnabledSettingKey) != "0";
        public static int ColorIndex => Clamp(ReadSetting(ColorSettingKey), Colors.Length);
        public static int LevelIndex => ReadSetting(LevelSettingKey) is string s ? Clamp(s, Levels.Length) : 1;

        public static void Apply()
        {
            var resources = Application.Current?.Resources;
            if (resources == null)
                return;

            foreach (string key in Keys)
                resources.Remove(key);
            if (!Enabled)
                return;

            var theme = resources.MergedDictionaries.LastOrDefault();
            if (theme == null)
                return;
            var level = Levels[LevelIndex];
            Color tint = Colors[ColorIndex].Color;
            if (Tinted(theme["WindowBackground"] as Brush, tint, level.Tint, level.WindowAlpha) is Brush window)
                resources["WindowBackground"] = window;
            if (Tinted(theme["PanelBackground"] as Brush, tint, level.Tint, level.PanelAlpha) is Brush panel)
                resources["PanelBackground"] = panel;
        }

        private static Brush? Tinted(Brush? source, Color tint, double amount, byte alpha)
        {
            double Luma(double r, double g, double b) => 0.2126 * r + 0.7152 * g + 0.0722 * b;
            double tintLuma = Luma(tint.R, tint.G, tint.B);
            Color Blend(Color c)
            {
                double baseLuma = Luma(c.R, c.G, c.B);
                if (baseLuma >= 128)
                    return Color.FromArgb(Math.Max(alpha, (byte)0xF7), (byte)Math.Round(c.R + (tint.R - c.R) * amount), (byte)Math.Round(c.G + (tint.G - c.G) * amount), (byte)Math.Round(c.B + (tint.B - c.B) * amount));
                double scale = Math.Min(1, Math.Min(baseLuma / Math.Max(1, tintLuma), (255 - baseLuma) / Math.Max(1, 255 - tintLuma)));
                byte Mix(byte a, byte t)
                {
                    double target = baseLuma + (t - tintLuma) * scale;
                    return (byte)Math.Clamp(Math.Round(a + (target - a) * amount * 1.5), 0, 255);
                }
                return Color.FromArgb(alpha, Mix(c.R, tint.R), Mix(c.G, tint.G), Mix(c.B, tint.B));
            }
            Brush? result = null;
            if (source is SolidColorBrush solid)
                result = new SolidColorBrush(Blend(solid.Color));
            else if (source is LinearGradientBrush gradient)
            {
                var copy = new LinearGradientBrush { StartPoint = gradient.StartPoint, EndPoint = gradient.EndPoint };
                foreach (GradientStop stop in gradient.GradientStops)
                    copy.GradientStops.Add(new GradientStop(Blend(stop.Color), stop.Offset));
                result = copy;
            }
            result?.Freeze();
            return result;
        }

        private static int Clamp(string? value, int count) =>
            int.TryParse(value, out int i) && i >= 0 && i < count ? i : 0;

        private static string? ReadSetting(string key)
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Settings.txt");
                if (!File.Exists(path))
                    return null;
                return File.ReadAllLines(path)
                    .Where(l => l.StartsWith(key + ":", StringComparison.OrdinalIgnoreCase))
                    .Select(l => l.Substring(key.Length + 1).Trim())
                    .LastOrDefault();
            }
            catch
            {
                return null;
            }
        }
    }
}
