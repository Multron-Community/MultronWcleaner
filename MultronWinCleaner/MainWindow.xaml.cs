using Hardcodet.Wpf.TaskbarNotification;
using MFK;
using Microsoft.VisualBasic.Logging;
using MultronWinCleaner;
using MultronWinCleaner.Processes;
using Ookii.Dialogs.Wpf;
using System;
using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Diagnostics.Tracing;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Policy;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Xml.Linq;
using static Multron_Win_Cleaner.MainWindow;
using static MultronWinCleaner.Processes.Clean;
using static MultronWinCleaner.Processes.Scan;
using static System.Net.WebRequestMethods;
using static System.Runtime.InteropServices.JavaScript.JSType;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Header;

namespace Multron_Win_Cleaner
{

    public partial class MainWindow : Window
    {
        private string diskModel = string.Empty;
        public Settings settings = null!;
        public MultronWinCleaner.Processes.Scan.MainViewModel viewModel = new MultronWinCleaner.Processes.Scan.MainViewModel();
        public MultronWinCleaner.Processes.Scan.MainViewModel viewModelbac = new MultronWinCleaner.Processes.Scan.MainViewModel();
        public List<string> logfiles = new List<string>();
        public SolidColorBrush brush = System.Windows.Media.Brushes.Black;
        public List<CheckBox> checkboxes2 = new List<CheckBox>();
        public readonly HashSet<CheckBox> cleaningEntryBoxes = new HashSet<CheckBox>();
        public List<StackPanel> stackpanels = new List<StackPanel>();
        public List<Expander> expanders = new List<Expander>();
        public List<ListBox> listboxes = new List<ListBox>();
        public List<ComboBox> comboboxlist = new List<ComboBox>();
        public Dictionary<string, bool> databaseDefaults = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        public System.Collections.Concurrent.ConcurrentDictionary<string, MultronWinCleaner.Processes.BrowserTag> browserPaths = new(StringComparer.OrdinalIgnoreCase);
        public System.Collections.Concurrent.ConcurrentDictionary<string, string> warningPaths = new(StringComparer.OrdinalIgnoreCase);
        public System.Collections.Concurrent.ConcurrentDictionary<string, string> extraDatabasePaths = new(StringComparer.OrdinalIgnoreCase);
        public List<string> paths = new List<string>();
        public CancellationTokenSource dismcancel = new CancellationTokenSource();
        public Utilities utilities = null!;
        public byte autoclean = 0;
        public byte startupclean = 0;
        public byte startupscan = 0;
        public byte onclean = 0;
        public byte killer = 0;
        public readonly TrayIconAnimator _trayAnimator ;
        public string extensions = MultronWinCleaner.Processes.DeepLogOptions.DefaultExtensions;
        string settingsPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Settings.txt");
        private DispatcherTimer diskSpaceTimer;
        public MainWindow()
        {
       

            InitializeComponent();
            _trayAnimator = new TrayIconAnimator(TrayIcon, "pack://application:,,,/MultronWinCleaner;component/Assets/mwc_icon.ico");
            Activated += (s, e) => ClearTrayResult();
            Loaded += (s, e) => UpdateShredBadge();
            Loaded += (s, e) => SetupFoundFiles();

            if (!System.IO.File.Exists(settingsPath))
            {
                string defaultSettings = "minutes:0" + Environment.NewLine +
                                         "autoclean:0" + Environment.NewLine +
                                         "trayicon:0" + Environment.NewLine +
                                         "themes:0";
                System.IO.File.WriteAllText(settingsPath, defaultSettings);
            }

            string fileContent = System.IO.File.ReadAllText(settingsPath);

            bool dark = ThemeFollowsWindows ? WindowsUsesDarkTheme() : fileContent.Contains("themes:1");
            SetThemeSwitch(dark);
            ApplyTheme(dark);
            Microsoft.Win32.SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
            Closed += (s, e) => Microsoft.Win32.SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
            Window.GetWindow(this)?.DragMove();

            label1_Copy.Text = Loc.T("Ready to scan");
            LoadNotices();
            if (NoticeArea.Parent is Grid noticeGrid)
                noticeGrid.SizeChanged += (s, e) => UpdateNoticeHeight();
            StatusContent.LayoutUpdated += (s, e) => UpdateNoticeHeight();
            TaskbarItemInfo = new System.Windows.Shell.TaskbarItemInfo();
            StateChanged += MainWindow_StateChanged;
            if (Application.Current != null)
                Application.Current.SessionEnding += (s, e) => sessionEnding = true;
            System.ComponentModel.DependencyPropertyDescriptor.FromProperty(ContentControl.ContentProperty, typeof(Button)).AddValueChanged(buttonStartScan, (s, e) => UpdateTaskbarProgress());
            MultronWinCleaner.Processes.CloudUploadTracker.Instance.PropertyChanged += (s, e) => UpdateTaskbarProgress();
            KeepAccentReadable(label1_Copy);
            KeepAccentReadable(StatusLoad);
            System.ComponentModel.DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock)).AddValueChanged(label1_Copy, (s, e) =>
            {
                if ((ResultCard.Visibility == Visibility.Visible || ScanLivePanel.Visibility == Visibility.Visible) && !string.IsNullOrEmpty(label1_Copy.Text))
                {
                    ScanLivePanel.Visibility = Visibility.Collapsed;
                    label1_Copy.Visibility = Visibility.Visible;
                }
            });

            string systemDrive = System.IO.Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
            _ = Task.Run(() => GetDiskModel(systemDrive)).ContinueWith(t =>
            {
                diskModel = t.Result;
                Dispatcher.BeginInvoke(UpdateDiskSpace);
            }, TaskScheduler.Default);

            diskSpaceTimer = new DispatcherTimer();
            diskSpaceTimer.Interval = TimeSpan.FromSeconds(5);
            diskSpaceTimer.Tick += DiskSpaceTimer_Tick;
            diskSpaceTimer.Start();
             
