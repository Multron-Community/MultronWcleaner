using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;

namespace MultronWinCleaner.Processes
{
    public static class TrayMenus
    {
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr handle);

        private static ContextMenu? open;

        public static void Attach(System.Windows.Forms.NotifyIcon icon, Func<ContextMenu> build)
        {
            icon.ContextMenuStrip = null;
            icon.MouseUp += (s, e) =>
            {
                if (e.Button == System.Windows.Forms.MouseButtons.Right)
                    Application.Current?.Dispatcher.BeginInvoke(new Action(() => Show(build())));
            };
        }

        public static void Show(ContextMenu menu)
        {
            if (open != null)
                open.IsOpen = false;
            open = menu;
            menu.Placement = PlacementMode.MousePoint;
            menu.StaysOpen = false;
            menu.Opened += (s, e) =>
            {
                if (PresentationSource.FromVisual(menu) is HwndSource source)
                    SetForegroundWindow(source.Handle);
                menu.Focus();
            };
            menu.Closed += (s, e) =>
            {
                if (ReferenceEquals(open, menu))
                    open = null;
            };
            menu.IsOpen = true;
        }

        public static MenuItem Item(string header, Action? click, string? icon = null, bool enabled = true)
        {
            var item = new MenuItem { Header = header, IsEnabled = enabled };
            if (icon != null)
                item.Icon = Icon(icon);
            if (click != null)
                item.Click += (s, e) => click();
            return item;
        }

        public static object? Icon(string name)
        {
            if (name.Length == 1)
                return new TextBlock
                {
                    Text = name,
                    FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                    FontSize = 14,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
            var source = ToolIcons.Source(name);
            return source == null ? null : new System.Windows.Controls.Image { Source = source, Width = 18, Height = 18 };
        }

        public static MenuItem Label(string text)
        {
            var item = new MenuItem { Header = text, IsEnabled = false, FontSize = 12 };
            return item;
        }
    }
}
