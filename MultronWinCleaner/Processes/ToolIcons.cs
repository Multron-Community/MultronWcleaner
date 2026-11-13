using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MultronWinCleaner.Processes
{
    public static class ToolIcons
    {
        private static readonly Dictionary<string, ImageSource> Cache = new();

        private static readonly System.Windows.Media.Effects.DropShadowEffect Shadow = CreateShadow();

        private static System.Windows.Media.Effects.DropShadowEffect CreateShadow()
        {
            var shadow = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 5, ShadowDepth = 1.5, Direction = 270, Opacity = 0.35, Color = Colors.Black };
            shadow.Freeze();
            return shadow;
        }

        private static readonly Dictionary<string, string> Steps = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Malware Scan"] = "malware",
            ["Duplicate Files"] = "duplicates",
            ["Large Files"] = "largefiles",
            ["Firewall Rules"] = "firewall",
            ["Shortcuts"] = "shortcuts",
            ["Security"] = "security",
            ["Windows Disk Cleanup"] = "diskcleanup",
            ["Windows Repair (DISM)"] = "dism",
            ["System File Check (SFC)"] = "sfc",
            ["Deep Log Files Scan"] = "deeplog"
        };

        public static readonly Dictionary<string, string> Tools = new()
        {
            ["Firewall Rule Cleaner"] = "firewall",
            ["Large File Finder"] = "largefiles",
            ["Duplicate File Finder"] = "duplicates",
            ["System Optimization"] = "optimize",
            ["Memory Cleaner"] = "memory",
            ["Startup Manager"] = "startup",
            ["Malware Scan"] = "malware",
            ["Shortcut Fixer"] = "shortcuts",
            ["Security Check"] = "security"
        };

        public static ImageSource? Source(string name)
        {
            if (Cache.TryGetValue(name, out var cached))
                return cached;
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.UriSource = new Uri($"pack://application:,,,/MultronWinCleaner;component/Assets/Icons/mwc_{name}.png");
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.EndInit();
                image.Freeze();
                Cache[name] = image;
                return image;
            }
            catch
            {
                return null;
            }
        }

        public static string? ForStep(string? key, string? title)
        {
            if (key != null && Steps.TryGetValue(key, out string? byKey))
                return byKey;
            foreach (var step in Steps)
            {
                if (title != null && (string.Equals(title.Trim(), step.Key, StringComparison.OrdinalIgnoreCase) || string.Equals(title.Trim(), Loc.T(step.Key), StringComparison.CurrentCultureIgnoreCase)))
                    return step.Value;
            }
            return null;
        }

        public static FrameworkElement? Tile(string? name, double size)
        {
            if (name == null || Source(name) is not ImageSource source)
                return null;
            var image = new Image { Source = source, Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center, Effect = Shadow };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            return image;
        }
    }
}
