using System;
using System.Windows;
using System.Windows.Media;

namespace MultronWinCleaner.Processes
{
    public static class ThemeColors
    {
        public static readonly Color WarningFill = Color.FromRgb(0xC2, 0x57, 0x0A);

        private static readonly Color LightSurface = Color.FromRgb(0xEC, 0xED, 0xEA);
        private static readonly Color DarkSurface = Color.FromRgb(0x1F, 0x20, 0x23);
        private static readonly Color WarningFallback = Color.FromRgb(0xFB, 0x8C, 0x00);

        private static volatile bool lightTheme;
        private static volatile SolidColorBrush? warningBrush;

        private static bool OnUiThread => Application.Current?.Dispatcher.CheckAccess() == true;

        public static void Refresh()
        {
            if (!OnUiThread)
                return;
            var app = Application.Current!;
            lightTheme = app.TryFindResource("TextPrimary") is SolidColorBrush text && Luminance(text.Color) < 0.3;
            warningBrush = app.TryFindResource("WarningText") is SolidColorBrush warning ? Frozen(warning.Color) : null;
        }

        public static bool IsLightTheme
        {
            get
            {
                Refresh();
                return lightTheme;
            }
        }

        public static Brush Warning
        {
            get
            {
                Refresh();
                return warningBrush ?? ReadableBrush(WarningFallback);
            }
        }

        public static Color Readable(Color color)
        {
            bool light = IsLightTheme;
            Color surface = light ? LightSurface : DarkSurface;
            Color result = color;
            for (int i = 0; i < 24 && Contrast(result, surface) < 4.5; i++)
                result = light ? Shade(result, 0.9) : Mix(result, Colors.White, 0.12);
            return Color.FromArgb(color.A, result.R, result.G, result.B);
        }

        public static SolidColorBrush ReadableBrush(Color color) => Frozen(Readable(color));

        public static SolidColorBrush ReadableBrush(string hex) => ReadableBrush((Color)ColorConverter.ConvertFromString(hex));

        private static SolidColorBrush Frozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        private static Color Shade(Color c, double factor) =>
            Color.FromRgb((byte)(c.R * factor), (byte)(c.G * factor), (byte)(c.B * factor));

        private static Color Mix(Color c, Color with, double amount) =>
            Color.FromRgb(
                (byte)Math.Round(c.R + (with.R - c.R) * amount),
                (byte)Math.Round(c.G + (with.G - c.G) * amount),
                (byte)Math.Round(c.B + (with.B - c.B) * amount));

        private static double Channel(byte value)
        {
            double v = value / 255.0;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }

        private static double Luminance(Color c) => 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);

        private static double Contrast(Color a, Color b)
        {
            double la = Luminance(a), lb = Luminance(b);
            return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
        }
    }
}
