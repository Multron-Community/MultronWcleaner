using Microsoft.VisualBasic.Devices;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace MultronWinCleaner
{
    public partial class MemoryMon : Window
    {
        private readonly MemCleaner _memcleaner;
        private bool _processesVisible = false;
        private readonly ObservableCollection<ProcessItem> _processes = new ObservableCollection<ProcessItem>();
        private readonly DispatcherTimer _updateTimer;
        private bool _isUpdatingProcesses = false;

      
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        private readonly Dictionary<int, TimeSpan> _lastCpuTimes = new Dictionary<int, TimeSpan>();
        private DateTime _lastCpuCheckTime = DateTime.Now;
        private readonly Dictionary<string, ImageSource> _iconCache = new Dictionary<string, ImageSource>();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        public MemoryMon(MemCleaner memcleaner)
        {
            InitializeComponent();
            _memcleaner = memcleaner;

            Loaded += MemoryMon_Loaded;
            Closed += MemoryMon_Closed;

            ProcessesListView.ItemsSource = _processes;
             
            var statusMonitor = new SystemStatusMonitor(this, _cts.Token);
            _ = Task.Run(() => statusMonitor.RunAsync());

            _updateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _updateTimer.Tick += UpdateTimer_Tick;

            Topmost = !_memcleaner.chkTopMostMonitor.IsEnabled;
            ApplyStyle(_memcleaner.cbWidgetStyle.SelectedIndex != 1);
            ApplyScale(_memcleaner.WidgetScale);
        }

        private double _scale = 1;

        public void ApplyScale(double scale)
        {
            _scale = scale;
            WidgetRoot.LayoutTransform = Math.Abs(scale - 1) < 0.001 ? Transform.Identity : new ScaleTransform(scale, scale);
            if (!_compact)
                Width = 216 * scale;
        }

        private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if ((System.Windows.Input.Keyboard.Modifiers & ModifierKeys.Control) == 0 || _memcleaner == null)
                return;
            _memcleaner.StepWidgetScale(e.Delta > 0 ? 1 : -1);
            e.Handled = true;
        }

        private bool _compact;

        public void ApplyStyle(bool compact)
        {
            _compact = compact;
            ClassicRoot.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            CompactRoot.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
            if (!compact)
                SetExpanded(false);
            if (compact)
            {
                SizeToContent = SizeToContent.WidthAndHeight;
                Width = double.NaN;
            }
            else
            {
                SizeToContent = SizeToContent.Height;
                Width = 216 * _scale;
            }
        }

        private void CompactToggle_Click(object sender, RoutedEventArgs e) => SetExpanded(!_expanded);

        private bool _expanded;
        private string _tab = "ram";
        private DispatcherTimer? _monitorTimer;
        private bool _monitorBusy;
        private readonly ObservableCollection<MonitorItem> _monitorItems = new();
        private readonly Dictionary<int, TimeSpan> _monitorCpu = new();
        private readonly Dictionary<int, ulong> _monitorIo = new();
        private DateTime _monitorSampleTime = DateTime.UtcNow;
        private readonly Dictionary<string, string> _descriptionCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> ProtectedProcesses = new(StringComparer.OrdinalIgnoreCase)
        {
            "System", "Idle", "Registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "lsaiso", "svchost",
            "fontdrvhost", "dwm", "Memory Compression", "Secure System", "MsMpEng", "explorer"
        };

        private void SetExpanded(bool expanded)
        {
            _expanded = expanded;
            CleanToast.Visibility = Visibility.Collapsed;
            CompactPill.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
            bool wasExpanded = ExpandedPanel.Visibility == Visibility.Visible;
            ExpandedPanel.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            if (expanded && !wasExpanded)
                Processes.Motion.Reveal(ExpandedPanel, -10);
            if (expanded)
            {
                if (_monitorTimer == null)
                {
                    MonitorList.ItemsSource = _monitorItems;
                    _monitorTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                    _monitorTimer.Tick += async (s, e) => await RefreshMonitorListAsync();
                }
                UpdateTabTexts();
                _monitorTimer.Start();
                _ = RefreshMonitorListAsync();
            }
            else
            {
                _monitorTimer?.Stop();
            }
        }

        public sealed class MonitorItem : System.ComponentModel.INotifyPropertyChanged
        {
            private bool _isChecked;
            private string _valueText = "";
            public string Name { get; init; } = "";
            public string DisplayName { get; init; } = "";
            public ImageSource? Icon { get; init; }
            public List<int> Ids { get; set; } = new();
            public string ValueText { get => _valueText; set { _valueText = value; Changed(nameof(ValueText)); } }
            public bool IsChecked { get => _isChecked; set { _isChecked = value; Changed(nameof(IsChecked)); } }
            public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
            private void Changed(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IoCounters
        {
            public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetProcessIoCounters(IntPtr process, out IoCounters counters);

        private void GaugeTab_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.Tag is not string tab || !IsLoaded)
                return;
            _tab = tab;
            UpdateTabTexts();
            _ = RefreshMonitorListAsync();
        }

        private string MetricTab => _tab == "cpu" || _tab == "disk" ? _tab : "ram";

        private void UpdateTabTexts()
        {
            ValueHeader.Text = MetricTab.ToUpperInvariant();
            ListSummary.Text = Loc.T(MetricTab switch
            {
                "cpu" => "Processes with high CPU usage",
                "disk" => "Processes with high disk activity",
                _ => "Processes with high RAM usage"
            });
        }

        private static string FormatBytes(double bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            int unit = 0;
            while (bytes >= 1024 && unit < units.Length - 1)
            {
                bytes /= 1024;
                unit++;
            }
            return unit == 0 ? $"{bytes:0} {units[unit]}" : $"{bytes:0.##} {units[unit]}";
        }

        private string DescriptionOf(Process process)
        {
            if (_descriptionCache.TryGetValue(process.ProcessName, out var cached))
                return cached;
            string text = process.ProcessName;
            try
            {
                string? path = process.MainModule?.FileName;
                if (!string.IsNullOrEmpty(path))
                {
                    string? description = FileVersionInfo.GetVersionInfo(path).FileDescription;
                    if (!string.IsNullOrWhiteSpace(description))
                        text = description.Trim();
                }
            }
            catch { }
            _descriptionCache[process.ProcessName] = text;
            return text;
        }

        private async Task RefreshMonitorListAsync()
        {
            if (_monitorBusy || !_expanded || Dispatcher.HasShutdownStarted)
                return;
            _monitorBusy = true;
            string metric = MetricTab;
            var checkedNames = new HashSet<string>(_monitorItems.Where(i => i.IsChecked).Select(i => i.Name), StringComparer.OrdinalIgnoreCase);
            try
            {
                var rows = await Task.Run(() =>
                {
                    var now = DateTime.UtcNow;
                    double seconds = Math.Max(0.5, (now - _monitorSampleTime).TotalSeconds);
                    _monitorSampleTime = now;
                    int self = Environment.ProcessId;
                    var groups = new Dictionary<string, (double Value, List<int> Ids, Process Sample)>(StringComparer.OrdinalIgnoreCase);
                    var seen = new HashSet<int>();
                    foreach (var process in Process.GetProcesses())
                    {
                        if (process.Id == 0 || process.Id == 4)
                        {
                            process.Dispose();
                            continue;
                        }
                        seen.Add(process.Id);
                        double value = 0;
                        try
                        {
                            if (metric == "ram")
                                value = process.WorkingSet64;
                            else if (metric == "cpu")
                            {
                                var cpu = process.TotalProcessorTime;
                                if (_monitorCpu.TryGetValue(process.Id, out var previous))
                                    value = (cpu - previous).TotalMilliseconds / (seconds * 1000) / Environment.ProcessorCount * 100;
                                _monitorCpu[process.Id] = cpu;
                            }
                            else if (GetProcessIoCounters(process.Handle, out var io))
                            {
                                ulong total = io.ReadTransferCount + io.WriteTransferCount + io.OtherTransferCount;
                                if (_monitorIo.TryGetValue(process.Id, out var previous) && total >= previous)
                                    value = (total - previous) / seconds;
                                _monitorIo[process.Id] = total;
                            }
                        }
                        catch { }
                        string name = process.ProcessName;
                        if (groups.TryGetValue(name, out var group))
                        {
                            group.Ids.Add(process.Id);
                            groups[name] = (group.Value + value, group.Ids, group.Sample);
                            process.Dispose();
                        }
                        else
                        {
                            groups[name] = (value, new List<int> { process.Id }, process);
                        }
                    }
                    foreach (int gone in _monitorCpu.Keys.Where(id => !seen.Contains(id)).ToList())
                        _monitorCpu.Remove(gone);
                    foreach (int gone in _monitorIo.Keys.Where(id => !seen.Contains(id)).ToList())
                        _monitorIo.Remove(gone);

                    var top = groups.OrderByDescending(g => g.Value.Value).Take(20).ToList();
                    var result = top.Select(g => (Name: g.Key, Display: DescriptionOf(g.Value.Sample), Icon: GetProcessIcon(g.Value.Sample, g.Key), g.Value.Value, g.Value.Ids)).ToList();
                    foreach (var g in groups.Values)
                        g.Sample.Dispose();
                    return result;
                });

                double total = rows.Sum(r => r.Value);
                string Format(double value) => metric switch
                {
                    "cpu" => $"{Math.Round(value, 1)}%",
                    "disk" => RateText(value),
                    _ => FormatBytes(value)
                };
                _monitorItems.Clear();
                foreach (var row in rows)
                {
                    var item = new MonitorItem { Name = row.Name, DisplayName = row.Display, Icon = row.Icon, Ids = row.Ids, ValueText = Format(row.Value), IsChecked = checkedNames.Contains(row.Name) };
                    item.PropertyChanged += (s, e) => UpdateEndTaskButton();
                    _monitorItems.Add(item);
                }
                ListTotal.Text = metric == "ram" ? Loc.F("total {0}", FormatBytes(total)) : string.Empty;
                UpdateEndTaskButton();
            }
            catch { }
            finally
            {
                _monitorBusy = false;
            }
        }

        private void UpdateEndTaskButton() => EndTaskButton.IsEnabled = _monitorItems.Any(i => i.IsChecked);

        private async void EndTask_Click(object sender, RoutedEventArgs e)
        {
            var chosen = _monitorItems.Where(i => i.IsChecked).Select(i => (i.Name, i.Ids)).ToList();
            string? result = await EndProcessesAsync(chosen);
            if (result == null)
                return;
            MonitorStatus.Text = result;
            await RefreshMonitorListAsync();
        }

        private void UpdateClassicEndTask() => ClassicEndTaskButton.IsEnabled = _processes.Any(i => i.IsChecked);

        private async void ClassicEndTask_Click(object sender, RoutedEventArgs e)
        {
            var chosen = _processes.Where(i => i.IsChecked).Select(i => (i.Name, new List<int> { i.Id })).ToList();
            string? result = await EndProcessesAsync(chosen);
            if (result == null)
                return;
            FreedLabel.Text = result;
            UpdateTimer_Tick(this, EventArgs.Empty);
        }

        private async Task<string?> EndProcessesAsync(List<(string Name, List<int> Ids)> chosen)
        {
            if (chosen.Count == 0)
                return null;
            if (AppDialog.Show(this, Loc.F("End {0} selected app(s)? Unsaved work in these apps will be lost.", chosen.Count), Loc.T("End Task"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return null;

            int self = Environment.ProcessId;
            int ended = 0, failed = 0;
            await Task.Run(() =>
            {
                foreach (var (name, ids) in chosen)
                {
                    if (ProtectedProcesses.Contains(name))
                    {
                        failed++;
                        continue;
                    }
                    bool any = false;
                    foreach (int id in ids)
                    {
                        if (id == self)
                            continue;
                        try
                        {
                            using var process = Process.GetProcessById(id);
                            process.Kill();
                            any = true;
                        }
                        catch { }
                    }
                    if (any) ended++; else failed++;
                }
            });
            return failed == 0 ? Loc.F("Ended {0} app(s).", ended) : Loc.F("Ended {0} app(s).", ended) + " " + Loc.F("{0} could not be ended.", failed);
        }

        private void OpenMemoryCleaner_Click(object sender, RoutedEventArgs e)
        {
            if (_memcleaner == null)
                return;
            _memcleaner.Visibility = Visibility.Visible;
            _memcleaner.Show();
            if (_memcleaner.WindowState == WindowState.Minimized)
                _memcleaner.WindowState = WindowState.Normal;
            var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
            var area = SystemParameters.WorkArea;
            double width = _memcleaner.ActualWidth > 0 ? _memcleaner.ActualWidth : _memcleaner.Width;
            double height = _memcleaner.ActualHeight > 0 ? _memcleaner.ActualHeight : _memcleaner.Height;
            if (double.IsNaN(_memcleaner.Left) || double.IsNaN(_memcleaner.Top) ||
                _memcleaner.Left + 80 > screen.Right || _memcleaner.Left + width < screen.Left + 80 ||
                _memcleaner.Top + 40 > screen.Bottom || _memcleaner.Top < screen.Top - 20)
            {
                _memcleaner.Left = area.Left + Math.Max(0, (area.Width - width) / 2);
                _memcleaner.Top = area.Top + Math.Max(0, (area.Height - height) / 2);
            }
            _memcleaner.Activate();
            _memcleaner.Topmost = true;
            _memcleaner.Topmost = false;
            _memcleaner.Focus();
        }

        private DispatcherTimer? _toastTimer;

        private bool _cleaning;
        private DispatcherTimer? _doneTimer;

        public void SetCleaning(bool cleaning)
        {
            if (_cleaning == cleaning)
                return;
            _cleaning = cleaning;
            var rotate = new RotateTransform(0, 13.4, 1);
            PillBroom.RenderTransform = rotate;
            var sparkle = new ScaleTransform(1, 1, 2.6, 3.1);
            PillSparkle.RenderTransform = sparkle;
            var shine = PillCleanButton.Template?.FindName("Shine", PillCleanButton) as System.Windows.Controls.Border;
            var done = PillCleanButton.Template?.FindName("Done", PillCleanButton) as System.Windows.Controls.Border;
            _doneTimer?.Stop();
            PillSparkle.BeginAnimation(OpacityProperty, null);
            PillCheck.BeginAnimation(OpacityProperty, null);
            PillBroom.BeginAnimation(OpacityProperty, null);
            done?.BeginAnimation(OpacityProperty, null);
            if (cleaning)
            {
                PillCheck.Opacity = 0;
                PillBroom.Opacity = 1;
                if (done != null) done.Opacity = 0;
                if (!Processes.Motion.Enabled)
                    return;
                rotate.BeginAnimation(RotateTransform.AngleProperty, Loop(-20, 16, 260));
                sparkle.BeginAnimation(ScaleTransform.ScaleXProperty, Loop(0.5, 1.25, 380));
                sparkle.BeginAnimation(ScaleTransform.ScaleYProperty, Loop(0.5, 1.25, 380));
                PillSparkle.BeginAnimation(OpacityProperty, Loop(0.35, 1, 380));
                if (shine != null)
                {
                    var move = new TranslateTransform(-1, 0);
                    shine.Background = new LinearGradientBrush
                    {
                        StartPoint = new Point(0, 0.5),
                        EndPoint = new Point(1, 0.5),
                        RelativeTransform = move,
                        GradientStops =
                        {
                            new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.3),
                            new GradientStop(Color.FromArgb(0x5A, 255, 255, 255), 0.5),
                            new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.7)
                        }
                    };
                    shine.Opacity = 1;
                    move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-1, 1, TimeSpan.FromMilliseconds(1100)) { RepeatBehavior = RepeatBehavior.Forever, SpeedRatio = 1 });
                }
                return;
            }
            if (shine != null)
                shine.Opacity = 0;
            PillSparkle.Opacity = 1;
            if (!Processes.Motion.Enabled)
                return;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            PillBroom.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(140)) { FillBehavior = FillBehavior.HoldEnd });
            var checkScale = new ScaleTransform(0.3, 0.3);
            PillCheck.RenderTransformOrigin = new Point(0.5, 0.5);
            PillCheck.RenderTransform = checkScale;
            var pop = new DoubleAnimation(0.3, 1, TimeSpan.FromMilliseconds(420)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 }, BeginTime = TimeSpan.FromMilliseconds(80) };
            checkScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            checkScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
            PillCheck.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)) { BeginTime = TimeSpan.FromMilliseconds(80), FillBehavior = FillBehavior.HoldEnd });
            done?.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease, FillBehavior = FillBehavior.HoldEnd });
            _doneTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1600) };
            _doneTimer.Tick -= DoneTimer_Tick;
            _doneTimer.Tick += DoneTimer_Tick;
            _doneTimer.Start();
        }

        private void DoneTimer_Tick(object? sender, EventArgs e)
        {
            _doneTimer?.Stop();
            if (_cleaning)
                return;
            var done = PillCleanButton.Template?.FindName("Done", PillCleanButton) as System.Windows.Controls.Border;
            var fade = TimeSpan.FromMilliseconds(380);
            done?.BeginAnimation(OpacityProperty, new DoubleAnimation(0, fade));
            PillCheck.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(200)));
            PillBroom.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, fade) { BeginTime = TimeSpan.FromMilliseconds(120) });
        }

        private static DoubleAnimation Loop(double from, double to, int ms) =>
            new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                SpeedRatio = 1,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };

        private void PopToastIcon()
        {
            if (!Processes.Motion.Enabled)
                return;
            var scale = new ScaleTransform(0.4, 0.4);
            ToastIcon.RenderTransformOrigin = new Point(0.5, 0.5);
            ToastIcon.RenderTransform = scale;
            var pop = new DoubleAnimation(0.4, 1, TimeSpan.FromMilliseconds(480)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.7 } };
            pop.Completed += (s, e) => ToastIcon.RenderTransform = Transform.Identity;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
            var ring = new ScaleTransform(1, 1);
            ToastRing.RenderTransformOrigin = new Point(0.5, 0.5);
            ToastRing.RenderTransform = ring;
            var grow = new DoubleAnimation(0.9, 1.7, TimeSpan.FromMilliseconds(700)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, BeginTime = TimeSpan.FromMilliseconds(120) };
            ring.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            ring.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            ToastRing.BeginAnimation(OpacityProperty, new DoubleAnimation(0.8, 0, TimeSpan.FromMilliseconds(700)) { BeginTime = TimeSpan.FromMilliseconds(120), FillBehavior = FillBehavior.Stop });
        }

        public void ShowCleanResult(string message, double megabytesFreed)
        {
            SetCleaning(false);
            FreedLabel.Text = message;
            ToastFreedText.Text = Loc.F("Freed {0:F2} MB", megabytesFreed);
            ToastRamText.Text = Loc.F("RAM now {0}%", PillRamValue.Text);
            if (!_compact)
                return;
            if (_expanded)
            {
                MonitorStatus.Text = ToastFreedText.Text;
                return;
            }
            bool toastShown = CleanToast.Visibility == Visibility.Visible;
            CleanToast.Visibility = Visibility.Visible;
            if (!toastShown)
                Processes.Motion.Reveal(CleanToast, -10);
            PopToastIcon();
            _toastTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
            _toastTimer.Tick -= ToastTimer_Tick;
            _toastTimer.Tick += ToastTimer_Tick;
            _toastTimer.Stop();
            _toastTimer.Start();
        }

        private void ToastTimer_Tick(object? sender, EventArgs e)
        {
            _toastTimer?.Stop();
            CleanToast.Visibility = Visibility.Collapsed;
        }

        private void CloseToast_Click(object sender, RoutedEventArgs e) => ToastTimer_Tick(this, EventArgs.Empty);

        private static void SetRing(System.Windows.Shapes.Ellipse ring, double fraction)
        {
            double length = Math.PI * (ring.Width > 0 ? ring.Width : 52 - ring.StrokeThickness) / ring.StrokeThickness;
            length = Math.PI * (52 - ring.StrokeThickness) / ring.StrokeThickness;
            double shown = Math.Clamp(fraction, 0, 1) * length;
            ring.StrokeDashArray = new DoubleCollection { shown <= 0.01 ? 0.001 : shown, 1000 };
            ring.Opacity = fraction <= 0.001 ? 0 : 1;
        }

        public static (string Value, string Unit) FormatRate(double bytesPerSecond)
        {
            string[] units = { "B/s", "KB/s", "MB/s", "GB/s" };
            int unit = 0;
            double value = Math.Max(0, bytesPerSecond);
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }
            string text = unit == 0 || value >= 100 ? Math.Round(value).ToString("0") : value.ToString("0.0");
            return (text, units[unit]);
        }

        private static string RateText(double bytesPerSecond)
        {
            var (value, unit) = FormatRate(bytesPerSecond);
            return value + " " + unit;
        }

        public class ProcessItem : System.ComponentModel.INotifyPropertyChanged
        {
            private bool _isChecked;
            public ImageSource? Icon { get; set; }
            public string Name { get; set; } = "";
            public string Cpu { get; set; } = "";
            public string Ram { get; set; } = "";
            public int Id { get; set; }
            public bool IsChecked { get => _isChecked; set { _isChecked = value; PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsChecked))); } }
            public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        }

        public class SystemStatusMonitor
        {
            private readonly MemoryMon _window;
            private readonly CancellationToken _cancellationToken;
            private double _lastRamPercent = -1;
            private int _lastCpuValue = -1;
            private PerformanceCounter? _diskIdle;
            private bool _diskTried;
            private bool _tempFailed;
            private int _tempCountdown;
            private double _lastTemp = -1;

            private double ReadTemperature()
            {
                if (_tempFailed)
                    return -1;
                if (_tempCountdown-- > 0)
                    return _lastTemp;
                _tempCountdown = 4;
                try
                {
                    using var searcher = new System.Management.ManagementObjectSearcher(@"root\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
                    double best = -1;
                    foreach (System.Management.ManagementObject item in searcher.Get())
                    {
                        using (item)
                        {
                            double celsius = Convert.ToDouble(item["CurrentTemperature"]) / 10 - 273.15;
                            if (celsius > 0 && celsius < 130)
                                best = Math.Max(best, celsius);
                        }
                    }
                    if (best < 0)
                        _tempFailed = true;
                    _lastTemp = best;
                    return best;
                }
                catch
                {
                    _tempFailed = true;
                    return -1;
                }
            }

            private long _prevReceived = -1;
            private long _prevSent = -1;
            private DateTime _prevNetTime;

            private int ReadDiskPercent()
            {
                if (!_diskTried)
                {
                    _diskTried = true;
                    try
                    {
                        _diskIdle = new PerformanceCounter("PhysicalDisk", "% Idle Time", "_Total");
                        _diskIdle.NextValue();
                    }
                    catch
                    {
                        _diskIdle = null;
                    }
                }
                if (_diskIdle == null)
                    return -1;
                try
                {
                    return (int)Math.Round(Math.Clamp(100 - _diskIdle.NextValue(), 0, 100));
                }
                catch
                {
                    return -1;
                }
            }

            private (double Down, double Up) ReadNetRates()
            {
                long received = 0, sent = 0;
                try
                {
                    foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
                    {
                        if (adapter.OperationalStatus != OperationalStatus.Up ||
                            adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                            adapter.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                            continue;
                        var stats = adapter.GetIPStatistics();
                        received += stats.BytesReceived;
                        sent += stats.BytesSent;
                    }
                }
                catch
                {
                    return (0, 0);
                }

                var now = DateTime.UtcNow;
                double seconds = (now - _prevNetTime).TotalSeconds;
                bool first = _prevReceived < 0;
                double down = first || seconds <= 0 ? 0 : Math.Max(0, received - _prevReceived) / seconds;
                double up = first || seconds <= 0 ? 0 : Math.Max(0, sent - _prevSent) / seconds;
                _prevReceived = received;
                _prevSent = sent;
                _prevNetTime = now;
                return (down, up);
            }

            private ulong _prevIdleTime = 0;
            private ulong _prevKernelTime = 0;
            private ulong _prevUserTime = 0;

            [StructLayout(LayoutKind.Sequential)]
            private struct FILETIME
            {
                public uint dwLowDateTime;
                public uint dwHighDateTime;
            }

            [DllImport("kernel32.dll", SetLastError = true)]
            private static extern bool GetSystemTimes(out FILETIME idleTime, out FILETIME kernelTime, out FILETIME userTime);

            public SystemStatusMonitor(MemoryMon window, CancellationToken cancellationToken)
            {
                _window = window;
                _cancellationToken = cancellationToken;
                UpdateCpuTimes(out _prevIdleTime, out _prevKernelTime, out _prevUserTime);
            }

            private void UpdateCpuTimes(out ulong idleTime, out ulong kernelTime, out ulong userTime)
            {
                GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);
                idleTime = ((ulong)idle.dwHighDateTime << 32) | idle.dwLowDateTime;
                kernelTime = ((ulong)kernel.dwHighDateTime << 32) | kernel.dwLowDateTime;
                userTime = ((ulong)user.dwHighDateTime << 32) | user.dwLowDateTime;
            }

            private int CalculateCpuUsage()
            {
                UpdateCpuTimes(out ulong idleTime, out ulong kernelTime, out ulong userTime);

                ulong sysIdle = idleTime - _prevIdleTime;
                ulong sysKernel = kernelTime - _prevKernelTime;
                ulong sysUser = userTime - _prevUserTime;
                ulong sysTotal = sysKernel + sysUser;

                _prevIdleTime = idleTime;
                _prevKernelTime = kernelTime;
                _prevUserTime = userTime;

                if (sysTotal == 0) return 0;

                ulong sysUsed = sysTotal - sysIdle;
                double cpuUsage = (sysUsed * 100.0) / sysTotal;
                return (int)Math.Round(Math.Max(0, Math.Min(100, cpuUsage)));
            }

            public async Task RunAsync()
            {
                var computerInfo = new ComputerInfo();

                while (!_cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        if (_window.Dispatcher.HasShutdownStarted || _window.Dispatcher.HasShutdownFinished)
                            break;

                        bool shown = await _window.Dispatcher.InvokeAsync(() => _window.IsVisible && _window.WindowState != WindowState.Minimized);
                        if (!shown)
                        {
                            await Task.Delay(2000, _cancellationToken);
                            continue;
                        }

                        ulong totalMemory = computerInfo.TotalPhysicalMemory;
                        ulong availableMemory = computerInfo.AvailablePhysicalMemory;
                        ulong usedMemory = totalMemory - availableMemory;
                        double ramPercent = Math.Round((usedMemory * 100.0) / totalMemory, 1);

                        int cpuValue = CalculateCpuUsage();
                        int diskValue = ReadDiskPercent();
                        var (down, up) = ReadNetRates();
                        double temperature = ReadTemperature();

                        if (_window.Dispatcher.HasShutdownStarted || _window.Dispatcher.HasShutdownFinished)
                            break;

                        await _window.Dispatcher.InvokeAsync(() =>
                        {
                            if (Math.Abs(ramPercent - _lastRamPercent) > 0.1)
                            {
                                _window.RamBar.Minimum = 0;
                                _window.RamBar.Maximum = 100;
                                Processes.Motion.Glide(_window.RamBar, ramPercent);
                                _window.RamPercentText.Text = $"{ramPercent:F1}%";
                                _lastRamPercent = ramPercent;
                            }

                            if (cpuValue != _lastCpuValue)
                            {
                                _window.CpuBar.Minimum = 0;
                                _window.CpuBar.Maximum = 100;
                                Processes.Motion.Glide(_window.CpuBar, cpuValue);
                                _window.CpuPercentText.Text = $"{cpuValue}%";
                                _lastCpuValue = cpuValue;
                            }

                            _window.ShowLiveValues(ramPercent, cpuValue, diskValue, down, up);
                            _window.ShowTemperature(temperature);
                        });

                        await Task.Delay(1000, _cancellationToken);
                    }
                    catch (TaskCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[SystemStatusMonitor] Hata: {ex.Message}");
                        await Task.Delay(1000, _cancellationToken);
                    }
                }
            }
        }

        public void ShowTemperature(double celsius)
        {
            bool known = celsius > 0;
            TempGlyph.Visibility = known ? Visibility.Collapsed : Visibility.Visible;
            TempGaugeText.Visibility = known ? Visibility.Visible : Visibility.Collapsed;
            TempGaugeText.Text = known ? $"{Math.Round(celsius)}°" : string.Empty;
            SetRing(TempRing, known ? celsius / 100 : 0);
            TempRing.Parent.SetValue(ToolTipProperty, known ? Loc.F("CPU temperature: {0} °C", Math.Round(celsius)) : Loc.T("Temperature sensor not available"));
        }

        public void ShowLiveValues(double ramPercent, int cpu, int disk, double down, double up)
        {
            RamGaugeText.Text = Math.Round(ramPercent).ToString("0");
            CpuGaugeText.Text = cpu.ToString();
            DiskGaugeText.Text = disk < 0 ? "–" : disk.ToString();
            DiskGaugeUnit.Text = disk < 0 ? "" : "%";
            SetRing(RamRing, ramPercent / 100);
            SetRing(CpuRing, cpu / 100.0);
            SetRing(DiskRing, Math.Max(0, disk) / 100.0);
            var (gaugeValue, gaugeUnit) = FormatRate(down + up);
            NetGaugeText.Text = gaugeValue;
            NetGaugeUnit.Text = gaugeUnit;
            SetRing(NetRing, Math.Log10(1 + (down + up) / 1024) / Math.Log10(1 + 102400));

            PillRamValue.Text = Math.Round(ramPercent).ToString("0");
            PillCpuValue.Text = cpu.ToString();
            PillDiskValue.Text = disk < 0 ? "–" : disk.ToString();
            PillDiskUnit.Text = disk < 0 ? "" : "%";
            Processes.Motion.Glide(DiskBar, Math.Max(0, disk));
            DiskPercentText.Text = disk < 0 ? "–" : $"{disk}%";

            var (value, unit) = FormatRate(down + up);
            PillNetValue.Text = value;
            PillNetUnit.Text = unit;
            NetTotalText.Text = value + " " + unit;
            NetDetailText.Text = $"↓ {RateText(down)}  ↑ {RateText(up)}";
            string tip = Loc.F("Download: {0}   Upload: {1}", RateText(down), RateText(up));
            PillNetBlock.ToolTip = tip;
            NetTile.ToolTip = tip;
        }

        private void LoadingBorder_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (LoadingBorder.IsVisible)
            {
                LoadingSpinnerRotation.BeginAnimation(RotateTransform.AngleProperty,
                    new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.7)) { RepeatBehavior = RepeatBehavior.Forever, SpeedRatio = 1 });
                LoadingText.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(1.0, 0.3, TimeSpan.FromSeconds(0.6)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, SpeedRatio = 1 });
            }
            else
            {
                LoadingSpinnerRotation.BeginAnimation(RotateTransform.AngleProperty, null);
                LoadingText.BeginAnimation(OpacityProperty, null);
            }
        }

        private void MemoryMon_Loaded(object sender, RoutedEventArgs e)
        {
            LoadSavedLocation();
        }

        private void LoadSavedLocation()
        {
            bool locationLoaded = false;
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\MultronWinCleaner"))
                {
                    if (key != null)
                    {
                        var leftValue = key.GetValue("MemMonLeft");
                        var topValue = key.GetValue("MemMonTop");

                        if (leftValue != null && topValue != null)
                        { 
                            if (double.TryParse(leftValue.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double left) &&
                                double.TryParse(topValue.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double top))
                            { 
                                var workArea = SystemParameters.WorkArea;
                                if (left + 50 < workArea.Right && top + 50 < workArea.Bottom)
                                {
                                    Left = left;
                                    Top = top;
                                    locationLoaded = true;
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            if (!locationLoaded)
            {
                var workingArea = SystemParameters.WorkArea;
                 
                double currentWidth = double.IsNaN(Width) ? ActualWidth : Width;
                double currentHeight = double.IsNaN(Height) ? ActualHeight : Height;

                Left = (workingArea.Width - currentWidth) / 2 + workingArea.Left;
                Top = (workingArea.Height - currentHeight) / 2 + workingArea.Top;
            }
        }

        private void SaveCurrentLocation()
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(@"Software\MultronWinCleaner"))
                { 
                    key.SetValue("MemMonLeft", Left.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    key.SetValue("MemMonTop", Top.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
            }
            catch { }
        }

        private async void CleanMemory_Click(object sender, RoutedEventArgs e)
        {
            if (_memcleaner != null && !_cleaning)
            {
                try
                {
                    await _memcleaner.cleanmemory();
                }
                finally
                {
                    SetCleaning(false);
                }
            }
        }

        private void Window_DragMove(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
                SaveCurrentLocation();
            }
        }

        private void SlowProcesses_Click(object sender, RoutedEventArgs e)
        {
            if (!_processesVisible)
            {
                ProcessesListView.Visibility = Visibility.Collapsed;
                LoadingBorder.Visibility = Visibility.Visible;

                _processesVisible = true;
                _updateTimer.Start();
                UpdateTimer_Tick(this, EventArgs.Empty);
            }
            else
            {
                _updateTimer.Stop();
                ProcessesListView.Visibility = Visibility.Collapsed;
                LoadingBorder.Visibility = Visibility.Collapsed;
                ClassicEndTaskButton.Visibility = Visibility.Collapsed;
                _processesVisible = false;
            }
        }

        private async void UpdateTimer_Tick(object? sender, EventArgs e)
        {
            if (_isUpdatingProcesses || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
            _isUpdatingProcesses = true;

            try
            {
                await Task.Run(() =>
                {
                    var now = DateTime.Now;
                    var timeDifference = now - _lastCpuCheckTime;
                    if (timeDifference.TotalMilliseconds <= 0) return;

                    var topProcs = Process.GetProcesses()
                        .OrderByDescending(p => p.WorkingSet64)
                        .Take(15)
                        .ToList();

                    var processList = new List<ProcessItem>();

                    foreach (var p in topProcs)
                    {
                        double cpuUsage = 0;
                        string processName = p.ProcessName;
                        long ramUsage = 0;

                        try { ramUsage = p.WorkingSet64; } catch { }

                        try
                        {
                            var cpuTime = p.TotalProcessorTime;
                            if (_lastCpuTimes.TryGetValue(p.Id, out var prevCpuTime))
                            {
                                var diff = cpuTime - prevCpuTime;
                                cpuUsage = (diff.TotalMilliseconds / timeDifference.TotalMilliseconds) / Environment.ProcessorCount * 100;
                            }
                            _lastCpuTimes[p.Id] = cpuTime;
                        }
                        catch { }

                        ImageSource? icon = GetProcessIcon(p, processName);

                        processList.Add(new ProcessItem
                        {
                            Icon = icon,
                            Name = processName,
                            Cpu = $"{Math.Max(0, Math.Round(cpuUsage, 1))}%",
                            Ram = $"{ramUsage / 1024 / 1024} MB",
                            Id = p.Id
                        });
                    }

                    _lastCpuCheckTime = now;

                    var currentIds = topProcs.Select(x => x.Id).ToHashSet();
                    var keysToRemove = _lastCpuTimes.Keys.Where(k => !currentIds.Contains(k)).ToList();
                    foreach (var k in keysToRemove) _lastCpuTimes.Remove(k);

                    if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;

                    Dispatcher.Invoke(() =>
                    {
                        var checkedIds = new HashSet<int>(_processes.Where(i => i.IsChecked).Select(i => i.Id));
                        _processes.Clear();
                        foreach (var item in processList)
                        {
                            item.IsChecked = checkedIds.Contains(item.Id);
                            item.PropertyChanged += (s, e) => UpdateClassicEndTask();
                            _processes.Add(item);
                        }
                        UpdateClassicEndTask();

                        LoadingBorder.Visibility = Visibility.Collapsed;
                        ProcessesListView.Visibility = Visibility.Visible;
                        ClassicEndTaskButton.Visibility = Visibility.Visible;
                    });
                });
            }
            catch { }
            finally
            {
                _isUpdatingProcesses = false;
            }
        }

        private ImageSource? GetProcessIcon(Process p, string processName)
        {
            if (_iconCache.TryGetValue(processName, out var cachedIcon))
            {
                return cachedIcon;
            }

            string? path = null;
            try { path = p.MainModule?.FileName; } catch { }

            ImageSource? iconSource = null;

            try
            {
                System.Drawing.Icon? ico = null;

                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    try { ico = System.Drawing.Icon.ExtractAssociatedIcon(path); } catch { }
                }

                if (ico == null)
                {
                    string defaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
                    try { ico = System.Drawing.Icon.ExtractAssociatedIcon(defaultPath); } catch { }
                }

                if (ico != null)
                {
                    IntPtr hIcon = ico.Handle;
                    var bmpSource = Imaging.CreateBitmapSourceFromHIcon(
                        hIcon,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());

                    bmpSource.Freeze();
                    DestroyIcon(hIcon);
                    ico.Dispose();

                    iconSource = bmpSource;
                }
            }
            catch { }

            if (iconSource != null)
            {
                _iconCache[processName] = iconSource;
            }

            return iconSource;
        }

        
        private void MemoryMon_Closed(object? sender, EventArgs e)
        {
            _cts?.Cancel();
            _monitorTimer?.Stop();

            if (_updateTimer != null)
            {
                _updateTimer.Stop();
                _updateTimer.Tick -= UpdateTimer_Tick; 
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            _updateTimer?.Stop();
            Close();

            if (_memcleaner?.chkSmartRAM != null)
            {
                _memcleaner.chkSmartRAM.IsChecked = false;
            }
        }
    }
}
