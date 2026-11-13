using System.Configuration;
using System.Data;
using System.IO;
using System.Windows;

namespace MultronWinCleaner
{
    public partial class App : Application
    {
        public static bool LaunchedFromStartup { get; set; } = false;

        public static bool LaunchedForMalwareScan { get; private set; }

        public static readonly string ScanQueueFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MultronWinCleaner", "ScanQueue");
        private static readonly TimeSpan QueueMaxAge = TimeSpan.FromMinutes(5);

        private static readonly string HostMutexName ="Local\\MultronWinCleaner-MalwareScan-" + System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value;
        private static Mutex? scanHostMutex;
        private static FileSystemWatcher? queueWatcher;
        private static readonly List<string> pendingScanPaths = new List<string>();
        private static System.Windows.Threading.DispatcherTimer? scanBatchTimer;

        static App()
        {
            Processes.Motion.Load();
        }

        protected override void OnStartup(StartupEventArgs e)
        {

            LaunchedFromStartup = e.Args.Any(arg => arg.Equals("-startup", StringComparison.OrdinalIgnoreCase));

            string appDirectory = AppContext.BaseDirectory;
            Environment.CurrentDirectory = appDirectory;

            Processes.Updater.WaitForPreviousInstance(e.Args);
            Processes.Updater.CleanupAfterUpdate();

            Loc.Init();
            WindowFit.Register();
            Processes.Motion.RegisterWindows();
            Processes.WindowIcons.Register();

            if (!MalwareScan.IsOfflineMode)
                _ = MalwareScanEula.RefreshAsync();

            List<string> scanPaths = ReadScanPaths(e.Args);
            bool forScan = scanPaths.Count > 0;
            if (forScan)
                WriteToQueue(scanPaths);

            if (!BecomeScanHost())
            {
                AllowSetForegroundWindow(AsfwAny);
                if (!forScan && !LaunchedFromStartup)
                    SignalRunningInstance();
                Shutdown();
                Environment.Exit(0);
                return;
            }
            else
            {
                LaunchedForMalwareScan = forScan;
            }

            base.OnStartup(e);
        }

        private static List<string> ReadScanPaths(string[] args)
        {
            int index = Array.FindIndex(args, a => a.Equals(Processes.ShellContextMenu.ScanArgument, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                return new List<string>();
            return args.Skip(index + 1).Select(a => a.Trim().Trim('"')).Where(a => a.Length > 0).ToList();
        }

        private static void WriteToQueue(List<string> paths)
        {
            try
            {
                Directory.CreateDirectory(ScanQueueFolder);
                string name = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "-" + Guid.NewGuid().ToString("N");
                string temp = Path.Combine(ScanQueueFolder, name + ".tmp");
                File.WriteAllLines(temp, paths);
                File.Move(temp, Path.Combine(ScanQueueFolder, name + ".txt"));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Could not queue the right-click scan: " + ex.Message);
            }
        }

        private static readonly string ShowEventName = "Local\\MultronWinCleaner-Show-" + System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value;
        private static EventWaitHandle? showEvent;
        private static RegisteredWaitHandle? showWait;
        private const int AsfwAny = -1;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool AllowSetForegroundWindow(int processId);

        private static void SignalRunningInstance()
        {
            try
            {
                if (EventWaitHandle.TryOpenExisting(ShowEventName, out EventWaitHandle? existing))
                    using (existing)
                        existing.Set();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Could not reach the running instance: " + ex.Message);
            }
        }

        private static void ListenForShowRequests()
        {
            try
            {
                showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
                showWait = ThreadPool.RegisterWaitForSingleObject(showEvent,
                    (state, timedOut) => Current?.Dispatcher.BeginInvoke(ShowMainWindowRequested),
                    null, Timeout.Infinite, false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Could not listen for a second launch: " + ex.Message);
            }
        }

        private static void ShowMainWindowRequested()
        {
            if (Current?.MainWindow is Multron_Win_Cleaner.MainWindow main)
                main.BringToFront();
        }

        private static bool BecomeScanHost()
        {
            scanHostMutex = new Mutex(true, HostMutexName, out bool owner);
            if (!owner)
            {
                scanHostMutex.Dispose();
                scanHostMutex = null;
                return false;
            }

            ListenForShowRequests();

            try
            {
                Directory.CreateDirectory(ScanQueueFolder);
                queueWatcher = new FileSystemWatcher(ScanQueueFolder, "*.txt") { NotifyFilter = NotifyFilters.FileName };
                queueWatcher.Created += (s, e) => Current?.Dispatcher.BeginInvoke(ReadQueue);
                queueWatcher.Renamed += (s, e) => Current?.Dispatcher.BeginInvoke(ReadQueue);
                queueWatcher.EnableRaisingEvents = true;
                ReadQueue();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Could not watch the right-click scan queue: " + ex.Message);
            }
            return true;
        }

        private static void ReadQueue()
        {
            string[] files;
            try { files = Directory.GetFiles(ScanQueueFolder, "*.txt"); }
            catch { return; }

            var paths = new List<string>();
            foreach (string file in files.OrderBy(f => f, StringComparer.Ordinal))
            {
                try
                {
                    if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) < QueueMaxAge)
                        paths.AddRange(File.ReadAllLines(file).Select(l => l.Trim()).Where(l => l.Length > 0));
                    File.Delete(file);
                }
                catch (IOException)
                {
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("Could not read a right-click scan request: " + ex.Message);
                }
            }
            if (paths.Count > 0)
                QueueMalwareScan(paths);
        }

        private static void QueueMalwareScan(List<string> paths)
        {
            pendingScanPaths.AddRange(paths);
            if (scanBatchTimer == null)
            {
                scanBatchTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
                scanBatchTimer.Tick += (s, e) => StartPendingMalwareScan();
            }
            scanBatchTimer.Stop();
            scanBatchTimer.Start();
        }

        private static Action<List<string>>? malwareScanStarter;

        public static void SetMalwareScanStarter(Action<List<string>> starter)
        {
            malwareScanStarter = starter;
            StartPendingMalwareScan();
        }

        private static void StartPendingMalwareScan()
        {
            if (malwareScanStarter == null || pendingScanPaths.Count == 0)
                return;
            scanBatchTimer?.Stop();
            var paths = pendingScanPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            pendingScanPaths.Clear();
            malwareScanStarter(paths);
        }
    }

}