            UpdateDiskSpace();
        }

        
        private string GetDiskModel(string driveLetter)
        {
            try
            {
              
                char letter = driveLetter[0];

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "powershell",
                 
                    Arguments = $"-Command \"(Get-Partition -DriveLetter '{letter}' | Get-Disk).FriendlyName\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (Process? process = Process.Start(psi))
                {
                    if (process == null) return Loc.T("Disk");
                    string output = process.StandardOutput.ReadToEnd().Trim();
                    process.WaitForExit();

                    return string.IsNullOrWhiteSpace(output) ? Loc.T("Disk") : output;
                }
            }
            catch
            {
                return Loc.T("Disk");
            }
        }
        private void DiskSpaceTimer_Tick(object? sender, EventArgs e)
        {
            UpdateDiskSpace();
        }
        private void UpdateDiskSpace()
        {
            try
            {
                string driveLetter = System.IO.Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
                DriveInfo drive = new DriveInfo(driveLetter);

                if (drive.IsReady)
                {
                    long free = drive.AvailableFreeSpace;
                    long total = drive.TotalSize;
                    double freeSpaceGB = free / (1024.0 * 1024 * 1024);
                    double totalSizeGB = total / (1024.0 * 1024 * 1024);
                    double used = total > 0 ? Math.Clamp((double)(total - free) / total, 0, 1) : 0;

                    string model = string.IsNullOrEmpty(diskModel) ? Loc.T("Disk") : diskModel;
                    labelDiskName.Text = $"{model} ({driveLetter.TrimEnd('\\')})";
                    labelDiskPercent.Text = Loc.F("{0:0}% used", used * 100);
                    labelDiskSize.Text = Loc.F("{0} free of {1}", formatsize(free), formatsize(total));
                    DiskUsedColumn.Width = new GridLength(used, GridUnitType.Star);
                    DiskFreeColumn.Width = new GridLength(1 - used, GridUnitType.Star);
                    var barColor = used >= 0.95 ? System.Windows.Media.Color.FromRgb(0xE5, 0x39, 0x35) : used >= 0.85 ? System.Windows.Media.Color.FromRgb(0xFB, 0x8C, 0x00) : System.Windows.Media.Color.FromRgb(0x1E, 0x88, 0xE5);
                    var barLight = System.Windows.Media.Color.FromRgb((byte)Math.Min(255, barColor.R + 50), (byte)Math.Min(255, barColor.G + 40), (byte)Math.Min(255, barColor.B + 20));
                    var barBrush = new LinearGradientBrush(barColor, barLight, 0);
                    barBrush.Freeze();
                    DiskUsedBar.Background = barBrush;
                    DiskPanel.ToolTip = Loc.F("[{0}] Drive {1} Free: {2:F2} GB / Total: {3:F2} GB", model, driveLetter, freeSpaceGB, totalSizeGB);
                }
            }
            catch (Exception)
            {
                labelDiskSize.Text = Loc.T("Disk size could not be read.");
            }
        }

        public sealed class NoticeData
        {
            public string Key { get; set; } = "";
            public string Title { get; set; } = "";
            public List<string> Lines { get; set; } = new();
            public List<string> Details { get; set; } = new();
            public bool DetailsCanceled { get; set; }
            public bool Problem { get; set; }
            public DateTime Time { get; set; }
            public int RestartFiles { get; set; }
            public bool RestartThreats { get; set; }
        }

        private static string NoticesPath => System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "notices.json");

        private static (string Key, string Title) NoticeIdentity(string title)
        {
            if (title == Loc.T("Duplicate File Finder") || title == Loc.T("Duplicate Files"))
                return ("duplicates", Loc.T("Duplicate Files"));
            if (title == Loc.T("Shortcut Fixer") || title == Loc.T("Shortcuts"))
                return ("shortcuts", Loc.T("Shortcuts"));
            if (title == Loc.T("Security Check") || title == Loc.T("Security Settings"))
                return ("security", Loc.T("Security Settings"));
            if (title == Loc.T("Firewall Rule Cleaner") || title == Loc.T("Firewall Rules"))
                return ("firewall", Loc.T("Firewall Rules"));
            if (title == Loc.T("Malware Scan") || title == Loc.T("Multron Malware Scan Cloud"))
                return ("malware", Loc.T("Malware Scan"));
            if (title == Loc.T("Large File Finder") || title == Loc.T("Large Files"))
                return ("largefiles", Loc.T("Large Files"));
            return (title, title);
        }

        public void ShowChangeCard(string title, string message, bool problem, int restartFiles = 0, bool restartThreats = false)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(() => ShowChangeCard(title, message, problem, restartFiles, restartThreats));
                return;
            }
            if (string.IsNullOrWhiteSpace(message))
                return;
            var (key, shownTitle) = NoticeIdentity(title);
            if (key == "malware" && restartFiles == 0)
            {
                restartFiles = malwareRestartFiles;
                restartThreats = true;
            }
            AddNotice(new NoticeData { Key = key, Title = shownTitle, Lines = SplitStatusLines(message), Problem = problem, Time = DateTime.Now, RestartFiles = restartFiles, RestartThreats = restartThreats });
        }

        public void ShowMessageCard(string message, bool problem, List<string>? details = null, int restartFiles = 0)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(() => ShowMessageCard(message, problem, details, restartFiles));
                return;
            }
            var lines = SplitStatusLines(message);
            if (lines.Count == 0)
                return;
            AddNotice(new NoticeData { Key = "kill", Title = lines[0], Lines = lines.Skip(1).ToList(), Problem = problem, Time = DateTime.Now, Details = details?.Take(500).ToList() ?? new List<string>(), DetailsCanceled = problem, RestartFiles = restartFiles });
        }

        private static bool RestartedSince(DateTime time) => DateTime.Now - TimeSpan.FromMilliseconds(Environment.TickCount64) > time;

        private Border RestartNotice(int files, bool threats = false)
        {
            var glyph = new TextBlock { Text = "\uE777", FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 13, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 8, 0) };
            glyph.SetResourceReference(TextBlock.ForegroundProperty, "WarningText");
            var label = new TextBlock { Text = threats ? Loc.F("{0} threat file(s) will be deleted at the next restart.", files) : Loc.F("{0} file(s) in use by Windows will be deleted at the next restart.", files), FontSize = 12.5, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
            label.SetResourceReference(TextBlock.ForegroundProperty, "WarningText");
            var restart = new Button { Content = Loc.T("Restart now"), Height = 28, FontSize = 12, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            restart.SetResourceReference(StyleProperty, "UtilityPrimaryButtonStyle");
            restart.Click += (s, e) =>
            {
                if (threats)
                    AskRestartForThreats();
                else
                    AskRestart();
            };
            var line = new DockPanel();
            DockPanel.SetDock(glyph, Dock.Left);
            line.Children.Add(glyph);
            line.Children.Add(label);
            var stack = new StackPanel();
            stack.Children.Add(line);
            stack.Children.Add(restart);
            var box = new Border
            {
                Child = stack,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 8, 10, 10),
                Margin = new Thickness(0, 8, 0, 2),
                BorderThickness = new Thickness(1)
            };
            box.SetResourceReference(Border.BackgroundProperty, "WarningTint");
            box.SetResourceReference(Border.BorderBrushProperty, "WarningBorder");
            return box;
        }

        private (Visibility Detected, Visibility Grid, Visibility Directories, Visibility Wrap, List<UIElement> Children, Button Button)? killLogRestore;

        private void ToggleKillLog(NoticeData data, Button button)
        {
            if (killLogRestore is { } back)
            {
                CloseKillLog();
                if (ReferenceEquals(back.Button, button))
                    return;
            }
            if (IsScanOrCleanBusy)
                return;
            killLogRestore = (ScrollViewerDetectedFiles.Visibility, Datagridscroll.Visibility, ScrollViewerDirectories.Visibility, wrapPanelDirectories.Visibility, wrapPanelDirectories.Children.Cast<UIElement>().ToList(), button);
            wrapPanelDirectories.Children.Clear();
            var heading = new TextBlock { Text = data.Title + " · " + data.Time.ToString("g"), FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(7, 2, 5, 8) };
            heading.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
            wrapPanelDirectories.Children.Add(heading);
            foreach (string entry in data.Details)
            {
                string[] parts = entry.Split('\t');
                if (parts.Length < 2 || !Enum.TryParse(parts[0], out Kill.KillLine kind))
                    continue;
                wrapPanelDirectories.Children.Add(Kill.KillRow(kind, parts[1], parts.Length > 2 && parts[2].Length > 0 ? parts[2] : null));
            }
            wrapPanelDirectories.Children.Add(Kill.KillSummary(data.Title + " " + string.Join(", ", data.Lines), data.DetailsCanceled));
            ScrollViewerDetectedFiles.Visibility = Visibility.Hidden;
            Datagridscroll.Visibility = Visibility.Hidden;
            ScrollViewerDirectories.Visibility = Visibility.Visible;
            wrapPanelDirectories.Visibility = Visibility.Visible;
            ScrollViewerDirectories.ScrollToTop();
            button.Content = Loc.T("Close");
        }

        private void CloseKillLog()
        {
            if (killLogRestore is not { } back)
                return;
            killLogRestore = null;
            wrapPanelDirectories.Children.Clear();
            foreach (var child in back.Children)
                wrapPanelDirectories.Children.Add(child);
            ScrollViewerDetectedFiles.Visibility = back.Detected;
            Datagridscroll.Visibility = back.Grid;
            ScrollViewerDirectories.Visibility = back.Directories;
            wrapPanelDirectories.Visibility = back.Wrap;
            back.Button.Content = Loc.T("Show details");
        }

        private bool HasNotice(string title) => NoticeStack.Children.OfType<Border>().Any(n => n.Tag is NoticeData d && d.Key == NoticeIdentity(title).Key);

        private void LoadNotices()
        {
            try
            {
                if (!System.IO.File.Exists(NoticesPath))
                    return;
                var saved = System.Text.Json.JsonSerializer.Deserialize<List<NoticeData>>(System.IO.File.ReadAllText(NoticesPath));
                if (saved == null)
                    return;
                foreach (var notice in saved.Take(20).Reverse())
                    AddNotice(notice, false);
            }
            catch { }
        }

        private void NoticeLayout_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateNoticeHeight();

        private const string NoticeHeightSettingKey = "noticeheight";
        private double noticeManualHeight = double.TryParse(ReadSettingValue(NoticeHeightSettingKey), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double saved) ? saved : 0;

        private double NoticeHeightLimit()
        {
            if (NoticeArea.Parent is not Grid grid || grid.RowDefinitions.Count < 4)
                return 600;
            return Math.Max(80, grid.ActualHeight - grid.RowDefinitions[0].ActualHeight - grid.RowDefinitions[3].ActualHeight - NoticeArea.Margin.Top - NoticeArea.Padding.Top - 90);
        }

        private void NoticeResizeGrip_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
        {
            double start = noticeManualHeight > 0 ? noticeManualHeight : NoticeScroll.ActualHeight;
            noticeManualHeight = Math.Clamp(start - e.VerticalChange, 60, NoticeHeightLimit());
            NoticeScroll.MaxHeight = noticeManualHeight;
            NoticeScroll.Height = noticeManualHeight;
        }

        private void NoticeResizeGrip_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            utilities?.savesettings($"{NoticeHeightSettingKey}:{noticeManualHeight.ToString("0", System.Globalization.CultureInfo.InvariantCulture)}");
            UpdateNoticeHeight();
        }

        private void NoticeResizeGrip_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount < 2)
                return;
            e.Handled = true;
            noticeManualHeight = 0;
            NoticeScroll.ClearValue(HeightProperty);
            utilities?.savesettings($"{NoticeHeightSettingKey}:0");
            UpdateNoticeHeight();
        }

        private void UpdateNoticeHeight()
        {
            var cards = NoticeStack.Children.OfType<FrameworkElement>().ToList();
            NoticeArea.Visibility = cards.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            if (NoticeArea.Parent is not Grid grid || grid.ActualHeight <= 0 || grid.RowDefinitions.Count < 4)
                return;
            int shown = DiskPanel.Visibility == Visibility.Visible ? 2 : 3;
            double two = cards.Take(shown).Sum(card => card.ActualHeight + card.Margin.Top + card.Margin.Bottom);
            double content = StatusContent.Children.OfType<FrameworkElement>().Where(x => x.Visibility != Visibility.Collapsed).Sum(x => x.ActualHeight + x.Margin.Top + x.Margin.Bottom)
                + StatusScroll.Margin.Top + StatusScroll.Margin.Bottom;
            double free = grid.ActualHeight - grid.RowDefinitions[0].ActualHeight - grid.RowDefinitions[3].ActualHeight
                - content - NoticeArea.Margin.Top - NoticeArea.Padding.Top - NoticeArea.BorderThickness.Top - 2;
            double wanted = cards.Count <= shown ? double.PositiveInfinity : two;
            double max = Math.Max(96, Math.Min(wanted, free));
            if (noticeManualHeight > 0)
            {
                max = Math.Clamp(noticeManualHeight, 60, NoticeHeightLimit());
                double contentHeight = NoticeStack.ActualHeight;
                double shownHeight = contentHeight > 0 ? Math.Min(max, contentHeight) : max;
                if (Math.Abs(NoticeScroll.Height - shownHeight) > 0.5 || double.IsNaN(NoticeScroll.Height))
                    NoticeScroll.Height = shownHeight;
            }
            if (Math.Abs(NoticeScroll.MaxHeight - max) > 0.5 || double.IsInfinity(NoticeScroll.MaxHeight) != double.IsInfinity(max))
                NoticeScroll.MaxHeight = max;
        }

        private void SaveNotices()
        {
            try
            {
                var list = NoticeStack.Children.OfType<Border>().Select(n => n.Tag).OfType<NoticeData>().ToList();
                if (list.Count == 0)
                {
                    if (System.IO.File.Exists(NoticesPath))
                        System.IO.File.Delete(NoticesPath);
                    return;
                }
                System.IO.File.WriteAllText(NoticesPath, System.Text.Json.JsonSerializer.Serialize(list));
            }
            catch { }
        }

        private void AddNotice(NoticeData data, bool persist = true)
        {
            if (persist)
                SetTrayResult(data.RestartFiles > 0 ? MultronWinCleaner.TrayBadge.Warning : data.Problem && data.Key == "malware" ? MultronWinCleaner.TrayBadge.Danger : MultronWinCleaner.TrayBadge.Info, data.Title);
            foreach (var old in NoticeStack.Children.OfType<Border>().Where(n => n.Tag is NoticeData d && d.Key == data.Key).ToList())
                NoticeStack.Children.Remove(old);
            while (NoticeStack.Children.Count >= 20)
                NoticeStack.Children.RemoveAt(NoticeStack.Children.Count - 1);
            bool problem = data.Problem;
            var color = problem ? System.Windows.Media.Color.FromRgb(0xFB, 0x8C, 0x00) : System.Windows.Media.Color.FromRgb(0x2E, 0xB8, 0x72);
            string accentKey = problem ? "VerdictSuspiciousText" : "ResultSuccessText";
            var glyph = new TextBlock
            {
                Text = problem ? "\uE7BA" : "\uE73E",
                FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            glyph.SetResourceReference(TextBlock.ForegroundProperty, accentKey);
            var icon = new Border
            {
                Width = 30,
                Height = 30,
                CornerRadius = new CornerRadius(15),
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x1F, color.R, color.G, color.B)),
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x40, color.R, color.G, color.B)),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 1, 12, 0),
                VerticalAlignment = VerticalAlignment.Top,
                Child = glyph
            };
            var text = new TextBlock { FontSize = 13, TextWrapping = TextWrapping.Wrap, LineHeight = 20 };
            var body = new StackPanel();
            body.Children.Add(text);
            if (data.RestartFiles > 0 && !RestartedSince(data.Time))
                body.Children.Add(RestartNotice(data.RestartFiles, data.RestartThreats));
            if (data.Details.Count > 0)
            {
                var details = new Button { Content = Loc.T("Show details"), Height = 28, FontSize = 12, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(0, 8, 0, 2), HorizontalAlignment = HorizontalAlignment.Left };
                details.SetResourceReference(StyleProperty, "UtilitySecondaryButtonStyle");
                details.Click += (s, e) => ToggleKillLog(data, details);
                body.Children.Add(details);
            }
            var heading = new System.Windows.Documents.Run(data.Title) { FontWeight = FontWeights.SemiBold, FontSize = 14 };
            heading.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, accentKey);
            text.Inlines.Add(heading);
            foreach (string part in data.Lines)
            {
                text.Inlines.Add(new System.Windows.Documents.LineBreak());
                var run = new System.Windows.Documents.Run(part);
                run.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "TextPrimary");
                text.Inlines.Add(run);
            }
            text.Inlines.Add(new System.Windows.Documents.LineBreak());
            var time = new System.Windows.Documents.Run(data.Time.ToString("g")) { FontSize = 11.5 };
            time.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "TextSecondary");
            text.Inlines.Add(time);
            text.ToolTip = data.Title + (data.Lines.Count > 0 ? "\n" + string.Join("\n", data.Lines) : "");
            var close = new Button
            {
                Content = new TextBlock { Text = "\uE711", FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 10 },
                Width = 24,
                Height = 24,
                Padding = new Thickness(0),
                Margin = new Thickness(8, -2, -4, 0),
                VerticalAlignment = VerticalAlignment.Top,
                ToolTip = Loc.T("Close")
            };
            close.SetResourceReference(StyleProperty, "DangerIconButtonStyle");
            var line = new DockPanel();
            DockPanel.SetDock(close, Dock.Right);
            DockPanel.SetDock(icon, Dock.Left);
            line.Children.Add(close);
            line.Children.Add(icon);
            line.Children.Add(body);
            line.Margin = new Thickness(16, 11, 10, 11);
            var sheen = new Border { CornerRadius = new CornerRadius(10), IsHitTestVisible = false };
            sheen.SetResourceReference(Border.BackgroundProperty, "CardSheen");
            var accent = new Border
            {
                Width = 3,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 12, 0, 12),
                CornerRadius = new CornerRadius(0, 2, 2, 0),
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xB3, color.R, color.G, color.B)),
                IsHitTestVisible = false
            };
            var face = new Grid();
            face.Children.Add(sheen);
            face.Children.Add(accent);
            face.Children.Add(line);
            var card = new Border
            {
                Child = face,
                CornerRadius = new CornerRadius(11),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x47, color.R, color.G, color.B)),
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x12, color.R, color.G, color.B))
            };
            var shadow = new Border { CornerRadius = new CornerRadius(11), Margin = new Thickness(1, 3, 1, -3), IsHitTestVisible = false };
            shadow.SetResourceReference(Border.BackgroundProperty, "CardShadow");
            var stack = new Grid();
            stack.Children.Add(shadow);
            stack.Children.Add(card);
            var notice = new Border
            {
                Tag = data,
                Child = stack,
                Margin = new Thickness(0, 0, 0, 10)
            };
            close.Click += (s, e) =>
            {
                if (killLogRestore is { } open && body.Children.Contains(open.Button))
                    CloseKillLog();
                NoticeStack.Children.Remove(notice);
                NoticeArea.Visibility = NoticeStack.Children.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
                SaveNotices();
            };
            NoticeStack.Children.Insert(0, notice);
            NoticeArea.Visibility = Visibility.Visible;
            if (persist)
            {
                SaveNotices();
                if (data.Key != "clean")
                    LogNotification(data.Title, string.Join(", ", data.Lines), null);
            }
        }

        public void LogNotification(string title, string text, string? detail)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(() => LogNotification(title, text, detail));
                return;
            }
            if (settings == null || settings.chkLogNotifications.IsChecked != true)
                return;
            string line = string.Join(" ", new[] { title.Trim().TrimEnd(':') + ":", text.Trim(), detail?.Trim() ?? "" }.Where(p => p.Length > 0 && p != ":"));
            AppendCleanupLog(line);
        }

        public const string LogAutoClearSettingKey = "logautoclear";
        public const string LogLastClearSettingKey = "loglastclear";

        public static string LogAutoClearMode => ReadSettingValue(LogAutoClearSettingKey) switch
        {
            "never" => "never",
            "week" => "week",
            "quarter" => "quarter",
            _ => "month"
        };

        public void ClearLogIfDue()
        {
            try
            {
                int days = LogAutoClearMode switch { "week" => 7, "month" => 30, "quarter" => 90, _ => 0 };
                if (days == 0 || settings == null)
                    return;
                string path = !string.IsNullOrWhiteSpace(settings.logfilepath) ? settings.logfilepath
                    : !string.IsNullOrWhiteSpace(settings.txtLogPath.Text) ? settings.txtLogPath.Text
                    : System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mwc_cleanlog.txt");
                if (!DateTime.TryParseExact(ReadSettingValue(LogLastClearSettingKey), "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime last))
                {
                    utilities?.savesettings($"{LogLastClearSettingKey}:{DateTime.Now:yyyyMMdd}");
                    return;
                }
                if ((DateTime.Now - last).TotalDays < days)
                    return;
                if (System.IO.File.Exists(path))
                    System.IO.File.WriteAllText(path, "");
                utilities?.savesettings($"{LogLastClearSettingKey}:{DateTime.Now:yyyyMMdd}");
                settings.RefreshLogPreview();
            }
            catch { }
        }

        public void AppendCleanupLog(string line, bool addTime = true)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(() => AppendCleanupLog(line, addTime));
                return;
            }
            if (settings == null || settings.chkEnableLog.IsChecked != true || string.IsNullOrWhiteSpace(line))
                return;
            ClearLogIfDue();
            try
            {
                if (string.IsNullOrWhiteSpace(settings.logfilepath))
                    settings.logfilepath = string.IsNullOrWhiteSpace(settings.txtLogPath.Text)
                        ? System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mwc_cleanlog.txt")
                        : settings.txtLogPath.Text;
                string entry = (addTime ? DateTime.Now.ToString("g") + "  " : "") + line.Replace("\r", " ").Replace("\n", " ");
                System.IO.File.AppendAllText(settings.logfilepath, entry + Environment.NewLine);
                settings.RefreshLogPreview();
            }
            catch { }
        }

        public void ShowLastLogLine(string? line)
        {
            bool show = settings?.chkShowLastLog.IsChecked == true && !string.IsNullOrWhiteSpace(line);
            LastLogText.Text = show ? Loc.F("Last log entry: {0}", line!.Trim()) : "";
            LastLogText.ToolTip = show ? line : null;
            LastLogText.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ShowReadyStatus()
        {
            if (IsScanOrCleanBusy || activeTool != null)
                return;
            if (ResultCard.Visibility == Visibility.Visible)
            {
                label1_Copy.Text = "";
                label1_Copy.Visibility = Visibility.Collapsed;
                return;
            }
            label1_Copy.Text = Loc.T("Ready to scan");
            label1_Copy.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
            label1_Copy.Visibility = Visibility.Visible;
        }

        private static List<string> SplitStatusLines(string text)
        {
            var lines = new List<string>();
            var current = new StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                current.Append(ch);
                bool ascii = ch is '!' or '?' or '.' or ',' or ';';
                bool wide = ch is '。' or '！' or '？' or '、' or '，' or '；';
                bool breakHere = wide || (ascii && i + 1 < text.Length && char.IsWhiteSpace(text[i + 1]) && !(ch == '.' && i > 0 && text[i - 1] == '.'));
                if (breakHere)
                {
                    lines.Add(current.ToString());
                    current.Clear();
                }
            }
            if (current.Length > 0)
                lines.Add(current.ToString());
            return lines.Select(l => l.Trim().TrimEnd(',', ';', '、', '，', '；', '。').Trim()).Select(l => l.EndsWith('.') && !l.EndsWith("..") ? l[..^1] : l).Where(l => l.Length > 0).ToList();
        }

        private void SetResultCardColor(System.Windows.Media.Color color, string glyph)
        {
            ResultCard.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x1A, color.R, color.G, color.B));
            ResultCard.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x80, color.R, color.G, color.B));
            ResultIcon.Text = glyph;
            ResultIcon.Foreground = ThemeColors.ReadableBrush(color);
            ResultAmount.Foreground = ThemeColors.ReadableBrush(color);
        }

        private void ShowResultCard()
        {
            ResultTime.Text = DateTime.Now.ToString("g");
            ScanLivePanel.Visibility = Visibility.Collapsed;
            label1_Copy.Text = "";
            label1_Copy.Visibility = Visibility.Collapsed;
            ResultCard.Visibility = Visibility.Visible;
        }

        public void ShowScanLive(string title, string detail)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(() => ShowScanLive(title, detail));
                return;
            }
            ScanLiveTitle.Text = title;
            ScanLiveDetail.Text = detail;
            ScanLiveDetail.ToolTip = detail;
            ScanLiveDetail.Visibility = detail.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            ResultCard.Visibility = Visibility.Collapsed;
            label1_Copy.Visibility = Visibility.Collapsed;
            ScanLivePanel.Visibility = Visibility.Visible;
        }

        public void ShowScanResult(bool canceled, bool automatic, long foundBytes, long componentStoreBytes)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => ShowScanResult(canceled, automatic, foundBytes, componentStoreBytes));
                return;
            }
            bool clean = !canceled && foundBytes <= 0;
            if (clean)
                SetResultCardColor(System.Windows.Media.Color.FromRgb(0x2E, 0xB8, 0x72), "\uE73E");
            else
                SetResultCardColor(canceled ? System.Windows.Media.Color.FromRgb(0xFB, 0x8C, 0x00) : System.Windows.Media.Color.FromRgb(0x42, 0xA5, 0xF5), canceled ? "\uE711" : "\uE721");
            ResultTitle.Text = canceled ? Loc.T("Scan canceled") : automatic ? Loc.T("Auto scan complete") : Loc.T("Scan complete");
            ResultAmount.Text = clean ? Loc.T("Your PC is clean") : formatsize(foundBytes);
            ResultAmount.FontSize = clean ? 24 : 32;
            ResultAmount.TextWrapping = TextWrapping.Wrap;
            ResultAmount.TextAlignment = TextAlignment.Center;
            ResultAmountCaption.Text = clean ? Loc.T("No junk files were found to clean.") : canceled ? Loc.T("found before the scan stopped") : Loc.T("can be cleaned");
            ResultCard.ToolTip = null;
            ResultDetails.Children.Clear();
            var success = ThemeColors.ReadableBrush(System.Windows.Media.Color.FromRgb(0x2E, 0xB8, 0x72));
            if (!canceled && foundBytes > 0 && foundBytes < 100L * 1024 * 1024)
                AddResultLine("\uE73E", Loc.T("Your PC is in good shape: only a little can be cleaned."), success);
            else if (!canceled && foundBytes < 1024L * 1024 * 1024)
                AddResultLine("\uE946", Loc.T("Some junk has built up. Cleaning will tidy it up."), ThemeColors.ReadableBrush(System.Windows.Media.Color.FromRgb(0x42, 0xA5, 0xF5)));
            else if (!canceled && foundBytes < 5L * 1024 * 1024 * 1024)
                AddResultLine("\uE946", Loc.T("Quite a lot of junk has built up. Cleaning is recommended."), ThemeColors.Warning);
            else if (!canceled)
                AddResultLine("\uE946", Loc.T("That is a lot of space. Cleaning is recommended."), ThemeColors.Warning);
            if (componentStoreBytes > 0)
                AddResultLine("\uE8F1", Loc.F("(+ {0} can be reclaimed from the Windows component store by Dism.exe)", formatsize(componentStoreBytes)).Trim('(', ')', ' ').TrimStart('+', ' '), null);
            if (!canceled && foundBytes > 0)
                AddResultLine("\uE74D", Loc.T("Click Clean to free this space."), null);
            ShowResultCard();
            SetTrayResult(MultronWinCleaner.TrayBadge.Info, ResultTitle.Text + ": " + ResultAmount.Text);
        }

        public void ShowCleanResult(string title, long freedBytes, long deletedBytes, long deletedFiles, int lockedFiles, int restartFiles)
        {
            string drive = (System.IO.Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\").TrimEnd('\\');
            SetResultCardColor(System.Windows.Media.Color.FromRgb(0x2E, 0xB8, 0x72), "\uE73E");
            bool nothing = freedBytes <= 0 && deletedFiles <= 0 && restartFiles <= 0;
            ResultTitle.Text = title;
            ResultAmount.Text = nothing ? Loc.T("Already clean") : formatsize(freedBytes);
            ResultAmount.FontSize = nothing ? 24 : 30;
            ResultAmount.TextWrapping = TextWrapping.Wrap;
            ResultAmount.TextAlignment = TextAlignment.Center;
            ResultAmountCaption.Text = nothing ? Loc.T("There were no files to remove.") : Loc.F("freed on drive {0}", drive);
            ResultCard.ToolTip = Loc.T("Freed space can be smaller than the deleted size: compressed files take less space on disk and files in use are removed only after a restart.");
            ResultDetails.Children.Clear();
            var warning = ThemeColors.Warning;
            if (!nothing)
                AddResultLine("\uE74D", Loc.F("{0:N0} files deleted ({1})", deletedFiles, formatsize(deletedBytes)), null);
            if (lockedFiles > 0)
                AddResultLine("\uE72E", Loc.F("{0:N0} files were in use and skipped", lockedFiles), warning);
            if (restartFiles > 0)
            {
                ResultDetails.Children.Add(RestartNotice(restartFiles));
                RestartRequiredPanel.Visibility = Visibility.Collapsed;
            }
            ShowResultCard();
            var noticeLines = new List<string> { nothing ? Loc.T("There were no files to remove.") : Loc.F("{0} freed on drive {1}", formatsize(freedBytes), drive) };
            if (!nothing)
                noticeLines.Add(Loc.F("{0:N0} files deleted ({1})", deletedFiles, formatsize(deletedBytes)));
            if (lockedFiles > 0)
                noticeLines.Add(Loc.F("{0:N0} files were in use and skipped", lockedFiles));
            AddNotice(new NoticeData { Key = "clean", Title = title, Lines = noticeLines, Problem = false, Time = DateTime.Now, RestartFiles = restartFiles });
            SetTrayResult(restartFiles > 0 ? MultronWinCleaner.TrayBadge.Warning : MultronWinCleaner.TrayBadge.Success, title + ": " + ResultAmount.Text);
        }

        private TextBlock AddResultLine(string glyph, string text, System.Windows.Media.Brush? color)
        {
            var icon = new TextBlock { Text = glyph, FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"), FontSize = 12, Margin = new Thickness(0, 2, 8, 0), VerticalAlignment = VerticalAlignment.Top };
            var label = new TextBlock { Text = text, FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
            if (color != null)
            {
                icon.Foreground = color;
                label.Foreground = color;
            }
            else
            {
                icon.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
                label.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
            }
            var line = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            DockPanel.SetDock(icon, Dock.Left);
            line.Children.Add(icon);
            line.Children.Add(label);
            ResultDetails.Children.Add(line);
            return label;
        }
        private void OpenGitHub_Click(object sender, RoutedEventArgs e)
        { 
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://github.com/winball501",
                UseShellExecute = true
            });
        }
        private void OpenPatreon_Click(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://www.patreon.com/cw/multron",
                UseShellExecute = true
            });
        }
        private void LoadingOverlay_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (LoadingOverlay.IsVisible)
            {
                LoadingVersionText.Text = "v" + MultronWinCleaner.Processes.Updater.CurrentAppVersion.ToString(2) + " beta";
                LoadingOverlay.BeginAnimation(OpacityProperty, null);
                LoadingOverlay.Opacity = 1;
                LoadingOverlay.IsHitTestVisible = true;
                LoadingSpinnerRotation.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty,
                    new System.Windows.Media.Animation.DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9)) { RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever, SpeedRatio = 1 });
                var soft = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
                LoadingCard.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(420)) { EasingFunction = soft, FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop });
                LoadingCardShift.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, new System.Windows.Media.Animation.DoubleAnimation(16, 0, TimeSpan.FromMilliseconds(480)) { EasingFunction = soft });
                var breathe = new System.Windows.Media.Animation.SineEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut };
                if (MultronWinCleaner.Processes.Motion.Enabled)
                LoadingLogoGlow.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0.35, 0.9, TimeSpan.FromMilliseconds(1400)) { AutoReverse = true, RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever, SpeedRatio = 1, EasingFunction = breathe });
                var grow = new System.Windows.Media.Animation.DoubleAnimation(0.9, 1.15, TimeSpan.FromMilliseconds(1400)) { AutoReverse = true, RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever, SpeedRatio = 1, EasingFunction = breathe };
                if (MultronWinCleaner.Processes.Motion.Enabled)
                {
                    LoadingLogoGlowScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, grow);
                    LoadingLogoGlowScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, grow);
                }
            }
            else
            {
                LoadingSpinnerRotation.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, null);
                LoadingSweepShift.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, null);
                LoadingLogoGlow.BeginAnimation(OpacityProperty, null);
                LoadingLogoGlowScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, null);
                LoadingLogoGlowScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, null);
                foreach (var icon in new[] { LoadingStepUpdateIcon, LoadingStepDatabaseIcon })
                    (icon.RenderTransform as System.Windows.Media.RotateTransform)?.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, null);
            }
        }

        private void HideLoadingOverlay()
        {
            if (LoadingOverlay.Visibility != Visibility.Visible)
                return;
            LoadingOverlay.IsHitTestVisible = false;
            var fade = new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(320)) { EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn } };
            fade.Completed += (s, e) =>
            {
                if (!LoadingOverlay.IsHitTestVisible)
                    LoadingOverlay.Visibility = Visibility.Collapsed;
            };
            LoadingOverlay.BeginAnimation(OpacityProperty, fade);
            MultronWinCleaner.Processes.Motion.Reveal(MainLeftPanel, 18, 60);
            MultronWinCleaner.Processes.Motion.Reveal(MainRightPanel, 18, 150);
        }

        public enum LoadingStepState { Waiting, Running, Done, Skipped }

        public void SetLoadingStep(bool database, LoadingStepState state)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(() => SetLoadingStep(database, state));
                return;
            }
            TextBlock icon = database ? LoadingStepDatabaseIcon : LoadingStepUpdateIcon;
            TextBlock label = database ? LoadingStepDatabaseState : LoadingStepUpdateState;
            Border chip = database ? LoadingStepDatabaseChip : LoadingStepUpdateChip;
            var accent = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x42, 0xA5, 0xF5));
            var green = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2E, 0xB8, 0x72));
            var spin = icon.RenderTransform as System.Windows.Media.RotateTransform;
            spin?.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, null);
            if (spin != null)
                spin.Angle = 0;
            chip.Background = state switch
            {
                LoadingStepState.Running => new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x26, 0x42, 0xA5, 0xF5)),
                LoadingStepState.Done => new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x26, 0x2E, 0xB8, 0x72)),
                _ => new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x1A, 0x80, 0x80, 0x80))
            };
            if (state == LoadingStepState.Running && spin != null)
                spin.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.1)) { RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever, SpeedRatio = 1 });
            if (state == LoadingStepState.Done && chip.RenderTransform is System.Windows.Media.ScaleTransform pop && !pop.IsFrozen)
            {
                var bounce = new System.Windows.Media.Animation.DoubleAnimation(0.6, 1, TimeSpan.FromMilliseconds(380)) { EasingFunction = new System.Windows.Media.Animation.BackEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut, Amplitude = 0.6 } };
                pop.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, bounce);
                pop.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, bounce);
            }
            switch (state)
            {
                case LoadingStepState.Running:
                    icon.Text = "\uE895";
                    icon.Foreground = accent;
                    label.Text = Loc.T("In progress");
                    label.Foreground = accent;
                    break;
                case LoadingStepState.Done:
                    icon.Text = "\uE73E";
                    icon.Foreground = green;
                    label.Text = Loc.T("Done");
                    label.Foreground = green;
                    break;
                case LoadingStepState.Skipped:
                    icon.Text = "\uE711";
                    icon.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
                    label.Text = Loc.T("Skipped");
                    label.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
                    break;
                default:
                    icon.Text = "\uE91F";
                    icon.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
                    label.Text = "";
                    break;
            }
        }

        public void SetLoadingProgress(double? percent)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(() => SetLoadingProgress(percent));
                return;
            }
            if (percent == null)
            {
                LoadingFill.BeginAnimation(WidthProperty, null);
                LoadingFill.Width = 0;
                LoadingPercent.Text = "";
                if (LoadingSweep.Visibility != Visibility.Visible)
                {
                    LoadingSweep.Visibility = Visibility.Visible;
                    double width = Math.Max(LoadingTrack.ActualWidth, 336);
                    LoadingSweepShift.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty,
                        new System.Windows.Media.Animation.DoubleAnimation(-90, width, TimeSpan.FromSeconds(1.3)) { RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever, SpeedRatio = 1 });
                }
                return;
            }
            LoadingSweep.Visibility = Visibility.Collapsed;
            LoadingSweepShift.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, null);
            double value = Math.Clamp(percent.Value, 0, 100);
            double target = Math.Max(LoadingTrack.ActualWidth, 336) * value / 100;
            double from = double.IsNaN(LoadingFill.Width) ? 0 : LoadingFill.ActualWidth;
            LoadingFill.BeginAnimation(WidthProperty, new System.Windows.Media.Animation.DoubleAnimation(from, target, TimeSpan.FromMilliseconds(260)) { EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } });
            LoadingPercent.Text = $"{value:0}%";
        }

        private void LoadingOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        { 
            if (e.LeftButton == MouseButtonState.Pressed)
            {
         
                this.DragMove();
            }
        }
        public void AnimateIcon(bool isCleaning)
        {
            Dispatcher.Invoke(() =>
            {
                if (isCleaning)
                {
                    _trayAnimator.Start();
                    cleanIcon.StartSpinning();
                }
                else
                {
                    _trayAnimator.Stop();
                    cleanIcon.StopSpinning();
                }
            });
        }

        public const string OfflineModeSettingKey = "offlinemode";

        public static bool IsOfflineModeEnabled()
        {
            try
            {
                string path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Settings.txt");
                if (!System.IO.File.Exists(path)) return false;
                return System.IO.File.ReadAllLines(path)
                    .Any(line => line.Trim().Equals(OfflineModeSettingKey + ":1", StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return false;
            }
        }

        public const string AutoSaveSelectionsSettingKey = "autosaveselections";
        private bool autoSaveSelections = IsAutoSaveSelectionsEnabled();

        public void SetAutoSaveSelections(bool enabled)
        {
            autoSaveSelections = enabled;
            SaveSettings.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        }
        private readonly Dictionary<string, bool?> pendingSelections = new Dictionary<string, bool?>(StringComparer.OrdinalIgnoreCase);
        private readonly object selectionsFileLock = new object();
        private DispatcherTimer? selectionsAutoSaveTimer;

        public const string AllBrowserProfilesSettingKey = "allbrowserprofiles";

        public static bool IsAutoSaveSelectionsEnabled() => IsSettingOnByDefault(AutoSaveSelectionsSettingKey);

        public static bool IsCleanAllBrowserProfilesEnabled() => IsSettingOnByDefault(AllBrowserProfilesSettingKey);

        public static bool IsSettingOnByDefault(string key)
        {
            try
            {
                string path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Settings.txt");
                if (!System.IO.File.Exists(path)) return true;
                return !System.IO.File.ReadAllLines(path)
                    .Any(line => line.Trim().Equals(key + ":0", StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return true;
            }
        }

        public void ReloadDatabaseIfIdle()
        {
            if (ReloadDb.IsEnabled && buttonStartScan.IsEnabled)
                ReloadDatabase_Click(ReloadDb, new RoutedEventArgs());
        }

        private void QueueSelectionAutoSave(CheckBox checkBox, string? path)
        {
            if (!autoSaveSelections || path == null || !databaseDefaults.TryGetValue(path, out bool databaseDefault)) return;

            bool isChecked = checkBox.IsChecked == true;
            lock (pendingSelections)
            {
                pendingSelections[path] = isChecked == databaseDefault ? null : isChecked;
            }

            if (selectionsAutoSaveTimer == null)
            {
                selectionsAutoSaveTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromMilliseconds(500) };
                selectionsAutoSaveTimer.Tick += async (s, e) =>
                {
                    selectionsAutoSaveTimer.Stop();
                    try
                    {
                        await Task.Run(FlushPendingSelections);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("Auto-save of selections failed: " + ex.Message);
                    }
                };
            }
            selectionsAutoSaveTimer.Stop();
            selectionsAutoSaveTimer.Start();
        }

        private void SaveCommandSelection(string key, bool isChecked)
        {
            lock (pendingSelections)
            {
                pendingSelections[key] = isChecked ? true : null;
            }
            _ = Task.Run(() =>
            {
                try
                {
                    FlushPendingSelections();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Could not save the command selection: " + ex.Message);
                }
            });
        }

        private void FlushPendingSelections()
        {
            Dictionary<string, bool?> changes;
            lock (pendingSelections)
            {
                if (pendingSelections.Count == 0) return;
                changes = new Dictionary<string, bool?>(pendingSelections, StringComparer.OrdinalIgnoreCase);
                pendingSelections.Clear();
            }

            lock (selectionsFileLock)
            {
                Dictionary<string, bool> selections = MultronWinCleaner.Processes.Load.ReadSelections();
                foreach (var change in changes)
                {
                    if (change.Value.HasValue) selections[change.Key] = change.Value.Value;
                    else selections.Remove(change.Key);
                }
                MultronWinCleaner.Processes.Load.WriteSelections(selections);
            }
        }

        public const string ShowDiskInfoSettingKey = "showdiskinfo";

        public void SetDiskInfoVisible(bool show) => DiskPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

        public const string ShowItemPathsSettingKey = "showitempaths";

        public void SetItemPathsVisible(bool show)
        {
            if (itemPathsPanel != null)
                itemPathsPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }

        private bool mainWindowLoaded;

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (mainWindowLoaded)
                return;
            mainWindowLoaded = true;
            SetDiskInfoVisible(ReadSettingValue(ShowDiskInfoSettingKey) != "0");
            AppDomain.CurrentDomain.ProcessExit += (s, args) =>
            {
                try { FlushPendingSelections(); } catch { }
            };
            SetAutoSaveSelections(autoSaveSelections);
            ApplyInterfaceToolbar();
            utilities = new Utilities(this);
            settings = new Settings(utilities.memcleaner, utilities, this, utilities.startupmanager);
            utilities.Show();
            utilities.Hide();
            settings.Show();
            settings.Hide();
            if (App.LaunchedFromStartup && !App.LaunchedForMalwareScan
                && !SettingIsOn("trayicon") && !SettingIsOn("utilitiestrayicon"))
            {
                await settings.RemoveFromStartup_ActAsync();
                ExitApplication();
                return;
            }
            await Task.Run(() => { var trayIconTask = new OpenTrayIcon(this).run();  });
            if (App.LaunchedFromStartup || App.LaunchedForMalwareScan)
            {
                this.Visibility = Visibility.Hidden;
            }
            App.SetMalwareScanStarter(paths => _ = utilities.ShowMalwareScan().ScanPathsAsync(paths));
            StartCleanReminder();

            bool offlineMode = IsOfflineModeEnabled();
            SetLoadingStep(false, LoadingStepState.Running);
            SetLoadingStep(true, LoadingStepState.Waiting);
            SetLoadingProgress(null);
            if (offlineMode)
            {
                StatusLoad.Text = Loc.T("Offline mode, skipping online checks...");
            }
            else if (!System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable())
                {
                    this.Visibility = Visibility.Visible;
                    LoadingOverlay.Visibility = Visibility.Visible;

                    const int networkTimeoutSeconds = 10;
                    for (int remaining = networkTimeoutSeconds; remaining > 0 && !System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable(); remaining--)
                    {
                        StatusLoad.Text = Loc.F("Waiting for internet connection... ({0}s)", remaining);
                        await Task.Delay(1000);
                    }

                    StatusLoad.Text = System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable()
                        ? Loc.T("Connected, loading...")
                        : Loc.T("No internet connection, continuing offline...");
                }
         
            bool isOnline = !offlineMode && System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable();
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;

            Environment.CurrentDirectory = baseDirectory;

            LoadingOverlay.Visibility = Visibility.Visible;

            if (isOnline)
            {
                MultronWinCleaner.Processes.Updater updater = new MultronWinCleaner.Processes.Updater(this);
                await Task.Run(() => updater.run());
                SetLoadingStep(false, LoadingStepState.Done);
            }
            else
            {
                SetLoadingStep(false, LoadingStepState.Skipped);
            }
            SetLoadingStep(true, LoadingStepState.Running);
            SetLoadingProgress(0);

            await this.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

            await this.Dispatcher.InvokeAsync(() =>
            {
                dataGridGroups.Visibility = Visibility.Hidden;
                Datagridscroll.Visibility = Visibility.Hidden;
            });

            string databasePath = System.IO.Path.Combine(baseDirectory, "database.txt");

            if (System.IO.File.Exists(databasePath))
            {
                await this.Dispatcher.InvokeAsync(() =>
                {
                    wrapPanelDirectories.Visibility = Visibility.Hidden;
                    ScrollViewerDirectories.Visibility = Visibility.Hidden;
                });
                await this.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                var load = new MultronWinCleaner.Processes.Load(this);
                await Task.Run(() => load.RunAsync());
                ScheduleCleanSelectionsCount();
                SetLoadingProgress(100);
                SetLoadingStep(true, LoadingStepState.Done);

                this.previousWidth = this.Width;
                this.previousHeight = this.Height;
                this.previousLeft = this.Left;
                this.previousTop = this.Top;

                StartDatabaseUpdateTimer();
                ApplyDatabaseUpdateSettings();
            }
            else if (offlineMode)
            {
                label1_Copy.Text = Loc.T("You are running in offline mode and the database could not be found.");
            }
            else
            {
                MessageBoxResult result = AppDialog.Show(
                    Loc.T("The database.txt file could not be found.") + "\n\n" +
                    Loc.T("The program tries to download the latest database.txt from GitHub at startup, but the download failed. Possible reasons:") + "\n" +
                    "  • " + Loc.T("No internet connection") + "\n" +
                    "  • " + Loc.T("A firewall blocking Multron Win Cleaner") + "\n" +
                    "  • " + Loc.T("An antivirus program blocking the download or the file") + "\n" +
                    "  • " + Loc.T("A proxy / VPN or DNS problem") + "\n" +
                    "  • " + Loc.T("GitHub being temporarily unreachable") + "\n\n" +
                    Loc.T("Would you like to be redirected to the GitHub page to manually download the latest database.txt file?"),
                    Loc.T("Multron Windows Cleaner"), MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "https://github.com/Multron-Community/MultronWcleaner-Database",
                        UseShellExecute = true
                    });
                }
            }
       
       
            TrayIcon.TrayMouseDoubleClick += TrayIcon_MouseDoubleClick;

            HideLoadingOverlay();

            autoCleaner = new AutoClean(this);
            _ = autoCleaner.RunAsync();
            await startup();

         

        }
        public class OpenTrayIcon
        {
            MainWindow main;
            public OpenTrayIcon(MainWindow mainWindow) { 
                main = mainWindow;
            }
            public async Task run()
            {
                while(true)
                {
                    await main.Dispatcher.InvokeAsync(() => {

                        if (main.settings.chkTrayIcon.IsChecked == true && main.TrayIcon.Visibility != Visibility.Visible)
                        {

                            main.TrayIcon.Visibility = Visibility.Visible;
                        }
                        else if (main.settings.chkTrayIcon.IsChecked == false && main.TrayIcon.Visibility == Visibility.Visible)
                        {
                            main.TrayIcon.Visibility = Visibility.Hidden;
                        }

                    });
                  
                    await Task.Delay(1250);  
                }
            }
        }

        private CheckBox? malwareScanCheckBox;
        private ComboBox? malwareScanModeBox;
        private Expander? malwareScanExpander;
        private StackPanel? malwareCloudPanel;
        private bool malwareScanWasCheckedBeforeOffline;

        public void ApplyOfflineModeToMalwareScan()
        {
            if (malwareScanCheckBox == null || malwareCloudPanel == null) return;
            bool offline = IsOfflineModeEnabled();
            bool wasOffline = !malwareCloudPanel.IsEnabled;
            if (offline == wasOffline) return;

            if (offline)
            {
                malwareScanWasCheckedBeforeOffline = malwareScanCheckBox.IsChecked == true;
                malwareScanCheckBox.Checked -= MalwareCheckBoxSave;
                malwareScanCheckBox.Unchecked -= MalwareCheckBoxSave;
                malwareScanCheckBox.IsChecked = false;
                malwareScanCheckBox.Checked += MalwareCheckBoxSave;
                malwareScanCheckBox.Unchecked += MalwareCheckBoxSave;
                malwareCloudPanel.IsEnabled = false;
                ToolTipService.SetShowOnDisabled(malwareCloudPanel, true);
                malwareCloudPanel.ToolTip = Loc.T("Malware scan is disabled in offline mode.");
            }
            else
            {
                malwareCloudPanel.IsEnabled = true;
                malwareCloudPanel.ToolTip = null;
                malwareScanCheckBox.Checked -= MalwareCheckBoxSave;
                malwareScanCheckBox.Unchecked -= MalwareCheckBoxSave;
                malwareScanCheckBox.IsChecked = malwareScanWasCheckedBeforeOffline;
                malwareScanCheckBox.Checked += MalwareCheckBoxSave;
                malwareScanCheckBox.Unchecked += MalwareCheckBoxSave;
            }
        }

        private void MalwareCheckBoxSave(object sender, RoutedEventArgs e)
        {
            utilities?.savesettings($"{MultronWinCleaner.MalwareScan.WithMainScanSettingKey}:{(malwareScanCheckBox?.IsChecked == true ? "1" : "0")}");
        }

        #region Microsoft MRT scan

        public const string MrtScanSettingKey = "mrtscanwithmain";
        public const string MrtModeSettingKey = "mrtscanmode";
        public const string MrtRemoveSettingKey = "mrtremovethreats";
        private const string MrtOptionKey = "mrtscan";
        private CheckBox? mrtScanCheckBox;
        private ComboBox? mrtModeBox;
        private CheckBox? mrtRemoveCheckBox;
        private bool mrtScanQueued;
        private bool mrtStoppedByUser;
        private CancellationTokenSource? mrtCts;

        public bool IsMrtScanning => mrtCts != null;

        private void AddMrtScanOption(Panel parent)
        {
            mrtScanCheckBox = null;
            mrtModeBox = null;
            mrtRemoveCheckBox = null;

            if (!MultronWinCleaner.Processes.MrtScanner.IsAvailable)
            {
                parent.Children.Add(new TextBlock
                {
                    Text = Loc.T("Microsoft MRT scan is not available: MRT.exe was not found on this PC. Windows Update installs it."),
                    Margin = new Thickness(12, 10, 10, 6),
                    FontSize = 12,
                    FontWeight = FontWeights.Regular,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 420,
                    Foreground = ThemeColors.ReadableBrush(WarningColor)
                });
                return;
            }

            CheckBox mrt = new CheckBox
            {
                Content = Loc.T("Microsoft MRT Scan") + "=" + Loc.T("Scans with the Malicious Software Removal Tool built into Windows. It runs hidden, before the cloud scan.") + "=" + MrtOptionKey,
                Margin = new Thickness(10),
                IsChecked = SettingIsOn(MrtScanSettingKey),
                BorderThickness = new Thickness(0),
                BorderBrush = new SolidColorBrush(Colors.Transparent),
                Background = new SolidColorBrush(System.Windows.Media.Colors.White),
                Foreground = brush,
                FontSize = 12,
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                FontWeight = FontWeights.Regular,
            };
            ApplyOptionTemplate(mrt);
            checkboxes2.Add(mrt);

            ComboBox mode = new ComboBox
            {
                Margin = new Thickness(34, 2, 6, 8),
                Height = 32,
                MaxWidth = 300,
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                ItemsSource = new List<string> { Loc.T("Quick Scan"), Loc.T("Full Scan") },
                SelectedIndex = string.Equals(ReadSettingValue(MrtModeSettingKey), "full", StringComparison.OrdinalIgnoreCase) ? 1 : 0,
                IsEnabled = mrt.IsChecked == true,
                ToolTip = Loc.T("Quick: the places malicious software usually uses, a few minutes.\nFull: every fixed drive, this can take hours.")
            };

            StyleCardComboBox(mode);

            CheckBox remove = new CheckBox
            {
                Content = Loc.T("Remove found malicious software right away"),
                Margin = new Thickness(34, 0, 10, 10),
                IsChecked = SettingIsOn(MrtRemoveSettingKey),
                IsEnabled = mrt.IsChecked == true,
                FontSize = 12,
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                FontWeight = FontWeights.Regular,
                ToolTip = Loc.T("When this is off, MRT only reports what it finds. You can then remove it with the Remove with MRT button in the results.")
            };
            remove.ContentTemplate = WrappingCheckBoxTemplate;
            remove.SetResourceReference(Control.ForegroundProperty, "Text");

            mrt.Checked += (s, e) => { mode.IsEnabled = true; remove.IsEnabled = true; utilities?.savesettings(MrtScanSettingKey + ":1"); };
            mrt.Unchecked += (s, e) => { mode.IsEnabled = false; remove.IsEnabled = false; utilities?.savesettings(MrtScanSettingKey + ":0"); };
            mode.SelectionChanged += (s, e) => utilities?.savesettings(MrtModeSettingKey + ":" + (mode.SelectedIndex == 1 ? "full" : "quick"));
            remove.Checked += (s, e) => utilities?.savesettings(MrtRemoveSettingKey + ":1");
            remove.Unchecked += (s, e) => utilities?.savesettings(MrtRemoveSettingKey + ":0");

            mrtScanCheckBox = mrt;
            mrtModeBox = mode;
            mrtRemoveCheckBox = remove;
            parent.Children.Add(mrt);
            parent.Children.Add(mode);
            parent.Children.Add(remove);
        }

        private static string FormatElapsed(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"mm\:ss");

        private MultronWinCleaner.Processes.MrtScanner.Result? lastMrtResult;

        private Task RunMrtScanAsync() =>
            RunMrtScanAsync(mrtModeBox?.SelectedIndex == 1, mrtRemoveCheckBox?.IsChecked == true);

        private async Task RunMrtScanAsync(bool full, bool remove)
        {
            bool automatic = autoclean == 1 || startupscan == 1;
            string scanName = remove ? Loc.T(full ? "Full scan with removal" : "Quick scan with removal") : Loc.T(full ? "Full Scan" : "Quick Scan");

            mrtStoppedByUser = false;
            mrtCts = new CancellationTokenSource();
            mrtRows.Clear();
            lastMrtResult = null;
            mrtThreatCount = 0;
            mrtPanelStatus = Loc.F("{0} running...", scanName);
            mrtPanelColor = InfoColor;
            RefreshMalwarePanel(scanning: true);
            FollowStep(MalwareResultsPanel);
            EnterMalwareCancelMode(Equals(buttonStartScan.Content, Loc.T("Clean")) ? "Clean" : "Scan");
            progressBar1.Value = 0;
            SetStatusText(Loc.F("Microsoft MRT: {0} started", scanName), InfoColor);

            var progress = new Progress<(double Percent, TimeSpan Elapsed)>(p =>
            {
                if (mrtCts == null) return;
                string text = Loc.F("{0}: {1}% · {2}", scanName, (int)p.Percent, FormatElapsed(p.Elapsed));
                mrtPanelStatus = text;
                MalwareResultsStatus.Text = ComposeMalwareStatus();
                if (malwareCancelRestore != null)
                {
                    progressBar1.Value = p.Percent;
                    SetStatusText(Loc.F("Microsoft MRT: {0}", text), InfoColor);
                }
            });

            MultronWinCleaner.Processes.MrtScanner.Result result;
            try
            {
                result = await MultronWinCleaner.Processes.MrtScanner.RunAsync(full, remove, progress, mrtCts.Token);
            }
            catch (Exception ex)
            {
                result = new MultronWinCleaner.Processes.MrtScanner.Result
                {
                    Outcome = MultronWinCleaner.Processes.MrtScanner.Outcome.Failed,
                    Message = Loc.F("Microsoft MRT could not run: {0}", ex.Message)
                };
            }
            finally
            {
                mrtCts.Dispose();
                mrtCts = null;
            }

            var outcome = result.Outcome;
            mrtStoppedByUser = outcome == MultronWinCleaner.Processes.MrtScanner.Outcome.Stopped;
            mrtPanelColor = outcome switch
            {
                MultronWinCleaner.Processes.MrtScanner.Outcome.Clean => OkColor,
                MultronWinCleaner.Processes.MrtScanner.Outcome.Threats => ProblemColor,
                _ => WarningColor
            };
            mrtPanelStatus = result.Message + (result.Elapsed > TimeSpan.Zero ? " (" + FormatElapsed(result.Elapsed) + ")" : "");
            mrtThreatCount = result.ThreatCount;
            lastMrtResult = outcome == MultronWinCleaner.Processes.MrtScanner.Outcome.Stopped ? null : result;

            if (outcome == MultronWinCleaner.Processes.MrtScanner.Outcome.Threats)
            {
                var lines = result.Summary.Where(l => !l.Contains("No infection found", StringComparison.OrdinalIgnoreCase)).ToList();
                if (lines.Count == 0)
                    lines.Add(Loc.T("Details are in mrt.log."));
                string verdict = Loc.T(result.CanRemove ? "Detected" : "Removed");
                foreach (string line in lines)
                    mrtRows.Add(new MalwareListRow(Loc.T("Microsoft MRT"), verdict, line, "", ""));
            }

            if (!mrtStoppedByUser)
                progressBar1.Value = 100;
            SetStatusText(Loc.F("Microsoft MRT: {0}", result.Message), mrtPanelColor);
            RefreshMalwarePanel(scanning: false);
            LeaveMalwareCancelMode();

            if (IsSettingOnByDefault(NotifyMalwareSettingKey)
                && (outcome == MultronWinCleaner.Processes.MrtScanner.Outcome.Threats || (automatic && outcome == MultronWinCleaner.Processes.MrtScanner.Outcome.Clean)))
            {
                bool threats = outcome == MultronWinCleaner.Processes.MrtScanner.Outcome.Threats;
                var notify = new Notify(Loc.T(threats ? "Microsoft MRT found malicious software!" : "Microsoft MRT scan finished."), result.Message, null, BringToFront);
                notify.Styled(threats ? Notify.Kind.Danger : Notify.Kind.Success, "\uEA18").Show();
            }
        }

        private void StopMrtScan()
        {
            try { mrtCts?.Cancel(); } catch (ObjectDisposedException) { }
        }

        private void MrtResultsButton_Click(object sender, RoutedEventArgs e)
        {
            if (lastMrtResult == null)
            {
                OpenMrtLog();
                return;
            }
            new MrtResultsWindow(this, lastMrtResult, mrtPanelColor,
                lastMrtResult.CanRemove ? () => _ = RemoveWithMrtAsync() : null, OpenMrtLog).Show();
        }

        private void MrtRemoveButton_Click(object sender, RoutedEventArgs e) => _ = RemoveWithMrtAsync();

        private async Task RemoveWithMrtAsync()
        {
            var previous = lastMrtResult;
            if (previous == null || !previous.CanRemove)
                return;
            if (IsMrtScanning || utilities?.malwarescan?.IsScanning == true || IsScanOrCleanBusy || malwareCancelRestore != null)
            {
                AppDialog.Show(Loc.T("Please wait until the current scan or clean has finished."), Loc.T("Microsoft MRT"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var answer = AppDialog.Show(
                Loc.F("Microsoft MRT scans again ({0}) and removes the malicious software it finds. Save your work first, a restart may be needed afterwards.\n\nContinue?",
                    Loc.T(previous.FullScan ? "Full Scan" : "Quick Scan")),
                Loc.T("Remove with MRT"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes)
                return;

            await RunMrtScanAsync(previous.FullScan, remove: true);
        }

        private void OpenMrtLog()
        {
            string log = MultronWinCleaner.Processes.MrtScanner.LogPath;
            if (!System.IO.File.Exists(log))
            {
                AppDialog.Show(Loc.F("Path not found:\n{0}", log), Loc.T("Microsoft MRT"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo("notepad.exe", "\"" + log + "\"") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                AppDialog.Show(Loc.F("Error:\n{0}", ex.Message), Loc.T("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Malware results panel (MRT and cloud scan in one list)

        public sealed class MalwareListRow
        {
            public bool IsSuspicious { get; }

            public MalwareListRow(string source, string verdictText, string fileName, string threatName, string filePath, bool isSuspicious = false)
            {
                IsSuspicious = isSuspicious;
                Source = source;
                VerdictText = verdictText;
                FileName = fileName;
                ThreatName = threatName;
                FilePath = filePath;
            }

            public string Source { get; }
            public string VerdictText { get; }
            public string FileName { get; }
            public string ThreatName { get; }
            public string FilePath { get; }
        }

        private readonly List<MalwareListRow> mrtRows = new List<MalwareListRow>();
        private List<MalwareListRow> cloudRows = new List<MalwareListRow>();
        private string? mrtPanelStatus;
        private string mrtPanelColor = InfoColor;
        private int mrtThreatCount;
        private string? cloudPanelStatus;
        private string cloudPanelColor = InfoColor;

        private string ComposeMalwareStatus()
        {
            if (mrtPanelStatus == null)
                return cloudPanelStatus ?? "";
            string text = Loc.F("Microsoft MRT: {0}", mrtPanelStatus);
            if (cloudPanelStatus != null)
                text += "\n" + Loc.F("Multron Cloud: {0}", cloudPanelStatus);
            return text;
        }

        private static int ColorRank(string color) => color switch
        {
            ProblemColor => 3,
            WarningColor => 2,
            InfoColor => 1,
            _ => 0
        };

        private void RefreshMalwarePanel(bool scanning)
        {
            string color = mrtPanelStatus == null ? cloudPanelColor
                : cloudPanelStatus == null ? mrtPanelColor
                : ColorRank(mrtPanelColor) >= ColorRank(cloudPanelColor) ? mrtPanelColor : cloudPanelColor;
            SetResultPanelColor(MalwareResultsPanel, MalwareResultsIcon, color);
            MalwareResultsTitle.Text = Loc.T(mrtPanelStatus != null ? "Malware Scan" : "Multron Malware Scan Cloud");
            MalwareResultsStatus.Text = ComposeMalwareStatus();
            MalwareResultsPanel.Visibility = Visibility.Visible;
            MalwareCancelButton.Visibility = scanning ? Visibility.Visible : Visibility.Collapsed;
            if (!scanning)
                MalwareCancelButton.IsEnabled = true;
            MalwarePanelModeBox.IsEnabled = !scanning;
            MalwareMrtResultsButton.Visibility = mrtPanelStatus != null && !scanning ? Visibility.Visible : Visibility.Collapsed;
            MalwareMrtRemoveButton.Visibility = !scanning && lastMrtResult?.CanRemove == true ? Visibility.Visible : Visibility.Collapsed;
            MalwareResultsButton.Visibility = cloudPanelStatus != null || mrtPanelStatus == null ? Visibility.Visible : Visibility.Collapsed;

            var rows = mrtRows.Concat(cloudRows).ToList();
            MalwareThreatsList.ItemsSource = rows;
            MalwareThreatsList.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        #endregion

        public async Task loadothers2()
        {
            CheckBox malscan = new CheckBox
            {
                Content = Loc.T("Multron Malware Scan Cloud") + "=" + Loc.T("Scans your system for malicious software in the cloud with the viruskov.com OPEN-EDR engine") + "=malscan",
                Margin = new Thickness(10),
                IsChecked = MultronWinCleaner.MalwareScan.IsAutoScanWithMainEnabled(),
                BorderThickness = new Thickness(0),
                BorderBrush = new SolidColorBrush(Colors.Transparent),
                Background = new SolidColorBrush(System.Windows.Media.Colors.White),
                Foreground = brush,
                FontSize = 12,
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                FontWeight = FontWeights.Regular,
                FontStyle = FontStyles.Normal,
            };
            ApplyOptionTemplate(malscan);
            checkboxes2.Add(malscan);

            ComboBox scanOptionsComboBox = new ComboBox
            {
                Margin = new Thickness(34, 2, 6, 10),
                Height = 32,
                MaxWidth = 300,
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                IsEnabled = malscan.IsChecked == true,
                ItemsSource = new List<string> { Loc.T("Quick Scan"), Loc.T("Full Scan"), Loc.T("Custom Scan"), Loc.T("Running Apps"), Loc.T("Running & Startup"), Loc.T("Critical Scan") },
                SelectedIndex = (int)MultronWinCleaner.MalwareScan.GetSavedMode(),
                ToolTip = Loc.T("Quick: running programs and the modules they load, startup items, services, drivers, scheduled tasks and other autostart entries, the Windows and System32 folders, AppData and ProgramData, Downloads, Desktop and temp folders.\nFull: Quick + the whole Windows drive.\nCustom: the drives, folders and files set in the Malware Scan window.")
            };

            StyleCardComboBox(scanOptionsComboBox);

            TextBlock malwareSettingsLink = new TextBlock { Margin = new Thickness(12, 0, 6, 10), FontSize = 12, TextWrapping = TextWrapping.Wrap };
            Hyperlink settingsHyperlink = new Hyperlink(new Run(Loc.T("Malware Scan settings & results")));
            settingsHyperlink.Click += (s, e) => utilities?.ShowMalwareScan();
            malwareSettingsLink.Inlines.Add(settingsHyperlink);

            malwareScanCheckBox = malscan;
            malwareScanModeBox = scanOptionsComboBox;
            MalwarePanelModeBox.SelectedIndex = scanOptionsComboBox.SelectedIndex;
            scanOptionsComboBox.SelectionChanged += (s, e) =>
            {
                if (MalwarePanelModeBox.SelectedIndex != scanOptionsComboBox.SelectedIndex)
                    MalwarePanelModeBox.SelectedIndex = scanOptionsComboBox.SelectedIndex;
                utilities?.malwarescan?.SetScanMode(scanOptionsComboBox.SelectedIndex);
            };

            malscan.Checked += (s, e) => scanOptionsComboBox.IsEnabled = true;
            malscan.Unchecked += (s, e) => scanOptionsComboBox.IsEnabled = false;
            malscan.Checked += MalwareCheckBoxSave;
            malscan.Unchecked += MalwareCheckBoxSave;
            scanOptionsComboBox.SelectionChanged += (s, e) =>
                utilities?.savesettings($"{MultronWinCleaner.MalwareScan.ModeSettingKey}:{(MultronWinCleaner.MalwareScan.ScanMode)scanOptionsComboBox.SelectedIndex}");

            StackPanel groupBoxContent2 = new StackPanel
            {
                Orientation = Orientation.Vertical,
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            stackpanels.Add(groupBoxContent2);

            StackPanel cloudPanel = new StackPanel { Orientation = Orientation.Vertical };
            cloudPanel.Children.Add(malscan);
            cloudPanel.Children.Add(scanOptionsComboBox);
            cloudPanel.Children.Add(malwareSettingsLink);
            malwareCloudPanel = cloudPanel;

            AddMrtScanOption(groupBoxContent2);
            groupBoxContent2.Children.Add(cloudPanel);

            Expander newExpander1 = new Expander
            {
                Header = IsSimpleInterface() ? Loc.T("Malware Scan") : Loc.T("Malware Scan") + " (" + Loc.T("Testing") + ")",
                Margin = new Thickness(5),
                Background = new SolidColorBrush(Colors.Transparent),
                Foreground = brush,
                BorderBrush = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(2),
                FontSize = 14,
                FontWeight = FontWeights.Regular,
                IsEnabled = true,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            };
            newExpander1.Content = groupBoxContent2;
            expanders.Add(newExpander1);
            malwareScanExpander = newExpander1;
            try
            {
                AddOptionExpander(newExpander1);
            }
            catch (Exception)
            {
            }
            ApplyOfflineModeToMalwareScan();

            AddDuplicateScanOption();
            AddLargeFileScanOption();
            AddFirewallScanOption();
            AddShortcutScanOption();
            AddSecurityScanOption();
          
        }

        public AutoClean autoCleaner = null!;
        public string AutoCleanStatus = "";

        public void SetAutoCleanStatus(string? text)
        {
            AutoCleanStatus = text ?? "";
            labelAutoClean.Text = AutoCleanStatus;
            labelAutoClean.Visibility = AutoCleanStatus.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

            bool scan = settings?.chkEnableStartupScan.IsChecked == true;
            bool clean = settings?.chkEnableStartupClean.IsChecked == true;
            labelStartupClean.Text = clean ? Loc.T("Startup Clean is on · scans and cleans when Windows starts")
                : scan ? Loc.T("Startup Scan is on · scans when Windows starts") : "";
            labelStartupClean.Visibility = clean || scan ? Visibility.Visible : Visibility.Collapsed;
        }

        public async Task<bool> StartAutoCleanAsync()
        {
            if (autoclean != 0 || !Equals(buttonStartScan.Content, Loc.T("Scan")) || LockedFilesWindowOverlay.Visibility == Visibility.Visible || utilities?.malwarescan?.IsScanning == true)
                return false;
            if (!buttonStartScan.IsEnabled)
            {
                if (reset != 1)
                    return false;
                ButtonReset_Click(this, new RoutedEventArgs());
            }

            await UpdateDatabaseIfDueAsync(true);
            if (autoclean != 0 || !buttonStartScan.IsEnabled || !Equals(buttonStartScan.Content, Loc.T("Scan")))
                return false;

            if (checkboxes2.All(cb => cb.IsChecked != true))
                return false;

            autoclean = 1;
            await startscan();
            return true;
        }

        public const string DuplicateScanSettingKey = "duplicatescanwithmain";
        private CheckBox? duplicateScanCheckBox;
        private bool duplicateReportRunning;

        private void AddDuplicateScanOption()
        {
            CheckBox dupscan = new CheckBox
            {
                Content = Loc.T("Find duplicate files after the scan (nothing is deleted automatically)"),
                Margin = new Thickness(10),
                IsChecked = IsSettingOnByDefault(DuplicateScanSettingKey),
                BorderThickness = new Thickness(0),
                BorderBrush = new SolidColorBrush(Colors.Transparent),
                Background = new SolidColorBrush(System.Windows.Media.Colors.White),
                FontSize = 12,
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                FontWeight = FontWeights.Regular,
                ToolTip = Loc.T("After a scan you start yourself, identical files in Desktop, Documents, Downloads, Pictures, Videos and Music are searched in the background and shown as a warning above the results.")
            };
            dupscan.Checked += (s, e) => utilities?.savesettings(DuplicateScanSettingKey + ":1");
            dupscan.Unchecked += (s, e) => utilities?.savesettings(DuplicateScanSettingKey + ":0");
            dupscan.ContentTemplate = WrappingCheckBoxTemplate;
            duplicateScanCheckBox = dupscan;
            dupscan.SetResourceReference(Control.ForegroundProperty, "Text");

            StackPanel content = new StackPanel
            {
                Orientation = Orientation.Vertical,
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            stackpanels.Add(content);
            content.Children.Add(dupscan);

            Expander expander = new Expander
            {
                Header = Loc.T("Duplicate Files"),
                Margin = new Thickness(5),
                Background = new SolidColorBrush(Colors.Transparent),
                Foreground = brush,
                BorderBrush = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(2),
                FontSize = 14,
                FontWeight = FontWeights.Regular,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Content = content
            };
            expanders.Add(expander);
            AddOptionExpander(expander);
        }

        public const string OkColor = "#28A745";
        public const string InfoColor = "#1E88E5";
        public const string WarningColor = "#FF9800";
        public const string ProblemColor = "#DC3545";

        public static void SetResultPanelColor(Border panel, System.Windows.Shapes.Ellipse icon, string hex)
        {
            var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
            icon.Fill = new SolidColorBrush(color);
            panel.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x66, color.R, color.G, color.B));
            panel.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x14, color.R, color.G, color.B));
        }

        private (Border Panel, System.Windows.Shapes.Ellipse Icon, TextBlock Status, CheckBox Run) SystemCheckControls(string id) => id switch
        {
            "store" => (ComponentStorePanel, ComponentStoreIcon, ComponentStoreStatus, ComponentStoreRun),
            "health" => (ComponentHealthPanel, ComponentHealthIcon, ComponentHealthStatus, ComponentHealthRun),
            _ => (SystemFilesPanel, SystemFilesIcon, SystemFilesStatus, SystemFilesRun)
        };

        private void SetSystemCheck(string id, string color, string text, bool showRun, bool runChecked)
        {
            SetStepBusy(id == "sfc" ? "System File Check (SFC)" : "Windows Repair (DISM)", false);
            var c = SystemCheckControls(id);
            c.Panel.Visibility = Visibility.Visible;
            SetResultPanelColor(c.Panel, c.Icon, color);
            c.Status.Text = text;
            c.Run.Visibility = showRun ? Visibility.Visible : Visibility.Collapsed;
            c.Run.IsChecked = runChecked;
        }

        private void HideSystemChecks()
        {
            foreach (string id in new[] { "store", "health", "sfc" })
            {
                var c = SystemCheckControls(id);
                c.Panel.Visibility = Visibility.Collapsed;
                c.Run.IsChecked = false;
            }
        }

        public void ShowSystemCheckRunning(string id)
        {
            string text = id switch
            {
                "store" => Loc.T("Analyzing the component store..."),
                "health" => Loc.T("Checking the component store for corruption..."),
                _ => Loc.T("Checking Windows system files (sfc /verifyonly)...")
            };
            SetSystemCheck(id, InfoColor, text, false, false);
            SetStepBusy(id == "sfc" ? "System File Check (SFC)" : "Windows Repair (DISM)", true);
        }

        public void ShowComponentStoreResult(SystemChecks.ComponentStoreInfo info)
        {
            if (!info.Parsed)
            {
                SetSystemCheck("store", WarningColor, Loc.T("Dism.exe did not report the component store size. Details are in dism_scan.log. You can still run the cleanup."), true, false);
                return;
            }

            var details = new List<string>();
            if (info.ActualSize >= 0) details.Add(Loc.F("size {0}", formatsize(info.ActualSize)));
            details.Add(Loc.F("reclaimable {0} (backups {1}, cache {2})", formatsize(info.Reclaimable), formatsize(Math.Max(0, info.Backups)), formatsize(Math.Max(0, info.Cache))));
            details.Add(Loc.F(info.ReclaimablePackages == 1 ? "{0} reclaimable package" : "{0} reclaimable packages", info.ReclaimablePackages));
            if (!string.IsNullOrEmpty(info.LastCleanup)) details.Add(Loc.F("last cleanup {0}", info.LastCleanup));
            string detailText = string.Join(" · ", details);
            if (info.RestartPending) detailText += Loc.T(" · a restart is pending");

            bool recommended = info.CleanupRecommended == true || info.ReclaimablePackages > 0;
            if (recommended)
                SetSystemCheck("store", WarningColor, Loc.F("Cleanup is recommended: {0}", detailText), true, true);
            else
                SetSystemCheck("store", OkColor, Loc.F("No cleanup needed: {0}", detailText), true, false);
        }

        public void ShowComponentHealthResult(SystemChecks.HealthState state, string detail)
        {
            switch (state)
            {
                case SystemChecks.HealthState.Healthy:
                    SetSystemCheck("health", OkColor, Loc.T("No component store corruption was found."), false, false);
                    break;
                case SystemChecks.HealthState.Repairable:
                    SetSystemCheck("health", ProblemColor, Loc.T("Corruption was found. Restore Health can repair it (an internet connection may be needed)."), true, true);
                    break;
                case SystemChecks.HealthState.NotRepairable:
                    SetSystemCheck("health", ProblemColor, Loc.T("Corruption was found that DISM reports as not repairable. Restore Health will try to download fresh files from Windows Update."), true, true);
                    break;
                default:
                    SetSystemCheck("health", WarningColor, Loc.F("The result could not be read: {0} (details in dism_health.log)", detail), true, false);
                    break;
            }
        }

        public void ShowSystemFilesResult(SystemChecks.SfcState state, string detail)
        {
            switch (state)
            {
                case SystemChecks.SfcState.Clean:
                    SetSystemCheck("sfc", OkColor, Loc.T("Windows Resource Protection found no integrity violations."), false, false);
                    break;
                case SystemChecks.SfcState.Unknown:
                    SetSystemCheck("sfc", WarningColor, Loc.F("The result could not be read: {0} (details in sfc_verify.log)", detail), true, false);
                    break;
                default:
                    SetSystemCheck("sfc", ProblemColor, Loc.T("Integrity violations were found in Windows system files. sfc /scannow can repair them."), true, true);
                    break;
            }
        }

        public (bool Cleanup, bool Restore, bool Sfc) GetSystemRepairChoices()
        {
            bool Selected(string id)
            {
                var c = SystemCheckControls(id);
                return c.Panel.Visibility == Visibility.Visible && c.Run.Visibility == Visibility.Visible && c.Run.IsChecked == true;
            }
            return (Selected("store"), Selected("health"), Selected("sfc"));
        }

        public const string NotifySecuritySettingKey = "notifysecurity";
        public const string NotifyFirewallSettingKey = "notifyfirewall";
        public const string NotifyDuplicatesSettingKey = "notifyduplicates";
        public const string NotifyMalwareSettingKey = "notifymalware";
        public const string FirewallScanSettingKey = "firewallscanwithmain";
        private CheckBox? firewallScanCheckBox;
        private bool firewallReportRunning;
        private List<MultronWinCleaner.Processes.FirewallRules.InvalidRule> foundFirewallRules = new List<MultronWinCleaner.Processes.FirewallRules.InvalidRule>();

        private void AddFirewallScanOption()
        {
            CheckBox fwscan = new CheckBox
            {
                Content = Loc.T("Find broken firewall rules after the scan"),
                Margin = new Thickness(10),
                IsChecked = IsSettingOnByDefault(FirewallScanSettingKey),
                BorderThickness = new Thickness(0),
                BorderBrush = new SolidColorBrush(Colors.Transparent),
                Background = new SolidColorBrush(System.Windows.Media.Colors.White),
                FontSize = 12,
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                FontWeight = FontWeights.Regular,
                ToolTip = Loc.T("After a scan, Windows Firewall rules for programs that no longer exist on this PC are listed above the results. They are only removed when you click Remove Rules.")
            };
            fwscan.Checked += (s, e) => utilities?.savesettings(FirewallScanSettingKey + ":1");
            fwscan.Unchecked += (s, e) => utilities?.savesettings(FirewallScanSettingKey + ":0");
            fwscan.ContentTemplate = WrappingCheckBoxTemplate;
            firewallScanCheckBox = fwscan;
            fwscan.SetResourceReference(Control.ForegroundProperty, "Text");

            StackPanel content = new StackPanel
            {
                Orientation = Orientation.Vertical,
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            stackpanels.Add(content);
            content.Children.Add(fwscan);

            Expander expander = new Expander
            {
                Header = Loc.T("Firewall Rules"),
                Margin = new Thickness(5),
                Background = new SolidColorBrush(Colors.Transparent),
                Foreground = brush,
                BorderBrush = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(2),
                FontSize = 14,
                FontWeight = FontWeights.Regular,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Content = content
            };
            expanders.Add(expander);
            AddOptionExpander(expander);
        }

        public const string ShortcutScanSettingKey = "shortcutscanwithmain";
        private CheckBox? shortcutScanCheckBox;
        private bool shortcutReportRunning;
        private List<MultronWinCleaner.Processes.ShortcutFixer.BrokenShortcut> foundShortcuts = new List<MultronWinCleaner.Processes.ShortcutFixer.BrokenShortcut>();

        private void AddShortcutScanOption()
        {
            CheckBox scscan = new CheckBox
            {
                Content = Loc.T("Find broken shortcuts after the scan"),
                Margin = new Thickness(10),
                IsChecked = IsSettingOnByDefault(ShortcutScanSettingKey),
                BorderThickness = new Thickness(0),
                BorderBrush = new SolidColorBrush(Colors.Transparent),
                Background = new SolidColorBrush(System.Windows.Media.Colors.White),
                FontSize = 12,
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                FontWeight = FontWeights.Regular,
                ToolTip = Loc.T("After a scan, Desktop and Start Menu shortcuts whose program no longer exists are listed above the results. They are only changed when you click Fix Shortcuts.")
            };
            scscan.Checked += (s, e) => utilities?.savesettings(ShortcutScanSettingKey + ":1");
            scscan.Unchecked += (s, e) => utilities?.savesettings(ShortcutScanSettingKey + ":0");
            scscan.ContentTemplate = WrappingCheckBoxTemplate;
            shortcutScanCheckBox = scscan;
            scscan.SetResourceReference(Control.ForegroundProperty, "Text");

            StackPanel content = new StackPanel
            {
                Orientation = Orientation.Vertical,
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            stackpanels.Add(content);
            content.Children.Add(scscan);

            Expander expander = new Expander
            {
                Header = Loc.T("Shortcuts"),
                Margin = new Thickness(5),
                Background = new SolidColorBrush(Colors.Transparent),
                Foreground = brush,
                BorderBrush = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(2),
                FontSize = 14,
                FontWeight = FontWeights.Regular,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Content = content
            };
            expanders.Add(expander);
            AddOptionExpander(expander);
        }

        public const string SecurityScanSettingKey = "securityscanwithmain";
        private CheckBox? securityScanCheckBox;
        private bool securityReportRunning;
        private List<MultronWinCleaner.Processes.SecurityCheck.Issue> foundSecurityIssues = new List<MultronWinCleaner.Processes.SecurityCheck.Issue>();

        private void AddSecurityScanOption()
        {
            CheckBox secscan = new CheckBox
            {
                Content = Loc.T("Check Windows security settings after the scan"),
                Margin = new Thickness(10),
                IsChecked = IsSettingOnByDefault(SecurityScanSettingKey),
                BorderThickness = new Thickness(0),
                BorderBrush = new SolidColorBrush(Colors.Transparent),
                Background = new SolidColorBrush(System.Windows.Media.Colors.White),
                FontSize = 12,
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                FontWeight = FontWeights.Regular,
                ToolTip = Loc.T("After a scan, settings that make Windows less secure are listed above the results, for example UAC or Windows Firewall turned off, AutoPlay on for USB drives or Defender turned off by a policy. They are only changed when you click Fix Selected.")
            };
            secscan.Checked += (s, e) => utilities?.savesettings(SecurityScanSettingKey + ":1");
            secscan.Unchecked += (s, e) => utilities?.savesettings(SecurityScanSettingKey + ":0");
            secscan.ContentTemplate = WrappingCheckBoxTemplate;
            securityScanCheckBox = secscan;
            secscan.SetResourceReference(Control.ForegroundProperty, "Text");

            StackPanel content = new StackPanel
            {
                Orientation = Orientation.Vertical,
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            stackpanels.Add(content);
            content.Children.Add(secscan);

            Expander expander = new Expander
            {
                Header = Loc.T("Security"),
                Margin = new Thickness(5),
                Background = new SolidColorBrush(Colors.Transparent),
                Foreground = brush,
                BorderBrush = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(2),
                FontSize = 14,
                FontWeight = FontWeights.Regular,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Content = content
            };
            expanders.Add(expander);
            AddOptionExpander(expander);
        }

        public static string DescribeSecurityIssues(List<MultronWinCleaner.Processes.SecurityCheck.Issue> issues, out string color)
        {
            int high = issues.Count(i => i.Severity == MultronWinCleaner.Processes.SecurityCheck.Level.High);
            int medium = issues.Count(i => i.Severity == MultronWinCleaner.Processes.SecurityCheck.Level.Medium);
            int low = issues.Count - high - medium;
            color = high > 0 ? ProblemColor : medium > 0 ? WarningColor : issues.Count > 0 ? InfoColor : OkColor;
            int ignored = MultronWinCleaner.Processes.SecurityCheck.IgnoredCount;
            string ignoredNote = ignored > 0 ? " " + Loc.F(ignored == 1 ? "{0} warning is ignored." : "{0} warnings are ignored.", ignored) : "";
            if (issues.Count == 0)
                return Loc.T("No insecure Windows settings were found.") + ignoredNote;
            var parts = new List<string>();
            if (high > 0) parts.Add(Loc.F("{0} high", high));
            if (medium > 0) parts.Add(Loc.F("{0} medium", medium));
            if (low > 0) parts.Add(Loc.F("{0} low", low));
            return Loc.F(issues.Count == 1 ? "{0} setting makes Windows less secure ({1} risk)." : "{0} settings make Windows less secure ({1} risk).", issues.Count, string.Join(", ", parts)) + ignoredNote;
        }

        private void IgnoreSecurityIssue_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is MultronWinCleaner.Processes.SecurityCheck.Issue issue)
                IgnoreSecurityIssue(issue);
        }

        public void IgnoreSecurityIssue(MultronWinCleaner.Processes.SecurityCheck.Issue issue)
        {
            var answer = AppDialog.Show(
                Loc.F("Ignore this warning from now on?\n\n{0}\n\nIt is no longer shown or reported. You can show ignored warnings again in Utilities > Security Check.", issue.Title),
                Loc.T("Security Check"), MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes)
                return;

            try
            {
                MultronWinCleaner.Processes.SecurityCheck.Ignore(issue.Id);
            }
            catch (Exception ex)
            {
                AppDialog.Show(Loc.F("Could not save the setting: {0}", ex.Message), Loc.T("Security Check"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string note = Loc.F("\"{0}\" is ignored.", issue.Title);
            if (foundSecurityIssues.Any(i => i.Id == issue.Id) && !securityReportRunning)
                ShowSecurityIssues(foundSecurityIssues.Where(i => i.Id != issue.Id).ToList(), note);
            utilities?.RemoveSecurityIssue(issue.Id, note);
        }

        private void ShowSecurityIssues(List<MultronWinCleaner.Processes.SecurityCheck.Issue> issues, string suffix)
        {
            foundSecurityIssues = issues;
            SecurityResultsList.ItemsSource = issues;
            string status = DescribeSecurityIssues(issues, out string color);
            SecurityResultsStatus.Text = suffix.Length > 0 ? status + " " + suffix : status;
            SetResultPanelColor(SecurityResultsPanel, SecurityResultsIcon, color);
            bool any = issues.Count > 0;
            SecurityResultsToggle.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
            SecurityFixButton.Visibility = issues.Any(i => i.CanFix) ? Visibility.Visible : Visibility.Collapsed;
            if (!any)
                SecurityResultsList.Visibility = Visibility.Collapsed;
            SecurityResultsToggle.Content = Loc.T(SecurityResultsList.Visibility == Visibility.Visible ? "Hide Issues" : "Show Issues");
        }

        public void ApplySecurityIssues(List<MultronWinCleaner.Processes.SecurityCheck.Issue> issues, string suffix)
        {
            if (securityReportRunning || SecurityResultsPanel.Visibility != Visibility.Visible)
                return;
            ShowSecurityIssues(issues, suffix);
        }

        public async Task RunSecurityReportAsync()
        {
            if (securityReportRunning || securityScanCheckBox?.IsChecked != true || cancelstatus.IsCancellationRequested)
                return;

            bool automatic = autoclean == 1 || startupscan == 1;
            securityReportRunning = true;
            SetStepBusy("Security", true);
            SecurityResultsPanel.Visibility = Visibility.Visible;
            SecurityResultsToggle.Visibility = Visibility.Collapsed;
            SecurityFixButton.Visibility = Visibility.Collapsed;
            SecurityResultsList.Visibility = Visibility.Collapsed;
            SecurityResultsStatus.Text = Loc.T("Checking Windows security settings...");
            SetResultPanelColor(SecurityResultsPanel, SecurityResultsIcon, InfoColor);
            try
            {
                var step = await RunScanStepAsync(Loc.T("Checking Windows security settings"),
                    WithFallback(utilities?.RunSecurityReportAsync(), MultronWinCleaner.Processes.SecurityCheck.Scan),
                    SecurityStatusButton, SecurityCancelButton);
                if (step.Stopped)
                {
                    SecurityResultsStatus.Text = Loc.T("The security check was skipped.");
                    SetResultPanelColor(SecurityResultsPanel, SecurityResultsIcon, WarningColor);
                    return;
                }
                var issues = step.Result;
                ShowSecurityIssues(issues, issues.Count > 0 ? Loc.T("Nothing was changed.") : "");
                if (automatic && issues.Count > 0 && IsSettingOnByDefault(NotifySecuritySettingKey))
                {
                    new Notify(Loc.T("Security Warning"),
                        DescribeSecurityIssues(issues, out _),
                        null,
                        () => utilities?.ShowSecurityCheck()).AsWarning().Show();
                }
            }
            catch (Exception ex)
            {
                SecurityResultsStatus.Text = Loc.F("Could not check the security settings: {0}", ex.Message);
                SetResultPanelColor(SecurityResultsPanel, SecurityResultsIcon, WarningColor);
            }
            finally
            {
                securityReportRunning = false;
                SetStepBusy("Security", false);
            }
        }

        private void SecurityResultsToggle_Click(object sender, RoutedEventArgs e)
        {
            bool show = SecurityResultsList.Visibility != Visibility.Visible;
            SecurityResultsList.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            SecurityResultsToggle.Content = Loc.T(show ? "Hide Issues" : "Show Issues");
        }

        private async void SecurityFixButton_Click(object sender, RoutedEventArgs e)
        {
            var selected = foundSecurityIssues.Where(i => i.IsSelected && i.CanFix).ToList();
            if (selected.Count == 0)
            {
                SecurityResultsList.Visibility = Visibility.Visible;
                SecurityResultsToggle.Content = Loc.T("Hide Issues");
                SecurityResultsStatus.Text = Loc.T("Tick the settings you want to fix.");
                return;
            }

            if (!await FixSecurityIssuesAsync(selected, SecurityFixButton, text => SecurityResultsStatus.Text = text))
                return;
            var issues = await Task.Run(MultronWinCleaner.Processes.SecurityCheck.Scan);
            ShowSecurityIssues(issues, lastSecurityFixMessage);
            utilities?.ShowSecurityIssues(issues, lastSecurityFixMessage);
        }

        private string lastSecurityFixMessage = "";

        public async Task<bool> FixSecurityIssuesAsync(List<MultronWinCleaner.Processes.SecurityCheck.Issue> selected, System.Windows.Controls.Button button, Action<string> setStatus)
        {
            string list = string.Join("\n", selected.Take(15).Select(i => "• " + i.Title)) + (selected.Count > 15 ? "\n• " + Loc.F("... and {0} more", selected.Count - 15) : "");
            bool restart = selected.Any(i => i.NeedsRestart);
            var answer = AppDialog.Show(Loc.F(selected.Count == 1 ? "Fix {0} security setting?" : "Fix {0} security settings?", selected.Count) + "\n\n" + list + "\n\n" + Loc.T("The current values are saved first, so you can undo the changes in Utilities > Security Check.") + (restart ? "\n\n" + Loc.T("Some changes need a restart.") : ""),
                Loc.T("Security Check"), MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes)
                return false;

            button.IsEnabled = false;
            setStatus(Loc.T("Fixing the selected settings..."));
            try
            {
                var result = await Task.Run(() => MultronWinCleaner.Processes.SecurityCheck.Fix(selected));
                lastSecurityFixMessage = Loc.F("{0} fixed", result.Fixed) + (result.Failed.Count > 0 ? Loc.F(", {0} could not be changed.", result.Failed.Count) : ".") + (result.NeedsRestart ? Loc.T(" Restart the PC to finish.") : "");
                setStatus(lastSecurityFixMessage);
                ShowChangeCard(Loc.T("Security Settings"), lastSecurityFixMessage, result.Failed.Count > 0);
                if (result.Failed.Count > 0)
                    AppDialog.Show(Loc.T("These settings could not be changed:") + "\n\n" + string.Join("\n", result.Failed.Select(f => "• " + f)), Loc.T("Security Check"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return true;
            }
            catch (Exception ex)
            {
                setStatus(Loc.F("Could not fix the settings: {0}", ex.Message));
                return false;
            }
            finally
            {
                button.IsEnabled = true;
            }
        }

        private TaskCompletionSource<bool>? scanStepStop;

        private static async Task<T> WithFallback<T>(Task<T?>? primary, Func<T> fallback) where T : class
        {
            T? result = primary == null ? null : await primary;
            return result ?? await Task.Run(fallback);
        }

        private async Task<(bool Stopped, T Result)> RunScanStepAsync<T>(string label, Task<T> work,
            System.Windows.Controls.Button statusButton, System.Windows.Controls.Button cancelButton)
        {
            var stop = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            scanStepStop = stop;
            bool owner = Equals(buttonStartScan.Content, Loc.T("Clean"));
            bool wasEnabled = buttonStartScan.IsEnabled;
            if (owner)
            {
                buttonStartScan.Content = Loc.T("Cancel");
                buttonStartScan.IsEnabled = true;
                buttonStartScan.ToolTip = Loc.F("Skip this step: {0}.", label.ToLower(Loc.Culture));
                label1_Copy.Text = label + "...";
                label1_Copy.Foreground = MultronWinCleaner.Processes.ThemeColors.ReadableBrush(InfoColor);
            }
            statusButton.Visibility = Visibility.Visible;
            cancelButton.IsEnabled = true;
            cancelButton.Visibility = Visibility.Visible;
            try
            {
                var finished = await Task.WhenAny(work, stop.Task);
                if (finished != work)
                    return (true, default!);
                return (false, await work);
            }
            finally
            {
                statusButton.Visibility = Visibility.Collapsed;
                cancelButton.Visibility = Visibility.Collapsed;
                if (scanStepStop == stop)
                    scanStepStop = null;
                if (owner)
                {
                    buttonStartScan.ToolTip = null;
                    if (Equals(buttonStartScan.Content, Loc.T("Cancel")))
                    {
                        buttonStartScan.Content = Loc.T("Clean");
                        buttonStartScan.IsEnabled = wasEnabled;
                    }
                }
            }
        }

        private void StopScanStep()
        {
            scanStepStop?.TrySetResult(true);
        }

        private void ScanStepCancelButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button button)
                button.IsEnabled = false;
            StopScanStep();
        }

        private void FirewallStatusButton_Click(object sender, RoutedEventArgs e) => utilities?.ShowFirewallCard();

        private void ShortcutStatusButton_Click(object sender, RoutedEventArgs e) => utilities?.ShowShortcutCard();

        private void SecurityStatusButton_Click(object sender, RoutedEventArgs e) => utilities?.ShowSecurityCheck();

        public async Task RunShortcutReportAsync()
        {
            if (shortcutReportRunning || shortcutScanCheckBox?.IsChecked != true || cancelstatus.IsCancellationRequested)
                return;

            shortcutReportRunning = true;
            SetStepBusy("Shortcuts", true);
            ShortcutResultsPanel.Visibility = Visibility.Visible;
            ShortcutResultsToggle.Visibility = Visibility.Collapsed;
            ShortcutFixButton.Visibility = Visibility.Collapsed;
            ShortcutResultsList.Visibility = Visibility.Collapsed;
            ShortcutResultsStatus.Text = Loc.T("Checking Desktop and Start Menu shortcuts...");
            SetResultPanelColor(ShortcutResultsPanel, ShortcutResultsIcon, InfoColor);
            try
            {
                var step = await RunScanStepAsync(Loc.T("Checking Desktop and Start Menu shortcuts"),
                    WithFallback(utilities?.RunShortcutReportAsync(), MultronWinCleaner.Processes.ShortcutFixer.FindBrokenShortcuts),
                    ShortcutStatusButton, ShortcutCancelButton);
                if (step.Stopped)
                {
                    ShortcutResultsStatus.Text = Loc.T("The shortcut check was skipped.");
                    SetResultPanelColor(ShortcutResultsPanel, ShortcutResultsIcon, WarningColor);
                    return;
                }
                foundShortcuts = step.Result;
                ShortcutResultsList.ItemsSource = MultronWinCleaner.Processes.FriendlyListRow.ForShortcuts(foundShortcuts);
                if (foundShortcuts.Count == 0)
                {
                    ShortcutResultsStatus.Text = Loc.T("No broken shortcuts were found.");
                    SetResultPanelColor(ShortcutResultsPanel, ShortcutResultsIcon, OkColor);
                }
                else
                {
                    int repairable = foundShortcuts.Count(s => s.RepairTarget != null);
                    ShortcutResultsStatus.Text = Loc.F(foundShortcuts.Count == 1 ? "{0} shortcut points to programs that no longer exist ({1} can be repaired). Nothing was changed." : "{0} shortcuts point to programs that no longer exist ({1} can be repaired). Nothing was changed.", foundShortcuts.Count, repairable);
                    ShortcutResultsToggle.Content = Loc.T("Show Shortcuts");
                    ShortcutResultsToggle.Visibility = Visibility.Visible;
                    ShortcutFixButton.Visibility = Visibility.Visible;
                    SetResultPanelColor(ShortcutResultsPanel, ShortcutResultsIcon, WarningColor);
                }
            }
            catch (Exception ex)
            {
                ShortcutResultsStatus.Text = Loc.F("Could not check the shortcuts: {0}", ex.Message);
                SetResultPanelColor(ShortcutResultsPanel, ShortcutResultsIcon, ProblemColor);
            }
            finally
            {
                shortcutReportRunning = false;
                SetStepBusy("Shortcuts", false);
            }
        }

        private void ShortcutResultsToggle_Click(object sender, RoutedEventArgs e)
        {
            bool show = ShortcutResultsList.Visibility != Visibility.Visible;
            ShortcutResultsList.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            ShortcutResultsToggle.Content = Loc.T(show ? "Hide Shortcuts" : "Show Shortcuts");
        }

        private async void ShortcutFixButton_Click(object sender, RoutedEventArgs e)
        {
            var items = foundShortcuts;
            if (items.Count == 0)
                return;

            var answer = AppDialog.Show(Loc.F(items.Count == 1 ? "Fix {0} broken shortcut?" : "Fix {0} broken shortcuts?", items.Count) + "\n\n" + Loc.T("Shortcuts whose program moved to a new version folder are repaired. The others are moved to the Recycle Bin, so you can restore them."),
                Loc.T("Shortcuts"), MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes)
                return;

            ShortcutFixButton.IsEnabled = false;
            try
            {
                var result = await Task.Run(() => MultronWinCleaner.Processes.ShortcutFixer.Fix(items));
                ShowShortcutFixResult(result.Repaired, result.Removed, result.Failed);
                utilities?.ShowShortcutFix(items, result);
            }
            catch (Exception ex)
            {
                ShortcutResultsStatus.Text = Loc.F("Could not fix the shortcuts: {0}", ex.Message);
            }
            finally
            {
                ShortcutFixButton.IsEnabled = true;
            }
        }

        private void ShowShortcutFixResult(int repaired, int removed, List<MultronWinCleaner.Processes.ShortcutFixer.BrokenShortcut> remaining)
        {
            foundShortcuts = remaining;
            ShortcutResultsList.ItemsSource = MultronWinCleaner.Processes.FriendlyListRow.ForShortcuts(foundShortcuts);
            ShortcutResultsStatus.Text = Loc.F("{0} repaired, {1} moved to the Recycle Bin", repaired, removed) + (remaining.Count > 0 ? Loc.F(", {0} could not be changed.", remaining.Count) : ".");
            ShowChangeCard(Loc.T("Shortcuts"), ShortcutResultsStatus.Text, remaining.Count > 0);
            if (remaining.Count == 0)
            {
                SetResultPanelColor(ShortcutResultsPanel, ShortcutResultsIcon, OkColor);
                ShortcutResultsList.Visibility = Visibility.Collapsed;
                ShortcutResultsToggle.Visibility = Visibility.Collapsed;
                ShortcutFixButton.Visibility = Visibility.Collapsed;
            }
        }

        public void ApplyShortcutFix(List<MultronWinCleaner.Processes.ShortcutFixer.BrokenShortcut> items, (int Repaired, int Removed, List<MultronWinCleaner.Processes.ShortcutFixer.BrokenShortcut> Failed) result)
        {
            if (shortcutReportRunning || ShortcutResultsPanel.Visibility != Visibility.Visible || foundShortcuts.Count == 0)
                return;
            var fixedPaths = new HashSet<string>(items.Except(result.Failed).Select(s => s.ShortcutPath), StringComparer.OrdinalIgnoreCase);
            var remaining = foundShortcuts.Where(s => !fixedPaths.Contains(s.ShortcutPath)).ToList();
            ShowShortcutFixResult(result.Repaired, result.Removed, remaining);
        }

        private void ShowFirewallRemovalResult(int removed, List<MultronWinCleaner.Processes.FirewallRules.InvalidRule> remaining)
        {
            foundFirewallRules = remaining;
            FirewallRulesList.ItemsSource = MultronWinCleaner.Processes.FriendlyListRow.ForFirewall(foundFirewallRules);
            FirewallResultsStatus.Text = remaining.Count == 0
                ? Loc.F(removed == 1 ? "{0} firewall rule removed." : "{0} firewall rules removed.", removed)
                : Loc.F(removed == 1 ? "{0} firewall rule removed." : "{0} firewall rules removed.", removed) + " " + Loc.F("{0} could not be removed (they may be managed by your organization).", remaining.Count);
            ShowChangeCard(Loc.T("Firewall Rules"), FirewallResultsStatus.Text, remaining.Count > 0);
            if (remaining.Count == 0)
            {
                SetResultPanelColor(FirewallResultsPanel, FirewallResultsIcon, OkColor);
                FirewallRulesList.Visibility = Visibility.Collapsed;
                FirewallResultsToggle.Visibility = Visibility.Collapsed;
                FirewallRemoveButton.Visibility = Visibility.Collapsed;
            }
        }

        public void ApplyFirewallRemoval(List<MultronWinCleaner.Processes.FirewallRules.InvalidRule> rules, (int Removed, List<MultronWinCleaner.Processes.FirewallRules.InvalidRule> Failed) result)
        {
            if (firewallReportRunning || FirewallResultsPanel.Visibility != Visibility.Visible || foundFirewallRules.Count == 0)
                return;
            var removedKeys = new HashSet<string>(rules.Except(result.Failed).Select(FirewallRuleKey));
            var remaining = foundFirewallRules.Where(r => !removedKeys.Contains(FirewallRuleKey(r))).ToList();
            ShowFirewallRemovalResult(result.Removed, remaining);
        }

        private static string FirewallRuleKey(MultronWinCleaner.Processes.FirewallRules.InvalidRule rule) =>
            rule.Name + "|" + rule.ApplicationPath + "|" + rule.Direction;

        public async Task RunFirewallReportAsync()
        {
            if (firewallReportRunning || firewallScanCheckBox?.IsChecked != true || cancelstatus.IsCancellationRequested)
                return;

            bool automatic = autoclean == 1 || startupscan == 1;
            firewallReportRunning = true;
            SetStepBusy("Firewall Rules", true);
            FirewallResultsPanel.Visibility = Visibility.Visible;
            FirewallResultsToggle.Visibility = Visibility.Collapsed;
            FirewallRemoveButton.Visibility = Visibility.Collapsed;
            FirewallRulesList.Visibility = Visibility.Collapsed;
            FirewallResultsStatus.Text = Loc.T("Checking Windows Firewall rules...");
            SetResultPanelColor(FirewallResultsPanel, FirewallResultsIcon, InfoColor);
            try
            {
                var step = await RunScanStepAsync(Loc.T("Checking Windows Firewall rules"),
                    WithFallback(utilities?.RunFirewallReportAsync(), MultronWinCleaner.Processes.FirewallRules.FindInvalidRules),
                    FirewallStatusButton, FirewallCancelButton);
                if (step.Stopped)
                {
                    FirewallResultsStatus.Text = Loc.T("The firewall rule check was skipped.");
                    SetResultPanelColor(FirewallResultsPanel, FirewallResultsIcon, WarningColor);
                    return;
                }
                foundFirewallRules = step.Result;
                FirewallRulesList.ItemsSource = MultronWinCleaner.Processes.FriendlyListRow.ForFirewall(foundFirewallRules);
                if (foundFirewallRules.Count == 0)
                {
                    FirewallResultsStatus.Text = Loc.T("No broken firewall rules were found.");
                    SetResultPanelColor(FirewallResultsPanel, FirewallResultsIcon, OkColor);
                }
                else
                {
                    FirewallResultsStatus.Text = Loc.F(foundFirewallRules.Count == 1 ? "{0} firewall rule points to programs that no longer exist on this PC. Nothing was removed." : "{0} firewall rules point to programs that no longer exist on this PC. Nothing was removed.", foundFirewallRules.Count);
                    FirewallResultsToggle.Content = Loc.T("Show Rules");
                    SetResultPanelColor(FirewallResultsPanel, FirewallResultsIcon, ProblemColor);
                    FirewallResultsToggle.Visibility = Visibility.Visible;
                    FirewallRemoveButton.Visibility = Visibility.Visible;
                    if (automatic && IsSettingOnByDefault(NotifyFirewallSettingKey))
                    {
                        new Notify(Loc.T("Firewall Rules"),
                            Loc.F(foundFirewallRules.Count == 1 ? "{0} firewall rule points to programs that no longer exist on this PC. Nothing was removed." : "{0} firewall rules point to programs that no longer exist on this PC. Nothing was removed.", foundFirewallRules.Count),
                            null,
                            () => utilities?.ShowFirewallCard()).AsWarning().Show();
                    }
                }
            }
            catch (Exception ex)
            {
                FirewallResultsStatus.Text = Loc.F("Could not read the firewall rules: {0}", ex.Message);
                SetResultPanelColor(FirewallResultsPanel, FirewallResultsIcon, WarningColor);
            }
            finally
            {
                firewallReportRunning = false;
                SetStepBusy("Firewall Rules", false);
            }
        }

        private void FirewallResultsToggle_Click(object sender, RoutedEventArgs e)
        {
            bool show = FirewallRulesList.Visibility != Visibility.Visible;
            FirewallRulesList.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            FirewallResultsToggle.Content = Loc.T(show ? "Hide Rules" : "Show Rules");
        }

        private async void FirewallRemoveButton_Click(object sender, RoutedEventArgs e)
        {
            var rules = foundFirewallRules;
            if (rules.Count == 0)
                return;

            var answer = AppDialog.Show(Loc.F(rules.Count == 1 ? "Remove {0} Windows Firewall rule for programs that no longer exist on this PC?" : "Remove {0} Windows Firewall rules for programs that no longer exist on this PC?", rules.Count),
                Loc.T("Firewall Rules"), MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes)
                return;

            FirewallRemoveButton.IsEnabled = false;
            try
            {
                var result = await Task.Run(() => MultronWinCleaner.Processes.FirewallRules.RemoveRules(rules));
                ShowFirewallRemovalResult(result.Removed, result.Failed);
                utilities?.ShowFirewallRemoval(rules, result);
            }
            catch (Exception ex)
            {
                FirewallResultsStatus.Text = Loc.F("Could not remove the firewall rules: {0}", ex.Message);
            }
            finally
            {
                FirewallRemoveButton.IsEnabled = true;
            }
        }

        public async Task RunDuplicateReportAsync()
        {
            if (duplicateReportRunning || duplicateScanCheckBox?.IsChecked != true || cancelstatus.IsCancellationRequested)
                return;
            bool automatic = autoclean == 1 || startupscan == 1;

            Duplicate_File_Finder? finder = utilities?.GetDuplicateFinder();
            if (finder == null || finder.IsBusy)
                return;

            duplicateReportRunning = true;
            SetStepBusy("Duplicate Files", true);
            DuplicateResultsPanel.Visibility = Visibility.Visible;
            DuplicateResultsButton.Content = Loc.T("Show Status");
            DuplicateResultsButton.Visibility = Visibility.Visible;
            DuplicateCancelButton.IsEnabled = true;
            DuplicateCancelButton.Visibility = Visibility.Visible;
            DuplicateResultsStatus.Text = Loc.T("Looking for identical files in your personal folders...");
            SetResultPanelColor(DuplicateResultsPanel, DuplicateResultsIcon, InfoColor);
            EnterDuplicateCancelMode();
            progressBar1.Value = 0;
            ShowDuplicateProgress(DuplicateResultsStatus.Text);

            var statusTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            statusTimer.Tick += (s, e) =>
            {
                string status = finder.CurrentStatus;
                if (status.Length == 0)
                    return;
                if (DuplicateCancelButton.IsEnabled)
                    DuplicateResultsStatus.Text = status;
                ShowDuplicateProgress(status);
            };
            statusTimer.Start();
            try
            {
                var result = await finder.RunReportScanAsync();
                statusTimer.Stop();
                DuplicateResultsButton.Content = Loc.T("Show Files");
                DuplicateResultsButton.Visibility = Visibility.Collapsed;
                if (finder.WasCanceled)
                {
                    DuplicateResultsStatus.Text = result.Copies > 0
                        ? Loc.F(result.Copies == 1 ? "The duplicate search was stopped. {0} duplicate copy found so far ({1}). Nothing was deleted." : "The duplicate search was stopped. {0} duplicate copies found so far ({1}). Nothing was deleted.", result.Copies, formatsize(result.Bytes))
                        : Loc.T("The duplicate search was stopped.");
                    DuplicateResultsButton.Visibility = result.Copies > 0 ? Visibility.Visible : Visibility.Collapsed;
                    SetResultPanelColor(DuplicateResultsPanel, DuplicateResultsIcon, WarningColor);
                }
                else if (result.Copies == 0)
                {
                    DuplicateResultsStatus.Text = Loc.T("No duplicate files were found in your personal folders.");
                    SetResultPanelColor(DuplicateResultsPanel, DuplicateResultsIcon, OkColor);
                }
                else
                {
                    DuplicateResultsStatus.Text = Loc.F(result.Copies == 1 ? "{0} duplicate copy of {1} file found ({2} could be freed). Nothing was deleted." : result.Groups == 1 ? "{0} duplicate copies of {1} file found ({2} could be freed). Nothing was deleted." : "{0} duplicate copies of {1} files found ({2} could be freed). Nothing was deleted.", result.Copies, result.Groups, formatsize(result.Bytes));
                    DuplicateResultsButton.Visibility = Visibility.Visible;
                    SetResultPanelColor(DuplicateResultsPanel, DuplicateResultsIcon, WarningColor);
                    if (automatic && IsSettingOnByDefault(NotifyDuplicatesSettingKey))
                    {
                        new Notify(Loc.T("Duplicate Files Found"),
                            Loc.F(result.Copies == 1 ? "{0} duplicate copy in your personal folders. Click to review." : "{0} duplicate copies in your personal folders. Click to review.", result.Copies),
                            formatsize(result.Bytes),
                            () => utilities?.ShowDuplicateFinder()).AsWarning().Show();
                    }
                }
            }
            catch (Exception ex)
            {
                DuplicateResultsStatus.Text = Loc.F("The duplicate file search failed: {0}", ex.Message);
                DuplicateResultsButton.Visibility = Visibility.Collapsed;
                SetResultPanelColor(DuplicateResultsPanel, DuplicateResultsIcon, ProblemColor);
            }
            finally
            {
                statusTimer.Stop();
                duplicateReportRunning = false;
                SetStepBusy("Duplicate Files", false);
                DuplicateCancelButton.Visibility = Visibility.Collapsed;
                bool wasWaiting = duplicateCancelMode;
                LeaveDuplicateCancelMode();
                if (wasWaiting && Equals(buttonStartScan.Content, Loc.T("Clean")))
                {
                    progressBar1.Value = 100;
                    ShowReadyStatus();
                }
            }
        }

        private bool duplicateCancelMode;
        private bool duplicateCancelWasEnabled;

        private void EnterDuplicateCancelMode()
        {
            if (duplicateCancelMode || !Equals(buttonStartScan.Content, Loc.T("Clean")))
                return;
            duplicateCancelMode = true;
            duplicateCancelWasEnabled = buttonStartScan.IsEnabled;
            buttonStartScan.Content = Loc.T("Cancel");
            buttonStartScan.IsEnabled = true;
            buttonStartScan.ToolTip = Loc.T("Stop the duplicate file search.");
        }

        private void LeaveDuplicateCancelMode()
        {
            if (!duplicateCancelMode)
                return;
            duplicateCancelMode = false;
            buttonStartScan.ToolTip = null;
            if (!Equals(buttonStartScan.Content, Loc.T("Cancel")))
                return;
            buttonStartScan.Content = Loc.T("Clean");
            buttonStartScan.IsEnabled = duplicateCancelWasEnabled;
        }

        public void ShowDuplicatesDeleted(int deleted, int total, long freedBytes, int failed)
        {
            if (DuplicateResultsPanel.Visibility != Visibility.Visible || duplicateReportRunning || total <= 0)
                return;
            DuplicateResultsStatus.Text = failed > 0
                ? Loc.F("Duplicate File Finder deleted {0} of {1} duplicate copies ({2} freed). {3} could not be deleted.", deleted, total, formatsize(freedBytes), failed)
                : Loc.F("Duplicate File Finder deleted {0} of {1} duplicate copies ({2} freed).", deleted, total, formatsize(freedBytes));
            ShowChangeCard(Loc.T("Duplicate Files"), DuplicateResultsStatus.Text, failed > 0);
            DuplicateResultsButton.Content = Loc.T("Show Files");
            DuplicateResultsButton.Visibility = failed > 0 ? Visibility.Visible : Visibility.Collapsed;
            SetResultPanelColor(DuplicateResultsPanel, DuplicateResultsIcon, failed > 0 ? WarningColor : OkColor);
        }

        private void ShowDuplicateProgress(string status)
        {
            if (!duplicateCancelMode)
                return;
            label1_Copy.Text = Loc.F("Duplicate files: {0}", status);
            label1_Copy.Foreground = MultronWinCleaner.Processes.ThemeColors.ReadableBrush(InfoColor);
        }

        private void CancelDuplicateReport()
        {
            if (!duplicateReportRunning)
                return;
            DuplicateCancelButton.IsEnabled = false;
            DuplicateResultsStatus.Text = Loc.T("Stopping the duplicate file search...");
            utilities?.GetDuplicateFinder()?.CancelScan();
        }

        private void DuplicateCancelButton_Click(object sender, RoutedEventArgs e)
        {
            CancelDuplicateReport();
        }

        public const string LargeFileScanSettingKey = "largefilescanwithmain";
        public const string LargeFileMonthsSettingKey = "largefilescanmonths";
        private CheckBox? largeFiles3Months;
        private CheckBox? largeFiles6Months;
        private bool largeFileReportRunning;
        private List<MultronWinCleaner.LargeFileFinder.LargeFileInfo> largeFileReport = new();

        private int SelectedLargeFileMonths => largeFiles6Months?.IsChecked == true ? 6 : largeFiles3Months?.IsChecked == true ? 3 : 0;

        private CheckBox CreateLargeFileChoice(int months, bool isChecked)
        {
            var box = new CheckBox
            {
                Content = Loc.F("Not used for {0} months", months),
                Margin = new Thickness(10, 6, 10, 6),
                IsChecked = isChecked,
                BorderThickness = new Thickness(0),
                BorderBrush = new SolidColorBrush(Colors.Transparent),
                Background = new SolidColorBrush(System.Windows.Media.Colors.White),
                FontSize = 12,
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                FontWeight = FontWeights.Regular,
                ToolTip = Loc.T("Looks in every user's folders and on your other drives for large personal files that have not been used for the selected time: photos from 25 MB, music from 50 MB, videos from 300 MB and other files from 500 MB. System, game and program files are left out.")
            };
            box.SetResourceReference(FrameworkElement.StyleProperty, "RadioCheckBoxStyle");
            box.SetResourceReference(Control.ForegroundProperty, "Text");
            ApplyOptionTemplate(box);
            return box;
        }

        private void AddLargeFileScanOption()
        {
            bool on = IsSettingOnByDefault(LargeFileScanSettingKey);
            bool sixMonths = ReadSettingValue(LargeFileMonthsSettingKey) != "3";
            CheckBox three = CreateLargeFileChoice(3, on && !sixMonths);
            CheckBox six = CreateLargeFileChoice(6, on && sixMonths);
            largeFiles3Months = three;
            largeFiles6Months = six;

            void Chosen(CheckBox box, CheckBox other, int months)
            {
                box.Checked += (s, e) =>
                {
                    if (other.IsChecked == true)
                        other.IsChecked = false;
                    utilities?.savesettings(LargeFileScanSettingKey + ":1");
                    utilities?.savesettings($"{LargeFileMonthsSettingKey}:{months}");
                };
                box.Unchecked += (s, e) =>
                {
                    if (other.IsChecked != true)
                        utilities?.savesettings(LargeFileScanSettingKey + ":0");
                };
            }
            Chosen(three, six, 3);
            Chosen(six, three, 6);

            var note = new TextBlock { Text = Loc.T("Nothing is deleted automatically."), FontSize = 11.5, FontWeight = FontWeights.Normal, Margin = new Thickness(10, 2, 10, 4), TextWrapping = TextWrapping.Wrap };
            note.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");

            StackPanel content = new StackPanel
            {
                Orientation = Orientation.Vertical,
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            stackpanels.Add(content);
            content.Children.Add(three);
            content.Children.Add(six);
            content.Children.Add(note);

            Expander expander = new Expander
            {
                Header = Loc.T("Large Files"),
                Margin = new Thickness(5),
                Background = new SolidColorBrush(Colors.Transparent),
                Foreground = brush,
                BorderBrush = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(2),
                FontSize = 14,
                FontWeight = FontWeights.Regular,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Content = content
            };
            expanders.Add(expander);
            AddOptionExpander(expander);
        }

        public async Task RunLargeFileReportAsync()
        {
            int months = SelectedLargeFileMonths;
            if (largeFileReportRunning || months == 0 || cancelstatus?.IsCancellationRequested == true)
                return;

            largeFileReportRunning = true;
            SetStepBusy("Large Files", true);
            LargeFilesResultsPanel.Visibility = Visibility.Visible;
            LargeFilesResultsButton.Visibility = Visibility.Collapsed;
            LargeFilesResultsStatus.Text = Loc.T("Looking for large files you have not used for a while...");
            SetResultPanelColor(LargeFilesResultsPanel, LargeFilesResultsIcon, InfoColor);
            try
            {
                var token = cancelstatus;
                var result = await Task.Run(() => MultronWinCleaner.Processes.LargeFileReport.Find(months, formatsize, () => token?.IsCancellationRequested == true));
                largeFileReport = result.Files;
                if (result.TotalFound == 0)
                {
                    LargeFilesResultsStatus.Text = Loc.F("No large files unused for {0} months were found.", months);
                    SetResultPanelColor(LargeFilesResultsPanel, LargeFilesResultsIcon, OkColor);
                }
                else
                {
                    LargeFilesResultsStatus.Text = Loc.F("Files unused for {0} months: {1} ({2}). Nothing was deleted.", months, result.TotalFound, formatsize(result.TotalBytes));
                    LargeFilesResultsButton.Visibility = Visibility.Visible;
                    SetResultPanelColor(LargeFilesResultsPanel, LargeFilesResultsIcon, WarningColor);
                }
            }
            catch (Exception ex)
            {
                LargeFilesResultsStatus.Text = Loc.F("The large file search failed: {0}", ex.Message);
                SetResultPanelColor(LargeFilesResultsPanel, LargeFilesResultsIcon, ProblemColor);
            }
            finally
            {
                largeFileReportRunning = false;
                SetStepBusy("Large Files", false);
            }
        }

        private void LargeFilesResultsButton_Click(object sender, RoutedEventArgs e)
        {
            utilities?.ShowLargeFileReport(largeFileReport);
        }

        private void DuplicateResultsButton_Click(object sender, RoutedEventArgs e)
        {
            utilities?.ShowDuplicateFinder();
        }

        public async Task startup()
        {
            if (App.LaunchedFromStartup)
            {
                await Task.Delay(3000);

                bool scanEnabled = false;
                bool cleanEnabled = false;

                await this.Dispatcher.InvokeAsync(() =>
                {
                    scanEnabled = settings.chkEnableStartupScan.IsChecked == true;
                    cleanEnabled = settings.chkEnableStartupClean.IsChecked == true;
                });

                if (scanEnabled || cleanEnabled)
                {
                  
                    this.startupscan = 1;
                    if (cleanEnabled)
                      this.startupclean = 1;
                   
                    await startscan();
                
                }
            }
        }
        public async Task loadothers()
        {
            string cleanmgr = OptionGroupTitle(Loc.T("cleanmgr.exe Commands"), Loc.T("Windows Disk Cleanup"));
            string dism = OptionGroupTitle(Loc.T("Dism.exe Commands"), Loc.T("Windows Repair (DISM)"));
            string sfc = OptionGroupTitle(Loc.T("SFC Commands"), Loc.T("System File Check (SFC)"));
            string logscan = OptionGroupTitle(Loc.T("Deep Log Files Scan"), Loc.T("Deep Log Files Scan"));

            await createshortcut("cleanmgr.exe=/d C:=" + Loc.T("(Opens Disk Cleanup for a specific drive)") + "=shortcut_0", cleanmgr);
            await createshortcut("cleanmgr.exe=/sagerun:1=" + Loc.T("(Configures advanced cleanup settings for auto-run)") + "=shortcut1", cleanmgr);
            await createshortcut("cleanmgr.exe=/lowdisk=" + Loc.T("(Prompts to clean default unnecessary files)") + "=shortcut2", cleanmgr);
            await createshortcut("cleanmgr.exe=/verylowdisk=" + Loc.T("(Silently clears default unnecessary files)") + "=shortcut3", cleanmgr);

            await createshortcut("Dism.exe=/Online /Cleanup-Image /StartComponentCleanup=warning=" + Loc.T("(No Warning)") + "=winsxs", dism);
            await createshortcut("Dism.exe=/Online /Cleanup-Image /RestoreHealth=warning=" + Loc.T("(No Warning)") + "=health", dism);
            await createshortcut("sfc.exe=sfc /scannow=warning=" + Loc.T("(No Warning)") + "=sfc", sfc);
            await createshortcut("Deep Log Files Scan=C:\\=warning=" + Loc.T("Its can take long time.") + "=logscan", logscan);

        }

        #region Interface mode

        public const string AdvancedInterfaceSettingKey = "advancedinterface";

        public static bool IsAdvancedInterface()
        {
            try
            {
                string path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Settings.txt");
                return System.IO.File.Exists(path) && System.IO.File.ReadLines(path)
                    .Any(line => line.Trim().Equals(AdvancedInterfaceSettingKey + ":1", StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return false;
            }
        }

        public static bool IsSimpleInterface() => !IsAdvancedInterface();

        public const string ShowEntryNotesSettingKey = "showentrynotes";
        public const string ConfirmRiskySettingKey = "confirmrisky";
        public static bool ShowEntryNotes() => ReadSettingValue(ShowEntryNotesSettingKey) != "0";
        public static bool ConfirmRiskyItems() => ReadSettingValue(ConfirmRiskySettingKey) != "0";
        public static bool IsInfoNote(string note) => note.StartsWith("Not selected by default:", StringComparison.Ordinal);

        public static string? ReadSettingValue(string key)
        {
            try
            {
                string path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Settings.txt");
                if (!System.IO.File.Exists(path)) return null;
                return System.IO.File.ReadLines(path)
                    .Where(line => line.StartsWith(key + ":", StringComparison.OrdinalIgnoreCase))
                    .Select(line => line.Substring(key.Length + 1).Trim())
                    .LastOrDefault();
            }
            catch
            {
                return null;
            }
        }

        private static string OptionGroupTitle(string advanced, string simple) => IsSimpleInterface() ? simple : advanced;

        public void ApplyInterfaceToolbar()
        {
            ApplySelections.Visibility = IsSimpleInterface() ? Visibility.Collapsed : Visibility.Visible;
            LibrarySearchPanel.Visibility = IsSimpleInterface() ? Visibility.Collapsed : Visibility.Visible;
            if (IsSimpleInterface() && LibrarySearchBox.Text.Length > 0)
                LibrarySearchBox.Clear();
        }

        public void ApplyInterfaceMode()
        {
            ApplyInterfaceToolbar();
            if (IsIdleForDatabaseReload())
                ReloadDatabaseIfIdle();
            else
                ShowDatabaseStatus(Loc.T("The new interface mode is used after the current scan or clean has finished and the list is reloaded."), InfoColor, always: true);
        }

        private static (string Title, string Description) FriendlyOption(string content)
        {
            string[] parts = content.Split('=');
            string key = parts.Length > 0 ? parts[^1] : "";
            return key switch
            {
                "shortcut_0" => (Loc.T("Disk Cleanup for drive C:"), Loc.T("Opens Windows Disk Cleanup (cleanmgr /d C:) so you can choose what to remove from drive C:.")),
                "shortcut1" => (Loc.T("Disk Cleanup with saved settings"), Loc.T("Runs Windows Disk Cleanup with the cleanup settings saved as set 1 (cleanmgr /sagerun:1).")),
                "shortcut2" => (Loc.T("Disk Cleanup, ask before cleaning"), Loc.T("Opens Windows Disk Cleanup with the usual unneeded files already selected (cleanmgr /lowdisk).")),
                "shortcut3" => (Loc.T("Disk Cleanup, clean silently"), Loc.T("Removes the usual unneeded files with Windows Disk Cleanup without asking (cleanmgr /verylowdisk).")),
                "winsxs" => (Loc.T("Clean up old Windows update files"), Loc.T("Removes replaced Windows component versions from the WinSxS folder (DISM /StartComponentCleanup).")),
                "health" => (Loc.T("Repair the Windows component store"), Loc.T("Checks and repairs the Windows component store, may download files from Windows Update (DISM /RestoreHealth).")),
                "sfc" => (Loc.T("Check and repair Windows system files"), Loc.T("Finds and repairs damaged Windows system files (sfc /scannow).")),
                "logscan" => (Loc.T("Find log files on the whole C: drive"), Loc.T("Searches all of drive C: for log files. This can take a long time.")),
                "malscan" => (Loc.T("Multron Malware Scan Cloud"), Loc.T("Scans your system for malicious software in the cloud with the viruskov.com OPEN-EDR engine")),
                MrtOptionKey => (Loc.T("Microsoft MRT Scan"), Loc.T("Scans with the Malicious Software Removal Tool built into Windows. It runs hidden, before the cloud scan.")),
                _ => (parts.Length > 0 ? parts[0] : content, content)
            };
        }

        private sealed class FriendlyOptionConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => FriendlyOption(value?.ToString() ?? "").Title;
            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
        }

        private static DataTemplate? friendlyCheckBoxTemplate;
        private static DataTemplate FriendlyCheckBoxTemplate
        {
            get
            {
                if (friendlyCheckBoxTemplate == null)
                {
                    var text = new FrameworkElementFactory(typeof(TextBlock));
                    text.SetBinding(TextBlock.TextProperty, new Binding { Converter = new FriendlyOptionConverter() });
                    text.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
                    friendlyCheckBoxTemplate = new DataTemplate { VisualTree = text };
                    friendlyCheckBoxTemplate.Seal();
                }
                return friendlyCheckBoxTemplate;
            }
        }

        private static void ApplyOptionTemplate(CheckBox box)
        {
            string content = box.Content?.ToString() ?? "";
            if (IsSimpleInterface())
            {
                var friendly = FriendlyOption(content);
                box.ContentTemplate = FriendlyCheckBoxTemplate;
                box.ToolTip ??= friendly.Description;
                box.Tag = friendly.Title + " " + content;
            }
            else
            {
                box.ContentTemplate = WrappingCheckBoxTemplate;
            }
        }

        private Grid? optionsSectionPanel;

        private readonly Dictionary<Expander, Action> optionStatusRefresh = new Dictionary<Expander, Action>();

        private static IEnumerable<CheckBox> OptionCheckBoxes(object? node)
        {
            if (node is CheckBox box)
            {
                yield return box;
                yield break;
            }
            if (node is not DependencyObject parent)
                yield break;
            foreach (object child in LogicalTreeHelper.GetChildren(parent))
                foreach (CheckBox inner in OptionCheckBoxes(child))
                    yield return inner;
        }

        private readonly Dictionary<string, Action<bool>> stepBusy = new Dictionary<string, Action<bool>>(StringComparer.OrdinalIgnoreCase);

        private static bool IsDarkBlue(System.Windows.Media.Brush? brush)
        {
            if (brush is not SolidColorBrush solid)
                return false;
            var c = solid.Color;
            return (c.R == 0x1E && c.G == 0x88 && c.B == 0xE5) || (c.R == 0x00 && c.G == 0x00 && c.B == 0xFF);
        }

        public static void KeepAccentReadable(TextBlock text)
        {
            System.ComponentModel.DependencyPropertyDescriptor.FromProperty(TextBlock.ForegroundProperty, typeof(TextBlock)).AddValueChanged(text, (s, e) =>
            {
                if (!IsDarkBlue(text.Foreground))
                    return;
                text.Dispatcher.BeginInvoke(() =>
                {
                    if (IsDarkBlue(text.Foreground))
                        text.SetResourceReference(TextBlock.ForegroundProperty, "AccentText");
                }, System.Windows.Threading.DispatcherPriority.Send);
            });
            if (IsDarkBlue(text.Foreground))
                text.SetResourceReference(TextBlock.ForegroundProperty, "AccentText");
        }

        public void SetStepBusy(string key, bool busy)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(() => SetStepBusy(key, busy));
                return;
            }
            if (stepBusy.TryGetValue(key, out var set))
                set(busy);
            if (key == "Malware Scan")
            {
                if (busy && !malwareStepFollowed)
                    FollowStep(MalwareResultsPanel);
                malwareStepFollowed = busy;
            }
        }

        private bool malwareStepFollowed;

        private void FollowStep(FrameworkElement panel)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                if (panel.Visibility != Visibility.Visible || Datagridscroll.Visibility != Visibility.Visible || Datagridscroll.Content is not Visual content)
                    return;
                Datagridscroll.UpdateLayout();
                Datagridscroll.ScrollToVerticalOffset(panel.TransformToAncestor(content).Transform(new System.Windows.Point(0, 0)).Y);
            }));
        }

        private void AttachOptionStatus(Expander expander)
        {
            if (expander.Header is not string title || optionStatusRefresh.ContainsKey(expander))
                return;
            expander.Tag = title;
            string? key = MultronWinCleaner.Processes.ItemInfo.Key(title);
            var accent = System.Windows.Media.Color.FromRgb(0x42, 0xA5, 0xF5);
            var green = System.Windows.Media.Color.FromRgb(0x2E, 0xB8, 0x72);

            FrameworkElement? toolIcon = MultronWinCleaner.Processes.ToolIcons.Tile(MultronWinCleaner.Processes.ToolIcons.ForStep(key, title), 36);
            if (toolIcon != null)
                toolIcon.Margin = new Thickness(0, 2, 12, 2);
            FrameworkElement icon = toolIcon ?? new Border
            {
                Width = 36,
                Height = 36,
                CornerRadius = new CornerRadius(10),
                Margin = new Thickness(0, 2, 12, 2),
                VerticalAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x26, accent.R, accent.G, accent.B)),
                Child = new TextBlock
                {
                    Text = MultronWinCleaner.Processes.ItemInfo.Glyph(key),
                    FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                    FontSize = 16,
                    FontWeight = FontWeights.Normal,
                    Foreground = MultronWinCleaner.Processes.ThemeColors.ReadableBrush(accent),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };

            var name = new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 2) };
            var titleLine = new WrapPanel { Orientation = Orientation.Horizontal };
            titleLine.Children.Add(name);
            var slowBadgeHost = new StackPanel { Orientation = Orientation.Horizontal };
            MultronWinCleaner.Processes.ItemInfo.Decorate(titleLine, name, title, slowBadgeHost);
            var description = new TextBlock
            {
                Text = MultronWinCleaner.Processes.ItemInfo.Description(title) ?? "",
                FontSize = 12,
                FontWeight = FontWeights.Normal,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 3, 0, 0)
            };
            description.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
            if (description.Text.Length == 0)
                description.Visibility = Visibility.Collapsed;

            var statusText = new TextBlock { FontSize = 11.5, FontWeight = FontWeights.SemiBold };
            var statusPill = new Border { CornerRadius = new CornerRadius(9), Padding = new Thickness(8, 2, 8, 2), Child = statusText, VerticalAlignment = VerticalAlignment.Center };
            var spinnerArc = new System.Windows.Shapes.Path
            {
                Data = System.Windows.Media.Geometry.Parse("M 10,2 A 8,8 0 1 1 2,10"),
                Stroke = new SolidColorBrush(accent),
                StrokeThickness = 2.4,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Width = 20,
                Height = 20,
                RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
                RenderTransform = new RotateTransform()
            };
            var spinner = new Grid { Width = 20, Height = 20, Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center };
            spinner.Children.Add(new System.Windows.Shapes.Ellipse { Stroke = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x33, accent.R, accent.G, accent.B)), StrokeThickness = 2.4, Margin = new Thickness(1.2) });
            spinner.Children.Add(spinnerArc);
            var statusArea = new Grid { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            statusArea.Children.Add(statusPill);
            statusArea.Children.Add(spinner);

            var header = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            icon.VerticalAlignment = VerticalAlignment.Top;
            titleLine.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRowSpan(icon, 2);
            Grid.SetColumn(titleLine, 1);
            Grid.SetColumn(statusArea, 2);
            Grid.SetRow(description, 1);
            Grid.SetColumn(description, 1);
            Grid.SetColumnSpan(description, 2);
            header.Children.Add(icon);
            header.Children.Add(titleLine);
            header.Children.Add(statusArea);
            header.Children.Add(description);
            Grid.SetRow(slowBadgeHost, 2);
            Grid.SetColumn(slowBadgeHost, 1);
            Grid.SetColumnSpan(slowBadgeHost, 2);
            header.Children.Add(slowBadgeHost);
            expander.Header = header;
            expander.Padding = new Thickness(60, 0, 12, 10);
            List<CheckBox>? remembered = null;
            MultronWinCleaner.Processes.CardLayout.MakeSelectable(expander, header, () =>
            {
                var boxes = OptionCheckBoxes(expander.Content).Where(b => b.Visibility == Visibility.Visible && b.IsEnabled).ToList();
                if (boxes.Count == 0)
                    return;
                var on = boxes.Where(b => b.IsChecked == true).ToList();
                if (on.Count > 0)
                {
                    remembered = on;
                    foreach (var box in on)
                        box.IsChecked = false;
                    return;
                }
                var restore = remembered?.Where(boxes.Contains).ToList();
                if (restore == null || restore.Count == 0)
                {
                    var primary = boxes.FirstOrDefault(b => (b.Content?.ToString() ?? "").EndsWith("=malscan", StringComparison.OrdinalIgnoreCase)) ?? boxes[0];
                    restore = new List<CheckBox> { primary };
                }
                foreach (var box in restore)
                    box.IsChecked = true;
            });

            bool busy = false;
            void Refresh()
            {
                if (busy)
                {
                    statusPill.Visibility = Visibility.Collapsed;
                    spinner.Visibility = Visibility.Visible;
                    return;
                }
                spinner.Visibility = Visibility.Collapsed;
                var boxes = OptionCheckBoxes(expander.Content).Where(b => b.Visibility == Visibility.Visible).ToList();
                int selected = boxes.Count(b => b.IsChecked != false);
                statusText.Text = boxes.Count == 0 ? "" : selected > 0 ? Loc.T("On") : Loc.T("Off");
                statusPill.ToolTip = boxes.Count > 1 ? Loc.F("{0} / {1} selected", selected, boxes.Count) : null;
                statusPill.Visibility = statusText.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
                MultronWinCleaner.Processes.CardLayout.SetSelected(expander, selected > 0);
                if (selected > 0)
                {
                    statusText.Foreground = MultronWinCleaner.Processes.ThemeColors.ReadableBrush(green);
                    statusPill.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x26, green.R, green.G, green.B));
                }
                else
                {
                    statusText.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
                    statusPill.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x1F, 0x80, 0x80, 0x80));
                }
            }
            if (key != null)
            {
                stepBusy[key] = value =>
                {
                    busy = value;
                    var rotate = (RotateTransform)spinnerArc.RenderTransform;
                    if (value)
                        rotate.BeginAnimation(RotateTransform.AngleProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9)) { RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever, SpeedRatio = 1 });
                    else
                        rotate.BeginAnimation(RotateTransform.AngleProperty, null);
                    expander.ToolTip = value ? Loc.T("Scanning...") : null;
                    Refresh();
                };
            }
            optionStatusRefresh[expander] = Refresh;
            RoutedEventHandler changed = (s, e) => Refresh();
            expander.AddHandler(System.Windows.Controls.Primitives.ToggleButton.CheckedEvent, changed);
            expander.AddHandler(System.Windows.Controls.Primitives.ToggleButton.UncheckedEvent, changed);
            expander.AddHandler(System.Windows.Controls.Primitives.ToggleButton.IndeterminateEvent, changed);
            expander.Loaded += (s, e) => Refresh();
            Refresh();
        }

        private static readonly string[] TopOptionOrder = { "Malware Scan", "Duplicate Files", "Large Files", "Firewall Rules", "Shortcuts", "Security" };

        private static int TopOptionRank(object? element)
        {
            string? key = element is Expander expander ? MultronWinCleaner.Processes.ItemInfo.Key(expander.Tag as string) : null;
            int index = key == null ? -1 : Array.IndexOf(TopOptionOrder, key);
            return index < 0 ? int.MaxValue : index;
        }

        private void AddOptionExpander(Expander expander)
        {
            AttachOptionStatus(expander);
            if (!IsSimpleInterface())
            {
                expander.Expanded += (s, e) =>
                {
                    if (ReferenceEquals(e.OriginalSource, expander))
                        MultronWinCleaner.Processes.CardLayout.AnimateOpen(expander);
                };
                int top = TopOptionRank(expander);
                if (top == int.MaxValue)
                {
                    wrapPanel1.Children.Add(expander);
                    return;
                }
                int index = 0;
                foreach (UIElement child in wrapPanel1.Children)
                {
                    if (TopOptionRank(child) > top)
                        break;
                    index++;
                }
                wrapPanel1.Children.Insert(index, expander);
                return;
            }

            if (optionsSectionPanel == null || optionsSectionPanel.Parent != wrapPanel1)
            {
                optionsSectionPanel = MultronWinCleaner.Processes.CardLayout.CreateGrid(new Thickness(0, 0, 0, 6));
                wrapPanel1.Children.Add(optionsSectionPanel);
                AddCleanSelectionsCard();
            }
            int rank = TopOptionRank(expander);
            int position = -1;
            if (rank != int.MaxValue)
            {
                position = 0;
                foreach (UIElement child in optionsSectionPanel.Children)
                {
                    if (Grid.GetColumnSpan(child) <= 1 && TopOptionRank(child) > rank)
                        break;
                    position++;
                }
            }
            MultronWinCleaner.Processes.CardLayout.Add(optionsSectionPanel, expander, position);
        }

        public readonly StackPanel CategoryPanel = new StackPanel { Orientation = Orientation.Vertical };
        private MultronWinCleaner.ToolSettingsWindow? cleanSelectionsWindow;
        private TextBlock? cleanSelectionsCount;
        private DispatcherTimer? cleanSelectionsTimer;
        private bool categoryPanelWatched;

        public Panel SectionHost => IsSimpleInterface() ? CategoryPanel : wrapPanel1;

        private void AddCleanSelectionsCard()
        {
            var accent = MultronWinCleaner.Processes.CardLayout.Accent;
            var title = new TextBlock { Text = Loc.T("Cleaning Selections"), FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
            title.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
            var description = new TextBlock { Text = Loc.T("Choose which Windows and app files are cleaned."), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
            description.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            texts.Children.Add(title);
            texts.Children.Add(description);

            cleanSelectionsCount = new TextBlock { FontSize = 11.5, FontWeight = FontWeights.SemiBold };
            cleanSelectionsCount.SetResourceReference(TextBlock.ForegroundProperty, "AccentText");
            var pill = new Border
            {
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x12, 0x1E, 0x88, 0xE5)),
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x38, 0x1E, 0x88, 0xE5)),
                BorderThickness = new Thickness(1),
                Child = cleanSelectionsCount
            };
            var chevron = new TextBlock
            {
                Text = "\uE76C",
                FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 12,
                Margin = new Thickness(10, 0, 2, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            chevron.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            FrameworkElement icon = MultronWinCleaner.Processes.CardLayout.Icon("\uE762");
            if (MultronWinCleaner.Processes.ToolIcons.Tile("selections", icon.Width > 0 ? icon.Width : 36) is FrameworkElement selectionsIcon)
            {
                selectionsIcon.Margin = icon.Margin;
                icon = selectionsIcon;
            }
            Grid.SetColumn(texts, 1);
            Grid.SetColumn(pill, 2);
            Grid.SetColumn(chevron, 3);
            grid.Children.Add(icon);
            grid.Children.Add(texts);
            grid.Children.Add(pill);
            grid.Children.Add(chevron);

            var normal = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x0F, 0x1E, 0x88, 0xE5));
            var hover = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x1C, 0x1E, 0x88, 0xE5));
            var card = new Border
            {
                CornerRadius = new CornerRadius(12),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(5),
                Padding = new Thickness(12, 12, 12, 12),
                Cursor = Cursors.Hand,
                Background = normal,
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x47, 0x1E, 0x88, 0xE5)),
                Child = grid
            };
            card.MouseEnter += (s, e) => card.Background = hover;
            card.MouseLeave += (s, e) => card.Background = normal;
            card.MouseLeftButtonUp += (s, e) => OpenCleanSelections();
            Grid.SetColumnSpan(card, 2);
            MultronWinCleaner.Processes.CardLayout.Add(optionsSectionPanel!, card);

            if (!categoryPanelWatched)
            {
                categoryPanelWatched = true;
                RoutedEventHandler changed = (s, e) => ScheduleCleanSelectionsCount();
                CategoryPanel.AddHandler(System.Windows.Controls.Primitives.ToggleButton.CheckedEvent, changed);
                CategoryPanel.AddHandler(System.Windows.Controls.Primitives.ToggleButton.UncheckedEvent, changed);
                CategoryPanel.LayoutUpdated += (s, e) => { if (cleanSelectionsCount?.Text.Length == 0) ScheduleCleanSelectionsCount(); };
            }
            ScheduleCleanSelectionsCount();
        }

        private void ScheduleCleanSelectionsCount()
        {
            if (cleanSelectionsTimer == null)
            {
                cleanSelectionsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
                cleanSelectionsTimer.Tick += (s, e) =>
                {
                    cleanSelectionsTimer.Stop();
                    UpdateCleanSelectionsCount();
                };
            }
            cleanSelectionsTimer.Stop();
            cleanSelectionsTimer.Start();
        }

        private void UpdateCleanSelectionsCount()
        {
            if (cleanSelectionsCount == null)
                return;
            int selected = 0;
            foreach (Expander section in ChildExpanders(CategoryPanel))
            {
                if (section.Content is not DependencyObject content)
                    continue;
                foreach (Expander group in ChildExpanders(content))
                {
                    if (OptionCheckBoxes(group.Content).Any(b => b.IsChecked == true) || OptionCheckBoxes(group.Header).Any(b => b.IsChecked == true))
                        selected++;
                }
            }
            cleanSelectionsCount.Text = Loc.F("{0} groups selected", selected);
        }

        private void OpenCleanSelections()
        {
            if (cleanSelectionsWindow == null)
            {
                var selectAll = new Button { Content = Loc.T("Select All"), Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(16, 0, 16, 0), Height = 36 };
                selectAll.SetResourceReference(StyleProperty, "UtilityPrimaryButtonStyle");
                selectAll.Click += SelectAll_CleanSelections_Click;
                var unselectAll = new Button { Content = Loc.T("Unselect All"), Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(16, 0, 16, 0), Height = 36 };
                unselectAll.SetResourceReference(StyleProperty, "UtilityNeutralButtonStyle");
                unselectAll.Click += UnselectAll_CleanSelections_Click;
                var search = new TextBox { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0), FontSize = 12.5, Background = System.Windows.Media.Brushes.Transparent, BorderThickness = new Thickness(0) };
                search.SetResourceReference(TextBox.ForegroundProperty, "TextPrimary");
                search.SetResourceReference(TextBox.CaretBrushProperty, "TextPrimary");
                var searchHint = new TextBlock { Text = Loc.T("Search apps or paths"), Margin = new Thickness(8, 0, 0, 0), FontSize = 12.5, Opacity = 0.45, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
                searchHint.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
                var searchIcon = new TextBlock { Text = "", FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"), FontSize = 12, Opacity = 0.55, VerticalAlignment = VerticalAlignment.Center };
                searchIcon.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
                var searchGrid = new Grid { Margin = new Thickness(12, 0, 10, 0) };
                searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                Grid.SetColumn(searchHint, 1);
                Grid.SetColumn(search, 1);
                searchGrid.Children.Add(searchIcon);
                searchGrid.Children.Add(searchHint);
                searchGrid.Children.Add(search);
                var searchBox = new Border { Height = 36, MinWidth = 160, CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(1), Child = searchGrid, VerticalAlignment = VerticalAlignment.Center };
                searchBox.SetResourceReference(Border.BackgroundProperty, "TextBoxBackground");
                searchBox.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
                var searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
                searchTimer.Tick += (s, e) =>
                {
                    searchTimer.Stop();
                    ApplyLibrarySearch(search.Text.Trim(), CategoryPanel);
                };
                search.TextChanged += (s, e) =>
                {
                    searchHint.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                    searchTimer.Stop();
                    searchTimer.Start();
                };
                var toolbar = new DockPanel { LastChildFill = true, Margin = new Thickness(5, 0, 5, 10) };
                DockPanel.SetDock(selectAll, Dock.Left);
                DockPanel.SetDock(unselectAll, Dock.Left);
                toolbar.Children.Add(selectAll);
                toolbar.Children.Add(unselectAll);
                toolbar.Children.Add(searchBox);
                var root = new DockPanel { LastChildFill = true };
                DockPanel.SetDock(toolbar, Dock.Top);
                root.Children.Add(toolbar);
                root.Children.Add(CategoryPanel);
                cleanSelectionsWindow = new MultronWinCleaner.ToolSettingsWindow(this, Loc.T("Cleaning Selections"), root, 1040, 760, true);
                cleanSelectionsWindow.SetFooter(BuildItemPathsPanel());
                cleanSelectionsWindow.IsVisibleChanged += (s, e) =>
                {
                    if (cleanSelectionsWindow != null && !cleanSelectionsWindow.IsVisible)
                    {
                        search.Clear();
                        UpdateCleanSelectionsCount();
                    }
                };
            }
            cleanSelectionsWindow.ShowOrActivate();
        }

        public static void StyleCardComboBox(ComboBox box)
        {
            box.SetResourceReference(StyleProperty, "PanelComboBoxStyle");
            double widest = double.IsInfinity(box.MaxWidth) ? 300 : box.MaxWidth;
            box.HorizontalAlignment = HorizontalAlignment.Left;
            void Fit()
            {
                if (VisualTreeHelper.GetParent(box) is FrameworkElement parent && parent.ActualWidth > 0)
                    box.Width = Math.Max(120, Math.Min(widest, parent.ActualWidth - box.Margin.Left - box.Margin.Right));
            }
            box.Loaded += (s, e) =>
            {
                if (VisualTreeHelper.GetParent(box) is FrameworkElement parent)
                {
                    parent.SizeChanged -= OnParentSize;
                    parent.SizeChanged += OnParentSize;
                }
                Fit();
            };
            void OnParentSize(object? s, SizeChangedEventArgs e) => Fit();
            box.Opacity = box.IsEnabled ? 1 : 0.5;
            box.IsEnabledChanged += (s, e) => box.Opacity = box.IsEnabled ? 1 : 0.5;
        }

        private Border? itemPathsPanel;
        private TextBlock? itemPathsTitle;
        private TextBlock? itemPathsCount;
        private StackPanel? itemPathsList;
        private Button? itemPathsCopy;
        private List<string> itemPathsShown = new();

        private Border BuildItemPathsPanel()
        {
            var icon = new TextBlock { Text = "\uE8B7", FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            icon.SetResourceReference(TextBlock.ForegroundProperty, "AccentText");
            var label = new TextBlock { Text = Loc.T("Paths"), FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            label.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
            itemPathsTitle = new TextBlock { FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            itemPathsTitle.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
            itemPathsCount = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 10, 0) };
            itemPathsCount.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
            itemPathsCopy = new Button { Content = Loc.T("Copy paths"), Height = 28, Padding = new Thickness(12, 0, 12, 0), IsEnabled = false, ToolTip = Loc.T("Copies every path of this item to the clipboard.") };
            itemPathsCopy.SetResourceReference(StyleProperty, "UtilitySecondaryButtonStyle");
            itemPathsCopy.Click += (s, e) =>
            {
                try { System.Windows.Clipboard.SetText(string.Join(Environment.NewLine, itemPathsShown)); }
                catch (Exception ex) { AppDialog.Show(Loc.F("Error:\n{0}", ex.Message), Loc.T("Error"), MessageBoxButton.OK, MessageBoxImage.Error); }
            };
            var header = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 6) };
            DockPanel.SetDock(icon, Dock.Left);
            DockPanel.SetDock(label, Dock.Left);
            DockPanel.SetDock(itemPathsCopy, Dock.Right);
            DockPanel.SetDock(itemPathsCount, Dock.Right);
            header.Children.Add(icon);
            header.Children.Add(label);
            header.Children.Add(itemPathsCopy);
            header.Children.Add(itemPathsCount);
            header.Children.Add(itemPathsTitle);

            itemPathsList = new StackPanel();
            var scroll = new ScrollViewer { Content = itemPathsList, MaxHeight = 112, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            var stack = new StackPanel();
            stack.Children.Add(header);
            stack.Children.Add(scroll);
            var panel = new Border { Padding = new Thickness(20, 10, 20, 12), BorderThickness = new Thickness(0, 1, 0, 0), CornerRadius = new CornerRadius(0, 0, 12, 12), Child = stack, MinHeight = 92 };
            panel.SetResourceReference(Border.BackgroundProperty, "PanelBackground");
            panel.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
            itemPathsPanel = panel;
            SetItemPathsVisible(ReadSettingValue(ShowItemPathsSettingKey) != "0");
            ShowItemPaths("", "", Array.Empty<string>());
            return panel;
        }

        public void ShowItemPaths(string title, string group, IReadOnlyList<string> paths)
        {
            if (itemPathsList == null || itemPathsTitle == null || itemPathsCount == null || itemPathsCopy == null)
                return;
            itemPathsList.Children.Clear();
            itemPathsShown = paths.Where(p => p.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            itemPathsCopy.IsEnabled = itemPathsShown.Count > 0;
            if (itemPathsShown.Count == 0)
            {
                itemPathsTitle.Text = "";
                itemPathsCount.Text = "";
                var hint = new TextBlock { Text = Loc.T("Point at an item to see the folders and files it cleans."), FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(22, 2, 0, 0) };
                hint.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
                itemPathsList.Children.Add(hint);
                return;
            }
            itemPathsTitle.Text = group.Length > 0 && !string.Equals(group, title, StringComparison.OrdinalIgnoreCase) ? group + "  ›  " + title : title;
            itemPathsCount.Text = itemPathsShown.Count == 1 ? Loc.T("1 path") : Loc.F("{0} paths", itemPathsShown.Count);
            foreach (string raw in itemPathsShown.Take(60))
            {
                string expanded = Environment.ExpandEnvironmentVariables(raw);
                int star = expanded.IndexOf('*');
                string folder = star >= 0 ? expanded.Substring(0, star).TrimEnd('\\') : expanded;
                bool exists = folder.Length > 0 && (System.IO.Directory.Exists(folder) || System.IO.File.Exists(folder));
                var row = new DockPanel { LastChildFill = true, Margin = new Thickness(22, 1, 0, 1) };
                var path = new TextBlock { Text = expanded, FontSize = 12, FontFamily = new System.Windows.Media.FontFamily("Cascadia Mono, Consolas, Segoe UI"), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, ToolTip = raw == expanded ? expanded : expanded + "\n" + raw };
                path.SetResourceReference(TextBlock.ForegroundProperty, exists ? "TextPrimary" : "TextSecondary");
                var side = new TextBlock { FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
                if (exists)
                {
                    var link = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run(Loc.T("Open"))) { ToolTip = Loc.T("Open location") };
                    link.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "AccentText");
                    string target = folder;
                    link.Click += (s, e) =>
                    {
                        try
                        {
                            if (System.IO.File.Exists(target))
                                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{target}\"");
                            else
                                System.Diagnostics.Process.Start("explorer.exe", $"\"{target}\"");
                        }
                        catch (Exception ex)
                        {
                            AppDialog.Show(Loc.F("Error:\n{0}", ex.Message), Loc.T("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    };
                    side.Inlines.Add(link);
                }
                else
                {
                    side.Text = Loc.T("Not on this PC");
                    side.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
                }
                DockPanel.SetDock(side, Dock.Right);
                row.Children.Add(side);
                row.Children.Add(path);
                itemPathsList.Children.Add(row);
            }
            if (itemPathsShown.Count > 60)
            {
                var more = new TextBlock { Text = Loc.F("...and {0} more", itemPathsShown.Count - 60), FontSize = 12, Margin = new Thickness(22, 2, 0, 0) };
                more.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
                itemPathsList.Children.Add(more);
            }
        }

        private MultronWinCleaner.ToolSettingsWindow? foundFilesWindow;
        private Border? foundFilesCard;
        private TextBlock? foundFilesSummary;
        private TextBlock? foundFilesSize;
        private ScrollViewer? foundFilesScroll;
        private TextBlock? foundFilesStatus;
        private DispatcherTimer? foundFilesTimer;
        private bool foundFilesReady;

        private void SetupFoundFiles()
        {
            if (foundFilesReady)
                return;
            foundFilesReady = true;
            ResultsStack.Children.Remove(FoundFilesToolbar);
            ResultsStack.Children.Remove(dataGridGroups);
            var contextBinding = new System.Windows.Data.Binding(nameof(DataContext)) { Source = this };
            FoundFilesToolbar.SetBinding(FrameworkElement.DataContextProperty, contextBinding);
            dataGridGroups.SetBinding(FrameworkElement.DataContextProperty, contextBinding);
            FoundFilesToolbar.Margin = new Thickness(5, 0, 5, 12);

            var title = new TextBlock { Text = Loc.T("Found Files"), FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
            title.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
            foundFilesSummary = new TextBlock { FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) };
            foundFilesSummary.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            texts.Children.Add(title);
            texts.Children.Add(foundFilesSummary);

            foundFilesSize = new TextBlock { FontSize = 12.5, FontWeight = FontWeights.SemiBold };
            foundFilesSize.SetResourceReference(TextBlock.ForegroundProperty, "AccentText");
            var pill = new Border
            {
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10, 3, 10, 3),
                Margin = new Thickness(12, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x12, 0x1E, 0x88, 0xE5)),
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x38, 0x1E, 0x88, 0xE5)),
                BorderThickness = new Thickness(1),
                Child = foundFilesSize
            };
            var chevron = new TextBlock
            {
                Text = "",
                FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 12,
                Margin = new Thickness(12, 0, 2, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            chevron.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
            FrameworkElement icon = MultronWinCleaner.Processes.CardLayout.Icon("");
            icon.VerticalAlignment = VerticalAlignment.Center;

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(texts, 1);
            Grid.SetColumn(pill, 2);
            Grid.SetColumn(chevron, 3);
            grid.Children.Add(icon);
            grid.Children.Add(texts);
            grid.Children.Add(pill);
            grid.Children.Add(chevron);

            var normal = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x0F, 0x1E, 0x88, 0xE5));
            var hover = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x1C, 0x1E, 0x88, 0xE5));
            foundFilesCard = new Border
            {
                CornerRadius = new CornerRadius(12),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 14),
                Padding = new Thickness(18, 14, 18, 14),
                Cursor = Cursors.Hand,
                Background = normal,
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x47, 0x1E, 0x88, 0xE5)),
                Child = grid,
                Visibility = Visibility.Collapsed,
                ToolTip = Loc.T("Opens the files the scan found, where you can search them and choose what is cleaned.")
            };
            foundFilesCard.MouseEnter += (s, e) => foundFilesCard.Background = hover;
            foundFilesCard.MouseLeave += (s, e) => foundFilesCard.Background = normal;
            foundFilesCard.MouseLeftButtonUp += (s, e) => OpenFoundFiles();
            ResultsStack.Children.Insert(0, foundFilesCard);

            viewModel.Groups.CollectionChanged += (s, e) => ScheduleFoundFilesUpdate();
            System.ComponentModel.DependencyPropertyDescriptor
                .FromProperty(UIElement.VisibilityProperty, typeof(UIElement))
                .AddValueChanged(dataGridGroups, (s, e) =>
                {
                    if (dataGridGroups.Visibility != Visibility.Visible)
                        foundFilesWindow?.Hide();
                    ScheduleFoundFilesUpdate();
                });
            UpdateFoundFilesCard();
        }

        private void ScheduleFoundFilesUpdate()
        {
            if (foundFilesTimer == null)
            {
                foundFilesTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
                foundFilesTimer.Tick += (s, e) =>
                {
                    foundFilesTimer.Stop();
                    UpdateFoundFilesCard();
                };
            }
            foundFilesTimer.Stop();
            foundFilesTimer.Start();
        }

        private void UpdateFoundFilesCard()
        {
            if (foundFilesCard == null || foundFilesSummary == null || foundFilesSize == null)
                return;
            var groups = (backuped == 1 ? viewModelbac.Groups : viewModel.Groups).ToList();
            bool show = dataGridGroups.Visibility == Visibility.Visible && groups.Count > 0;
            foundFilesCard.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (!show)
            {
                if (foundFilesStatus != null)
                    foundFilesStatus.Text = "";
                return;
            }
            long files = groups.Sum(g => (long)(g.allFiles?.Count ?? 0));
            long bytes = groups.Sum(g => g.TotalSizeBytes);
            long selectedFiles = groups.Sum(g => (long)(g.allFiles?.Count(f => f.IsChecked) ?? 0));
            long selectedBytes = groups.Sum(g => g.allFiles?.Where(f => f.IsChecked).Sum(f => f.SizeBytes) ?? 0);
            foundFilesSummary.Text = selectedFiles == files
                ? Loc.F("{0} files in {1} folders", files.ToString("N0", Loc.Culture), groups.Count.ToString("N0", Loc.Culture))
                : Loc.F("{0} of {1} files selected in {2} folders", selectedFiles.ToString("N0", Loc.Culture), files.ToString("N0", Loc.Culture), groups.Count.ToString("N0", Loc.Culture));
            foundFilesSize.Text = formatsize(selectedBytes);
            foundFilesSize.ToolTip = Loc.F("{0} selected of {1} found", formatsize(selectedBytes), formatsize(bytes));
            if (foundFilesStatus != null)
                foundFilesStatus.Text = foundFilesSummary.Text + " · " + formatsize(selectedBytes);
        }

        private void OpenFoundFiles()
        {
            if (foundFilesWindow == null)
            {
                foundFilesScroll = new ScrollViewer
                {
                    Content = dataGridGroups,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Margin = new Thickness(5, 0, 0, 0)
                };
                foundFilesScroll.PreviewMouseWheel += NestedScroll_PreviewMouseWheel;
                var root = new DockPanel { LastChildFill = true };
                DockPanel.SetDock(FoundFilesToolbar, Dock.Top);
                foundFilesStatus = new TextBlock { FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
                foundFilesStatus.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
                var export = new Button { Content = Loc.T("Export list"), Height = 32, Padding = new Thickness(14, 0, 14, 0), FontSize = 12.5, Margin = new Thickness(12, 0, 0, 0), ToolTip = Loc.T("Saves the list of found files as a CSV file you can open in Excel.") };
                export.SetResourceReference(StyleProperty, "UtilitySecondaryButtonStyle");
                export.Click += (s, e) => ExportFoundFiles();
                var footer = new DockPanel { Margin = new Thickness(6, 10, 5, 0) };
                DockPanel.SetDock(export, Dock.Right);
                footer.Children.Add(export);
                footer.Children.Add(foundFilesStatus);
                DockPanel.SetDock(footer, Dock.Bottom);
                root.Children.Add(FoundFilesToolbar);
                root.Children.Add(footer);
                root.Children.Add(foundFilesScroll);
                foundFilesWindow = new MultronWinCleaner.ToolSettingsWindow(this, Loc.T("Found Files"), root, 1040, 760, true);
            }
            UpdateFoundFilesCard();
            foundFilesWindow.ShowOrActivate();
        }

        public static readonly string[] FoundFilesHeader = { "Folder", "File Name", "Path", "Size", "Bytes", "Selected" };

        public System.Collections.Generic.IEnumerable<string[]> FoundFilesRows()
        {
            var groups = (backuped == 1 ? viewModelbac.Groups : viewModel.Groups).ToList();
            foreach (var group in groups)
            {
                foreach (var file in group.allFiles)
                    yield return new[] { group.Path, file.DisplayName, file.Path, file.SizeText, file.SizeBytes.ToString(System.Globalization.CultureInfo.InvariantCulture), Loc.T(file.IsChecked ? "Yes" : "No") };
            }
        }

        private void ExportFoundFiles()
        {
            MultronWinCleaner.Processes.CsvExport.Save(foundFilesWindow ?? (Window)this, "MultronWinCleaner_found_files", FoundFilesHeader.Select(Loc.T), FoundFilesRows());
        }

        private static string? GroupFolder(object sender) =>
            sender is MenuItem item && item.DataContext is GroupViewModel group && System.IO.Directory.Exists(group.Path) ? group.Path : null;

        private void ShowNotAFolder() =>
            AppDialog.Show(Loc.T("This group is not a single folder, so it cannot be opened or skipped."), Loc.T("Open folder"), MessageBoxButton.OK, MessageBoxImage.Information);

        private void OpenGroupFolder_Click(object sender, RoutedEventArgs e)
        {
            if (GroupFolder(sender) is not string folder)
            {
                ShowNotAFolder();
                return;
            }
            try
            {
                System.Diagnostics.Process.Start("explorer.exe", "\"" + folder + "\"");
            }
            catch
            {
            }
        }

        private void CopyGroupPath_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem item && item.DataContext is GroupViewModel group)
            {
                try
                {
                    Clipboard.SetText(group.Path);
                }
                catch
                {
                }
            }
        }

        private void SkipGroupFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem item || item.DataContext is not GroupViewModel group)
                return;
            if (GroupFolder(sender) is not string folder)
            {
                ShowNotAFolder();
                return;
            }
            settings.addexception(folder);
            foreach (var file in group.allFiles)
                file.IsChecked = false;
            label1_Copy.Text = Loc.F("Added to Exceptions: {0}", folder);
            ScheduleFoundFilesUpdate();
        }

        #endregion

        private static DataTemplate? wrappingCheckBoxTemplate;
        private static DataTemplate WrappingCheckBoxTemplate
        {
            get
            {
                if (wrappingCheckBoxTemplate == null)
                {
                    var text = new FrameworkElementFactory(typeof(TextBlock));
                    text.SetBinding(TextBlock.TextProperty, new Binding());
                    text.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
                    wrappingCheckBoxTemplate = new DataTemplate { VisualTree = text };
                    wrappingCheckBoxTemplate.Seal();
                }
                return wrappingCheckBoxTemplate;
            }
        }

        public void UpdateShredBadge()
        {
            if (labelShred == null) return;
            var mode = MultronWinCleaner.Processes.SecureDelete.ReadConfiguredMode();
            if (mode == MultronWinCleaner.Processes.SecureDelete.Mode.Off)
            {
                labelShred.Visibility = Visibility.Collapsed;
                return;
            }
            string detail = MultronWinCleaner.Processes.SecureDelete.Describe(mode);
            labelShred.Text = Loc.F("Shred & Delete is on · {0}", detail);
            labelShred.Visibility = Visibility.Visible;
            labelShred.ToolTip = Loc.F("Cleaning overwrites files before deleting them so they cannot be recovered. Setting: {0}. Change it in Settings > Secure Deletion (Shredding).", detail);
        }

        private bool suppressCleanmgrExclusive;

        private void CleanmgrExclusive_Checked(object sender, RoutedEventArgs e)
        {
            if (suppressCleanmgrExclusive || sender is not CheckBox picked) return;
            suppressCleanmgrExclusive = true;
            try
            {
                foreach (CheckBox cb in checkboxes2)
                {
                    if (ReferenceEquals(cb, picked)) continue;
                    if (cb.Content is string content
                        && content.StartsWith("cleanmgr.exe", StringComparison.OrdinalIgnoreCase)
                        && cb.IsChecked == true)
                        cb.IsChecked = false;
                }
            }
            finally
            {
                suppressCleanmgrExclusive = false;
            }
        }

        public async Task createshortcut(string checkboxname, string expander)
        { 
            CheckBox checkbox = new CheckBox
            {
                Content = checkboxname,
                Margin = new Thickness(10),
                IsChecked = false,
                BorderThickness = new Thickness(0),
                BorderBrush = new SolidColorBrush(Colors.Transparent),
                Background = new SolidColorBrush(System.Windows.Media.Colors.White),
                Foreground = brush, 
                FontSize = 12,
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                FontWeight = FontWeights.Regular,
                FontStyle = FontStyles.Normal,
            };
            checkbox.Checked += CheckBox2_Checked;
            checkbox.Unchecked += CheckBox2_Unchecked;
            string commandKey = "command:" + checkboxname.Split('=').Last();
            checkbox.IsChecked = MultronWinCleaner.Processes.Load.ReadSelections().TryGetValue(commandKey, out bool savedCommand) && savedCommand;
            checkbox.Checked += (s, e) => SaveCommandSelection(commandKey, true);
            checkbox.Unchecked += (s, e) => SaveCommandSelection(commandKey, false);
            if (checkboxname.StartsWith("cleanmgr.exe", StringComparison.OrdinalIgnoreCase))
            {
                checkbox.Checked += CleanmgrExclusive_Checked;
                checkbox.SetResourceReference(FrameworkElement.StyleProperty, "RadioCheckBoxStyle");
                if (checkbox.IsChecked == true && checkboxes2.Any(other => other.IsChecked == true
                        && other.Content is string otherName && otherName.StartsWith("cleanmgr.exe", StringComparison.OrdinalIgnoreCase)))
                    checkbox.IsChecked = false;
            }
            ApplyOptionTemplate(checkbox);
            checkboxes2.Add(checkbox);

            Expander? existingExpander = expanders.FirstOrDefault(e => (e.Tag as string ?? e.Header?.ToString()) == expander);

            if (existingExpander != null)
            {
                
                if (existingExpander.Content is StackPanel existingPanel)
                {
                    existingPanel.Children.Add(checkbox);
                    if (optionStatusRefresh.TryGetValue(existingExpander, out var refresh))
                        refresh();
                }
            }
            else
            {
               
                StackPanel groupBoxContent2 = new StackPanel
                {
                    Orientation = Orientation.Vertical,
                    VerticalAlignment = VerticalAlignment.Top,
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                stackpanels.Add(groupBoxContent2);

                groupBoxContent2.Children.Add(checkbox);

                Expander newExpander1 = new Expander
                {
                    Header = expander,
                    Margin = new Thickness(5),
                    Background = new SolidColorBrush(Colors.Transparent),
                    Foreground = brush,
                    BorderBrush = new SolidColorBrush(Colors.Transparent),
                    BorderThickness = new Thickness(2),
                    FontSize = 14,
                    FontWeight = FontWeights.Regular,
                    IsEnabled = true,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top
                };

                newExpander1.Content = groupBoxContent2;
                expanders.Add(newExpander1);

                try
                {
                    AddOptionExpander(newExpander1);
                }
                catch (Exception)
                {
                }
            }
        }

        private void CheckBox2_Checked(object sender, RoutedEventArgs e)
        {
            ScheduleFoundFilesUpdate();
            if (sender is not CheckBox checkBox) return;
            if (checkBox == null || checkBox.Content == null) return;

            string content = checkBox.Content?.ToString() ?? "";
             
            if (content.Contains("Dism.exe") || content.Contains("cleanmgr.exe") || content.Contains("Deep Log Files Scan") || content.Contains("sfc.exe"))
            {
                database.Insert(0, content);
            }
        }
        private void CheckBox2_Unchecked(object sender, RoutedEventArgs e)

        {
            ScheduleFoundFilesUpdate();

            if (sender is not CheckBox checkBox) return;

            string content = checkBox.Content?.ToString() ?? "";

            if (content.Contains("Dism.exe") || content.StartsWith("cleanmgr.exe") || content.Contains("Deep Log Files Scan") || content.Contains("sfc.exe"))

            {
                        database.Remove(content);

            }

        }
        private void ProgressBar1_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateArc(progressBar1.Value);
            UpdateTaskbarProgress();
        }

        private void UpdateTaskbarProgress()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(UpdateTaskbarProgress));
                return;
            }
            var info = TaskbarItemInfo;
            if (info == null)
                return;
            bool busy = Equals(buttonStartScan.Content, Loc.T("Cancel")) || activeTool != null;
            var tracker = MultronWinCleaner.Processes.CloudUploadTracker.Instance;
            if (!busy)
            {
                bool found = lastMalwareThreats > 0 && MalwareResultsPanel.Visibility == Visibility.Visible;
                info.ProgressState = !found ? System.Windows.Shell.TaskbarItemProgressState.None
                    : OnlySuspiciousFound ? System.Windows.Shell.TaskbarItemProgressState.Paused : System.Windows.Shell.TaskbarItemProgressState.Error;
                info.ProgressValue = 1;
                return;
            }
            bool malware = malwareCancelRestore != null && tracker.IsScanning;
            double value = malware ? tracker.Percent / 100.0 : progressBar1.Value / 100.0;
            bool unknown = malware ? tracker.Indeterminate || value <= 0 : value <= 0;
            if (malware && (tracker.HasThreats || tracker.HasSuspicious))
            {
                info.ProgressState = tracker.HasThreats ? System.Windows.Shell.TaskbarItemProgressState.Error : System.Windows.Shell.TaskbarItemProgressState.Paused;
                info.ProgressValue = unknown ? 1 : Math.Clamp(value, 0.03, 1);
                return;
            }
            info.ProgressState = unknown ? System.Windows.Shell.TaskbarItemProgressState.Indeterminate : System.Windows.Shell.TaskbarItemProgressState.Normal;
            info.ProgressValue = Math.Clamp(value, 0, 1);
        }

        private System.Windows.Shapes.Path? _arcPath;
        public void UpdateArc(double percent)
        {

            percent = Math.Max(0, Math.Min(100, percent));

            if (_arcPath == null)
                _arcPath = (System.Windows.Shapes.Path)progressBar1.Template.FindName("PART_Indicator", progressBar1);
            if (_arcHead == null)
                _arcHead = progressBar1.Template.FindName("PART_Head", progressBar1) as System.Windows.Shapes.Ellipse;

            if (_arcPath == null) return;

            double thickness = _arcPath.StrokeThickness;
            double radius = progressBar1.ActualWidth / 2 - thickness / 2;
            var center = new System.Windows.Point(progressBar1.ActualWidth / 2, progressBar1.ActualHeight / 2);
            double capAngle = radius > 0 ? (thickness / 2) / radius * 180 / Math.PI : 0;
            double startAngle = capAngle;
            double angle = 360 * (percent / 100) - capAngle;

            Geometry g;
            System.Windows.Point? head = null;

            if (percent <= 0 || radius <= 0)
            {
                g = Geometry.Empty;
            }
            else if (percent >= 99.5)
            {
                g = new EllipseGeometry(center, radius, radius);
            }
            else
            {
                if (angle < startAngle)
                {
                    startAngle = angle = 360 * (percent / 100) / 2;
                }
                double startRad = (Math.PI / 180) * (startAngle - 90);
                var start = new System.Windows.Point(
                    center.X + radius * Math.Cos(startRad),
                    center.Y + radius * Math.Sin(startRad));

                double rad = (Math.PI / 180) * (angle - 90);
                var end = new System.Windows.Point(
                    center.X + radius * Math.Cos(rad),
                    center.Y + radius * Math.Sin(rad));
                head = end;

                bool largeArc = angle - startAngle > 180;

                var fig = new PathFigure { StartPoint = start };
                fig.Segments.Add(new ArcSegment(
                    end,
                    new System.Windows.Size(radius, radius),
                    0,
                    largeArc,
                    SweepDirection.Clockwise,
                    true));

                g = new PathGeometry(new[] { fig });
            }

            _arcPath.Data = g;
            bool busy = percent > 0 && percent < 99.5 && Equals(buttonStartScan.Content, Loc.T("Cancel"));
            ProgressPercentText.Text = Loc.F("{0}%", ((int)percent).ToString(Loc.Culture));
            ProgressPercentText.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            MultronWinCleaner.Processes.OrbHover.SetLevel(buttonStartScan, percent / 100.0, Equals(buttonStartScan.Content, Loc.T("Cancel")) && percent > 0 && percent < 99.5);
            if (_arcHead != null)
            {
                _arcHead.Visibility = head.HasValue ? Visibility.Visible : Visibility.Collapsed;
                if (head is System.Windows.Point p)
                    _arcHead.Margin = new Thickness(p.X - _arcHead.Width / 2, p.Y - _arcHead.Height / 2, 0, 0);
            }
        }

        private System.Windows.Shapes.Ellipse? _arcHead;

        private void TrayIcon_MouseDoubleClick(object sender, RoutedEventArgs e)
        {

            if (this.Visibility == Visibility.Hidden)
            {
                this.Show();
                this.WindowState = WindowState.Normal;
                this.Visibility = Visibility.Visible;
            }
            else
            {
                this.WindowState = WindowState.Normal;
                this.Visibility = Visibility.Visible;
            }

            this.Activate();
        }
        private void HideApp_Click(object sender, RoutedEventArgs e)
        {
            this.Hide();
            settings.Hide();
        }
        private static bool SettingIsOn(string key)
        {
            try
            {
                string path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Settings.txt");
                if (!System.IO.File.Exists(path))
                    return false;
                return System.IO.File.ReadLines(path).Any(line => line.Trim().Equals(key + ":1", StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return true;
            }
        }

        public void BringToFront()
        {
            Show();
            Visibility = Visibility.Visible;
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
            Activate();
            Topmost = true;
            Topmost = false;
            Focus();
        }

        private void OpenApp_Click(object sender, RoutedEventArgs e)
        {
            this.Show();
            this.WindowState = WindowState.Normal;
            this.Activate();
        }

        private void ExitApp_Click(object sender, RoutedEventArgs e)
        {
            if (!ConfirmExit())
                return;
            ExitApplication();
        }

        private static int exiting;

        public void ExitApplication()
        {
            if (System.Threading.Interlocked.Exchange(ref exiting, 1) == 1)
                return;

            var watchdog = new System.Threading.Thread(() =>
            {
                System.Threading.Thread.Sleep(3000);
                try { Process.GetCurrentProcess().Kill(); } catch { }
            }) { IsBackground = true };
            watchdog.Start();

            try { FlushPendingSelections(); } catch { }
            try { MultronWinCleaner.Processes.MrtScanner.KillRunning(); } catch { }
            try { utilities?.malwarescan?.RemoveTrayIcon(); } catch { }
            try { utilities?.DisposeTrayIcon(); } catch { }
            try { TrayIcon?.Dispose(); } catch { }

            Environment.Exit(0);
        }

        private string? BusyOperationName()
        {
            try
            {
                if (IsScanOrCleanBusy)
                    return Loc.T("a scan or cleanup");
                if (activeTool != null)
                    return activeTool;
                if (IsAppUpdateBusy)
                    return Loc.T("an app update");
            }
            catch { }
            return null;
        }

        public const string ConfirmExitSettingKey = "confirmexit";
        public const string CloseActionSettingKey = "trayclose";
        public const string MinimizeToTraySettingKey = "trayminimize";
        private static bool sessionEnding;

        public static string CloseAction => ReadSettingValue(CloseActionSettingKey) switch
        {
            "ask" => "ask",
            "exit" => "exit",
            _ => "hide"
        };

        private bool TrayActive => settings != null && settings.chkTrayIcon.IsChecked == true && TrayIcon.Visibility == Visibility.Visible;

        private void HideToTray()
        {
            Hide();
            settings.Hide();
        }

        private bool ConfirmExit()
        {
            if (sessionEnding || System.Threading.Volatile.Read(ref exiting) == 1)
                return true;
            if (BusyOperationName() != null)
                return ConfirmExitIfBusy();
            if (!IsSettingOnByDefault(ConfirmExitSettingKey))
                return true;
            return AppDialog.Show(IsVisible ? this : null, Loc.T("Do you want to close Multron Win Cleaner completely?"),
                Loc.T("Close Multron Win Cleaner?"), MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        }

        private void HandleCloseRequest()
        {
            if (TrayActive)
            {
                string action = CloseAction;
                if (action == "hide")
                {
                    HideToTray();
                    return;
                }
                if (action == "ask")
                {
                    var (choice, remember) = CloseChoiceWindow.Ask(IsVisible ? this : null);
                    if (choice == CloseChoiceWindow.Choice.Cancel)
                        return;
                    if (remember)
                    {
                        utilities.savesettings($"{CloseActionSettingKey}:{(choice == CloseChoiceWindow.Choice.Hide ? "hide" : "exit")}");
                        settings.LoadCloseOptions();
                    }
                    if (choice == CloseChoiceWindow.Choice.Hide)
                    {
                        HideToTray();
                        return;
                    }
                    if (!ConfirmExitIfBusy())
                        return;
                    ExitApplication();
                    return;
                }
            }
            if (!ConfirmExit())
                return;
            ExitApplication();
        }

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized && TrayActive && ReadSettingValue(MinimizeToTraySettingKey) == "1")
            {
                HideToTray();
                WindowState = WindowState.Normal;
            }
        }

        private bool ConfirmExitIfBusy()
        {
            if (System.Threading.Volatile.Read(ref exiting) == 1)
                return true;
            string? op = BusyOperationName();
            if (op == null)
                return true;
            MessageBoxResult answer = AppDialog.Show(
                Loc.F("An operation is still running: {0}.", op) + "\n\n" +
                Loc.T("If you close Multron Win Cleaner now, it will be stopped. Do you want to close anyway?"),
                Loc.T("Multron Windows Cleaner"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
            return answer == MessageBoxResult.Yes;
        }

        public string? stringtokenizer(string input, string token, int index)
        {

            string[] tokens = input.Split(token);

            if (index >= 0 && index < tokens.Length)
            {
                return (tokens[index]);
            }
            else
            {
                return (null);
            }
        }
        public List<string> database = new List<string>();
        public static int scanstatus = 0;
        public CancellationTokenSource cancelstatus = new CancellationTokenSource();

  

        public string formatsize(long size)
        {
            if (size < 0)
                return Loc.T("0 Byte");

            string[] sizes = { Loc.T("Byte"), "KB", "MB", "GB", "TB" };
            double len = size;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
        private void FilesListBox_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not ListBox listBox) return;
            var scrollViewer = FindVisualChild<ScrollViewer>(listBox);
            if (scrollViewer != null)
            {
                scrollViewer.ScrollChanged += FilesListBox_ScrollChanged;
            }
        }

        private bool isLoading = false;

        private async void FilesListBox_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - 100)
            {
                if (sender is ScrollViewer scrollViewer && scrollViewer.DataContext is MultronWinCleaner.Processes.Scan.GroupViewModel vm)
                {
                    if (isLoading) return;

                    isLoading = true;
                    await vm.LoadMoreFilesAsync();
                    isLoading = false;
                }
            }
        }

        public T? FindVisualChild<T>(DependencyObject obj) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(obj, i);
                if (child is T t)
                    return t;

                var childOfChild = FindVisualChild<T>(child);
                if (childOfChild != null)
                    return childOfChild;
            }
            return null;
        }
        public static T? FindVisualParent<T>(DependencyObject obj) where T : DependencyObject
        {
            DependencyObject parent = VisualTreeHelper.GetParent(obj);
            while (parent != null)
            {
                if (parent is T t)
                    return t;

                parent = VisualTreeHelper.GetParent(parent);
            }
            return null;
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }
        private void TopPanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                this.DragMove();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => HandleCloseRequest();
       
     

        public void CheckBox_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox checkBox) return;
            if (checkBox != null)
            {

                string content = checkBox.Content?.ToString() ?? "";

            
               string? name = stringtokenizer(content, "=", 0);
               string? path = stringtokenizer(content, "=", 1);

               database.Add(name + "=" + path);
               QueueSelectionAutoSave(checkBox, path);

            }
        }
        public void CheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox checkBox) return;
            if (checkBox != null)
            {
                string content = checkBox.Content?.ToString() ?? "";
                string? name = stringtokenizer(content, "=", 0);
                string? path = stringtokenizer(content, "=", 1);
                database.Remove(name + "=" + path);
                QueueSelectionAutoSave(checkBox, path);
           }

          
        }
        private async void ScrollViewerWrap_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {

            if (sender is not ScrollViewer scrollViewer) return;

            double verticalOffset = scrollViewer.VerticalOffset;
            double viewportHeight = scrollViewer.ViewportHeight;
            double extentHeight = scrollViewer.ExtentHeight;

            const double threshold = 20.0;
            bool isAtBottom = (extentHeight - (verticalOffset + viewportHeight)) <= threshold;

            if (!isAtBottom) return;

            foreach (Expander expander in expanders)
            {
                if (expander.Parent == null && !wrapPanel1.Children.Contains(expander))
                {
                    wrapPanel1.Children.Add(expander);
                    break;
                }
            }

        }
        private double previousWidth, previousHeight, previousLeft, previousTop;

        private bool isMaximized = false;

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (isMaximized)
            {
                this.WindowState = WindowState.Normal;
                this.Width = previousWidth;
                this.Height = previousHeight;
                this.Left = previousLeft;
                this.Top = previousTop;
                isMaximized = false;
                buttonMaximize.Content = "\uE922";
            }
            else
            {

                previousWidth = this.Width;
                previousHeight = this.Height;
                previousLeft = this.Left;
                previousTop = this.Top;

                this.WindowState = WindowState.Normal;
                this.Left = SystemParameters.WorkArea.Left;
                this.Top = SystemParameters.WorkArea.Top;
                this.Width = SystemParameters.WorkArea.Width;
                this.Height = SystemParameters.WorkArea.Height;

                isMaximized = true;
                buttonMaximize.Content = "\uE923";
            }
        }
        public static byte[] getrandombytes(long size)
        {
            byte[] randombs = new byte[size];
            System.Security.Cryptography.RandomNumberGenerator.Create().GetNonZeroBytes(randombs);
            return randombs;
        }

        public int cancelclean = 0;
        public int reset = 0;

        public static class SystemActions
        {

            [DllImport("user32.dll", SetLastError = true)]
            private static extern bool ExitWindowsEx(uint uFlags, uint dwReason);

            const uint EWX_LOGOFF = 0x00000000;
            const uint EWX_SHUTDOWN = 0x00000001;
            const uint EWX_REBOOT = 0x00000002;
            const uint EWX_FORCE = 0x00000004;
            const uint EWX_FORCEIFHUNG = 0x00000010;

            public static bool LogOff()
            {
                return ExitWindowsEx(EWX_LOGOFF | EWX_FORCE, 0);
            }

            public static void Restart()
            {
                Process.Start(new ProcessStartInfo("shutdown", "/r /t 0")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
            }

            public static void Shutdown()
            {
                Process.Start(new ProcessStartInfo("shutdown", "/s /t 0")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
            }
        }
        public class Kill
        {
            private MainWindow main;
            private long totalsize = 0;
            private CancellationTokenSource cts = new CancellationTokenSource();

            public Kill(MainWindow main)
            {
                this.main = main;
            }

            public async Task ScandotsAsync(string text, CancellationTokenSource cancellationToken)
            {
                try
                {
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        await main.Dispatcher.InvokeAsync(() =>
                        {
                            main.label1_Copy.Text = text + ".";
                            main.label1_Copy.Foreground = MultronWinCleaner.Processes.ThemeColors.ReadableBrush("#1e88e5");
                        });
                        await Task.Delay(1000, cancellationToken.Token);

                        await main.Dispatcher.InvokeAsync(() => { main.label1_Copy.Text = text + ".."; });
                        await Task.Delay(1000, cancellationToken.Token);

                        await main.Dispatcher.InvokeAsync(() => { main.label1_Copy.Text = text + "..."; });
                        await Task.Delay(1000, cancellationToken.Token);
                    }
                }
                catch (TaskCanceledException) { }
            }
            public async Task run()
            {
                await main.Dispatcher.InvokeAsync(() =>
                {
                    main.ScrollViewerDirectories.Visibility = Visibility.Visible;
                    main.wrapPanelDirectories.Visibility = Visibility.Visible;
                    main.progressBar1.Minimum = 0;
                    main.progressBar1.Maximum = 100;
                    main.progressBar1.Value = 0;

                    var started = new TextBlock
                    {
                        Text = Loc.T("Started..."),
                        FontSize = 12,
                        FontWeight = FontWeights.SemiBold,
                        Margin = new Thickness(7, 2, 5, 8)
                    };
                    started.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
                    main.wrapPanelDirectories.Children.Add(started);
                });

                var dotsTask = ScandotsAsync(Loc.T("Killing"), cts);
                int progress = 0;
                int killed = 0;
                int deleted = 0;
                int reboot = 0;
                List<string> lockedPaths = await main.Dispatcher.InvokeAsync(() =>
                    main.paths.Select(p => main.stringtokenizer(p, "=", 0) ?? "").Where(p => p.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
                int total = Math.Max(1, lockedPaths.Count);

                async Task ReportProgress()
                {
                    int done = Interlocked.Increment(ref progress);
                    double percent = Math.Min(100, done * 100.0 / total);
                    await main.Dispatcher.InvokeAsync(() =>
                    {
                        main.progressBar1.Value = percent;
                        main.UpdateArc(percent);
                    });
                }

                async Task Deleted(string path, long size)
                {
                    Interlocked.Increment(ref deleted);
                    Interlocked.Add(ref totalsize, size);
                    await AddKillRow(KillLine.Deleted, Loc.F("Deleted: {0} ({1})", System.IO.Path.GetFileName(path), main.formatsize(size)), path);
                    await ReportProgress();
                }

                var remaining = new System.Collections.Concurrent.ConcurrentBag<(string Path, long Size, string Error, bool InUse)>();
                await Parallel.ForEachAsync(lockedPaths, new ParallelOptions { MaxDegreeOfParallelism = 8 }, async (path, _) =>
                {
                    if (main.cancelstatus.IsCancellationRequested) return;
                    if (!System.IO.File.Exists(path))
                    {
                        await AddKillRow(KillLine.Missing, Loc.F("File no longer exists: {0}", System.IO.Path.GetFileName(path)), path);
                        await ReportProgress();
                        return;
                    }
                    long size = 0;
                    string? error = TryDeleteLocked(path, ref size, out bool inUse);
                    if (error != null && MultronWinCleaner.Processes.ForceDelete.TryHardDelete(path))
                        error = null;
                    if (error == null)
                        await Deleted(path, size);
                    else
                        remaining.Add((path, size, error, inUse));
                });

                var killedPids = new HashSet<int>();
                foreach (var item in remaining.OrderBy(r => r.Path, StringComparer.OrdinalIgnoreCase))
                {
                    if (main.cancelstatus.IsCancellationRequested) break;

                    string path = item.Path;
                    long fileSize = item.Size;
                    string? error = item.Error;
                    bool inUse = item.InUse;

                    if (inUse)
                    {
                        bool closedAny = false;
                        foreach (var locker in WhoUsesFile.GetLockerDetails(path))
                        {
                            if (locker.IsService)
                            {
                                await AddKillRow(KillLine.Skipped, Loc.F("Skipped Windows service: {0} (PID {1}) for {2}", locker.Name, locker.Id, System.IO.Path.GetFileName(path)), path);
                                continue;
                            }
                            if (WhoUsesFile.IsProtectedProcess(locker.Id, locker.Name))
                            {
                                await AddKillRow(KillLine.Skipped, Loc.F("Skipped system process: {0} (PID {1}) for {2}", locker.Name, locker.Id, System.IO.Path.GetFileName(path)), path);
                                continue;
                            }
                            if (!killedPids.Add(locker.Id))
                                continue;

                            try
                            {
                                await AddKillRow(KillLine.Killed, Loc.F("Killing: {0} (PID {1}) for {2}", locker.Name, locker.Id, System.IO.Path.GetFileName(path)), path);
                                using (Process proc = Process.GetProcessById(locker.Id))
                                {
                                    proc.Kill(entireProcessTree: true);
                                    proc.WaitForExit(5000);
                                }
                                killed++;
                                closedAny = true;
                            }
                            catch (ArgumentException)
                            {
                                closedAny = true;
                            }
                            catch (Exception ex)
                            {
                                await AddKillRow(KillLine.Failed, Loc.F("Cannot Kill: {0} (PID {1}) - {2}", locker.Name, locker.Id, ex.Message), path);
                            }
                        }

                        for (int attempt = 0; attempt < 4 && error != null && (closedAny || attempt == 0); attempt++)
                        {
                            await Task.Delay(attempt == 0 ? 150 : 400);
                            error = TryDeleteLocked(path, ref fileSize, out inUse);
                            if (error != null && MultronWinCleaner.Processes.ForceDelete.TryHardDelete(path))
                                error = null;
                        }
                    }

                    if (error == null || !System.IO.File.Exists(path))
                    {
                        await Deleted(path, fileSize);
                        continue;
                    }

                    if (MultronWinCleaner.Processes.ForceDelete.ScheduleDeleteAtReboot(path))
                    {
                        reboot++;
                        await AddKillRow(KillLine.Restart, Loc.F("Will be deleted at the next restart: {0}", System.IO.Path.GetFileName(path)), path);
                    }
                    else
                    {
                        await AddKillRow(KillLine.Failed, Loc.F("Cannot Delete: {0} - {1}", System.IO.Path.GetFileName(path), error), path);
                    }
                    await ReportProgress();
                }

                await cts.CancelAsync();
                await dotsTask;

                await main.Dispatcher.InvokeAsync(() =>
                {
                    string message;

                    if (main.cancelstatus.IsCancellationRequested)
                    {
                        message = Loc.F("Killing Canceled. {0} Process Killed, {1} Files Deleted. Freed Space: {2}", killed, deleted, main.formatsize(totalsize));
                    }
                    else
                    {
                        message = Loc.F("Killing Done! {0} Process Killed, {1} Files Deleted. Freed Space: {2}", killed, deleted, main.formatsize(totalsize));
                    }
                    string cardMessage = message;
                    if (reboot > 0)
                        message += " " + Loc.F("{0} file(s) in use by Windows will be deleted at the next restart.", reboot);

                    main.wrapPanelDirectories.Children.Add(KillSummary(message, main.cancelstatus.IsCancellationRequested));
                    main.ShowMessageCard(cardMessage, main.cancelstatus.IsCancellationRequested, killLog.ToList(), reboot);
                    main.buttonReset.Visibility = Visibility.Visible;
                    main.buttonStartScan.Content = Loc.T("Scan");
                    main.buttonStartScan.IsEnabled = false;
                    main.paths.Clear();
                    main.ShowReadyStatus();
                });
            }

            internal enum KillLine { Deleted, Killed, Skipped, Failed, Restart, Missing }

            private readonly System.Collections.Concurrent.ConcurrentQueue<string> killLog = new();

            private static (string Glyph, string Brush) KillStyle(KillLine kind) => kind switch
            {
                KillLine.Deleted => ("\uE73E", "ResultSuccessText"),
                KillLine.Killed => ("\uE711", "ResultProblemText"),
                KillLine.Restart => ("\uE72C", "AccentText"),
                KillLine.Missing => ("\uE946", "TextSecondary"),
                _ => ("\uE7BA", "VerdictSuspiciousText")
            };

            private async Task AddKillRow(KillLine kind, string title, string? path)
            {
                killLog.Enqueue(kind + "\t" + title + "\t" + (path ?? ""));
                await main.Dispatcher.InvokeAsync(() => main.wrapPanelDirectories.Children.Add(KillRow(kind, title, path)));
            }

            internal static Border KillRow(KillLine kind, string title, string? path)
            {
                var (glyph, brush) = KillStyle(kind);
                var icon = new TextBlock
                {
                    Text = glyph,
                    FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                    FontSize = 13,
                    Margin = new Thickness(0, 2, 11, 0),
                    VerticalAlignment = VerticalAlignment.Top
                };
                icon.SetResourceReference(TextBlock.ForegroundProperty, brush);
                var heading = new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
                heading.SetResourceReference(TextBlock.ForegroundProperty, kind == KillLine.Deleted || kind == KillLine.Missing ? "TextPrimary" : brush);
                var text = new StackPanel();
                text.Children.Add(heading);
                string folder = string.IsNullOrEmpty(path) ? "" : System.IO.Path.GetDirectoryName(path) ?? "";
                if (folder.Length > 0)
                {
                    var location = new TextBlock { Text = folder, FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0) };
                    location.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
                    text.Children.Add(location);
                }
                var line = new DockPanel();
                DockPanel.SetDock(icon, Dock.Left);
                line.Children.Add(icon);
                line.Children.Add(text);
                return new Border
                {
                    Child = line,
                    ToolTip = string.IsNullOrEmpty(path) ? title : title + "\n" + path,
                    Padding = new Thickness(12, 8, 12, 8),
                    Margin = new Thickness(5, 2, 5, 2),
                    CornerRadius = new CornerRadius(8),
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x14, 0x80, 0x80, 0x80))
                };
            }

            internal static Border KillSummary(string message, bool canceled)
            {
                var color = canceled ? System.Windows.Media.Color.FromRgb(0xFB, 0x8C, 0x00) : System.Windows.Media.Color.FromRgb(0x2E, 0xB8, 0x72);
                var icon = new TextBlock
                {
                    Text = canceled ? "\uE711" : "\uE73E",
                    FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                    FontSize = 16,
                    Margin = new Thickness(0, 1, 12, 0),
                    VerticalAlignment = VerticalAlignment.Top
                };
                icon.SetResourceReference(TextBlock.ForegroundProperty, canceled ? "VerdictSuspiciousText" : "ResultSuccessText");
                var label = new TextBlock { Text = message, FontSize = 14, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
                label.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
                var line = new DockPanel();
                DockPanel.SetDock(icon, Dock.Left);
                line.Children.Add(icon);
                line.Children.Add(label);
                return new Border
                {
                    Child = line,
                    CornerRadius = new CornerRadius(10),
                    BorderThickness = new Thickness(1),
                    BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x66, color.R, color.G, color.B)),
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x1F, color.R, color.G, color.B)),
                    Padding = new Thickness(14, 11, 14, 11),
                    Margin = new Thickness(5, 12, 5, 6)
                };
            }

            private static string? TryDeleteLocked(string path, ref long fileSize, out bool inUse)
            {
                inUse = false;
                try
                {
                    FileInfo info = new FileInfo(path);
                    if (!info.Exists)
                        return null;
                    fileSize = info.Length;
                    if (info.IsReadOnly)
                        info.IsReadOnly = false;
                    info.Delete();
                    return null;
                }
                catch (IOException ex) when ((ex.HResult & 0xFFFF) == 0x20 || (ex.HResult & 0xFFFF) == 0x21)
                {
                    inUse = true;
                    return ex.Message;
                }
                catch (UnauthorizedAccessException)
                {
                    return Loc.T("Access denied. Only Windows can delete this file.");
                }
                catch (Exception ex)
                {
                    return ex.Message;
                }
            }
        }
        public ObservableCollection<MultronWinCleaner.Processes.Clean.LockedFileGroupViewModel> LockedFileGroups { get; } = new();
        public CollectionViewSource groupedProcesses = new CollectionViewSource();
        public List<MultronWinCleaner.Processes.Clean.LockedProcessViewModel> _allLockedFileGroups = new List<MultronWinCleaner.Processes.Clean.LockedProcessViewModel>();
        public ObservableCollection<MultronWinCleaner.Processes.Clean.LockedProcessViewModel> LockedProcesses = new ObservableCollection<MultronWinCleaner.Processes.Clean.LockedProcessViewModel>();
        public int _itemsLoaded = 0;
        public int _pageSize = 50;

        public async Task startscan()
        {
            if (buttonStartScan.Content.Equals(Loc.T("Clean")))
            {
                cancelstatus = new CancellationTokenSource();
                dismcancel = new CancellationTokenSource();
                scanstatus = 0;
                cancelclean = 0;
             

                buttonStartScan.Content = Loc.T("Cancel");
                HideResultCard();

                wrapPanelDirectories.Children.Clear();
                MultronWinCleaner.Processes.Clean clean = new MultronWinCleaner.Processes.Clean(this);
                MultronWinCleaner.Processes.ThemeColors.Refresh();
                await Task.Run(() => clean.run());
            }
            else if (buttonStartScan.Content.Equals(Loc.T("Scan")))
            {
                HideResultCard();
                HideStepResults();
                cancelstatus.Dispose();
                dismcancel.Dispose();
                cancelstatus = new CancellationTokenSource();
                dismcancel = new CancellationTokenSource();

                scanstatus = 0;
                cancelclean = 0;

                malwareScanQueued = malwareScanCheckBox?.IsChecked == true;
                mrtScanQueued = mrtScanCheckBox?.IsChecked == true && MultronWinCleaner.Processes.MrtScanner.IsAvailable;
                mrtStoppedByUser = false;
                ResetMalwareResultsPanel();

                MultronWinCleaner.Processes.Scan scan = new MultronWinCleaner.Processes.Scan(this);

                buttonStartScan.Content = Loc.T("Cancel");

                await Task.Run(() => scan.run());

            }
            else if (buttonStartScan.Content.Equals(Loc.T("Cancel")))
            {
                StopScanStep();
                CancelDuplicateReport();
                cancelstatus.Cancel();
                dismcancel.Cancel();
                cancelclean = 2;
                buttonReset.Visibility = Visibility.Visible;

                
            }
            else if (buttonStartScan.Content.Equals(Loc.T("Kill")))
            {
                if (paths.Count != 0)
                {
                    ForgetKillLogView();
                    killer = 1;
                    cancelstatus.Dispose();
                    dismcancel.Dispose();
                    cancelstatus = new CancellationTokenSource();
                    dismcancel = new CancellationTokenSource();
                    cancelclean = 0;
                    LockedFilesWindowOverlay.Visibility = Visibility.Collapsed;
                    wrapPanelDirectories.Children.Clear();
              
                    buttonStartScan.Content = Loc.T("Cancel");
                    Kill kill = new Kill(this);
                    await Task.Run(() => kill.run());
                }
                else
                {
                    label1_Copy.Text = Loc.T("No Process Selected.");
                    label1_Copy.SetResourceReference(TextBlock.ForegroundProperty, "WarningText");
                }
            }

       
            if (scanstatus == 1 || scanstatus == 2)
            {
                ScrollViewerDirectories.Visibility = Visibility.Hidden;
                wrapPanelDirectories.Visibility = Visibility.Hidden;
                wrapPanelDirectories.Children.Clear();
                buttonStartScan.IsEnabled = true;

                wrapPanel1.Visibility = Visibility.Visible;
                buttonStartScan.Content = Loc.T("Scan");

                
                 
                scanstatus = 0;
            }
            else if (cancelclean == 1)
            {
                ScrollViewerDirectories.Visibility = Visibility.Hidden;
                wrapPanelDirectories.Visibility = Visibility.Hidden;
                wrapPanelDirectories.Children.Clear();
                buttonStartScan.IsEnabled = true;

                wrapPanel1.Visibility = Visibility.Visible;
                cancelclean = 2;
                buttonStartScan.Content = Loc.T("Scan");
            }
        }
        private bool malwareScanQueued;
        private MultronWinCleaner.MalwareScan.ScanState? lastMalwareState;
        private string lastMalwareStatus = "";
        private int lastMalwareThreats;

        public async Task RunQueuedMalwareScanAsync()
        {
            bool runMrt = mrtScanQueued;
            mrtScanQueued = false;
            if (runMrt && !cancelstatus.IsCancellationRequested)
                await RunMrtScanAsync();

            if (!malwareScanQueued) return;
            malwareScanQueued = false;

            if (cancelstatus.IsCancellationRequested || mrtStoppedByUser || utilities == null) return;

            var mode = malwareScanModeBox != null && malwareScanModeBox.SelectedIndex >= 0
                ? (MultronWinCleaner.MalwareScan.ScanMode)malwareScanModeBox.SelectedIndex
                : MultronWinCleaner.MalwareScan.ScanMode.Quick;

            bool automatic = autoclean == 1 || startupscan == 1;
            lastMalwareState = null;
            try
            {
                await utilities.RunQueuedMalwareScanAsync(mode);
                if (IsSettingOnByDefault(NotifyMalwareSettingKey) && lastMalwareState != null)
                {
                    bool threatsFound = lastMalwareThreats > 0 || lastMalwareState == MultronWinCleaner.MalwareScan.ScanState.Threats;
                    if (threatsFound || (automatic && lastMalwareState == MultronWinCleaner.MalwareScan.ScanState.Clean))
                    {
                        var notify = new Notify(Loc.T(threatsFound ? ThreatsFoundTitle : "Malware scan finished."),
                            lastMalwareStatus, null, () => Dispatcher.Invoke(() => utilities?.ShowMalwareScan().ShowResults()));
                        notify.Styled(!threatsFound ? Notify.Kind.Success : OnlySuspiciousFound ? Notify.Kind.Warning : Notify.Kind.Danger, "\uEA18").Show();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Queued malware scan failed: " + ex.Message);
            }
            finally
            {
                if (Equals(malwareCancelRestore, "Clean"))
                    LeaveMalwareCancelMode();
            }
        }

        private bool lastMalwareOnlySuspicious;
        private int lastMalwareMalicious;
        private int lastMalwareSuspicious;

        private bool OnlySuspiciousFound => lastMalwareOnlySuspicious && mrtThreatCount == 0;

        public void SetMalwareOnlySuspicious(bool onlySuspicious) => lastMalwareOnlySuspicious = onlySuspicious;

        private string ThreatsFoundTitle => OnlySuspiciousFound
            ? "Malware scan finished: suspicious files found!"
            : "Malware scan finished: threats found!";

        public void FinishMalwareStep()
        {
            if (MalwareResultsPanel.Visibility != Visibility.Visible)
                return;
            bool threats = lastMalwareThreats > 0 || mrtThreatCount > 0;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                if (Datagridscroll.Visibility != Visibility.Visible)
                    return;
                if (!threats)
                    Datagridscroll.ScrollToTop();
                else if (Datagridscroll.VerticalOffset >= 1)
                {
                    MalwareTopNoticeTitle.Text = Loc.T(ThreatsFoundTitle);
                    var noticeColor = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(OnlySuspiciousFound ? "#FFB86200" : "#FFC62F3B"));
                    MalwareTopNotice.Background = noticeColor;
                    MalwareTopNoticeButton.Foreground = noticeColor;
                    string findings = OtherFindingsText();
                    MalwareTopNoticeDetail.Text = findings;
                    MalwareTopNoticeDetail.Visibility = findings.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
                    MalwareTopNotice.Visibility = Visibility.Visible;
                }
            }));
        }

        private string OtherFindingsText()
        {
            var parts = new List<string>();
            void Add(FrameworkElement panel, string title, Func<int> count)
            {
                if (panel.Visibility == Visibility.Visible && count() is int found && found > 0)
                    parts.Add($"{title}: {found}");
            }
            if (lastMalwareMalicious > 0 && lastMalwareSuspicious > 0)
            {
                parts.Add($"{Loc.T("Threats")}: {lastMalwareMalicious}");
                parts.Add($"{Loc.T("Suspicious")}: {lastMalwareSuspicious}");
            }
            Add(FirewallResultsPanel, Loc.T("Firewall Rules"), () => foundFirewallRules.Count);
            Add(ShortcutResultsPanel, Loc.T("Shortcuts"), () => foundShortcuts.Count);
            Add(SecurityResultsPanel, Loc.T("Security Settings"), () => foundSecurityIssues.Count);
            Add(DuplicateResultsPanel, Loc.T("Duplicate Files"), () => utilities?.GetDuplicateFinder()?.CopyCount ?? 0);
            Add(LargeFilesResultsPanel, Loc.T("Large Files"), () => largeFileReport.Count);
            return string.Join(" · ", parts);
        }

        private void MalwareTopNotice_Top_Click(object sender, RoutedEventArgs e)
        {
            MalwareTopNotice.Visibility = Visibility.Collapsed;
            Datagridscroll.ScrollToTop();
        }

        private void MalwareTopNotice_Close_Click(object sender, RoutedEventArgs e) => MalwareTopNotice.Visibility = Visibility.Collapsed;

        private void Datagridscroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (ReferenceEquals(e.OriginalSource, Datagridscroll) && Datagridscroll.VerticalOffset < 1)
                MalwareTopNotice.Visibility = Visibility.Collapsed;
        }

        private void Datagridscroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e) => NestedScroll_PreviewMouseWheel(sender, e);

        private void NestedScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Handled || e.Delta == 0 || sender is not ScrollViewer outer)
                return;
            bool up = e.Delta > 0;
            int lines = Math.Max(1, Math.Abs(e.Delta) / 40);
            DependencyObject? node = e.OriginalSource as DependencyObject;
            while (node != null && !ReferenceEquals(node, outer))
            {
                if (node is ScrollViewer inner && inner.ScrollableHeight > 0
                    && (up ? inner.VerticalOffset > 0 : inner.VerticalOffset < inner.ScrollableHeight))
                {
                    ScrollLines(inner, up, lines);
                    e.Handled = true;
                    return;
                }
                node = node is Visual || node is System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(node)
                    : LogicalTreeHelper.GetParent(node);
            }
            ScrollLines(outer, up, lines);
            e.Handled = true;
        }

        private static void ScrollLines(ScrollViewer viewer, bool up, int lines)
        {
            for (int i = 0; i < lines; i++)
            {
                if (up)
                    viewer.LineUp();
                else
                    viewer.LineDown();
            }
        }

        private void DataGridGroups_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
        {
            if (e.TargetObject is DataGridRow || e.TargetObject is DataGridCell || e.TargetObject is System.Windows.Controls.Primitives.DataGridDetailsPresenter)
                e.Handled = true;
        }

        private void Datagridscroll_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!Datagridscroll.IsVisible)
                MalwareTopNotice.Visibility = Visibility.Collapsed;
        }

        public void ResetMalwareResultsPanel()
        {
            MalwareTopNotice.Visibility = Visibility.Collapsed;
            lastMalwareState = null;
            lastMalwareThreats = 0;
            lastMalwareOnlySuspicious = false;
            lastMalwareMalicious = 0;
            lastMalwareSuspicious = 0;
            MalwareResultsPanel.Visibility = Visibility.Collapsed;
            MalwareResultsStatus.Text = "";
            UpdateTaskbarProgress();
            MalwareThreatsList.ItemsSource = null;
            MalwareThreatsList.Visibility = Visibility.Collapsed;
            mrtRows.Clear();
            cloudRows = new List<MalwareListRow>();
            mrtPanelStatus = null;
            cloudPanelStatus = null;
            mrtThreatCount = 0;
            lastMrtResult = null;
        }

        private string? lastMalwareNoticeStatus;

        public void UpdateMalwareResultsPanel(MultronWinCleaner.MalwareScan.ScanState state, string status,
            IEnumerable<MultronWinCleaner.Processes.CloudScanResult>? threats = null, int threatCount = 0)
        {
            var threatList = threats?.ToList();
            int threatsFound = Math.Max(threatCount, threatList?.Count ?? 0);
            if (threatList != null)
            {
                lastMalwareSuspicious = threatList.Count(t => t.Verdict == "suspicious");
                lastMalwareMalicious = threatList.Count - lastMalwareSuspicious;
                lastMalwareOnlySuspicious = threatList.Count > 0 && lastMalwareMalicious == 0;
            }
            lastMalwareState = state;
            SetStepBusy("Malware Scan", state == MultronWinCleaner.MalwareScan.ScanState.Scanning);
            lastMalwareStatus = status;
            lastMalwareThreats = threatsFound;
            string color = state switch
            {
                _ when threatsFound > 0 => OnlySuspiciousFound ? WarningColor : ProblemColor,
                MultronWinCleaner.MalwareScan.ScanState.Clean => OkColor,
                MultronWinCleaner.MalwareScan.ScanState.Threats => ProblemColor,
                MultronWinCleaner.MalwareScan.ScanState.Stopped or MultronWinCleaner.MalwareScan.ScanState.Failed => WarningColor,
                _ => InfoColor
            };
            bool scanning = state == MultronWinCleaner.MalwareScan.ScanState.Scanning;
            if (!scanning && !string.IsNullOrWhiteSpace(status) && status != lastMalwareNoticeStatus)
            {
                lastMalwareNoticeStatus = status;
                ShowChangeCard(Loc.T("Malware Scan"), status, threatsFound > 0 || state != MultronWinCleaner.MalwareScan.ScanState.Clean);
            }
            else if (scanning)
                lastMalwareNoticeStatus = null;
            cloudPanelStatus = status;
            cloudPanelColor = color;
            if (threatList != null || !scanning)
                cloudRows = (threatList ?? new List<MultronWinCleaner.Processes.CloudScanResult>())
                    .Select(t => new MalwareListRow(Loc.T("Multron Cloud"), t.VerdictText, t.FileName, t.ThreatName, t.FilePath, t.Verdict == "suspicious"))
                    .ToList();
            MalwareResultsButton.Content = Loc.T(state == MultronWinCleaner.MalwareScan.ScanState.Threats || threatsFound > 0 ? (OnlySuspiciousFound ? "Show Suspicious Files" : "Show Threats") : "Open Results");
            Dispatcher.BeginInvoke(new Action(UpdateTaskbarProgress), DispatcherPriority.Background);
            RefreshMalwarePanel(scanning);
            if (scanning)
            {
                bool starting = malwareCancelRestore == null;
                EnterMalwareCancelMode("Clean");
                if (Equals(malwareCancelRestore, "Clean"))
                {
                    if (starting)
                        progressBar1.Value = 0;
                    label1_Copy.Text = status == Loc.T("Stopping the malware scan...") ? status : Loc.F("Malware scan: {0}", status);
                    label1_Copy.Foreground = MultronWinCleaner.Processes.ThemeColors.ReadableBrush(InfoColor);
                }
            }
            else if (Equals(malwareCancelRestore, "Clean"))
                LeaveMalwareCancelMode();
            else
                RefreshShownToolStatus(Loc.T("Malware Scan"), status, threatsFound > 0);

            if (state != MultronWinCleaner.MalwareScan.ScanState.Scanning && Equals(buttonStartScan.Content, Loc.T("Clean")))
            {
                progressBar1.Value = 100;
                ShowReadyStatus();
            }
        }

        private void MalwareThreatsList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (MalwareThreatsList.Template.FindName("ThreatsScroll", MalwareThreatsList) is not ScrollViewer list)
                return;

            bool atTop = list.VerticalOffset <= 0;
            bool atBottom = list.VerticalOffset >= list.ScrollableHeight;
            if ((e.Delta > 0 && !atTop) || (e.Delta < 0 && !atBottom))
                return;

            e.Handled = true;
            var forwarded = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = UIElement.MouseWheelEvent, Source = sender };
            (VisualTreeHelper.GetParent(MalwareThreatsList) as UIElement)?.RaiseEvent(forwarded);
        }

        private void MalwareResultsButton_Click(object sender, RoutedEventArgs e)
        {
            utilities?.ShowMalwareScan().ShowResults();
        }

        public void SetMalwareScanMode(int index)
        {
            if (malwareScanModeBox != null && index >= 0 && malwareScanModeBox.SelectedIndex != index)
                malwareScanModeBox.SelectedIndex = index;
        }

        private void MalwarePanelModeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (malwareScanModeBox != null && MalwarePanelModeBox.SelectedIndex >= 0 && malwareScanModeBox.SelectedIndex != MalwarePanelModeBox.SelectedIndex)
                malwareScanModeBox.SelectedIndex = MalwarePanelModeBox.SelectedIndex;
        }

        private void MalwareCancelButton_Click(object sender, RoutedEventArgs e)
        {
            MalwareCancelButton.IsEnabled = false;
            MalwareResultsStatus.Text = Loc.T("Stopping the malware scan...");
            if (IsMrtScanning)
                StopMrtScan();
            else
                utilities?.malwarescan?.StopScan();
        }

        public void SetMalwareScanProgress(int done, int total)
        {
            if (total <= 0 || !Equals(malwareCancelRestore, "Clean"))
                return;
            progressBar1.Value = Math.Min(100, done * 100.0 / total);
            label1_Copy.Text = Loc.F("Malware scan: {0} / {1} files checked", done, total);
            label1_Copy.Foreground = MultronWinCleaner.Processes.ThemeColors.ReadableBrush(InfoColor);
        }

        public bool IsScanOrCleanBusy => Equals(buttonStartScan.Content, Loc.T("Cancel")) && !Equals(malwareCancelRestore, "Scan");

        public void CancelScanOrClean()
        {
            if (Equals(buttonStartScan.Content, Loc.T("Cancel")) && buttonStartScan.IsEnabled)
                ButtonStartScan_Click(buttonStartScan, new RoutedEventArgs());
        }

        private string? malwareCancelRestore;
        private bool malwareCancelWasEnabled;

        private void EnterMalwareCancelMode(string restore)
        {
            if (malwareCancelRestore != null || !Equals(buttonStartScan.Content, Loc.T(restore)))
                return;
            malwareCancelRestore = restore;
            malwareCancelWasEnabled = buttonStartScan.IsEnabled;
            buttonStartScan.Content = Loc.T("Cancel");
            buttonStartScan.IsEnabled = true;
            buttonStartScan.ToolTip = Loc.T("Stop the malware scan.");
        }

        private void LeaveMalwareCancelMode()
        {
            if (malwareCancelRestore == null)
                return;
            if (Equals(buttonStartScan.Content, Loc.T("Cancel")))
            {
                buttonStartScan.Content = Loc.T(malwareCancelRestore);
                buttonStartScan.IsEnabled = malwareCancelWasEnabled;
            }
            buttonStartScan.ToolTip = null;
            malwareCancelRestore = null;
        }

        private string? activeTool;
        private double toolSavedProgress;
        private string? mirrorTool;
        private TextBlock? mirrorSource;
        private System.Windows.Controls.ProgressBar? mirrorBar;

        private static readonly System.ComponentModel.DependencyPropertyDescriptor MirrorTextDescriptor =
            System.ComponentModel.DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock));
        private static readonly System.ComponentModel.DependencyPropertyDescriptor MirrorValueDescriptor =
            System.ComponentModel.DependencyPropertyDescriptor.FromProperty(System.Windows.Controls.Primitives.RangeBase.ValueProperty, typeof(System.Windows.Controls.ProgressBar));

        private void SetStatusText(string text, string color)
        {
            label1_Copy.Text = text;
            label1_Copy.Foreground = MultronWinCleaner.Processes.ThemeColors.ReadableBrush(color);
        }

        public void ShowToolStatus(string tool, string text, double? percent = null, bool warning = false)
        {
            if (IsScanOrCleanBusy || string.IsNullOrWhiteSpace(text))
                return;
            if (activeTool == null)
            {
                toolSavedProgress = progressBar1.Value;
                progressBar1.Value = 0;
            }
            activeTool = tool;
            UpdateTaskbarProgress();
            if (percent.HasValue)
                progressBar1.Value = Math.Clamp(percent.Value, 0, 100);
            SetStatusText(Loc.F("{0}: {1}", tool, text), warning ? WarningColor : InfoColor);
        }

        public void FinishToolStatus(string tool, string text, bool problem)
        {
            if (activeTool != tool)
                return;
            activeTool = null;
            UpdateTaskbarProgress();
            if (IsScanOrCleanBusy)
                return;
            progressBar1.Value = toolSavedProgress;
            if (!string.IsNullOrWhiteSpace(text))
                ShowChangeCard(tool, text, problem);
            ShowReadyStatus();
        }

        private void RefreshShownToolStatus(string tool, string text, bool problem)
        {
            if (HasNotice(tool) && !string.IsNullOrWhiteSpace(text))
                ShowChangeCard(tool, text, problem);
        }

        public void MirrorToolStatus(string tool, TextBlock source, System.Windows.Controls.ProgressBar? bar = null)
        {
            StopMirroring();
            mirrorTool = tool;
            mirrorSource = source;
            mirrorBar = bar;
            MirrorTextDescriptor.AddValueChanged(source, MirrorChanged);
            if (bar != null)
                MirrorValueDescriptor.AddValueChanged(bar, MirrorChanged);
            MirrorChanged(null, EventArgs.Empty);
        }

        public void EndMirrorToolStatus(TextBlock source, bool problem = false)
        {
            if (!ReferenceEquals(mirrorSource, source) || mirrorTool == null)
                return;
            string tool = mirrorTool;
            StopMirroring();
            FinishToolStatus(tool, source.Text, problem);
        }

        private void MirrorChanged(object? sender, EventArgs e)
        {
            if (mirrorSource == null || mirrorTool == null)
                return;
            double? percent = null;
            if (mirrorBar != null && !mirrorBar.IsIndeterminate && mirrorBar.Maximum > mirrorBar.Minimum)
                percent = (mirrorBar.Value - mirrorBar.Minimum) * 100 / (mirrorBar.Maximum - mirrorBar.Minimum);
            ShowToolStatus(mirrorTool, mirrorSource.Text, percent);
        }

        private void StopMirroring()
        {
            if (mirrorSource != null)
                MirrorTextDescriptor.RemoveValueChanged(mirrorSource, MirrorChanged);
            if (mirrorBar != null)
                MirrorValueDescriptor.RemoveValueChanged(mirrorBar, MirrorChanged);
            mirrorTool = null;
            mirrorSource = null;
            mirrorBar = null;
        }

        public void SetStandaloneMalwareScan(bool running)
        {
            if (running)
            {
                EnterMalwareCancelMode("Scan");
                ShowToolStatus(Loc.T("Malware Scan"), Loc.T("collecting files..."), 0);
            }
            else if (Equals(malwareCancelRestore, "Scan"))
            {
                LeaveMalwareCancelMode();
            }
        }

        public void SetStandaloneMalwareProgress(int done, int total, int threats, int suspicious = 0)
        {
            if (total <= 0 || activeTool != Loc.T("Malware Scan"))
                return;
            ShowToolStatus(Loc.T("Malware Scan"), suspicious > 0 && suspicious < threats ? Loc.F("{0:N0} / {1:N0} files checked, {2} threat(s) and {3} suspicious file(s) found", done, total, threats - suspicious, suspicious) : suspicious > 0 ? Loc.F("{0:N0} / {1:N0} files checked, {2} suspicious file(s) found", done, total, threats) : Loc.F("{0:N0} / {1:N0} files checked, {2} threat(s) found", done, total, threats),
                done * 100.0 / total, threats > 0);
        }

        public void SetStandaloneMalwareStatus(string text, bool problem) => FinishToolStatus(Loc.T("Malware Scan"), text, problem);

        public const string ReminderSettingKey = "cleanreminder";
        public const string ReminderDaysSettingKey = "cleanreminderdays";
        public const string ReminderRepeatSettingKey = "cleanreminderrepeat";
        public const string ReminderBackgroundSettingKey = "cleanreminderbackground";
        private const string LastMainActivitySettingKey = "lastmainactivity";
        private const string LastReminderSettingKey = "lastcleanreminder";
        private DispatcherTimer? cleanReminderTimer;

        private static DateTime? ReadTicks(string key) =>
            long.TryParse(ReadSettingValue(key), out long ticks) && ticks > 0 ? new DateTime(ticks, DateTimeKind.Utc) : null;

        private static int ReadDays(string key, int fallback) =>
            int.TryParse(ReadSettingValue(key), out int days) && days is >= 1 and <= 365 ? days : fallback;

        private void MarkMainActivity() => utilities?.savesettings($"{LastMainActivitySettingKey}:{DateTime.UtcNow.Ticks}");

        private void StartCleanReminder()
        {
            if (ReadTicks(LastMainActivitySettingKey) == null)
                MarkMainActivity();
            cleanReminderTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(2) };
            cleanReminderTimer.Tick += (s, e) =>
            {
                cleanReminderTimer.Interval = TimeSpan.FromHours(1);
                CheckCleanReminder();
            };
            cleanReminderTimer.Start();
        }

        private void CheckCleanReminder()
        {
            if (!IsSettingOnByDefault(ReminderSettingKey) || ReadTicks(LastMainActivitySettingKey) is not DateTime lastActivity)
                return;
            bool inBackground = !IsVisible || WindowState == WindowState.Minimized;
            if (IsSettingOnByDefault(ReminderBackgroundSettingKey) && !inBackground)
                return;
            int idleDays = (int)(DateTime.UtcNow - lastActivity).TotalDays;
            if (idleDays < ReadDays(ReminderDaysSettingKey, 30))
                return;
            if (ReadTicks(LastReminderSettingKey) is DateTime lastReminder && (DateTime.UtcNow - lastReminder).TotalDays < ReadDays(ReminderRepeatSettingKey, 7))
                return;
            utilities?.savesettings($"{LastReminderSettingKey}:{DateTime.UtcNow.Ticks}");
            new Notify(Loc.T("Time for a cleanup"), Loc.F("No scan or cleaning has been started for {0} days. Click to open Multron Win Cleaner.", idleDays), null, BringToFront)
                .Styled(Notify.Kind.Info, "\uE787").Show();
        }

        private async void ButtonStartScan_Click(object sender, RoutedEventArgs e)
        {
            MarkMainActivity();
            if (scanStepStop != null)
            {
                buttonStartScan.IsEnabled = false;
                StopScanStep();
                return;
            }
            if (duplicateCancelMode)
            {
                buttonStartScan.IsEnabled = false;
                duplicateCancelWasEnabled = true;
                label1_Copy.Text = Loc.T("Stopping the duplicate file search...");
                label1_Copy.Foreground = MultronWinCleaner.Processes.ThemeColors.ReadableBrush(WarningColor);
                CancelDuplicateReport();
                return;
            }
            if (malwareCancelRestore != null)
            {
                buttonStartScan.IsEnabled = false;
                if (Equals(malwareCancelRestore, "Clean"))
                    MalwareCancelButton.IsEnabled = false;
                label1_Copy.Text = Loc.T("Stopping the malware scan...");
                label1_Copy.Foreground = MultronWinCleaner.Processes.ThemeColors.ReadableBrush(WarningColor);
                if (IsMrtScanning)
                    StopMrtScan();
                else
                    utilities?.malwarescan?.StopScan();
                return;
            }

            bool allUnchecked = !Equals(buttonStartScan.Content, Loc.T("Cancel")) && checkboxes2.All(cb => cb.IsChecked != true);

            if (allUnchecked)
            {

                label1_Copy.Text = Loc.T("Nothing is selected!");
                label1_Copy.Foreground = MultronWinCleaner.Processes.ThemeColors.ReadableBrush("#1e88e5");
                return;
            }

            if (Equals(buttonStartScan.Content, Loc.T("Scan")))
            {
                buttonStartScan.IsEnabled = false;
                try
                {
                    await UpdateDatabaseIfDueAsync(true);
                }
                finally
                {
                    buttonStartScan.IsEnabled = true;
                }
            }

            await startscan();
        }

        private void OpenFileLocation_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem) return;
            if (menuItem.DataContext is not Scan.FileItem fileItem) return;

            string path = fileItem.Path;
            try
            {
                if (System.IO.File.Exists(path))
                    Process.Start("explorer.exe", $"/select,\"{path}\"");
                else
                {
                    string? dir = System.IO.Path.GetDirectoryName(path);
                    if (dir != null && Directory.Exists(dir))
                        Process.Start("explorer.exe", dir);
                    else
                        AppDialog.Show(Loc.F("Path not found:\n{0}", path), Loc.T("Open File Location"),
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                AppDialog.Show(Loc.F("Error:\n{0}", ex.Message), Loc.T("Error"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AddToExceptions_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.DataContext is Scan.FileItem fileItem)
            {
                settings.addexception(fileItem.Path);
                fileItem.IsChecked = false;
                label1_Copy.Text = Loc.F("Added to Exceptions: {0}", fileItem.Path);
            }
        }

        private void CopyPath_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem &&
                menuItem.DataContext is Scan.FileItem fileItem)
            {
                Clipboard.SetText(fileItem.Path);
            }
        }

        private void OpenDiscord_Click(object sender, RoutedEventArgs e)
        {
            string url = "https://discord.gg/xXmQw3MUAR";

            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        private void OpenSettings_Click(object sender, RoutedEventArgs e) => settings.BringToFront();

        private void UtilitiesButton_Click(object sender, RoutedEventArgs e) => utilities.BringToFront();

        private void TopBarGrid_SizeChanged(object sender, SizeChangedEventArgs e) => FitTopBar();

        private void FitTopBar()
        {
            UIElement[] optional = { TopBarThemeLabel, TopBarVersion, TopBarSettingsLabel, TopBarUtilitiesLabel };
            foreach (var element in optional)
            {
                element.Visibility = Visibility.Visible;
                InvalidateUpToTopBar(element);
            }
            foreach (var element in optional)
            {
                if (TopBarFits())
                    return;
                element.Visibility = Visibility.Collapsed;
                InvalidateUpToTopBar(element);
            }
        }

        private void InvalidateUpToTopBar(DependencyObject element)
        {
            for (var current = element; current != null && !ReferenceEquals(current, TopBarGrid); current = VisualTreeHelper.GetParent(current))
                (current as UIElement)?.InvalidateMeasure();
        }

        private bool TopBarFits()
        {
            var unlimited = new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity);
            TopBarLeft.Measure(unlimited);
            TopBarRight.Measure(unlimited);
            return TopBarLeft.DesiredSize.Width + TopBarRight.DesiredSize.Width + 16 <= TopBarGrid.ActualWidth;
        }

        private void TopBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                this.DragMove();
            }
        }

        public class LoadLockedFiles
        {
            MainWindow main;
            public LoadLockedFiles(MainWindow main)
            {
                this.main = main;
            }

            private sealed class LockedFileInfo
            {
                public string Path = "";
                public string GroupName = "";
                public string SizeText = "";
                public List<(string Id, string Name)> Processes = new List<(string Id, string Name)>();
            }

            private async Task<List<LockedFileInfo>> CheckWhoUsesMultipleAsync(List<string> entries)
            {
                await main.Dispatcher.InvokeAsync(() =>
                {
                    main.label1_Copy.Foreground = MultronWinCleaner.Processes.ThemeColors.ReadableBrush("#1e88e5");
                });

                var files = new Dictionary<string, (string Id, string Name, string Group)>(StringComparer.OrdinalIgnoreCase);
                foreach (string entry in entries)
                {
                    string path = main.stringtokenizer(entry, "=", 0) ?? "";
                    if (path.Length > 0 && !files.ContainsKey(path))
                        files[path] = (main.stringtokenizer(entry, "=", 1) ?? "", main.stringtokenizer(entry, "=", 2) ?? "", main.stringtokenizer(entry, "=", 3) ?? "");
                }

                var results = new ConcurrentBag<LockedFileInfo>();
                int total = files.Count;
                int processedCount = 0;
                var reportTimer = Stopwatch.StartNew();

                await Task.Run(() => Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = 8 }, file =>
                {
                    try
                    {
                        var info = new FileInfo(file.Key);
                        if (!info.Exists)
                            return;

                        var processes = WhoUsesFile.GetLockers(file.Key).Select(p => (p.Id.ToString(), p.Name)).ToList();
                        if (processes.Count == 0)
                            processes.Add((file.Value.Id, file.Value.Name));

                        results.Add(new LockedFileInfo
                        {
                            Path = file.Key,
                            GroupName = file.Value.Group,
                            SizeText = main.formatsize(info.Length),
                            Processes = processes
                        });
                    }
                    catch (Exception) { }
                    finally
                    {
                        int done = Interlocked.Increment(ref processedCount);
                        if (done == total || reportTimer.ElapsedMilliseconds > 150)
                        {
                            reportTimer.Restart();
                            main.Dispatcher.InvokeAsync(() => main.label1_Copy.Text = Loc.F("Loading locked files: {0}/{1}", done, total));
                        }
                    }
                }));

                return results.OrderBy(r => r.GroupName).ThenBy(r => r.Path).ToList();
            }

            public async Task run()
            {
                List<string> entries = await main.Dispatcher.InvokeAsync(() => main.paths.ToList());
                List<LockedFileInfo> lockedFiles = await CheckWhoUsesMultipleAsync(entries);

                var allItems = new List<LockedProcessViewModel>();
                var added = new HashSet<string>();
                foreach (var file in lockedFiles)
                {
                    foreach (var proc in file.Processes)
                    {
                        if (!added.Add($"{proc.Id}|{file.Path}"))
                            continue;

                        allItems.Add(new LockedProcessViewModel
                        {
                            FilePath = file.Path + " " + file.SizeText,
                            FilePathWithoutSize = file.Path,
                            GroupName = file.GroupName,
                            DisplayName = proc.Id == "0" ? Loc.T("No locking process found (delete will be retried)") : $"{proc.Name} (PID {proc.Id})",
                            Id = proc.Id,
                            IsChecked = true,
                            OnCheckedChanged = (model, state) =>
                            {
                                string key = model.FilePathWithoutSize + "=" + model.Id + "=" + model.DisplayName;
                                if (state)
                                {
                                    if (!main.paths.Contains(key))
                                        main.paths.Add(key);
                                }
                                else
                                {
                                    main.paths.RemoveAll(p =>
                                        string.Equals(main.stringtokenizer(p, "=", 0), model.FilePathWithoutSize, StringComparison.OrdinalIgnoreCase));
                                }
                            }
                        });
                    }
                }

                await main.Dispatcher.InvokeAsync(() =>
                {
                    main.LockedFileGroups.Clear();
                    main._allLockedFileGroups = allItems;
                    main.LockedProcesses = new ObservableCollection<LockedProcessViewModel>(allItems);
                    main._itemsLoaded = allItems.Count;

                    var cvs = new CollectionViewSource { Source = main.LockedProcesses };
                    cvs.GroupDescriptions.Add(new PropertyGroupDescription(nameof(LockedProcessViewModel.GroupName)));

                    var view = cvs.View;
                    string search = (main.SearchBox.Text ?? "").Trim().ToLower();

                    if (!string.IsNullOrWhiteSpace(search))
                    {
                        view.Filter = item =>
                        {
                            if (item is LockedProcessViewModel p)
                                return p.FilePathWithoutSize?.ToLower().Contains(search) == true ||
                                       p.GroupName?.ToLower().Contains(search) == true ||
                                       p.DisplayName?.ToLower().Contains(search) == true;
                            return false;
                        };
                    }

                    main.groupedProcesses = cvs;
                    main.listBoxProcesses.ItemsSource = view;

                    var stillLockedPaths = lockedFiles.Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    main.paths.RemoveAll(p => !stillLockedPaths.Contains(main.stringtokenizer(p, "=", 0) ?? ""));

                    main.label1_Copy.Text = Loc.F("Locked Files: {0} files, {1} processes", lockedFiles.Count, allItems.Count);
                });
            }
        }

        private async void ButtonLocked_Click(object sender, RoutedEventArgs e)
        {
            wrapPanelDirectories.Visibility = Visibility.Hidden;
            dataGridGroups.Visibility = Visibility.Hidden;
            ScrollViewerDetectedFiles.Visibility = Visibility.Hidden;
            Datagridscroll.Visibility = Visibility.Hidden;
            ScrollViewerDirectories.Visibility = Visibility.Hidden;
            wrapPanelDirectories.Visibility = Visibility.Hidden;
            wrapPanelDirectories.Children.Clear();
            viewModel.Groups.Clear();
            Datagridscroll.Visibility = Visibility.Hidden;
            dataGridGroups.Visibility = Visibility.Hidden;
            ScrollViewerDetectedFiles.Visibility = Visibility.Hidden;
            wrapPanel1.Visibility = Visibility.Hidden;
            ButtonLockedFiles.Visibility = Visibility.Collapsed;
            buttonStartScan.IsEnabled = true;
            buttonStartScan.Content = Loc.T("Kill");
            LockedFilesWindowOverlay.Visibility = Visibility.Visible;
            LoadLockedFiles lockedfiles = new LoadLockedFiles(this);
            await Task.Run(() => lockedfiles.run());

        }
        private void CloseLockedFiles_Click(object sender, RoutedEventArgs e)
        {
            LockedFilesWindowOverlay.Visibility = Visibility.Collapsed;
        }

        private void CloseLockedFiles_MouseDown(object sender, MouseButtonEventArgs e)
        {
            LockedFilesWindowOverlay.Visibility = Visibility.Collapsed;
        }

        private void LockedSelectAll_Click(object sender, RoutedEventArgs e) => SetLockedSelection(true);

        private void LockedUnselectAll_Click(object sender, RoutedEventArgs e) => SetLockedSelection(false);

        private void SetLockedSelection(bool selected)
        {
            foreach (var item in listBoxProcesses.Items.OfType<MultronWinCleaner.Processes.Clean.LockedProcessViewModel>().ToList())
                item.IsChecked = selected;
        }

        public void ShowRestartRequired(int files)
        {
            if (files <= 0)
            {
                RestartRequiredPanel.Visibility = Visibility.Collapsed;
                return;
            }
            RestartRequiredText.Text = Loc.F("{0} file(s) are in use by Windows and will be deleted when the PC restarts. Restart to finish cleaning.", files);
            RestartRequiredPanel.ToolTip = RestartRequiredText.Text;
            RestartRequiredPanel.Visibility = Visibility.Visible;
        }

        public void AskRestart() => RestartNow_Click(this, new RoutedEventArgs());

        public void AskRestartForThreats()
        {
            var answer = AppDialog.Show(this, Loc.T("Restart the PC now to finish removing the threats? Save your work in other programs first."),
                Loc.T("Restart required"), MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Yes)
                SystemActions.Restart();
        }

        private int malwareRestartFiles;

        public void ShowMalwareRestart(int files)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(() => ShowMalwareRestart(files));
                return;
            }
            malwareRestartFiles = files;
            MalwareRestartHost.Content = files > 0 ? RestartNotice(files, true) : null;
            MalwareRestartHost.Visibility = files > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void RestartNow_Click(object sender, RoutedEventArgs e)
        {
            var answer = AppDialog.Show(this, Loc.T("Restart the PC now to finish cleaning? Save your work in other programs first."),
                Loc.T("Restart required"), MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Yes)
                SystemActions.Restart();
        }

        private void ButtonReset_Click(object sender, RoutedEventArgs e)
        {
            foreach (var set in stepBusy.Values)
                set(false);
            RestartRequiredPanel.Visibility = Visibility.Collapsed;
            wrapPanelDirectories.Visibility = Visibility.Hidden;

            dataGridGroups.Visibility = Visibility.Hidden;
            ScrollViewerDetectedFiles.Visibility = Visibility.Hidden;
            Datagridscroll.Visibility = Visibility.Hidden;
            ScrollViewerDirectories.Visibility = Visibility.Hidden;
            wrapPanelDirectories.Visibility = Visibility.Hidden;
            wrapPanelDirectories.Children.Clear();
            viewModel.Groups.Clear();
            Datagridscroll.Visibility = Visibility.Hidden;
            dataGridGroups.Visibility = Visibility.Hidden;
            buttonReset.Visibility = Visibility.Collapsed;
            ScrollViewerDetectedFiles.Visibility = Visibility.Visible;
            wrapPanel1.Visibility = Visibility.Visible;
            ButtonLockedFiles.Visibility = Visibility.Collapsed;
            buttonStartScan.Content = Loc.T("Scan");
            buttonStartScan.IsEnabled = true;

            progressBar1.Value = 0;
            startupscan = 0;
            startupclean = 0;
            autoclean = 0;
            cancelclean = 0;
            scanstatus = 0;
            reset = 0;

            HideResultCard();
            HideStepResults();
            if (malwareCancelRestore == null)
                ResetMalwareResultsPanel();
            label1_Copy.Text = Loc.T("Ready to scan");
            label1_Copy.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
            label1_Copy.Visibility = Visibility.Visible;
        }

        private void ForgetKillLogView()
        {
            if (killLogRestore is { } back)
                back.Button.Content = Loc.T("Show details");
            killLogRestore = null;
        }

        private void HideResultCard()
        {
            ForgetKillLogView();
            ResultCard.Visibility = Visibility.Collapsed;
            ScanLivePanel.Visibility = Visibility.Collapsed;
            label1_Copy.Visibility = Visibility.Visible;
        }

        private void HideStepResults()
        {
            DuplicateResultsPanel.Visibility = Visibility.Collapsed;
            LargeFilesResultsPanel.Visibility = Visibility.Collapsed;
            FirewallResultsPanel.Visibility = Visibility.Collapsed;
            ShortcutResultsPanel.Visibility = Visibility.Collapsed;
            SecurityResultsPanel.Visibility = Visibility.Collapsed;
            HideSystemChecks();
        }
        int backuped = 0;

        private void ResultsSearchMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded && ResultsFilterBox != null && ResultsFilterBox.Text.Trim().Length > 0)
                TextBox_TextChanged(ResultsFilterBox, null!);
        }

        private async void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is not TextBox textBox) return;
            string searchText = textBox.Text.Trim();

            if (backuped == 0)
            {
                viewModelbac.Groups.Clear();
                foreach (var group in viewModel.Groups)
                    viewModelbac.Groups.Add(group);

                backuped = 1;
            }

            if (string.IsNullOrWhiteSpace(searchText))
            {
                viewModel.Groups.Clear();
                foreach (var group in viewModelbac.Groups)
                {
                    group.ApplyFileFilter(null);
                    viewModel.Groups.Add(group);
                }

                backuped = 0;
                return;
            }

            var source = viewModelbac.Groups.ToList();
            int searchMode = ResultsSearchMode?.SelectedIndex ?? 0;
            bool exact = (ResultsMatchMode?.SelectedIndex ?? 0) == 1;
            bool inFolders = searchMode != 2;
            bool inFiles = searchMode != 1;
            var filteredList = await Task.Run(() =>
                source
                    .Select(g => (Group: g, PathMatch: inFolders && GroupViewModel.FolderMatches(g.Path, searchText, exact)))
                    .Where(x => x.PathMatch || (inFiles && x.Group.allFiles.Any(f => GroupViewModel.FileMatches(f, searchText, exact))))
                    .ToList());

            Dispatcher.Invoke(() =>
            {
                if (textBox.Text.Trim() != searchText || (ResultsSearchMode?.SelectedIndex ?? 0) != searchMode || ((ResultsMatchMode?.SelectedIndex ?? 0) == 1) != exact)
                    return;
                viewModel.Groups.Clear();
                foreach (var group in source)
                    group.ApplyFileFilter(null);
                foreach (var (group, pathMatch) in filteredList)
                {
                    group.ApplyFileFilter(pathMatch ? null : searchText, exact);
                    viewModel.Groups.Add(group);
                }
            });
        }

        public const string ThemeFollowsWindowsSettingKey = "themefollowwindows";
        public static bool ThemeFollowsWindows => IsSettingOnByDefault(ThemeFollowsWindowsSettingKey);
        private bool themeSwitchUpdating;
        private bool currentThemeDark;

        public static bool WindowsUsesDarkTheme()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
            }
            catch
            {
                return false;
            }
        }

        public void SetThemeFollowsWindows(bool follow)
        {
            utilities?.savesettings(ThemeFollowsWindowsSettingKey + (follow ? ":1" : ":0"));
            if (follow)
                ApplyWindowsTheme();
        }

        private void SystemEvents_UserPreferenceChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
        {
            if (e.Category == Microsoft.Win32.UserPreferenceCategory.General)
                Dispatcher.BeginInvoke(() => { if (ThemeFollowsWindows) ApplyWindowsTheme(); });
        }

        private void ApplyWindowsTheme()
        {
            bool dark = WindowsUsesDarkTheme();
            if (dark == currentThemeDark)
                return;
            SetThemeSwitch(dark);
            ApplyTheme(dark);
        }

        private void SetThemeSwitch(bool dark)
        {
            themeSwitchUpdating = true;
            try { ToggleThemeSwitch.IsChecked = dark; }
            finally { themeSwitchUpdating = false; }
        }

        private void ApplyTheme(bool dark)
        {
            currentThemeDark = dark;
            string themePath = dark ? "Themes/Dark.xaml" : "Themes/Light.xaml";
            var resourceDictionary = new ResourceDictionary
            {
                Source = new Uri(themePath, UriKind.Relative)
            };
            brush = (SolidColorBrush)resourceDictionary["Text"];

            ThemeSelector.selector(new Uri(themePath, UriKind.Relative));

            foreach (CheckBox box in checkboxes2)
                box.Foreground = brush;

            foreach (Expander er in expanders)
                er.Foreground = brush;
        }

        private void ToggleThemeSwitch_Checked(object sender, RoutedEventArgs e) => OnThemeSwitchChanged(true);

        private void ToggleThemeSwitch_Unchecked(object sender, RoutedEventArgs e) => OnThemeSwitchChanged(false);

        private void OnThemeSwitchChanged(bool dark)
        {
            if (themeSwitchUpdating)
                return;

            utilities?.savesettings("themes:" + (dark ? "1" : "0"));
            utilities?.savesettings(ThemeFollowsWindowsSettingKey + ":0");
            settings?.SetThemeFollowsWindowsBox(false);
            ApplyTheme(dark);
        }

        private ObservableCollection<MultronWinCleaner.Processes.Clean.LockedFileGroupViewModel> _backupLockedFileGroups = new ObservableCollection<MultronWinCleaner.Processes.Clean.LockedFileGroupViewModel>();
        private async void ProcessBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string searchText = (sender as TextBox)?.Text?.Trim().ToLower() ?? "";

            await UpdateProcessViewModel(searchText);
        }
        private async void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            string searchText = (sender as TextBox)?.Text?.Trim().ToLower() ?? "";

            await UpdateListViewAsync(searchText);
        }
        private async Task UpdateProcessViewModel(string searchText)
        {

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (string.IsNullOrWhiteSpace(searchText))
                {
                    groupedProcesses.View.Filter = null;

                }
                else
                {

                    groupedProcesses.View.Filter = item =>
                    {
                        if (item is MultronWinCleaner.Processes.Clean.LockedProcessViewModel proc)
                        {
                            return proc.DisplayName?.ToLower().Contains(searchText) == true;
                        }
                        return false;
                    };
                }
                listBoxProcesses.ItemsSource = groupedProcesses.View;
            });
        }
        private async Task UpdateListViewAsync(string searchText)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (string.IsNullOrWhiteSpace(searchText))
                {
                    groupedProcesses.View.Filter = null;
                }
                else
                {
                    string search = searchText.ToLower();
                    groupedProcesses.View.Filter = item =>
                    {
                        if (item is MultronWinCleaner.Processes.Clean.LockedProcessViewModel proc)
                        {
                            return proc.FilePathWithoutSize?.ToLower().Contains(search) == true ||
                                   proc.GroupName?.ToLower().Contains(search) == true ||
                                   proc.DisplayName?.ToLower().Contains(search) == true;
                        }
                        return false;
                    };
                }

                groupedProcesses.View.Refresh();
                listBoxProcesses.ItemsSource = groupedProcesses.View;
            });
        }
        private bool _isLoadingMore = false;

        private async void MainScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (e.VerticalOffset + e.ViewportHeight >= e.ExtentHeight - 10)
            {
                if (!_isLoadingMore)
                {
                    _isLoadingMore = true;
                    await LoadMoreItemsAsync();
                    _isLoadingMore = false;
                }
            }
        }

        #region Cleaning list search

        private DispatcherTimer? librarySearchTimer;
        private readonly HashSet<Expander> searchExpanded = new HashSet<Expander>();

        private void LibrarySearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            bool empty = LibrarySearchBox.Text.Length == 0;
            LibrarySearchHint.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            LibrarySearchClear.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
            if (librarySearchTimer == null)
            {
                librarySearchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
                librarySearchTimer.Tick += (s, args) =>
                {
                    librarySearchTimer.Stop();
                    ApplyLibrarySearch(LibrarySearchBox.Text.Trim());
                };
            }
            librarySearchTimer.Stop();
            librarySearchTimer.Start();
        }

        private void LibrarySearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
                LibrarySearchBox.Clear();
        }

        private void LibrarySearchClear_Click(object sender, RoutedEventArgs e)
        {
            LibrarySearchBox.Clear();
            LibrarySearchBox.Focus();
        }

        private void ApplyLibrarySearch(string query) => ApplyLibrarySearch(query, wrapPanel1);

        private void ApplyLibrarySearch(string query, Panel root)
        {
            foreach (var expander in searchExpanded)
                expander.IsExpanded = false;
            searchExpanded.Clear();

            foreach (var expander in ChildExpanders(root).ToList())
                FilterNode(expander, query);
        }

        private bool FilterNode(Expander expander, string query)
        {
            var children = expander.Content is DependencyObject content ? ChildExpanders(content).ToList() : new List<Expander>();
            if (children.Count == 0)
                return FilterGroup(expander, query);

            bool any = false;
            foreach (var child in children)
                any |= FilterNode(child, query);
            expander.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
            if (any && query.Length > 0 && !expander.IsExpanded)
            {
                expander.IsExpanded = true;
                searchExpanded.Add(expander);
            }
            return any;
        }

        private static IEnumerable<Expander> ChildExpanders(DependencyObject parent)
        {
            IEnumerable<object> children = parent switch
            {
                Panel panel => panel.Children.Cast<object>(),
                Decorator decorator when decorator.Child != null => new object[] { decorator.Child },
                ContentControl control when control is not Expander && control.Content != null => new[] { control.Content },
                _ => Array.Empty<object>()
            };
            foreach (var child in children)
            {
                if (child is Expander expander)
                    yield return expander;
                else if (child is DependencyObject inner)
                    foreach (var nested in ChildExpanders(inner))
                        yield return nested;
            }
        }

        private bool FilterGroup(Expander group, string query)
        {
            var items = GroupCheckBoxes(group).ToList();
            if (query.Length == 0)
            {
                group.Visibility = Visibility.Visible;
                foreach (var box in items)
                    box.Visibility = Visibility.Visible;
                return true;
            }

            string? headerText = group.Header as string ?? group.Tag as string;
            bool headerMatch = SearchMatches(headerText, query);
            bool anyItem = false;
            foreach (var box in items)
            {
                string text = box.Tag as string ?? box.Content?.ToString() ?? "";
                bool match = SearchMatches(text, query) || SearchMatches(Environment.ExpandEnvironmentVariables(text), query);
                box.Visibility = headerMatch || match ? Visibility.Visible : Visibility.Collapsed;
                anyItem |= match;
            }

            bool show = headerMatch || anyItem;
            group.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (anyItem && !group.IsExpanded)
            {
                group.IsExpanded = true;
                searchExpanded.Add(group);
            }
            return show;
        }

        private IEnumerable<CheckBox> GroupCheckBoxes(Expander group)
        {
            if (group.Content is ListBox list)
            {
                var shown = list.Items.OfType<CheckBox>().ToList();
                foreach (var box in shown)
                    yield return box;
                if (!string.IsNullOrEmpty(group.Name))
                    foreach (var box in checkboxes2.Where(c => c.Name == group.Name && !shown.Contains(c)))
                        yield return box;
            }
            else if (group.Content is Panel panel)
            {
                foreach (var box in panel.Children.OfType<CheckBox>())
                    yield return box;
            }
        }

        private static bool SearchMatches(string? text, string query) =>
            text != null && text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

        #endregion

        private void ToolbarMenu_Click(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is CheckBox)
                return;
            SelectionsMenuToggle.IsChecked = false;
            DatabaseMenuToggle.IsChecked = false;
        }

        private bool extraDatabasesChanged;

        private void DatabasePopup_Opened(object sender, EventArgs e) => FillExtraDatabases();

        private async void DatabasePopup_Closed(object sender, EventArgs e)
        {
            if (!extraDatabasesChanged)
                return;
            extraDatabasesChanged = false;
            await ReloadAfterDatabaseChangeAsync();
        }

        private void FillExtraDatabases()
        {
            ExtraDatabasesPanel.Children.Clear();
            var databases = ExtraDatabases.List();
            NoExtraDatabasesText.Visibility = databases.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            foreach (var database in databases)
            {
                var row = new Grid { Margin = new Thickness(10, 2, 4, 2) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var label = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 210 };
                label.Inlines.Add(new System.Windows.Documents.Run(database.Name));
                if (database.Url != null)
                    label.Inlines.Add(new System.Windows.Documents.Run("  \uE774") { FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"), FontSize = 11 });
                var box = new CheckBox
                {
                    Content = label,
                    IsChecked = database.Enabled,
                    FontSize = 13.5,
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = database.Url ?? database.FilePath
                };
                string fileName = database.FileName;
                box.Checked += (s, e) => { ExtraDatabases.SetEnabled(fileName, true); extraDatabasesChanged = true; };
                box.Unchecked += (s, e) => { ExtraDatabases.SetEnabled(fileName, false); extraDatabasesChanged = true; };
                row.Children.Add(box);

                var remove = new Button
                {
                    Content = "\uE74D",
                    Style = (Style)FindResource("ChromeCloseButtonStyle"),
                    Width = 28,
                    Height = 26,
                    FontSize = 11,
                    ToolTip = Loc.T("Remove this database")
                };
                remove.Click += async (s, e) =>
                {
                    DatabaseMenuToggle.IsChecked = false;
                    if (AppDialog.Show(this, Loc.F("Remove the database {0}?\n\nIts file is deleted from the Databases folder.", database.Name),
                            Loc.T("Additional databases"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                        return;
                    try
                    {
                        ExtraDatabases.Remove(fileName);
                    }
                    catch (Exception ex)
                    {
                        AppDialog.Show(this, Loc.F("The database could not be removed: {0}", ex.Message), Loc.T("Additional databases"), MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    await ReloadAfterDatabaseChangeAsync();
                };
                Grid.SetColumn(remove, 1);
                row.Children.Add(remove);
                ExtraDatabasesPanel.Children.Add(row);
            }
        }

        private async Task ReloadAfterDatabaseChangeAsync()
        {
            if (!IsIdleForDatabaseReload())
            {
                databaseReloadPending = true;
                ShowDatabaseStatus(Loc.T("The database list changed. It is loaded after the current scan or clean."), WarningColor, always: true);
                return;
            }
            await ReloadDatabaseAsync(true);
            databaseReloadPending = false;
            UpdateTrayWarning();
        }

        private async void AddDatabaseUrl_Click(object sender, RoutedEventArgs e)
        {
            string? url = AppDialog.Prompt(this,
                Loc.T("Internet address (https://) of a database .txt file in the Multron Win Cleaner format.") + "\n\n" +
                Loc.T("Only add databases from sources you trust: a database decides which files and folders are deleted. Items from additional databases are never cleaned by automatic or startup cleaning, and they cannot run programs."),
                Loc.T("Add a database"), 500);
            if (string.IsNullOrWhiteSpace(url))
                return;
            ShowDatabaseStatus(Loc.T("Downloading the database..."), InfoColor, always: true);
            try
            {
                var database = await ExtraDatabases.AddFromUrlAsync(url, CancellationToken.None);
                ShowDatabaseStatus(Loc.F("The database {0} was added.", database.Name), OkColor, always: true);
            }
            catch (Exception ex)
            {
                ShowDatabaseStatus(Loc.F("Could not add the database: {0}", ex.Message), WarningColor, always: true);
                return;
            }
            await ReloadAfterDatabaseChangeAsync();
        }

        private async void AddDatabaseFile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = Loc.T("Choose a database"),
                Filter = Loc.T("Database") + " (*.txt)|*.txt",
                Multiselect = true
            };
            if (dialog.ShowDialog(this) != true)
                return;

            var added = new List<string>();
            var failed = new List<string>();
            foreach (string file in dialog.FileNames)
            {
                try
                {
                    added.Add(ExtraDatabases.AddFromFile(file).Name);
                }
                catch (Exception ex)
                {
                    failed.Add(System.IO.Path.GetFileName(file) + ": " + ex.Message);
                }
            }
            if (failed.Count > 0)
                AppDialog.Show(this, Loc.F("Could not add the database: {0}", string.Join("\n", failed)), Loc.T("Additional databases"), MessageBoxButton.OK, MessageBoxImage.Warning);
            if (added.Count == 0)
                return;
            ShowDatabaseStatus(Loc.F("The database {0} was added.", string.Join(", ", added)), OkColor, always: true);
            await ReloadAfterDatabaseChangeAsync();
        }

        private static bool ConfirmSelectAll(object sender) =>
            AppDialog.Show(Window.GetWindow((DependencyObject)sender),
                Loc.T("Selecting all items across all groups may cause critical like browser history & downloads and recent files to be permanently deleted during cleanup.\n\nAre you sure you want to select everything?"),
                Loc.T("Warning"), MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

        private void SelectAll_wpanel_Click(object sender, RoutedEventArgs e)
        {
            if (!ConfirmSelectAll(sender))
                return;

            foreach (CheckBox box in checkboxes2)
                box.IsChecked = true;
        }

        private void SelectAll_CleanSelections_Click(object sender, RoutedEventArgs e)
        {
            if (!ConfirmSelectAll(sender))
                return;

            foreach (CheckBox box in cleaningEntryBoxes.ToList())
                box.IsChecked = true;
        }

        private void UnselectAll_CleanSelections_Click(object sender, RoutedEventArgs e)
        {
            foreach (CheckBox box in cleaningEntryBoxes.ToList())
                box.IsChecked = false;
        }

        private void UnselectAll_wpanel_Click(object sender, RoutedEventArgs e)
        {
            foreach (CheckBox box in checkboxes2.ToList())
                box.IsChecked = false;
            if (optionsSectionPanel == null)
                return;
            foreach (var card in optionsSectionPanel.Children.OfType<Expander>().ToList())
                foreach (CheckBox box in OptionCheckBoxes(card.Content).Concat(OptionCheckBoxes(card.Header)).ToList())
                    box.IsChecked = false;
        }
        private void SelectAll_AllGroups_Click(object sender, RoutedEventArgs e)
        {
            ScheduleFoundFilesUpdate();
           
       

            foreach (var item in dataGridGroups.Items)
            {
                if (item is GroupViewModel group)
                {
                    foreach (var file in group.allFiles)
                    {
                        file.IsChecked = true;
                    }

                    var sp = stackpanels.FirstOrDefault(s => s.DataContext == group);
                    if (sp != null)
                    {
                        foreach (var child in sp.Children)
                        {
                            if (child is CheckBox cb)
                            {
                                cb.IsChecked = true;
                            }
                        }
                    }
                }
            }
        }
        #region Custom Dialog Infrastructure

        public enum CustomDialogIcon
        {
            Info,
            Warning,
            Error,
            Question
        }

        public enum CustomDialogButtons
        {
            Ok,
            YesNo
        }

        public enum CustomDialogResult
        {
            None,
            Ok,
            Yes,
            No
        }

        private TaskCompletionSource<CustomDialogResult>? _dialogTcs;

        public Task<CustomDialogResult> ShowCustomDialogAsync(
            string title,
            string message,
            CustomDialogIcon icon = CustomDialogIcon.Info,
            CustomDialogButtons buttons = CustomDialogButtons.Ok)
        {
            _dialogTcs = new TaskCompletionSource<CustomDialogResult>();

            Dispatcher.Invoke(() =>
            {
                DialogTitleText.Text = title;
                DialogMessageText.Text = message;
                 
                (string glyph, string hex) = icon switch
                {
                    CustomDialogIcon.Warning => ("", "#FF9800"),
                    CustomDialogIcon.Error => ("", "#DC3545"),
                    CustomDialogIcon.Question => ("", "#1E88E5"),
                    _ => ("", "#1E88E5")
                };
                var iconColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
                DialogIconText.Text = glyph;
                DialogIconText.Foreground = ThemeColors.ReadableBrush(iconColor);
                DialogIconBorder.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0x20, iconColor.R, iconColor.G, iconColor.B));

                if (buttons == CustomDialogButtons.Ok)
                {
                    DialogOkButton.Visibility = Visibility.Visible;
                    DialogYesButton.Visibility = Visibility.Collapsed;
                    DialogNoButton.Visibility = Visibility.Collapsed;
                }
                else if (buttons == CustomDialogButtons.YesNo)
                {
                    DialogOkButton.Visibility = Visibility.Collapsed;
                    DialogYesButton.Visibility = Visibility.Visible;
                    DialogNoButton.Visibility = Visibility.Visible;
                }

                CustomDialogOverlay.Visibility = Visibility.Visible;
            });

            return _dialogTcs.Task;
        }

        private void DialogOkButton_Click(object sender, RoutedEventArgs e)
        {
            CustomDialogOverlay.Visibility = Visibility.Collapsed;
            _dialogTcs?.TrySetResult(CustomDialogResult.Ok);
        }

        private void DialogYesButton_Click(object sender, RoutedEventArgs e)
        {
            CustomDialogOverlay.Visibility = Visibility.Collapsed;
            _dialogTcs?.TrySetResult(CustomDialogResult.Yes);
        }

        private void DialogNoButton_Click(object sender, RoutedEventArgs e)
        {
            CustomDialogOverlay.Visibility = Visibility.Collapsed;
            _dialogTcs?.TrySetResult(CustomDialogResult.No);
        }

        #endregion

        private void UnselectAll_AllGroups_Click(object sender, RoutedEventArgs e)
        {
            ScheduleFoundFilesUpdate();
            foreach (var item in dataGridGroups.Items)
            {
                if (item is GroupViewModel group)
                {
                    foreach (var file in group.allFiles)
                    {
                        file.IsChecked = false;
                    }

                    var sp = stackpanels.FirstOrDefault(s => s.DataContext == group);
                    if (sp != null)
                    {
                        foreach (var child in sp.Children)
                        {
                            if (child is CheckBox cb)
                            {
                                cb.IsChecked = false;
                            }
                        }
                    }
                }
            }
        }
        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            ScheduleFoundFilesUpdate();
            if (sender is Button button)
            {
                if (button.Tag is GroupViewModel groupVm)
                {
                    foreach (var file in groupVm.allFiles.Concat(groupVm.Files.Except(groupVm.Files)))
                    {
                        file.IsChecked = true;
                    }
                }
                else if (button.Tag is PathGroup group)
                {
                    foreach (var file in group.Files)
                    {
                        file.IsChecked = true;
                    }
                }
            }
        }

        private void ResetSelections_Click(object sender, RoutedEventArgs e)
        {
            if (!ReloadDb.IsEnabled || !buttonStartScan.IsEnabled)
            {
                AppDialog.Show(Loc.T("Please wait until the current scan or clean has finished."), Loc.T("Reset Selections"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var answer = AppDialog.Show(Loc.T("Reset all your selections to the database defaults?\n\nThis deletes selections.txt, including your cleanmgr, Dism.exe, SFC and Deep Log Files Scan choices."),
                Loc.T("Reset Selections"), MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes)
                return;

            selectionsAutoSaveTimer?.Stop();
            lock (pendingSelections)
            {
                pendingSelections.Clear();
            }
            lock (selectionsFileLock)
            {
                try
                {
                    if (System.IO.File.Exists(MultronWinCleaner.Processes.Load.SelectionsPath))
                        System.IO.File.Delete(MultronWinCleaner.Processes.Load.SelectionsPath);
                }
                catch (Exception ex)
                {
                    AppDialog.Show(Loc.F("Could not reset the selections:\n{0}", ex.Message), Loc.T("Reset Selections"), MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }
            ReloadDatabase_Click(ReloadDb, new RoutedEventArgs());
        }

        private async void ReloadDatabase_Click(object sender, RoutedEventArgs e)
        {
            await ReloadDatabaseAsync(false);
            databaseReloadPending = false;
            UpdateTrayWarning();
        }

        private bool databaseUpdateRunning;
        private bool databaseReloadPending;
        private string? downloadedDatabaseVersion;
        private DispatcherTimer? databaseUpdateTimer;

        private void StartDatabaseUpdateTimer()
        {
            if (databaseUpdateTimer != null) return;
            databaseUpdateTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(10) };
            databaseUpdateTimer.Tick += async (s, e) =>
            {
                await UpdateDatabaseIfDueAsync(false);
                await CheckAppUpdateIfDueAsync();
            };
            databaseUpdateTimer.Start();
        }

        internal void SetTrayResult(MultronWinCleaner.TrayBadge badge, string? tip = null)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => SetTrayResult(badge, tip)));
                return;
            }
            if (IsActive && IsVisible)
                return;
            var current = _trayAnimator?.Badge;
            if (current == MultronWinCleaner.TrayBadge.Danger && badge != MultronWinCleaner.TrayBadge.Danger)
                return;
            _trayAnimator?.SetBadge(badge);
            if (!string.IsNullOrWhiteSpace(tip))
                trayResultTip = tip;
            UpdateTrayWarning();
        }

        private string? trayResultTip;

        private void ClearTrayResult()
        {
            if (_trayAnimator?.Badge == null && trayResultTip == null)
                return;
            _trayAnimator?.SetBadge(null);
            trayResultTip = null;
            UpdateTrayWarning();
        }

        private void UpdateTrayWarning()
        {
            var warnings = new List<string>();
            if (availableAppUpdate != null)
                warnings.Add(Loc.F("Multron Win Cleaner {0} is available. Click to install the update.", availableAppUpdate.Version));
            if (databaseReloadPending)
                warnings.Add(Loc.T("A new database was downloaded. Click Reload Database to load it."));
            bool offline = IsOfflineModeEnabled();
            if (offline || !MultronWinCleaner.Processes.Updater.IsAutoUpdateEnabled())
            {
                TimeSpan? age = MultronWinCleaner.Processes.Updater.GetDatabaseAge();
                if (age != null && age.Value >= StaleDatabaseAge(offline))
                    warnings.Add(Loc.F(offline
                        ? "The database has not been updated for {0} (offline mode is on)."
                        : "The database has not been updated for {0} (automatic database updates are off).", FormatAge(age.Value)));
            }

            try
            {
                _trayAnimator?.SetWarning(warnings.Count > 0);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Tray warning icon failed: " + ex.Message);
            }
            string tip = "Multron Win Cleaner" + (trayResultTip != null ? "\n" + trayResultTip : "") + (warnings.Count > 0 ? "\n⚠ " + string.Join("\n⚠ ", warnings) : "");
            TrayIcon.ToolTipText = tip.Length > 127 ? tip.Substring(0, 124) + "..." : tip;
        }

        private MultronWinCleaner.Processes.Updater.AppUpdate? availableAppUpdate;
        private bool appUpdateRunning;

        private async Task CheckAppUpdateIfDueAsync()
        {
            if (appUpdateRunning || availableAppUpdate != null || IsOfflineModeEnabled()
                || !MultronWinCleaner.Processes.Updater.IsAppAutoUpdateEnabled() || !MultronWinCleaner.Processes.Updater.IsAppUpdateCheckDue())
                return;

            appUpdateRunning = true;
            try
            {
                var update = await Task.Run(MultronWinCleaner.Processes.Updater.FindAppUpdateAsync);
                if (update == null)
                    return;

                availableAppUpdate = update;
                string text = Loc.F("Multron Win Cleaner {0} is available. Click to install the update.", update.Version);
                if (IsIdleForDatabaseReload())
                    ShowDatabaseStatus(text, InfoColor, always: true);
                new Notify(Loc.T("Update Available"), text, "", () => _ = InstallAppUpdateAsync()).Styled(Notify.Kind.Info, "\uE896").Show();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("App update check failed: " + ex.Message);
            }
            finally
            {
                appUpdateRunning = false;
                UpdateTrayWarning();
            }
        }

        private async Task InstallAppUpdateAsync()
        {
            var update = availableAppUpdate;
            if (update == null)
                return;
            if (!IsIdleForDatabaseReload())
            {
                AppDialog.Show(Loc.T("Please wait until the current scan or clean has finished."), Loc.T("Update Available"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var answer = AppDialog.Show(Loc.F("Install Multron Win Cleaner {0} now? The app closes and starts again during the update.", update.Version),
                Loc.T("Update Available"), MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes)
                return;

            await InstallAppUpdateNowAsync(update, this);
        }

        public bool IsAppUpdateBusy => appUpdateRunning;

        public async Task<bool> InstallAppUpdateNowAsync(MultronWinCleaner.Processes.Updater.AppUpdate update, Window owner, IProgress<int>? extraProgress = null)
        {
            if (appUpdateRunning)
                return false;
            appUpdateRunning = true;
            var progress = new Progress<int>(percent =>
            {
                ShowDatabaseStatus(Loc.F("Downloading Update {0}%", percent), InfoColor, always: true);
                extraProgress?.Report(percent);
            });
            try
            {
                await Task.Run(() => MultronWinCleaner.Processes.Updater.InstallAppUpdateAsync(update, progress));
                ShowDatabaseStatus(Loc.T("Restarting to finish the update..."), InfoColor, always: true);
                MultronWinCleaner.Processes.Updater.RestartAfterUpdate();
                ExitApplication();
                return true;
            }
            catch (Exception ex)
            {
                ShowDatabaseStatus(Loc.F("Could not install the update: {0}", MultronWinCleaner.Processes.Updater.DescribeError(ex)), WarningColor, always: true);
                AppDialog.Show(owner, Loc.F("Could not install the update: {0}", MultronWinCleaner.Processes.Updater.DescribeError(ex)), Loc.T("Update Available"), MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            finally
            {
                appUpdateRunning = false;
            }
        }

        public bool IsIdleForDatabaseReload() =>
            ReloadDb.IsEnabled && buttonStartScan.IsEnabled && Equals(buttonStartScan.Content, Loc.T("Scan")) && autoclean == 0
            && LockedFilesWindowOverlay.Visibility != Visibility.Visible && utilities?.malwarescan?.IsScanning != true && !IsMrtScanning;

        private void ShowDatabaseStatus(string text, string color, bool always = false)
        {
            if (!always && !MultronWinCleaner.Processes.Updater.IsStatusEnabled()) return;
            label1_Copy.Text = text;
            label1_Copy.Foreground = MultronWinCleaner.Processes.ThemeColors.ReadableBrush(color);
        }

        private static TimeSpan StaleDatabaseAge(bool offline) =>
            offline ? TimeSpan.FromDays(1) : TimeSpan.FromHours(MultronWinCleaner.Processes.Updater.GetUpdateHours());

        private static string FormatAge(TimeSpan age) =>
            age.TotalDays >= 1 ? Loc.F("{0} days", (int)age.TotalDays) : Loc.F("{0} hours", Math.Max(1, (int)age.TotalHours));

        private void ShowStaleDatabaseWarning()
        {
            bool offline = IsOfflineModeEnabled();
            if (!offline && MultronWinCleaner.Processes.Updater.IsAutoUpdateEnabled()) return;
            TimeSpan? age = MultronWinCleaner.Processes.Updater.GetDatabaseAge();
            if (age == null || age.Value < StaleDatabaseAge(offline)) return;
            if (!IsIdleForDatabaseReload()) return;
            ShowDatabaseStatus(Loc.F(offline
                ? "The database has not been updated for {0} (offline mode is on)."
                : "The database has not been updated for {0} (automatic database updates are off).", FormatAge(age.Value)), WarningColor, always: true);
        }

        public void ApplyDatabaseUpdateSettings()
        {
            UpdateDb.Visibility = MultronWinCleaner.Processes.Updater.IsButtonEnabled() ? Visibility.Visible : Visibility.Collapsed;
            ShowStaleDatabaseWarning();
            UpdateTrayWarning();
        }

        private async void UpdateDatabase_Click(object sender, RoutedEventArgs e)
        {
            if (IsOfflineModeEnabled())
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(MultronWinCleaner.Processes.Updater.DatabaseReleasesUrl) { UseShellExecute = true });
                return;
            }
            if (!IsIdleForDatabaseReload())
            {
                ShowDatabaseStatus(Loc.T("Please wait until the current scan or clean has finished."), WarningColor, always: true);
                return;
            }

            UpdateDb.IsEnabled = false;
            try
            {
                await UpdateDatabaseIfDueAsync(beforeScan: true, force: true);
            }
            finally
            {
                UpdateDb.IsEnabled = true;
            }
        }

        private async Task UpdateDatabaseIfDueAsync(bool beforeScan, bool force = false)
        {
            if (databaseUpdateRunning) return;
            databaseUpdateRunning = true;
            try
            {
                if (!IsOfflineModeEnabled() && (force || (MultronWinCleaner.Processes.Updater.IsAutoUpdateEnabled() && MultronWinCleaner.Processes.Updater.IsUpdateCheckDue())))
                {
                    bool showProgress = beforeScan || IsIdleForDatabaseReload();
                    if (showProgress)
                        ShowDatabaseStatus(Loc.T("Checking for latest database"), InfoColor, force);
                    var progress = new Progress<int>(percent =>
                    {
                        if (beforeScan || IsIdleForDatabaseReload())
                            ShowDatabaseStatus(Loc.F("Downloading Latest Database {0}%", percent), InfoColor, force);
                    });
                    string? version = await Task.Run(() => MultronWinCleaner.Processes.Updater.UpdateDatabaseAsync(progress));
                    if (version != null)
                    {
                        databaseReloadPending = true;
                        downloadedDatabaseVersion = version;
                    }
                    else if (showProgress)
                    {
                        ShowDatabaseStatus(Loc.T("The database is up to date."), OkColor, force);
                    }
                    var (extraChanged, extraErrors) = await ExtraDatabases.RefreshFromUrlsAsync(CancellationToken.None);
                    if (extraChanged > 0)
                        databaseReloadPending = true;
                    if (extraErrors.Count > 0 && showProgress)
                        ShowDatabaseStatus(Loc.F("Could not update {0} additional database(s): {1}", extraErrors.Count, string.Join("; ", extraErrors)), WarningColor, force);
                }

                if (databaseReloadPending)
                {
                    if (beforeScan || (MultronWinCleaner.Processes.Updater.IsAutoReloadEnabled() && IsIdleForDatabaseReload()))
                    {
                        ShowDatabaseStatus(Loc.T("Loading the new database..."), InfoColor, force);
                        await ReloadDatabaseAsync(true);
                        databaseReloadPending = false;
                        ShowDatabaseStatus(downloadedDatabaseVersion != null
                            ? Loc.F("The database was updated to version {0}.", downloadedDatabaseVersion)
                            : Loc.T("The additional databases were updated."), OkColor, force);
                        downloadedDatabaseVersion = null;
                    }
                    else if (IsIdleForDatabaseReload())
                    {
                        ShowDatabaseStatus(Loc.T("A new database was downloaded. Click Reload Database to load it."), WarningColor, force);
                    }
                }
                else if (!beforeScan)
                {
                    ShowStaleDatabaseWarning();
                }
            }
            catch (Exception ex)
            {
                if (beforeScan || IsIdleForDatabaseReload())
                    ShowDatabaseStatus(Loc.F("Could not update the database: {0}", MultronWinCleaner.Processes.Updater.DescribeError(ex)), WarningColor, force);
            }
            finally
            {
                databaseUpdateRunning = false;
                UpdateTrayWarning();
            }
        }

        private async Task ReloadDatabaseAsync(bool keepChecks)
        {
            Dictionary<string, bool>? checks = null;
            if (keepChecks)
            {
                checks = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                foreach (CheckBox box in checkboxes2)
                {
                    string? key = box.Content?.ToString();
                    if (!string.IsNullOrEmpty(key)) checks[key] = box.IsChecked == true;
                }
            }

            ReloadDb.IsEnabled = false;
            selectionsAutoSaveTimer?.Stop();
            await Task.Run(FlushPendingSelections);
            wrapPanel1.Children.Clear();
            CategoryPanel.Children.Clear();
            wrapPanelDirectories.Children.Clear();
            if (!ReferenceEquals(ScrollViewerDirectories.Content, wrapPanelDirectories))
                ScrollViewerDirectories.Content = wrapPanelDirectories;
            checkboxes2.Clear();
            cleaningEntryBoxes.Clear();
            listboxes.Clear();
            expanders.Clear();
            optionStatusRefresh.Clear();
            stackpanels.Clear();
            comboboxlist.Clear();
            databaseDefaults.Clear();
            database.Clear();
          
            if (System.IO.File.Exists(System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) + "\\database.txt") == true)
            {

                wrapPanelDirectories.Visibility = Visibility.Hidden;
                ScrollViewerDirectories.Visibility = Visibility.Hidden;

                var load = new MultronWinCleaner.Processes.Load(this);
                await Task.Run(() => load.RunAsync());
                ScheduleCleanSelectionsCount();
                ReloadDb.IsEnabled = true;

                if (checks != null)
                {
                    foreach (CheckBox box in checkboxes2)
                    {
                        string? key = box.Content?.ToString();
                        if (!string.IsNullOrEmpty(key) && checks.TryGetValue(key, out bool isChecked) && box.IsChecked != isChecked)
                            box.IsChecked = isChecked;
                    }
                }
            }
            else
            {
                AppDialog.Show(Loc.T("Any database file not found, Program closing..."), Loc.T("Multron Windows Cleaner"), MessageBoxButton.OK, MessageBoxImage.Error);
                Environment.Exit(0);
            }
        }
        public async Task savetodatabase(bool showMessage = true)
        {
            var checkBoxStates = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            await Dispatcher.InvokeAsync(() =>
            {
                checkBoxStates.Clear();
                foreach (CheckBox box in checkboxes2)
                {
                    string? path = stringtokenizer(box.Content?.ToString() ?? "", "=", 1);
                    if (path != null) checkBoxStates[path] = box.IsChecked == true;
                }
            });

            await Task.Run(() =>
            {
                lock (selectionsFileLock)
                {
                    Dictionary<string, bool> selections = MultronWinCleaner.Processes.Load.ReadSelections();

                    foreach (var kvp in checkBoxStates)
                    {
                        if (!databaseDefaults.TryGetValue(kvp.Key, out bool databaseDefault)) continue;

                        if (kvp.Value == databaseDefault) selections.Remove(kvp.Key);
                        else selections[kvp.Key] = kvp.Value;
                    }

                    MultronWinCleaner.Processes.Load.WriteSelections(selections);
                }
            });

            await Dispatcher.InvokeAsync(() =>
            {
                if (showMessage) AppDialog.Show(Loc.T("Selections saved successfully."), Loc.T("Success"), MessageBoxButton.OK, MessageBoxImage.Information);
                SaveSettings.IsEnabled = true;
                SaveSettings.Content = Loc.T("Save Selections");
            });
        }
        private async void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            SaveSettings.IsEnabled = false;
            SaveSettings.Content = Loc.T("Saving...");
            await savetodatabase();
        }

        private async void ApplySelectionsToDatabase_Click(object sender, RoutedEventArgs e)
        {
            MessageBoxResult answer = AppDialog.Show(
                Loc.T("This writes your saved selections into database.txt.") + "\n\n" +
                Loc.T("Selections written into database.txt are lost when a newer database is downloaded.") + " " +
                Loc.T("Selections kept in selections.txt are not.") + "\n\n" +
                Loc.T("Continue?"),
                Loc.T("Multron Windows Cleaner"), MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;

            ApplySelections.IsEnabled = false;
            try
            {
                SaveSettings.IsEnabled = false;
                await savetodatabase(showMessage: false);

                int applied = await Task.Run(() =>
                {
                    Dictionary<string, bool> selections = MultronWinCleaner.Processes.Load.ReadSelections();
                    if (selections.Count == 0) return 0;

                    string filePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "database.txt");
                    string[] lines = System.IO.File.ReadAllLines(filePath);
                    var appliedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    string? profileRoot = null;
                    List<string>? profileFolders = null;

                    for (int i = 0; i < lines.Length; i++)
                    {
                        string line = lines[i];
                        if (line.StartsWith("{") || line.StartsWith("}"))
                        {
                            profileRoot = null;
                            profileFolders = null;
                            continue;
                        }
                        if (line.StartsWith("#profile#="))
                        {
                            profileRoot = (stringtokenizer(line, "=", 1) ?? "").Replace("{##}", Environment.UserName);
                            profileFolders = Directory.Exists(profileRoot)
                                ? Directory.GetDirectories(profileRoot).Select(d => System.IO.Path.GetFileName(d)).ToList()
                                : new List<string>();
                            continue;
                        }

                        string? path = null;
                        if (line.Contains("#profileget#"))
                        {
                            if (profileRoot == null) continue;
                            string? subPath = stringtokenizer(line, "=", 2);
                            path = (profileFolders ?? new List<string>()).Select(folder => profileRoot + folder + subPath).FirstOrDefault(selections.ContainsKey);
                        }
                        else
                        {
                            string? linePath = stringtokenizer(line.Replace("{##}", Environment.UserName), "=", 1);
                            if (linePath != null && selections.ContainsKey(linePath)) path = linePath;
                        }
                        if (path == null) continue;

                        lines[i] = selections[path] ? line.Replace("=false", "=true") : line.Replace("=true", "=false");
                        appliedPaths.Add(path);
                    }

                    string tempPath = filePath + ".tmp";
                    System.IO.File.WriteAllLines(tempPath, lines);
                    System.IO.File.Move(tempPath, filePath, overwrite: true);

                    foreach (string path in appliedPaths) selections.Remove(path);
                    MultronWinCleaner.Processes.Load.WriteSelections(selections);
                    return appliedPaths.Count;
                });

                AppDialog.Show(Loc.F("{0} selections were written to database.txt.", applied), Loc.T("Multron Windows Cleaner"), MessageBoxButton.OK, MessageBoxImage.Information);
                if (applied > 0) ReloadDatabase_Click(sender, e);
            }
            catch (Exception ex)
            {
                AppDialog.Show(Loc.F("Could not write the selections to database.txt: {0}", ex.Message), Loc.T("Multron Windows Cleaner"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ApplySelections.IsEnabled = true;
            }
        }

        private void UnselectAll_Click(object sender, RoutedEventArgs e)
        {
            ScheduleFoundFilesUpdate();
            if (sender is Button button)
            {
                if (button.Tag is GroupViewModel groupVm)
                {
                    foreach (var file in groupVm.allFiles.Concat(groupVm.Files.Except(groupVm.Files)))
                    {
                        file.IsChecked = false;
                    }
                }
                else if (button.Tag is PathGroup group)
                {
                    foreach (var file in group.Files)
                    {
                        file.IsChecked = false;
                    }
                }
            }
        }
        private async Task LoadMoreItemsAsync()
        {

            var toAdd = _allLockedFileGroups.Skip(_itemsLoaded).Take(_pageSize).ToList();

            if (toAdd.Count == 0)
                return;

            await this.Dispatcher.InvokeAsync(() =>
            {
                foreach (var proc in toAdd)
                    LockedProcesses.Add(proc);
            });

            _itemsLoaded += toAdd.Count;
        }
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
 
                 base.OnClosing(e);
        }
        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (sessionEnding || System.Threading.Volatile.Read(ref exiting) == 1)
            {
                ExitApplication();
                return;
            }
            e.Cancel = true;
            Dispatcher.BeginInvoke(new Action(HandleCloseRequest));
        }
    }
}
