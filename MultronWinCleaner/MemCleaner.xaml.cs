using Microsoft.Win32;
using Multron_Win_Cleaner;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
 

namespace MultronWinCleaner
{
    public partial class MemCleaner : Window
    {
     
        private const uint PROCESS_SET_QUOTA = 0x0100;
        private const uint PROCESS_QUERY_INFORMATION = 0x0400;
        #region Native Methods (Windows API)
        [DllImport("kernel32.dll")]
        static extern bool SetProcessWorkingSetSize(IntPtr hProcess, IntPtr dwMinimumWorkingSetSize, IntPtr dwMaximumWorkingSetSize);

        [DllImport("psapi.dll")]
        static extern bool EmptyWorkingSet(IntPtr hProcess);
 

        [DllImport("kernel32.dll", SetLastError = true)]
   
        static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

        [DllImport("psapi.dll", SetLastError = true)]
        public static extern bool GetPerformanceInfo(out PERFORMANCE_INFORMATION pPerformanceInformation, int size);
        [DllImport("kernel32.dll", SetLastError = true)]

        private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);
        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr hObject);
        [StructLayout(LayoutKind.Sequential)]
        struct SYSTEM_POWER_STATUS
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte SystemStatusFlag;
            public uint BatteryLifeTime;
            public uint BatteryFullLifeTime;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PERFORMANCE_INFORMATION
        {
            public uint cb;
            public ulong CommitTotal;
            public ulong CommitLimit;
            public ulong CommitPeak;
            public ulong PhysicalTotal;
            public ulong PhysicalAvailable;
            public ulong SystemCache;
            public ulong KernelTotal;
            public ulong KernelPaged;
            public ulong KernelNonpaged;
            public ulong PageSize;
            public uint HandleCount;
            public uint ProcessCount;
            public uint ThreadCount;
        }

        public static ulong GetAvailablePhysicalMemory()
        {
            PERFORMANCE_INFORMATION pi = new PERFORMANCE_INFORMATION();
            pi.cb = (uint)Marshal.SizeOf(typeof(PERFORMANCE_INFORMATION));

            if (GetPerformanceInfo(out pi, Marshal.SizeOf(typeof(PERFORMANCE_INFORMATION))))
            {
                return pi.PhysicalAvailable * pi.PageSize;
            }
            return 0;
        }
        #endregion

        public MemoryMon memorymon = null!;
        MainWindow window;
        AutoCleanMem autocleanmem = null!;

     
        private PerformanceCounter? cpuCounter;

        public MemCleaner(MainWindow window)
        {
            InitializeComponent();

            this.window = window;
            Height = Math.Min(Height, SystemParameters.WorkArea.Height);
            LoadMemoryOptions();
     



        }
        private const string MemoryOptionsKey = @"Software\MultronWinCleaner";
        private bool memoryOptionsLoaded;
        private DateTime lastHighRamClean = DateTime.MinValue;
        private System.Windows.Threading.DispatcherTimer? highRamTimer;

        private CheckBox[] MemoryOptionBoxes => new[] { chkCleanOnHighRam, chkClearDns, chkReleaseCache, chkKeepForeground, chkSystemMemory, chkMergePages };

        private bool repeatLoaded;

        private int SelectedRepeats => cbRepeat?.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag as string, out int repeats) ? repeats : 3;

        private void Repeat_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (repeatLoaded)
                SaveMemoryOption("MemRepeat", SelectedRepeats);
        }

        private void LoadMemoryOptions()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(MemoryOptionsKey, false);
                foreach (CheckBox box in MemoryOptionBoxes)
                    box.IsChecked = key?.GetValue((string)box.Tag) is int value ? value == 1 : box == chkSystemMemory;
                if (key?.GetValue("MemRepeat") is int repeats)
                    foreach (ComboBoxItem choice in cbRepeat.Items)
                        if ((string)choice.Tag == repeats.ToString())
                            cbRepeat.SelectedItem = choice;
                repeatLoaded = true;
                if (key?.GetValue("MemExcludedApps") is string[] apps)
                    foreach (string app in apps)
                        lstExcludedApps.Items.Add(app);
                if (key?.GetValue("MemHighRamPercent") is int percent)
                    SelectRamThreshold(percent);
                else if (key?.GetValue("MemHighRamIndex") is int index && index >= 0 && index <= 2)
                    SelectRamThreshold(70 + index * 10);
                if (key?.GetValue("MemWidgetStyle") is int style && style >= 0 && style < cbWidgetStyle.Items.Count)
                    cbWidgetStyle.SelectedIndex = style;
                if (key?.GetValue("MemWidgetSize") is int size && size >= 0 && size < cbWidgetSize.Items.Count)
                    cbWidgetSize.SelectedIndex = size;
            }
            catch { }
            memoryOptionsLoaded = true;
            highRamTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            highRamTimer.Tick += async (s, e) => await CleanIfRamIsHighAsync();
            highRamTimer.Start();
        }

        private void cbWidgetStyle_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SaveMemoryOption("MemWidgetStyle", cbWidgetStyle.SelectedIndex);
            memorymon?.ApplyStyle(cbWidgetStyle.SelectedIndex != 1);
        }

        private static readonly double[] WidgetScales = { 0.6, 0.7, 0.8, 0.9, 1.0, 1.15, 1.3 };

        public double WidgetScale => cbWidgetSize == null ? 1 : WidgetScales[Math.Clamp(cbWidgetSize.SelectedIndex, 0, WidgetScales.Length - 1)];

        public void StepWidgetScale(int step) =>
            cbWidgetSize.SelectedIndex = Math.Clamp(cbWidgetSize.SelectedIndex + step, 0, cbWidgetSize.Items.Count - 1);

        private void cbWidgetSize_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SaveMemoryOption("MemWidgetSize", cbWidgetSize.SelectedIndex);
            memorymon?.ApplyScale(WidgetScale);
        }

        private void SaveMemoryOption(string name, int value)
        {
            if (!memoryOptionsLoaded) return;
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(MemoryOptionsKey, true);
                key.SetValue(name, value, RegistryValueKind.DWord);
            }
            catch { }
        }

        private static readonly System.Collections.Generic.Dictionary<string, string> SettingHelp = new()
        {
            ["chkEnableAutoClean"] = "Runs a cleaning automatically at the interval you choose, as long as Memory Cleaner is running.",
            ["chkCleanOnHighRam"] = "Checks RAM usage every 30 seconds. When it goes above the chosen percentage, a cleaning runs, at most once every 5 minutes.",
            ["chkSmartRAM"] = "Shows a small window with live RAM and CPU usage, a clean button and the apps using the most memory.",
            ["chkTopMostMonitor"] = "Keeps the widget above other windows so it stays visible.",
            ["chkClearTemp"] = "After cleaning memory, deletes the files in your temporary folder (%TEMP%). Files in use are skipped. This frees disk space, not RAM.",
            ["chkClearClipboard"] = "Empties the clipboard before cleaning, so anything you copied can no longer be pasted.",
            ["chkClearDns"] = "Forgets saved website addresses. This can fix connection problems, and the first visit to each site may take a moment longer. It frees very little RAM.",
            ["chkSystemMemory"] = "Besides programs, also trims the memory Windows itself uses, shrinks the file cache and releases low-priority cached data. This frees more memory, but files may open a little slower at first.",
            ["chkMergePages"] = "Finds memory pages with exactly the same content and keeps one copy. Programs keep working normally. It uses extra CPU time and helps most when many similar programs are open.",
            ["chkReleaseCache"] = "Empties the data Windows keeps in memory to open files and programs faster. Free memory rises the most, but data must be read from disk again, so the PC can feel slower for a while.",
            ["chkKeepForeground"] = "Does not clean the app in the window you are using, so it stays fast. The system-wide memory step is skipped too.",
            ["chkShowNotification"] = "When a cleaning finishes while this window is closed or minimized, shows a notification with how much memory was freed.",
            ["chkSkipOnLowBattery"] = "Automatic cleanings are skipped when the battery is below 30% or its level cannot be read. Cleaning by hand still works.",
            ["chkStartWithWinCleaner"] = "Loads Memory Cleaner in the background when Multron Win Cleaner starts, so automatic cleaning and the widget work without opening this window.",
        };

        private void SettingHelp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { TemplatedParent: CheckBox box } && SettingHelp.TryGetValue(box.Name, out string? text))
                AppDialog.Show(Loc.T(text), box.Content?.ToString() ?? "", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void AddExcludedApp(string name)
        {
            string app = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name.Substring(0, name.Length - 4) : name;
            if (app.Length == 0 || lstExcludedApps.Items.Cast<object>().Any(i => string.Equals(i.ToString(), app, StringComparison.OrdinalIgnoreCase)))
                return;
            lstExcludedApps.Items.Add(app);
            SaveExcludedApps();
        }

        private void SaveExcludedApps()
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(MemoryOptionsKey, true);
                key.SetValue("MemExcludedApps", lstExcludedApps.Items.Cast<object>().Select(i => i.ToString() ?? "").ToArray(), RegistryValueKind.MultiString);
            }
            catch { }
        }

        private void AddExcludedFile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Title = Loc.T("Select a program to exclude"), Filter = "*.exe|*.exe", Multiselect = true };
            if (dialog.ShowDialog(this) == true)
                foreach (string file in dialog.FileNames)
                    AddExcludedApp(Path.GetFileName(file));
        }

        private void AddExcludedRunning_Click(object sender, RoutedEventArgs e)
        {
            var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Process process in Process.GetProcesses())
            {
                try
                {
                    if (process.MainWindowHandle != IntPtr.Zero)
                        names.Add(process.ProcessName);
                }
                catch { }
                finally
                {
                    process.Dispose();
                }
            }
            var menu = new ContextMenu { PlacementTarget = sender as UIElement, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
            foreach (string name in names)
            {
                var item = new MenuItem { Header = name };
                item.Click += (s, args) => AddExcludedApp(name);
                menu.Items.Add(item);
            }
            menu.IsOpen = true;
        }

        private void RemoveExcluded_Click(object sender, RoutedEventArgs e)
        {
            if (lstExcludedApps.SelectedItem == null) return;
            lstExcludedApps.Items.Remove(lstExcludedApps.SelectedItem);
            SaveExcludedApps();
        }

        private async void MemOption_Changed(object sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox box)
                return;
            SaveMemoryOption((string)box.Tag, box.IsChecked == true ? 1 : 0);
            if (box == chkCleanOnHighRam && box.IsChecked == true && memoryOptionsLoaded)
                await CleanIfRamIsHighAsync();
        }

        private int RamThresholdPercent =>
            cbRamThreshold.SelectedItem is ComboBoxItem { Tag: string tag } && int.TryParse(tag, out int percent) ? percent : 80;

        private void SelectRamThreshold(int percent)
        {
            foreach (ComboBoxItem item in cbRamThreshold.Items)
                if (item.Tag is string tag && tag == percent.ToString())
                    cbRamThreshold.SelectedItem = item;
        }

        private async void cbRamThreshold_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!memoryOptionsLoaded)
                return;
            SaveMemoryOption("MemHighRamPercent", RamThresholdPercent);
            await CleanIfRamIsHighAsync();
        }

        private async Task CleanIfRamIsHighAsync()
        {
            if (chkCleanOnHighRam.IsChecked != true || !CleanButton.IsEnabled || DateTime.UtcNow - lastHighRamClean < TimeSpan.FromMinutes(5))
                return;
            if (chkSkipOnLowBattery.IsChecked == true && autocleanmem != null)
            {
                int battery = autocleanmem.GetBatteryPercent();
                if (battery < 30) return;
            }
            if (Processes.MemoryOptimizer.UsedPercent() < RamThresholdPercent)
                return;
            lastHighRamClean = DateTime.UtcNow;
            await cleanmemory();
        }

        private void btnResetWidgetLocation_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                
                        using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\MultronWinCleaner", true))
                        {
                            if (key != null)
                            {
                                key.DeleteValue("MemMonLeft", false);
                                key.DeleteValue("MemMonTop", false);
                            }
                        }
 
             
                    
                        memorymon.Close();
                        memorymon = new MemoryMon(this);
                        memorymon.Show();
                        memorymon.Activate();
                         
                     
              
            }
            catch (Exception ex)
            {
                AppDialog.Show(Loc.F("Error resetting location: {0}", ex.Message), Loc.T("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            StartRamGlow();
            EnterPanels();
            IsVisibleChanged += (s, a) =>
            {
                if (IsVisible)
                {
                    StartRamGlow();
                    EnterPanels();
                }
            };
            memorymon = new MemoryMon(this);
            if(this.chkSmartRAM.IsChecked == true)
            {
                memorymon.Show();
                if(chkTopMostMonitor.IsChecked == true)
                {
                    memorymon.Topmost = true;
                }
            }
            autocleanmem = new AutoCleanMem(this);
            InitializeSystemInfo();
            UpdateSystemStats();
            await CreateCpuCounterAsync();

            if (System.ComponentModel.DesignerProperties.GetIsInDesignMode(this))
            {
                return;
            }
         

        }
        private void InitializeSystemInfo()
        {
            try
            {
                using RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                CpuNameText.Text = key?.GetValue("ProcessorNameString")?.ToString() ?? "Unknown CPU";
            }
            catch (Exception)
            {
                CpuNameText.Text = Loc.T("CPU Information Unavailable");
            }
        }

        private async Task CreateCpuCounterAsync()
        {
            try
            {
                cpuCounter = await Task.Run(() =>
                {
                    var counter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                    counter.NextValue();
                    return counter;
                });
            }
            catch (Exception)
            {
                cpuCounter = null;
            }
        }

    
        public void UpdateSystemStats()
        {
            try
            {
                var computerInfo = new Microsoft.VisualBasic.Devices.ComputerInfo();
                ulong totalMemory = computerInfo.TotalPhysicalMemory;
                ulong availableMemory = computerInfo.AvailablePhysicalMemory;
                ulong usedMemory = totalMemory - availableMemory;

                double usedPercent = (double)usedMemory * 100 / totalMemory;

                double totalGb = totalMemory / (1024.0 * 1024.0 * 1024.0);
                double usedGb = usedMemory / (1024.0 * 1024.0 * 1024.0);
                RamInfoText.Text = $"RAM: {usedGb:F1} GB / {totalGb:F1} GB";

                if (cpuCounter != null)
                {
                    int currentCpuUsage = (int)cpuCounter.NextValue();
                    CpuUsageText.Text = Loc.F("CPU Load: {0}%", currentCpuUsage);
                }

                AnimateRam(usedPercent);
            }
            catch
            {
                
            }
        }

        private static readonly DependencyProperty ShownRamProperty = DependencyProperty.Register(
            "ShownRam", typeof(double), typeof(MemCleaner), new PropertyMetadata(-1.0, (d, e) => ((MemCleaner)d).DrawRam((double)e.NewValue)));

        private bool ramShown;
        private int glowTier = -1;

        private void DrawRam(double percent)
        {
            if (percent < 0)
                return;
            RamUsagePercentText.Text = $"{percent:F0}%";
            UpdateArcGraphic(percent);
        }

        private void AnimateRam(double percent)
        {
            SetRamGlow(percent);
            if (!ramShown || !Processes.Motion.Enabled || !IsVisible)
            {
                ramShown = true;
                BeginAnimation(ShownRamProperty, null);
                SetValue(ShownRamProperty, percent);
                return;
            }
            double from = (double)GetValue(ShownRamProperty);
            if (Math.Abs(from - percent) < 0.05)
                return;
            BeginAnimation(ShownRamProperty, new System.Windows.Media.Animation.DoubleAnimation(from, percent, TimeSpan.FromMilliseconds(650))
            {
                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut },
                SpeedRatio = 1
            });
        }

        private void SetRamGlow(double percent)
        {
            int tier = percent >= 90 ? 2 : percent >= 75 ? 1 : 0;
            if (tier == glowTier)
                return;
            glowTier = tier;
            var color = tier == 2 ? Color.FromRgb(0xE5, 0x48, 0x4D) : tier == 1 ? Color.FromRgb(0xFB, 0x8C, 0x00) : Color.FromRgb(0x1E, 0x88, 0xE5);
            var glow = new RadialGradientBrush();
            glow.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, color.R, color.G, color.B), 0.55));
            glow.GradientStops.Add(new GradientStop(Color.FromArgb(0x55, color.R, color.G, color.B), 0.74));
            glow.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, color.R, color.G, color.B), 1.0));
            glow.Freeze();
            RamGlow.Fill = glow;
        }

        private void StartRamGlow()
        {
            if (!Processes.Motion.Enabled)
            {
                RamGlow.BeginAnimation(OpacityProperty, null);
                RamGlowScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                RamGlowScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                RamGlow.Opacity = 0.6;
                return;
            }
            var ease = new System.Windows.Media.Animation.SineEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut };
            RamGlow.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0.45, 1, TimeSpan.FromMilliseconds(1800)) { AutoReverse = true, RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever, SpeedRatio = 1, EasingFunction = ease });
            var grow = new System.Windows.Media.Animation.DoubleAnimation(0.96, 1.05, TimeSpan.FromMilliseconds(1800)) { AutoReverse = true, RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever, SpeedRatio = 1, EasingFunction = ease };
            RamGlowScale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            RamGlowScale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        }

        private void SetRamSweep(bool on)
        {
            if (on)
            {
                RamSweep.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.FromMilliseconds(250)));
                RamSweepRotate.BeginAnimation(RotateTransform.AngleProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(1100)) { RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever, SpeedRatio = 1 });
                return;
            }
            var fade = new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(350));
            fade.Completed += (s, e) => RamSweepRotate.BeginAnimation(RotateTransform.AngleProperty, null);
            RamSweep.BeginAnimation(OpacityProperty, fade);
        }

        private void PopResult()
        {
            if (!Processes.Motion.Enabled)
                return;
            var pop = new ScaleTransform(0.7, 0.7);
            CleanedMemoryLabel.RenderTransform = pop;
            var bounce = new System.Windows.Media.Animation.DoubleAnimation(0.7, 1, TimeSpan.FromMilliseconds(420))
            {
                EasingFunction = new System.Windows.Media.Animation.BackEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut, Amplitude = 0.5 }
            };
            pop.BeginAnimation(ScaleTransform.ScaleXProperty, bounce);
            pop.BeginAnimation(ScaleTransform.ScaleYProperty, bounce);
            CleanedMemoryLabel.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)) { FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop });
        }

        private void EnterPanels()
        {
            if (!Processes.Motion.Enabled)
                return;
            int delay = 0;
            foreach (FrameworkElement panel in new FrameworkElement[] { RamCard, MemOptionsPanel })
            {
                var shift = new TranslateTransform(0, 16);
                panel.RenderTransform = shift;
                var ease = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
                var move = new System.Windows.Media.Animation.DoubleAnimation(16, 0, TimeSpan.FromMilliseconds(420)) { EasingFunction = ease, BeginTime = TimeSpan.FromMilliseconds(delay) };
                var target = panel;
                move.Completed += (s, e) =>
                {
                    if (ReferenceEquals(target.RenderTransform, shift))
                        target.ClearValue(RenderTransformProperty);
                };
                shift.BeginAnimation(TranslateTransform.YProperty, move);
                panel.Opacity = 0;
                var fade = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(380)) { EasingFunction = ease, BeginTime = TimeSpan.FromMilliseconds(delay) };
                fade.Completed += (s, e) =>
                {
                    target.BeginAnimation(OpacityProperty, null);
                    target.Opacity = 1;
                };
                panel.BeginAnimation(OpacityProperty, fade);
                delay += 90;
            }
        }

        private void UpdateArcGraphic(double usedPercent)
        {
            double stroke = 10;
            double size = 150;
            double radius = (size / 2.0) - (stroke / 2.0);
            double center = size / 2.0;

            double startAngle = -90;
            double sweepAngle = usedPercent * 360.0 / 100.0;

            sweepAngle = Math.Clamp(sweepAngle, 0.01, 359.99);
            double endAngle = startAngle + sweepAngle;

            double startRadians = Math.PI * startAngle / 180.0;
            double endRadians = Math.PI * endAngle / 180.0;

            Point startPoint = new Point(center + radius * Math.Cos(startRadians), center + radius * Math.Sin(startRadians));
            Point endPoint = new Point(center + radius * Math.Cos(endRadians), center + radius * Math.Sin(endRadians));

            var figure = new PathFigure
            {
                StartPoint = startPoint,
                IsClosed = false,
                Segments = new PathSegmentCollection
                {
                    new ArcSegment
                    {
                        Point = endPoint,
                        Size = new Size(radius, radius),
                        IsLargeArc = sweepAngle > 180,
                        SweepDirection = SweepDirection.Clockwise
                    }
                }
            };

            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);

            ArcPath.Stretch = Stretch.None;
            ArcPath.RenderTransform = new TranslateTransform(0, 0);
            ArcPath.Data = geometry;
        }
 
        public async Task cleanmemory()
        {
            CleanButton.IsEnabled = false;
            CleanButton.Content = Loc.T("Cleaning...");
            CleanedMemoryLabel.Text = "";
            SetRamSweep(true);
            memorymon?.SetCleaning(true);

            bool clearTempFiles = chkClearTemp.IsChecked == true;
            bool clearClipboardData = chkClearClipboard.IsChecked == true;
            bool clearDns = chkClearDns.IsChecked == true;
            var excludedApps = new System.Collections.Generic.HashSet<string>(lstExcludedApps.Items.Cast<object>().Select(i => i.ToString() ?? ""), StringComparer.OrdinalIgnoreCase);

            var cleanOptions = new Processes.MemoryOptimizer.Options(SelectedRepeats, chkKeepForeground.IsChecked == true, chkSystemMemory.IsChecked == true, chkReleaseCache.IsChecked == true, chkMergePages.IsChecked == true, excludedApps);
            var memoryBefore = await Task.Run(Processes.MemoryOptimizer.Read);

            if (clearClipboardData)
            {
                try { Clipboard.Clear(); } catch {   }
            }

            await Task.Run(() =>
            {
                Processes.MemoryOptimizer.Clean(cleanOptions);
                if (clearDns)
                    Processes.MemoryOptimizer.FlushDnsCache();

                try
                {
                    using Process currentProc = Process.GetCurrentProcess();
                    EmptyWorkingSet(currentProc.Handle);
                }
                catch { }

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                if (clearTempFiles)
                {
                    CleanWindowsTemp();
                }
            });

            var memoryAfter = await Task.Run(Processes.MemoryOptimizer.Read);
            long freed = Processes.MemoryOptimizer.Freed(memoryBefore, memoryAfter);
            double mbFreed = freed / (1024.0 * 1024.0);

            string freedMessage = Loc.F("Freed {0:F2} MB {1:T}", mbFreed, DateTime.Now);
            SetRamSweep(false);
            CleanedMemoryLabel.Text = freedMessage;
            PopResult();

            if (memorymon != null)
                memorymon.ShowCleanResult(freedMessage, mbFreed);

            CleanButton.Content = Loc.T("Clean Memory Now");
            CleanButton.IsEnabled = true;
             
            UpdateSystemStats();

            if (chkShowNotification.IsChecked == true && (this.WindowState == WindowState.Minimized || !this.IsVisible))
            {
                Notify notify = new Notify(Loc.T("Memory Cleaned"), Loc.T("Your system memory has been optimized successfully.") + "\r\n", Loc.F("Freed {0:F2} MB", mbFreed));
                notify.Styled(Notify.Kind.Success, "\uE9D9").Show();
            }
        }

        private void CleanWindowsTemp()
        {
            string tempPath = Path.GetTempPath();
            DirectoryInfo di = new DirectoryInfo(tempPath);

            if (!di.Exists) return;

            foreach (FileInfo file in di.GetFiles())
            {
                try { file.Delete(); } catch {   }
            }
            foreach (DirectoryInfo dir in di.GetDirectories())
            {
                try { dir.Delete(true); } catch {   }
            }
        }

        #region Window & UI Events

        private async void CleanMemoryButton_Click(object sender, RoutedEventArgs e)
        {
            await cleanmemory();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => this.Visibility = Visibility.Collapsed;
        private void MinimizeButton_Click(object sender, RoutedEventArgs e) => this.WindowState = WindowState.Minimized;

        private void WindowDragMove(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                this.DragMove();
        }

        private void cbCleanInterval_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (autocleanmem == null) return;
            autocleanmem.StopAutoClean();
            if (chkEnableAutoClean.IsChecked == true)
            {
                autocleanmem.StartAutoClean();
            }
        }

        private void chkTopMostMonitor_Checked(object sender, RoutedEventArgs e) { window.utilities.savesettings("topmost:1"); if (memorymon != null) memorymon.Topmost = true; }
        private void chkTopMostMonitor_Unchecked(object sender, RoutedEventArgs e) { if (window != null) window.utilities.savesettings("topmost:0"); if (memorymon != null) memorymon.Topmost = false; }
        private void chkSmartRAM_Checked(object sender, RoutedEventArgs e) {  window.utilities.savesettings("mon:1"); memorymon = new MemoryMon(this); memorymon.Show(); }
        private void chkSmartRAM_Unchecked(object sender, RoutedEventArgs e) { memorymon?.Close(); window.utilities.savesettings("mon:0"); }
        private void chkEnableAutoClean_Checked(object sender, RoutedEventArgs e) { window.utilities.savesettings("automemclean:1"); autocleanmem?.StartAutoClean(); }
        private void chkEnableAutoClean_UnChecked(object sender, RoutedEventArgs e) { window.utilities.savesettings("automemclean:0"); autocleanmem?.StopAutoClean(); }
        private void chkShowNotification_Checked(object sender, RoutedEventArgs e) { window.utilities.savesettings("sendnotify:1"); }
        private void chkShowNotification_UnChecked(object sender, RoutedEventArgs e) { window.utilities.savesettings("sendnotify:0"); }
        private void chkSkipOnLowBattery_Checked(object sender, RoutedEventArgs e) { window.utilities.savesettings("skipiflow:1"); }
        private void chkSkipOnLowBattery_UnChecked(object sender, RoutedEventArgs e) { window.utilities.savesettings("skipiflow:0"); }
        private void chkClearTemp_Checked(object sender, RoutedEventArgs e) { if (window != null) window.utilities.savesettings("cleartemp:1"); }
        private void chkClearTemp_Unchecked(object sender, RoutedEventArgs e)  { if (window != null) window.utilities.savesettings("cleartemp:0"); }
        private void chkClearClipboard_Checked(object sender, RoutedEventArgs e)  { if (window != null) window.utilities.savesettings("clearclip:1");   }
        private void chkClearClipboard_Unchecked(object sender, RoutedEventArgs e) { if (window != null) window.utilities.savesettings("clearclip:0"); }
 
        private void chkStartWithWinCleaner_Checked(object sender, RoutedEventArgs e) { if (window != null) window.utilities.savesettings("startwithwincleaner:1"); }
        private void chkStartWithWinCleaner_UnChecked(object sender, RoutedEventArgs e) { if (window != null) window.utilities.savesettings("startwithwincleaner:0"); }

        #endregion

        #region AutoClean Class
        public class AutoCleanMem
        {
            private readonly MemCleaner memcleaner;
            private CancellationTokenSource? _autoCleanCts;

            public AutoCleanMem(MemCleaner memcleaner)
            {
                this.memcleaner = memcleaner;
            }

            public async void StartAutoClean()
            {
                _autoCleanCts?.Cancel();
                _autoCleanCts = new CancellationTokenSource();
                var token = _autoCleanCts.Token;

                int intervalMinutes = 5;

                await memcleaner.Dispatcher.InvokeAsync(() =>
                {
                    if (memcleaner.cbCleanInterval.SelectedItem is ComboBoxItem selectedItem)
                    {
                        string content = Loc.En(selectedItem.Content).ToLower();
                        intervalMinutes = content switch
                        {
                            "1 minute" => 1,
                            "10 minutes" => 10,
                            "15 minutes" => 15,
                            "30 minutes" => 30,
                            "1 hour" => 60,
                            "2 hours" => 120,
                            _ => 5
                        };
                    }
                });

                memcleaner.window.utilities.savesettings($"mcminutes:{intervalMinutes} minutes");

                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        for (int i = intervalMinutes * 60; i > 0; i--)
                        {
                            token.ThrowIfCancellationRequested();

                            int mins = i / 60;
                            int secs = i % 60;

                            memcleaner.Dispatcher.Invoke(() =>
                            {
                                memcleaner.Status.Text = Loc.F("Next clean: {0:D2}:{1:D2}", mins, secs);
                            });

                            await Task.Delay(1000, token);
                        }

                        bool isSkipChecked = await memcleaner.Dispatcher.InvokeAsync(() => memcleaner.chkSkipOnLowBattery.IsChecked == true);

                        if (isSkipChecked)
                        {
                            int battery = GetBatteryPercent();
                            if (battery == -1)
                            {
                                memcleaner.Dispatcher.Invoke(() => memcleaner.Status.Text = Loc.T("Battery unknown, skipping..."));
                                await Task.Delay(5000, token);
                            }
                            else if (battery <= 30)
                            {
                                memcleaner.Dispatcher.Invoke(() => memcleaner.Status.Text = Loc.T("Skipping clean (Low battery)."));
                                await Task.Delay(5000, token);
                                continue;
                            }
                        }

                        await memcleaner.Dispatcher.InvokeAsync(() => memcleaner.cleanmemory());
                    }
                }
                catch (OperationCanceledException)
                {
                  
                }
            }

            public void StopAutoClean()
            {
                if (_autoCleanCts != null)
                {
                    _autoCleanCts.Cancel();
                    _autoCleanCts.Dispose();
                    _autoCleanCts = null;
                }

                memcleaner.Dispatcher.Invoke(() =>
                {
                    memcleaner.Status.Text = Loc.T("Auto clean stopped.");
                });
            }

            public int GetBatteryPercent()
            {
                GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);
                return status.BatteryLifePercent == 255 ? -1 : status.BatteryLifePercent;
            }
        }
        #endregion
    }
}
