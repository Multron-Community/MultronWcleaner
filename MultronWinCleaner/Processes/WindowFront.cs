using System.Windows;

namespace MultronWinCleaner.Processes
{
    public static class WindowFront
    {
        public static void BringToFront(this Window window)
        {
            if (!window.IsVisible)
                window.Show();
            if (window.WindowState == WindowState.Minimized)
                window.WindowState = WindowState.Normal;
            window.Activate();
            if (!window.Topmost)
            {
                window.Topmost = true;
                window.Topmost = false;
            }
            window.Focus();
        }
    }
}
