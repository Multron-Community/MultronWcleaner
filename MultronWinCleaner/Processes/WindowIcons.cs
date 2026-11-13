using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace MultronWinCleaner.Processes
{
    public static class WindowIcons
    {
        private const int WM_SETICON = 0x0080;
        private const int ICON_SMALL = 0;
        private const int ICON_BIG = 1;
        private const int SM_CXICON = 11;
        private const int SM_CXSMICON = 49;

        private static readonly Dictionary<string, System.Drawing.Icon> cache = new();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int index);

        public static void Register()
        {
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnLoaded));
        }

        private static void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is Window window && ReferenceEquals(e.OriginalSource, window))
                Apply(window);
        }

        public static void Apply(Window window)
        {
            try
            {
                if (window.Icon is not BitmapFrame frame)
                    return;
                string source = frame.ToString();
                if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) || !source.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
                    return;
                IntPtr hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero)
                    return;
                var big = Load(uri, Math.Max(32, GetSystemMetrics(SM_CXICON)));
                var small = Load(uri, Math.Max(16, GetSystemMetrics(SM_CXSMICON)));
                if (big != null)
                    SendMessage(hwnd, WM_SETICON, (IntPtr)ICON_BIG, big.Handle);
                if (small != null)
                    SendMessage(hwnd, WM_SETICON, (IntPtr)ICON_SMALL, small.Handle);
            }
            catch
            {
            }
        }

        private static System.Drawing.Icon? Load(Uri uri, int size)
        {
            string key = uri + "|" + size;
            if (cache.TryGetValue(key, out var icon))
                return icon;
            var info = Application.GetResourceStream(uri);
            if (info == null)
                return null;
            using (info.Stream)
                icon = new System.Drawing.Icon(info.Stream, size, size);
            cache[key] = icon;
            return icon;
        }
    }
}
