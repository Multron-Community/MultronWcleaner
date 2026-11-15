using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace MultronWinCleaner.Processes
{
    public static class CrashLog
    {
        private static readonly object FileLock = new();
        private static DateTime lastNotice = DateTime.MinValue;

        public static string FilePath => Path.Combine(AppContext.BaseDirectory, "crash.log");

        public static void Register(Application app)
        {
            app.DispatcherUnhandledException += OnDispatcherException;
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Write(e.ExceptionObject as Exception, "fatal");
            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                Write(e.Exception, "background task");
                e.SetObserved();
            };
        }

        public static void Write(Exception? ex, string kind = "error")
        {
            if (ex == null)
                return;
            try
            {
                lock (FileLock)
                    File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{kind}] v{Updater.CurrentAppVersion}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
            }
            catch
            {
            }
        }

        private static void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Write(e.Exception);
            e.Handled = true;
            if ((DateTime.Now - lastNotice).TotalSeconds < 10)
                return;
            lastNotice = DateTime.Now;
            try
            {
                AppDialog.Show(Loc.F("Something went wrong, but Multron Win Cleaner keeps running:\n\n{0}\n\nThe details were saved to crash.log in the program folder.", e.Exception.Message),
                    Loc.T("Multron Win Cleaner"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch
            {
            }
        }
    }
}
