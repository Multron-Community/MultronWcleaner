using Microsoft.Win32;
using Multron_Win_Cleaner;
using MultronWinCleaner;
using MultronWinCleaner.Processes;
using NetFwTypeLib;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace MultronWinCleaner
{
    public partial class Utilities : System.Windows.Window
    {
        List<MultronWinCleaner.Processes.FirewallRules.InvalidRule> invalidrules = new List<MultronWinCleaner.Processes.FirewallRules.InvalidRule>();
        public LargeFileFinder? largefilefinder;
        public MainWindow window;
        public MemCleaner? memcleaner;
        public StartupManager? startupmanager;
        public Duplicate_File_Finder? Duplicate_File_Finder;
        public Configure configure;
        public MalwareScan? malwarescan;

        [DllImport("kernel32.dll")]
        private static extern bool SetProcessWorkingSetSize(IntPtr procHandle, int min, int max);

        public bool isSettingsLoaded = false;

        private static readonly Dictionary<string, string> CardGlyphs = new Dictionary<string, string>
        {
            ["Firewall Rule Cleaner"] = "",
            ["Large File Finder"] = "",
            ["Duplicate File Finder"] = "",
            ["System Optimization"] = "",
            ["Memory Cleaner"] = "",
            ["Startup Manager"] = "",
            ["Malware Scan"] = "",
            ["Shortcut Fixer"] = "",
            ["Security Check"] = "",
        };

        private static readonly Dictionary<string, string> ShortCardTexts = new Dictionary<string, string>
        {
            ["Finds Windows settings that make your PC less secure, for example UAC, Windows Firewall or Defender turned off, AutoPlay on for USB drives, SMB 1.0, Remote Desktop without protection, blocked Task Manager or backdoors on the sign-in screen. Tick the settings to fix; the current values are saved first, so every fix can be undone."]
                = "Finds Windows settings that make your PC less secure and fixes the ones you choose. Every fix can be undone.",
            ["Sends executables from running processes, startup items or selected folders to the cloud with the viruskov.com static engine over an encrypted connection and checks them for malware."]
                = "Checks the programs on your PC for malware with a secure cloud scan.",
            ["Finds Desktop and Start Menu shortcuts whose program no longer exists. Shortcuts to programs that moved to a new version folder are repaired, the others are moved to the Recycle Bin."]
                = "Finds shortcuts to programs that no longer exist and repairs or removes them.",
        };

        private static IEnumerable<TextBlock> CardTextBlocks(DependencyObject root)
        {
            foreach (object child in LogicalTreeHelper.GetChildren(root))
            {
                if (child is TextBlock text)
                    yield return text;
                if (child is DependencyObject inner)
                    foreach (TextBlock nested in CardTextBlocks(inner))
                        yield return nested;
            }
        }

        private void DecorateCards()
        {
            var accent = System.Windows.Media.Color.FromRgb(0x42, 0xA5, 0xF5);
            var texts = CardTextBlocks(this).ToList();
            foreach (TextBlock title in texts.Where(t => t.FontSize == 16 && t.FontWeight == FontWeights.SemiBold))
            {
                string? key = CardGlyphs.Keys.FirstOrDefault(k => Loc.T(k) == title.Text);
                if (key == null)
                    continue;
                FrameworkElement icon = MultronWinCleaner.Processes.ToolIcons.Tile(MultronWinCleaner.Processes.ToolIcons.Tools.GetValueOrDefault(key), 36) ?? new Border
                {
                    Width = 36,
                    Height = 36,
                    CornerRadius = new CornerRadius(10),
                    Margin = new Thickness(0, 0, 12, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x26, accent.R, accent.G, accent.B)),
                    Child = new TextBlock
                    {
                        Text = CardGlyphs[key],
                        FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                        FontSize = 17,
                        FontWeight = FontWeights.Normal,
                        Foreground = MultronWinCleaner.Processes.ThemeColors.ReadableBrush(accent),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                };
                icon.Margin = new Thickness(0, 0, 12, 0);
                if (title.Parent is System.Windows.Controls.Grid header)
                {
                    icon.Margin = new Thickness(0, 0, 8, 0);
                    System.Windows.Controls.Grid.SetColumn(icon, 0);
                    header.Children.Add(icon);
                }
                else if (title.Parent is System.Windows.Controls.StackPanel { Orientation: System.Windows.Controls.Orientation.Horizontal } row)
                {
                    row.Children.Insert(0, icon);
                }
                else if (title.Parent is System.Windows.Controls.Panel parent)
                {
                    int index = parent.Children.IndexOf(title);
                    parent.Children.RemoveAt(index);
                    var line = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = title.Margin };
                    title.Margin = new Thickness(0);
                    title.VerticalAlignment = VerticalAlignment.Center;
                    line.Children.Add(icon);
                    line.Children.Add(title);
                    parent.Children.Insert(index, line);
                }
            }
            foreach (TextBlock text in texts)
            {
                foreach (var pair in ShortCardTexts)
                {
                    if (text.Text == Loc.T(pair.Key))
                    {
                        text.ToolTip = new System.Windows.Controls.ToolTip { Content = new TextBlock { Text = text.Text, TextWrapping = TextWrapping.Wrap, MaxWidth = 380 } };
                        text.Text = Loc.T(pair.Value);
                        break;
                    }
                }
            }
        }

        public Utilities(MainWindow window)
        {
            InitializeComponent();
            DecorateCards();
            this.window = window;
            memcleaner = new MemCleaner(window);
            configure = new Configure(this);
            startupmanager = new StartupManager(this);

            WatchToolProgress(FirewallProgress, "Firewall Rule Cleaner", () => (FirewallScanResultLabel.Content?.ToString() ?? "", FirewallScanResultLabel.Foreground));
            WatchToolProgress(ShortcutProgress, "Shortcut Fixer", () => (ShortcutResultLabel.Content?.ToString() ?? "", ShortcutResultLabel.Foreground));
            WatchToolProgress(SecurityProgress, "Security Check", () => (SecurityResultLabel.Content?.ToString() ?? "", SecurityResultLabel.Foreground));

            var availabilityTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            SecurityUndo.Visibility = MultronWinCleaner.Processes.SecurityCheck.HasBackup ? Visibility.Visible : Visibility.Collapsed;
            availabilityTimer.Tick += (s, e) => UpdateToolAvailability();
            IsVisibleChanged += (s, e) =>
            {
                if (IsVisible)
                {
                    UpdateToolAvailability();
                    availabilityTimer.Start();
                }
                else
                {
                    availabilityTimer.Stop();
                }
            };
        }

        private bool firewallRunning;
        private bool shortcutRunning;
        private bool securityRunning;
        private object? securitySavedContent;

        private object? firewallSavedContent;
        private object? shortcutSavedContent;

        private void UpdateToolAvailability()
        {
            bool busy = window?.IsScanOrCleanBusy == true;
            if (!firewallRunning)
                SetMainCancelMode(FirewallScan, ref firewallSavedContent, busy);
            if (!shortcutRunning)
                SetMainCancelMode(ShortcutScan, ref shortcutSavedContent, busy);
            if (!securityRunning)
                SetMainCancelMode(SecurityScan, ref securitySavedContent, busy);
        }

        private static void SetMainCancelMode(System.Windows.Controls.Button button, ref object? saved, bool busy)
        {
            if (busy)
            {
                if (!Equals(button.Content, Loc.T("Cancel")))
                    saved = button.Content;
                button.Content = Loc.T("Cancel");
                button.IsEnabled = true;
                button.ToolTip = Loc.T("Cancel the scan or clean running on the main screen.");
            }
            else if (saved != null)
            {
                button.Content = saved;
                saved = null;
                button.IsEnabled = true;
                button.ToolTip = null;
            }
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
         

            LoadSettings();
            UpdateMainWindowButton();
            _ = ReapplyOptimizationAtStartupAsync();
            StartStatusTask();

            if (chkTrayIconUtil.IsChecked == true)
                SetupTrayIcon();

            if (memcleaner?.chkStartWithWinCleaner.IsChecked == true)
            {
                memcleaner.Show();
                memcleaner.Hide();
            }
            startupmanager?.Show();
            startupmanager?.Hide();
        }

 

        public void StartStatusTask()
        {
        

            _ = Task.Run(async () =>
            {
                try
                {
                    while (true)
                    {
                        await Dispatcher.InvokeAsync(() =>
                        {
                            if (!IsVisible) return;
                            try
                            {
                                bool isLargeFile = IsToolRunning(largefilefinder);
                                lblStatusLargeFile.Visibility = isLargeFile ? Visibility.Visible : Visibility.Collapsed;
                                btnCloseLargeFile.Visibility = isLargeFile ? Visibility.Visible : Visibility.Collapsed;

                                bool isDuplicate = IsToolRunning(Duplicate_File_Finder);
                                lblStatusDuplicate.Visibility = isDuplicate ? Visibility.Visible : Visibility.Collapsed;
                                btnCloseDuplicate.Visibility = isDuplicate ? Visibility.Visible : Visibility.Collapsed;

                                bool isMem = IsToolRunning(memcleaner);
                                lblStatusMemCleaner.Visibility = isMem ? Visibility.Visible : Visibility.Collapsed;
                                btnCloseMemCleaner.Visibility = isMem ? Visibility.Visible : Visibility.Collapsed;

                                bool isStartup = IsToolRunning(startupmanager);
                                lblStatusStartup.Visibility = isStartup ? Visibility.Visible : Visibility.Collapsed;
                                btnCloseStartup.Visibility = isStartup ? Visibility.Visible : Visibility.Collapsed;

                                bool isMalwareScan = IsToolRunning(malwarescan);
                                lblStatusMalwareScan.Visibility = isMalwareScan ? Visibility.Visible : Visibility.Collapsed;
                                btnCloseMalwareScan.Visibility = isMalwareScan ? Visibility.Visible : Visibility.Collapsed;


                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"UI Update Error: {ex.Message}");
                            }
                        });

                        await Task.Delay(1000);
                    }
                }
                catch (TaskCanceledException)
                {
                    System.Diagnostics.Debug.WriteLine("Status Task was canceled.");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Status Task Critical Error: {ex.Message}");
                }
            });
        }

        private System.Windows.Controls.ContextMenu BuildTrayMenu()
        {
            var menu = new System.Windows.Controls.ContextMenu();
            menu.Items.Add(Processes.TrayMenus.Item(Loc.T("Show Utilities"), () => { Show(); WindowState = WindowState.Normal; Activate(); }, "utilities"));
            menu.Items.Add(Processes.TrayMenus.Item(Loc.T("Hide Utilities"), Hide, "\uED1A", IsVisible));
            menu.Items.Add(new System.Windows.Controls.Separator());
            var tools = new List<(string Name, string Icon, System.Windows.Window? Window, Action Open, Action Close)>
            {
                (Loc.T("Memory Cleaner"), "memory", memcleaner, () => OpenMemoryCleaner_Click(this, new RoutedEventArgs()), () => CloseMemCleaner_Click(this, new RoutedEventArgs())),
                (Loc.T("Malware Scan"), "malware", malwarescan, () => ShowMalwareScan(), () => CloseMalwareScan_Click(this, new RoutedEventArgs())),
                (Loc.T("Startup Manager"), "startup", startupmanager, () => StartupManager_Click(this, new RoutedEventArgs()), () => CloseStartup_Click(this, new RoutedEventArgs())),
                (Loc.T("Large File Finder"), "largefiles", largefilefinder, () => LargeFilesScan_Click(this, new RoutedEventArgs()), () => CloseLargeFile_Click(this, new RoutedEventArgs())),
                (Loc.T("Duplicate File Finder"), "duplicates", Duplicate_File_Finder, () => DuplicateFilesScan_Click(this, new RoutedEventArgs()), () => CloseDuplicate_Click(this, new RoutedEventArgs()))
            };
            var running = tools.Where(t => IsToolRunning(t.Window)).ToList();
            menu.Items.Add(Processes.TrayMenus.Label(running.Count == 0 ? Loc.T("No tools are running") : Loc.T("Running tools")));
            foreach (var tool in running)
            {
                var entry = Processes.TrayMenus.Item(tool.Name, null, tool.Icon);
                entry.Items.Add(Processes.TrayMenus.Item(Loc.T("Show"), tool.Open, "\uE8A7"));
                entry.Items.Add(Processes.TrayMenus.Item(Loc.T("Close"), tool.Close, "\uE711"));
                menu.Items.Add(entry);
            }
            if (running.Count > 1)
            {
                menu.Items.Add(new System.Windows.Controls.Separator());
                menu.Items.Add(Processes.TrayMenus.Item(Loc.T("Close all tools"), () =>
                {
                    foreach (var tool in running)
                        tool.Close();
                }, "\uE8BB"));
            }
            return menu;
        }

        private bool IsToolRunning(System.Windows.Window? toolWindow)
        {
            if (toolWindow == null)
                return false;
            return  toolWindow.IsLoaded && toolWindow.IsVisible || toolWindow.Visibility == Visibility.Collapsed;
        }

        private void CloseLargeFile_Click(object sender, RoutedEventArgs e)
        {
            largefilefinder?.Close();
            largefilefinder = null;

        }

        private void CloseDuplicate_Click(object sender, RoutedEventArgs e)
        {
            Duplicate_File_Finder?.Close();
            Duplicate_File_Finder = null;

        }

        private void CloseMemCleaner_Click(object sender, RoutedEventArgs e)
        {
            if (memcleaner?.memorymon != null)
               memcleaner.memorymon.Close();
            memcleaner?.Close();
            memcleaner = null;


        }

        private void CloseStartup_Click(object sender, RoutedEventArgs e)
        {
            startupmanager?.Close();
            startupmanager = null;

        }

        private void MalwareScan_Click(object sender, RoutedEventArgs e)
        {
            ShowMalwareScan();
        }

        private void CloseMalwareScan_Click(object sender, RoutedEventArgs e)
        {
            if (malwarescan != null && malwarescan.IsScanning)
            {
                var answer = AppDialog.Show(Loc.T("A malware scan is running. Stop it and close the window?"), Loc.T("Malware Scan"), MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (answer != MessageBoxResult.Yes) return;
            }
            malwarescan?.Close();
            malwarescan = null;
        }

        public MalwareScan ShowMalwareScan()
        {
            if (malwarescan == null)
                malwarescan = new MalwareScan(this);

            malwarescan.BringToFront();
            return malwarescan;
        }

        public async Task RunQueuedMalwareScanAsync(MalwareScan.ScanMode mode)
        {
            if (malwarescan == null)
                malwarescan = new MalwareScan(this);
            await malwarescan.StartScanAsync(mode, fromMainScan: true);
        }

        private System.Windows.Forms.NotifyIcon? trayIcon;

        internal void DisposeTrayIcon()
        {
            if (trayIcon == null) return;
            trayIcon.Visible = false;
            trayIcon.Dispose();
            trayIcon = null;
        }

        private void SetupTrayIcon()
        {
            if (trayIcon == null)
            {
                trayIcon = new System.Windows.Forms.NotifyIcon();

                try
                {
                    Uri iconUri = new Uri("pack://application:,,,/Assets/Icons/mwc_utilities.ico", UriKind.Absolute);
                    var streamInfo = Application.GetResourceStream(iconUri);
                    if (streamInfo != null)
                    {
                        trayIcon.Icon = new System.Drawing.Icon(streamInfo.Stream);
                    }
                    else
                    {
                        trayIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location);
                    }
                }
                catch
                {
                    trayIcon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location);
                }

                trayIcon.Text = Loc.T("Multron Win Cleaner Utilities");

                trayIcon.DoubleClick += (s, e) =>
                {
                    this.Show();
                    this.WindowState = WindowState.Normal;
                };

                Processes.TrayMenus.Attach(trayIcon, BuildTrayMenu);
                AppDomain.CurrentDomain.ProcessExit += (s, e) => trayIcon?.Dispose();
            }

            trayIcon.Visible = true;
        }

        private void LoadSettings()
        {
            try
            {
                string filePath = System.IO.Path.Combine(Environment.CurrentDirectory, "Settings.txt");
                if (System.IO.File.Exists(filePath))
                {
                    string[] lines = System.IO.File.ReadAllLines(filePath);
                    foreach (string line in lines)
                    {
                        if (line.StartsWith("utilitiestrayicon:", StringComparison.OrdinalIgnoreCase))
                        {
                            chkTrayIconUtil.IsChecked = line.Split(':')[1] == "1";
                        }
                        else if (line.StartsWith("turboboost:", StringComparison.OrdinalIgnoreCase))
                        {
                            turboBoostActive = line.Split(':')[1] == "1";
                        }
                        else if (line.StartsWith("newboostautomem:", StringComparison.OrdinalIgnoreCase))
                        {
                            int.TryParse(line.Split(':')[1], out newBoostAutoMemPrevious);
                        }
                        else if (line.StartsWith("newboost:", StringComparison.OrdinalIgnoreCase))
                        {
                            newBoostActive = line.Split(':')[1] == "1";
                        }
                        else if (line.StartsWith("optimizationprofile:", StringComparison.OrdinalIgnoreCase))
                        {
                            OptimizationProfile.SelectedIndex = int.TryParse(line.Split(':')[1], out int profile) && profile is >= 0 and <= 2 ? profile : 0;
                        }
                        else if (line.StartsWith("selectedtweak:", StringComparison.OrdinalIgnoreCase))
                        {
                            string[] parts = line.Split(':');
                            var tweak = parts.Length >= 3 ? newSystemTweaks.FirstOrDefault(t => t.ServiceName == parts[1]) : null;
                            if (tweak != null)
                                tweak.IsSelected = parts[2] == "true";
                        }
                    }
                }
            }
            catch { }
            finally
            {
                if (OptimizationProfile.SelectedIndex < 0)
                    OptimizationProfile.SelectedIndex = 0;
                RefreshOptimizationCard();
                isSettingsLoaded = true;
            }
        }

        private void chkTrayIconUtil_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (chkTrayIconUtil.IsChecked == true)
            {
                SetupTrayIcon();
            }
            else if (trayIcon != null)
            {
                trayIcon.Visible = false;
            }
            UpdateMainWindowButton();
            savesettings($"utilitiestrayicon:{(chkTrayIconUtil.IsChecked == true ? "1" : "0")}");
        }

        private void UpdateMainWindowButton()
        {
            if (MainWindowButton != null)
                MainWindowButton.Visibility = chkTrayIconUtil.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            if (TitleDivider != null)
                TitleDivider.Visibility = chkTrayIconUtil.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }

        private void WatchToolProgress(System.Windows.Controls.ProgressBar bar, string tool, Func<(string Text, Brush Brush)> result)
        {
            bool running = false;
            void Check(object? sender, EventArgs e)
            {
                bool now = bar.Visibility == Visibility.Visible && bar.IsIndeterminate;
                if (now == running)
                    return;
                running = now;
                if (now)
                {
                    window?.ShowToolStatus(Loc.T(tool), Loc.T("Working..."));
                    return;
                }
                Dispatcher.BeginInvoke(() =>
                {
                    var (text, brush) = result();
                    bool ok = brush is SolidColorBrush solid &&
                        (solid.Color == Colors.LimeGreen || solid.Color == ((System.Windows.Media.SolidColorBrush)FindResource("ResultSuccessText")).Color || solid.Color == (Color)ColorConverter.ConvertFromString(MainWindow.OkColor));
                    window?.FinishToolStatus(Loc.T(tool), text, !ok);
                });
            }
            System.ComponentModel.DependencyPropertyDescriptor.FromProperty(UIElement.VisibilityProperty, typeof(System.Windows.Controls.ProgressBar)).AddValueChanged(bar, Check);
            System.ComponentModel.DependencyPropertyDescriptor.FromProperty(System.Windows.Controls.ProgressBar.IsIndeterminateProperty, typeof(System.Windows.Controls.ProgressBar)).AddValueChanged(bar, Check);
        }

        private void FirewallReset_Click(object sender, RoutedEventArgs e)
        {
            FirewallInvalidRulesList.Items.Clear();
            FirewallInvalidRulesList.Visibility = Visibility.Collapsed;
            FirewallScanResultLabel.Content = "";
            FirewallScanResultLabel.Visibility = Visibility.Collapsed;
            FirewallProgress.Value = 0;
            FirewallProgress.Visibility = Visibility.Collapsed;
            FirewallScan.Content = Loc.T("Start Scan");
            FirewallScan.IsEnabled = true;
            FirewallReset.Visibility = Visibility.Collapsed;
        }

        private void FirewallClean_Click(object sender, RoutedEventArgs e)
        {
            if (firewallSavedContent != null)
            {
                window?.CancelScanOrClean();
                return;
            }
            firewallRunning = true;
            if (FirewallScan.Content.Equals(Loc.T("Start Scan")) || FirewallScan.Content.Equals(Loc.T("ReScan")))
            {
                FirewallScan.Content = Loc.T("Clean");
                FirewallScan.IsEnabled = false;
                scanforrules();
                FirewallReset.Visibility = Visibility.Visible;
            }
            else
            {
                FirewallScan.IsEnabled = false;
                deleterules();
                FirewallReset.Visibility = Visibility.Visible;
            }
        }

        List<MultronWinCleaner.Processes.ShortcutFixer.BrokenShortcut> brokenShortcuts = new List<MultronWinCleaner.Processes.ShortcutFixer.BrokenShortcut>();

        private async void ShortcutScan_Click(object sender, RoutedEventArgs e)
        {
            if (shortcutSavedContent != null)
            {
                window?.CancelScanOrClean();
                return;
            }
            shortcutRunning = true;
            ShortcutScan.IsEnabled = false;
            ShortcutProgress.Visibility = Visibility.Visible;
            ShortcutList.Items.Clear();
            ShortcutList.Visibility = Visibility.Collapsed;
            ShortcutResultLabel.Visibility = Visibility.Collapsed;
            try
            {
                if (ShortcutScan.Content.Equals(Loc.T("Fix")) && brokenShortcuts.Count > 0)
                {
                    var items = brokenShortcuts;
                    var result = await Task.Run(() => MultronWinCleaner.Processes.ShortcutFixer.Fix(items));
                    ShowShortcutFix(items, result);
                    window?.ApplyShortcutFix(items, result);
                }
                else
                {
                    brokenShortcuts = await Task.Run(MultronWinCleaner.Processes.ShortcutFixer.FindBrokenShortcuts);
                    foreach (var item in brokenShortcuts)
                        ShortcutList.Items.Add(MultronWinCleaner.Processes.FriendlyListRow.ForShortcut(item));
                    if (brokenShortcuts.Count > 0)
                    {
                        ShortcutResultLabel.Content = Loc.F("{0} shortcut(s) point to files that no longer exist.", brokenShortcuts.Count);
                        ShortcutResultLabel.SetResourceReference(ForegroundProperty, "ResultProblemText");
                        ShortcutScan.Content = Loc.T("Fix");
                    }
                    else
                    {
                        ShortcutResultLabel.Content = Loc.T("No broken shortcuts found.");
                        ShortcutResultLabel.SetResourceReference(ForegroundProperty, "ResultSuccessText");
                        ShortcutScan.Content = Loc.T("ReScan");
                    }
                }
                ShortcutList.Visibility = ShortcutList.Items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                ShortcutResultLabel.Content = Loc.F("Shortcut Fixer failed: {0}", ex.Message);
                ShortcutResultLabel.SetResourceReference(ForegroundProperty, "ResultProblemText");
            }
            finally
            {
                ShortcutProgress.Visibility = Visibility.Collapsed;
                ShortcutResultLabel.Visibility = Visibility.Visible;
                ShortcutScan.IsEnabled = true;
                ShortcutReset.Visibility = Visibility.Visible;
                shortcutRunning = false;
            }
        }

        public void ShowShortcutFix(List<MultronWinCleaner.Processes.ShortcutFixer.BrokenShortcut> items, (int Repaired, int Removed, List<MultronWinCleaner.Processes.ShortcutFixer.BrokenShortcut> Failed) result)
        {
            if (shortcutRunning && !ReferenceEquals(items, brokenShortcuts))
                return;
            ShortcutList.Items.Clear();
            foreach (var item in items)
                ShortcutList.Items.Add(MultronWinCleaner.Processes.FriendlyListRow.ForShortcutResult(item, result.Failed.Contains(item)));
            brokenShortcuts = new List<MultronWinCleaner.Processes.ShortcutFixer.BrokenShortcut>();
            ShortcutResultLabel.Content = Loc.F("{0} repaired, {1} moved to the Recycle Bin", result.Repaired, result.Removed) + (result.Failed.Count > 0 ? Loc.F(", {0} failed.", result.Failed.Count) : ".");
            ShortcutResultLabel.SetResourceReference(ForegroundProperty, result.Failed.Count == 0 ? "ResultSuccessText" : "ResultProblemText");
            ShortcutResultLabel.Visibility = Visibility.Visible;
            ShortcutList.Visibility = ShortcutList.Items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            ShortcutReset.Visibility = Visibility.Visible;
            ShortcutScan.Content = Loc.T("ReScan");
        }

        public void ShowSecurityIssues(List<MultronWinCleaner.Processes.SecurityCheck.Issue> issues, string suffix)
        {
            if (securityRunning)
                return;
            securityIssues = issues;
            SecurityList.ItemsSource = securityIssues;
            SecurityListScroll.Visibility = securityIssues.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            string status = MainWindow.DescribeSecurityIssues(securityIssues, out string color);
            SecurityResultLabel.Content = suffix.Length > 0 ? status + " " + suffix : status;
            SecurityResultLabel.Foreground = MultronWinCleaner.Processes.ThemeColors.ReadableBrush(color);
            SecurityResultLabel.Visibility = Visibility.Visible;
            SecurityFix.Visibility = securityIssues.Any(i => i.CanFix) ? Visibility.Visible : Visibility.Collapsed;
            SecurityUndo.Visibility = MultronWinCleaner.Processes.SecurityCheck.HasBackup ? Visibility.Visible : Visibility.Collapsed;
            SecurityReset.Visibility = Visibility.Visible;
            SecurityScan.Content = Loc.T("ReScan");
            UpdateShowIgnoredButton();
        }

        private List<MultronWinCleaner.Processes.SecurityCheck.Issue> securityIssues = new List<MultronWinCleaner.Processes.SecurityCheck.Issue>();

        private void IgnoreSecurityIssue_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is MultronWinCleaner.Processes.SecurityCheck.Issue issue)
                window?.IgnoreSecurityIssue(issue);
        }

        public void RemoveSecurityIssue(string id, string note)
        {
            if (securityRunning || !securityIssues.Any(i => i.Id == id))
            {
                UpdateShowIgnoredButton();
                return;
            }
            ShowSecurityIssues(securityIssues.Where(i => i.Id != id).ToList(), note);
        }

        private void UpdateShowIgnoredButton()
        {
            int ignored = MultronWinCleaner.Processes.SecurityCheck.IgnoredCount;
            SecurityShowIgnored.Content = Loc.F("Show Ignored Warnings ({0})", ignored);
            SecurityShowIgnored.Visibility = ignored > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void SecurityShowIgnored_Click(object sender, RoutedEventArgs e)
        {
            if (securityRunning)
                return;
            var answer = AppDialog.Show(Loc.T("Show all ignored security warnings again?"),
                Loc.T("Security Check"), MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes)
                return;
            try
            {
                MultronWinCleaner.Processes.SecurityCheck.ClearIgnored();
            }
            catch (Exception ex)
            {
                AppDialog.Show(Loc.F("Could not save the setting: {0}", ex.Message), Loc.T("Security Check"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            await RunSecurityScanAsync("");
            window?.ApplySecurityIssues(securityIssues, "");
        }

        private void ShowCard(FrameworkElement element)
        {
            Show();
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
            Activate();
            element.BringIntoView();
        }

        public void ShowFirewallCard() => ShowCard(FirewallScan);

        public void ShowShortcutCard() => ShowCard(ShortcutScan);

        public async Task<List<MultronWinCleaner.Processes.FirewallRules.InvalidRule>?> RunFirewallReportAsync()
        {
            if (firewallRunning)
                return null;

            firewallRunning = true;
            FirewallScan.IsEnabled = false;
            FirewallReset.Visibility = Visibility.Collapsed;
            FirewallProgress.Visibility = Visibility.Visible;
            FirewallProgress.IsIndeterminate = true;
            FirewallInvalidRulesList.Items.Clear();
            FirewallInvalidRulesList.Visibility = Visibility.Collapsed;
            FirewallScanResultLabel.Content = Loc.T("Checking the firewall rules for the main screen scan...");
            FirewallScanResultLabel.SetResourceReference(ForegroundProperty, "AccentText");
            FirewallScanResultLabel.Visibility = Visibility.Visible;
            try
            {
                invalidrules = await Task.Run(MultronWinCleaner.Processes.FirewallRules.FindInvalidRules);
                foreach (var row in MultronWinCleaner.Processes.FriendlyListRow.ForFirewall(invalidrules))
                FirewallInvalidRulesList.Items.Add(row);
                FirewallInvalidRulesList.Visibility = invalidrules.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                FirewallScanResultLabel.Content = invalidrules.Count > 0 ? Loc.F("{0} firewall rule(s) left behind by removed programs.", invalidrules.Count) : Loc.T("No invalid firewall rules found.");
                FirewallScanResultLabel.SetResourceReference(ForegroundProperty, invalidrules.Count > 0 ? "ResultProblemText" : "ResultSuccessText");
                FirewallScan.Content = Loc.T(invalidrules.Count > 0 ? "Clean" : "ReScan");
                return invalidrules;
            }
            catch (Exception ex)
            {
                FirewallScanResultLabel.Content = Loc.F("Failed to read firewall rules: {0}", ex.Message);
                FirewallScanResultLabel.SetResourceReference(ForegroundProperty, "ResultProblemText");
                throw;
            }
            finally
            {
                FirewallProgress.IsIndeterminate = false;
                FirewallProgress.Visibility = Visibility.Collapsed;
                FirewallReset.Visibility = Visibility.Visible;
                firewallRunning = false;
                FirewallScan.IsEnabled = true;
            }
        }

        public async Task<List<MultronWinCleaner.Processes.ShortcutFixer.BrokenShortcut>?> RunShortcutReportAsync()
        {
            if (shortcutRunning)
                return null;

            shortcutRunning = true;
            ShortcutScan.IsEnabled = false;
            ShortcutReset.Visibility = Visibility.Collapsed;
            ShortcutProgress.Visibility = Visibility.Visible;
            ShortcutList.Items.Clear();
            ShortcutList.Visibility = Visibility.Collapsed;
            ShortcutResultLabel.Content = Loc.T("Checking the shortcuts for the main screen scan...");
            ShortcutResultLabel.SetResourceReference(ForegroundProperty, "AccentText");
            ShortcutResultLabel.Visibility = Visibility.Visible;
            try
            {
                brokenShortcuts = await Task.Run(MultronWinCleaner.Processes.ShortcutFixer.FindBrokenShortcuts);
                foreach (var item in brokenShortcuts)
                    ShortcutList.Items.Add(MultronWinCleaner.Processes.FriendlyListRow.ForShortcut(item));
                ShortcutList.Visibility = brokenShortcuts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                ShortcutResultLabel.Content = brokenShortcuts.Count > 0 ? Loc.F("{0} shortcut(s) point to files that no longer exist.", brokenShortcuts.Count) : Loc.T("No broken shortcuts found.");
                ShortcutResultLabel.SetResourceReference(ForegroundProperty, brokenShortcuts.Count > 0 ? "ResultProblemText" : "ResultSuccessText");
                ShortcutScan.Content = Loc.T(brokenShortcuts.Count > 0 ? "Fix" : "ReScan");
                return brokenShortcuts;
            }
            catch (Exception ex)
            {
                ShortcutResultLabel.Content = Loc.F("Shortcut Fixer failed: {0}", ex.Message);
                ShortcutResultLabel.SetResourceReference(ForegroundProperty, "ResultProblemText");
                throw;
            }
            finally
            {
                ShortcutProgress.Visibility = Visibility.Collapsed;
                ShortcutReset.Visibility = Visibility.Visible;
                shortcutRunning = false;
                ShortcutScan.IsEnabled = true;
            }
        }

        public async Task<List<MultronWinCleaner.Processes.SecurityCheck.Issue>?> RunSecurityReportAsync()
        {
            if (securityRunning)
                return null;
            await RunSecurityScanAsync("");
            return securityIssues;
        }

        public void ShowSecurityCheck()
        {
            Show();
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
            Activate();
            SecurityCard.BringIntoView();
            if (!securityRunning && securityIssues.Count == 0 && SecurityScan.IsEnabled && securitySavedContent == null)
                SecurityScan_Click(SecurityScan, new RoutedEventArgs());
        }

        private async Task RunSecurityScanAsync(string suffix)
        {
            securityRunning = true;
            SecurityScan.IsEnabled = false;
            SecurityFix.Visibility = Visibility.Collapsed;
            SecurityProgress.Visibility = Visibility.Visible;
            SecurityResultLabel.Visibility = Visibility.Collapsed;
            try
            {
                securityIssues = await Task.Run(MultronWinCleaner.Processes.SecurityCheck.Scan);
                SecurityList.ItemsSource = securityIssues;
                SecurityListScroll.Visibility = securityIssues.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                string status = MainWindow.DescribeSecurityIssues(securityIssues, out string color);
                SecurityResultLabel.Content = suffix.Length > 0 ? status + " " + suffix : status;
                SecurityResultLabel.Foreground = MultronWinCleaner.Processes.ThemeColors.ReadableBrush(color);
                SecurityFix.Visibility = securityIssues.Any(i => i.CanFix) ? Visibility.Visible : Visibility.Collapsed;
                SecurityScan.Content = Loc.T("ReScan");
                UpdateShowIgnoredButton();
            }
            catch (Exception ex)
            {
                SecurityResultLabel.Content = Loc.F("Security Check failed: {0}", ex.Message);
                SecurityResultLabel.SetResourceReference(ForegroundProperty, "ResultProblemText");
            }
            finally
            {
                SecurityProgress.Visibility = Visibility.Collapsed;
                SecurityResultLabel.Visibility = Visibility.Visible;
                SecurityUndo.Visibility = MultronWinCleaner.Processes.SecurityCheck.HasBackup ? Visibility.Visible : Visibility.Collapsed;
                SecurityReset.Visibility = Visibility.Visible;
                SecurityScan.IsEnabled = true;
                securityRunning = false;
            }
        }

        private async void SecurityScan_Click(object sender, RoutedEventArgs e)
        {
            if (securitySavedContent != null)
            {
                window?.CancelScanOrClean();
                return;
            }
            await RunSecurityScanAsync("");
        }

        private async void SecurityFix_Click(object sender, RoutedEventArgs e)
        {
            var selected = securityIssues.Where(i => i.IsSelected && i.CanFix).ToList();
            if (selected.Count == 0)
            {
                SecurityResultLabel.Content = Loc.T("Tick the settings you want to fix.");
                SecurityResultLabel.SetResourceReference(ForegroundProperty, "ResultProblemText");
                SecurityResultLabel.Visibility = Visibility.Visible;
                return;
            }

            string message = "";
            bool done = await window.FixSecurityIssuesAsync(selected, SecurityFix, text =>
            {
                message = text;
                SecurityResultLabel.Content = text;
                SecurityResultLabel.Visibility = Visibility.Visible;
            });
            if (done)
            {
                MultronWinCleaner.Processes.Optimizer.AppendLog(selected.Select(i => Loc.F("Security fix: {0}", i.Title)));
                RefreshOptimizationCard();
                await RunSecurityScanAsync(message);
                window?.ApplySecurityIssues(securityIssues, message);
            }
        }

        private async void SecurityUndo_Click(object sender, RoutedEventArgs e)
        {
            var answer = AppDialog.Show(Loc.T("Undo all security fixes made by Multron Win Cleaner?\n\nThe saved values are written back, so the settings become as insecure as they were before."),
                Loc.T("Security Check"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes)
                return;

            SecurityUndo.IsEnabled = false;
            SecurityProgress.Visibility = Visibility.Visible;
            try
            {
                var log = await Task.Run(MultronWinCleaner.Processes.SecurityCheck.Undo);
                if (log.Count > 1)
                    AppDialog.Show(string.Join("\n", log), Loc.T("Security Check"), MessageBoxButton.OK, MessageBoxImage.Warning);
                await RunSecurityScanAsync(log.FirstOrDefault() ?? "");
                window?.ApplySecurityIssues(securityIssues, log.FirstOrDefault() ?? "");
            }
            finally
            {
                SecurityUndo.IsEnabled = true;
            }
        }

        private void SecurityReset_Click(object sender, RoutedEventArgs e)
        {
            securityIssues = new List<MultronWinCleaner.Processes.SecurityCheck.Issue>();
            SecurityList.ItemsSource = null;
            SecurityListScroll.Visibility = Visibility.Collapsed;
            SecurityResultLabel.Content = "";
            SecurityResultLabel.Visibility = Visibility.Collapsed;
            SecurityProgress.Visibility = Visibility.Collapsed;
            SecurityFix.Visibility = Visibility.Collapsed;
            SecurityScan.Content = Loc.T("Start Scan");
            SecurityScan.IsEnabled = true;
            SecurityReset.Visibility = Visibility.Collapsed;
            SecurityUndo.Visibility = MultronWinCleaner.Processes.SecurityCheck.HasBackup ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ShortcutReset_Click(object sender, RoutedEventArgs e)
        {
            brokenShortcuts = new List<MultronWinCleaner.Processes.ShortcutFixer.BrokenShortcut>();
            ShortcutList.Items.Clear();
            ShortcutList.Visibility = Visibility.Collapsed;
            ShortcutResultLabel.Content = "";
            ShortcutResultLabel.Visibility = Visibility.Collapsed;
            ShortcutProgress.Visibility = Visibility.Collapsed;
            ShortcutScan.Content = Loc.T("Start Scan");
            ShortcutScan.IsEnabled = true;
            ShortcutReset.Visibility = Visibility.Collapsed;
        }

        public async void scanforrules()
        {
            FirewallProgress.Visibility = Visibility.Visible;
            FirewallProgress.IsIndeterminate = true;
            FirewallInvalidRulesList.Items.Clear();
            invalidrules = new List<MultronWinCleaner.Processes.FirewallRules.InvalidRule>();

            try
            {
                invalidrules = await Task.Run(MultronWinCleaner.Processes.FirewallRules.FindInvalidRules);
            }
            catch (Exception ex)
            {
                AppDialog.Show(Loc.F("Failed to read firewall rules: {0}", ex.Message), Loc.T("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }

            FirewallProgress.IsIndeterminate = false;
            FirewallProgress.Visibility = Visibility.Collapsed;
            foreach (var row in MultronWinCleaner.Processes.FriendlyListRow.ForFirewall(invalidrules))
                FirewallInvalidRulesList.Items.Add(row);

            FirewallScanResultLabel.Visibility = Visibility.Visible;
            if (invalidrules.Count > 0)
            {
                FirewallInvalidRulesList.Visibility = Visibility.Visible;
                FirewallScanResultLabel.Content = Loc.F("{0} firewall rule(s) left behind by removed programs.", invalidrules.Count);
                FirewallScanResultLabel.SetResourceReference(ForegroundProperty, "ResultProblemText");
            }
            else
            {
                FirewallScanResultLabel.Content = Loc.T("No invalid firewall rules found.");
                FirewallScanResultLabel.SetResourceReference(ForegroundProperty, "ResultSuccessText");
                FirewallScan.Content = Loc.T("ReScan");
            }
            firewallRunning = false;
            FirewallScan.IsEnabled = true;
        }

        private async void deleterules()
        {
            FirewallProgress.Visibility = Visibility.Visible;
            FirewallProgress.IsIndeterminate = true;
            FirewallInvalidRulesList.Items.Clear();

            var rules = invalidrules;
            (int Removed, List<MultronWinCleaner.Processes.FirewallRules.InvalidRule> Failed) result;
            try
            {
                result = await Task.Run(() => MultronWinCleaner.Processes.FirewallRules.RemoveRules(rules));
            }
            catch (Exception ex)
            {
                AppDialog.Show(Loc.F("Failed to remove firewall rules: {0}", ex.Message), Loc.T("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                result = (0, rules);
            }
            ShowFirewallRemoval(rules, result);
            window?.ApplyFirewallRemoval(rules, result);
            firewallRunning = false;
            FirewallScan.IsEnabled = true;
        }

        public void ShowFirewallRemoval(List<MultronWinCleaner.Processes.FirewallRules.InvalidRule> rules, (int Removed, List<MultronWinCleaner.Processes.FirewallRules.InvalidRule> Failed) result)
        {
            if (firewallRunning && !ReferenceEquals(rules, invalidrules))
                return;
            FirewallInvalidRulesList.Items.Clear();
            foreach (var row in MultronWinCleaner.Processes.FriendlyListRow.ForFirewallResult(rules, result.Failed))
                FirewallInvalidRulesList.Items.Add(row);
            invalidrules = new List<MultronWinCleaner.Processes.FirewallRules.InvalidRule>();

            FirewallProgress.IsIndeterminate = false;
            FirewallProgress.Visibility = Visibility.Collapsed;
            FirewallReset.Visibility = Visibility.Visible;
            FirewallInvalidRulesList.Visibility = Visibility.Visible;
            FirewallScanResultLabel.Content = result.Failed.Count == 0
                ? Loc.F("{0} invalid firewall rule(s) deleted.", result.Removed)
                : Loc.F("{0} invalid firewall rule(s) deleted, {1} could not be deleted.", result.Removed, result.Failed.Count);
            FirewallScanResultLabel.SetResourceReference(ForegroundProperty, result.Failed.Count == 0 ? "ResultSuccessText" : "ResultProblemText");
            FirewallScanResultLabel.Visibility = Visibility.Visible;
            FirewallScan.Content = Loc.T("ReScan");
        }

        private void LargeFilesScan_Click(object sender, RoutedEventArgs e)
        {
            if (largefilefinder == null)
            {
                largefilefinder = new LargeFileFinder();
                window.settings.Close();
                window.settings = new Settings(memcleaner, window.utilities, window, startupmanager);
                window.settings.Show();
                window.settings.Hide();
          
            }
                
            
            
            largefilefinder.BringToFront();
        }
        private void TopPanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left)
                return;
            if (e.ClickCount == 2)
                ToggleFullScreen();
            else if (!isFullScreen)
                DragMove();
        }

        private bool isFullScreen;
        private double previousWidth, previousHeight, previousLeft, previousTop;

        private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void MaximizeButton_Click(object sender, RoutedEventArgs e) => ToggleFullScreen();

        private void ToggleFullScreen()
        {
            if (isFullScreen)
            {
                Width = previousWidth;
                Height = previousHeight;
                Left = previousLeft;
                Top = previousTop;
                buttonMaximize.Content = "\uE922";
            }
            else
            {
                previousWidth = Width;
                previousHeight = Height;
                previousLeft = Left;
                previousTop = Top;
                Left = SystemParameters.WorkArea.Left;
                Top = SystemParameters.WorkArea.Top;
                Width = SystemParameters.WorkArea.Width;
                Height = SystemParameters.WorkArea.Height;
                buttonMaximize.Content = "\uE923";
            }
            isFullScreen = !isFullScreen;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => this.Hide();

        private void ShowMainWindow_Click(object sender, RoutedEventArgs e)
        {
            if (window == null)
                return;
            if (!window.IsVisible)
                window.Show();
            if (window.WindowState == WindowState.Minimized)
                window.WindowState = WindowState.Normal;
            window.Activate();
            window.Topmost = true;
            window.Topmost = false;
        }
        public Duplicate_File_Finder? GetDuplicateFinder()
        {
            if (Duplicate_File_Finder == null)
                Duplicate_File_Finder = new Duplicate_File_Finder();
            return Duplicate_File_Finder;
        }

        public void ShowLargeFileReport(List<LargeFileFinder.LargeFileInfo> files)
        {
            LargeFilesScan_Click(this, new RoutedEventArgs());
            largefilefinder?.ShowReport(files);
        }

        public void ShowDuplicateFinder()
        {
            DuplicateFilesScan_Click(this, new RoutedEventArgs());
            Duplicate_File_Finder?.ShowSideBySide();
        }

        private void DuplicateFilesScan_Click(object sender, RoutedEventArgs e) {
            
            if(Duplicate_File_Finder == null)
            {
                Duplicate_File_Finder = new Duplicate_File_Finder();
                window.settings.Close();
                window.settings = new Settings(memcleaner, window.utilities, window, startupmanager);
                window.settings.Show();
                window.settings.Hide();
        
            }
        
            
            Duplicate_File_Finder.BringToFront();
        
        }


        private void OpenMemoryCleaner_Click(object sender, RoutedEventArgs e) { 
            if(memcleaner == null)
            {
                memcleaner = new MemCleaner(window);
                window.settings.Close();
                window.settings = new Settings(memcleaner, window.utilities, window, startupmanager);
                window.settings.Show();
                window.settings.Hide();
            
            }
         
            
            memcleaner.BringToFront();
        
        }

        public bool turboBoostActive = false;
        public ObservableCollection<ServiceItem> turboBoostServices = new ObservableCollection<ServiceItem>
        {
            Svc("SysMain", "SysMain (Superfetch)", "Preloads apps into memory. Pausing it frees memory and disk activity on older PCs.", true),
            Svc("WSearch", "Windows Search", "Indexes files in the background. Searching is slower while it is paused.", true),
            Svc("DiagTrack", "Connected User Experiences and Telemetry", "Sends diagnostic data to Microsoft.", true),
            Svc("dmwappushservice", "Device Management WAP Push", "Routes device management messages.", true),
            Svc("WerSvc", "Windows Error Reporting", "Collects and sends crash reports.", true),
            Svc("PcaSvc", "Program Compatibility Assistant", "Watches programs for compatibility problems.", true),
            Svc("TrkWks", "Distributed Link Tracking Client", "Keeps links to files on other drives up to date.", true),
            Svc("XblGameSave", "Xbox Live Game Save", "Syncs Xbox game saves.", true),
            Svc("XblAuthManager", "Xbox Live Auth Manager", "Signs in to Xbox Live.", true),
            Svc("XboxNetApiSvc", "Xbox Live Networking", "Xbox Live networking for games.", true),
            Svc("XboxGipSvc", "Xbox Accessory Management", "Needed for Xbox controllers.", false),
            Svc("MapsBroker", "Downloaded Maps Manager", "Updates offline maps.", true),
            Svc("WMPNetworkSvc", "Windows Media Player Network Sharing", "Shares media libraries on the network.", true),
            Svc("RetailDemo", "Retail Demo", "Store demo mode.", true),
            Svc("Fax", "Fax", "Sends and receives faxes.", true),
            Svc("RemoteRegistry", "Remote Registry", "Lets other computers change this PC's registry. It is normally off already.", true),
            Svc("SSDPSRV", "SSDP Discovery", "Finds network devices such as media players and TVs.", false),
            Svc("upnphost", "UPnP Device Host", "Hosts UPnP devices on this PC.", false),
            Svc("FDResPub", "Function Discovery Resource Publication", "Makes this PC visible to other PCs on the network.", false),
            Svc("Spooler", "Print Spooler", "Needed for printing. Printing does not work while it is paused.", false),
            Svc("PrintNotify", "Printer Extensions and Notifications", "Printer notifications.", false),
            Svc("TouchKeyboardAndHandwritingPanelService", "Touch Keyboard and Handwriting", "Needed for touch screens and pens.", false),
            Svc("bthserv", "Bluetooth Support", "Needed for Bluetooth devices. They disconnect while it is paused.", false)
        };

        public ObservableCollection<ServiceItem> newSystemTweaks = MultronWinCleaner.Processes.Optimizer.CreateNewSystemTweaks();
        public bool newBoostActive = false;
        public int newBoostAutoMemPrevious = -1;

        private static ServiceItem Svc(string name, string title, string description, bool selected) =>
            new ServiceItem { ServiceName = name, DisplayName = Loc.T(title), Description = Loc.T(description), IsSelected = selected };

        private bool IsNewProfile => OptimizationProfile.SelectedIndex == 1;

        private bool IsAllProfiles => OptimizationProfile.SelectedIndex == 2;

        private bool SelectedProfilesActive => IsAllProfiles ? newBoostActive && turboBoostActive : IsNewProfile ? newBoostActive : turboBoostActive;

        private void RefreshOptimizationLog()
        {
            if (OptimizationLog == null) return;
            var lines = MultronWinCleaner.Processes.Optimizer.ReadLog();
            if (lines.Count == 0)
                lines.Add(Loc.T("No optimization has been applied yet."));
            OptimizationLog.ItemsSource = lines;
        }

        private async Task OfferStartupAsync()
        {
            var settings = window?.settings;
            if (settings == null || await settings.IsInStartup_ActAsync())
                return;

            var answer = AppDialog.Show(
                Loc.T("Some of these optimizations do not last after a restart: Windows starts paused services again, the DNS cache fills up again and other programs can change settings back. Multron Win Cleaner applies them again every time it starts.") + "\n\n" +
                Loc.T("Add Multron Win Cleaner to Windows startup, so this happens automatically after every restart?") + "\n\n" +
                Loc.T("If you choose No, they are applied again whenever you open the app."),
                Loc.T("System Optimization"), MessageBoxButton.YesNo, MessageBoxImage.Question);

            string result;
            if (answer != MessageBoxResult.Yes)
            {
                result = Loc.T("Not added to Windows startup: the optimization is applied again whenever the app is opened");
            }
            else if (await settings.CreateStartupTaskAsync())
            {
                settings.SetRunAtStartupBox(true);
                result = Loc.T("Added Multron Win Cleaner to Windows startup");
            }
            else
            {
                result = Loc.T("Could not add Multron Win Cleaner to Windows startup");
                AppDialog.Show(Loc.T("Multron Win Cleaner could not be added to Windows startup. You can try again in Settings."), Loc.T("System Optimization"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            MultronWinCleaner.Processes.Optimizer.AppendLog(new[] { result });
            RefreshOptimizationLog();
        }

        private async Task ReapplyOptimizationAtStartupAsync()
        {
            var messages = new List<string>();
            try
            {
                if (newBoostActive)
                {
                    var tweaks = newSystemTweaks.ToList();
                    messages.AddRange(await Task.Run(() => MultronWinCleaner.Processes.Optimizer.ReapplyNewSystemTweaks(tweaks)));
                }
                if (turboBoostActive)
                {
                    var services = turboBoostServices.ToList();
                    var stopped = await Task.Run(() => MultronWinCleaner.Processes.Optimizer.StopOldSystemServices(services));
                    if (stopped.Count > 0)
                        messages.Add(Loc.F("Paused again at startup (Windows had started them): {0}", string.Join(", ", stopped)));
                }
            }
            catch (Exception ex)
            {
                messages.Add(Loc.F("Could not apply the optimization at startup: {0}", ex.Message));
            }

            if (messages.Count > 0)
            {
                MultronWinCleaner.Processes.Optimizer.AppendLog(messages);
                RefreshOptimizationLog();
            }
        }

        private void RefreshOptimizationCard()
        {
            RefreshOptimizationLog();
            Turbo.Content = Loc.T(SelectedProfilesActive ? "Undo Optimization" : "Apply Optimization");
            OptimizationDescription.Text = IsAllProfiles
                ? Loc.T("Applies both: pauses background Windows services to free memory and CPU, and applies the performance settings (power plan, Game Mode, game and multimedia priority and more). Only the one that is not on yet is applied. Undo restores both.")
                : IsNewProfile
                ? Loc.T("Applies performance settings for modern PCs: power plan, Game Mode, game and multimedia priority and more. Every changed setting is saved first, so Undo restores your previous values. While it is on, settings that were changed back are applied again and the DNS cache is flushed every time the app starts.")
                : Loc.T("Pauses background Windows services to free memory and CPU on older PCs. Windows starts them again after a restart, so while this is on they are paused again every time the app starts. Undo starts exactly the services that were paused.");
        }

        private void OptimizationProfile_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Turbo == null || OptimizationDescription == null) return;
            RefreshOptimizationCard();
            if (isSettingsLoaded)
                savesettings($"optimizationprofile:{OptimizationProfile.SelectedIndex}");
        }

        public void savesettings(string setting)
        {
            try
            {
                string filePath = System.IO.Path.Combine(Environment.CurrentDirectory, "Settings.txt");
                List<string> lines = System.IO.File.Exists(filePath)
                    ? System.IO.File.ReadAllLines(filePath).ToList()
                    : new List<string>();

                string selectionPrefix = setting.StartsWith("selectedtweak:") ? "selectedtweak:" : "selectedservice:";
                if (setting.StartsWith(selectionPrefix))
                {
                    string[] parts = setting.Split(':');
                    if (parts.Length < 3) return;

                    string serviceName = parts[1];
                    string newValue = parts[2];
                    int index = lines.FindIndex(line =>
                        line.StartsWith(selectionPrefix) &&
                        line.Split(':').Length >= 3 &&
                        line.Split(':')[1] == serviceName);

                    if (index >= 0)
                    {
                        lines[index] = $"{selectionPrefix}{serviceName}:{newValue}";
                    }
                    else
                    {
                        lines.Add($"{selectionPrefix}{serviceName}:{newValue}");
                    }
                }
                else
                {
                    string key = setting.Split(':')[0];
                    int index = lines.FindIndex(line => line.StartsWith(key + ":"));
                    if (index >= 0)
                        lines[index] = setting;
                    else
                        lines.Add(setting);
                }

                System.IO.File.WriteAllLines(filePath, lines);
            }
            catch
            {
            }
        }

        private void ConfigureTurboBoost_Click(object sender, RoutedEventArgs e)
        {
            if (IsAllProfiles)
            {
                var menu = new ContextMenu { PlacementTarget = sender as UIElement ?? Turbo };
                var services = new MenuItem { Header = Loc.T("Services to pause (old systems)...") };
                services.Click += (s, a) => ConfigureProfile(false);
                var tweaks = new MenuItem { Header = Loc.T("Performance settings (new systems)...") };
                tweaks.Click += (s, a) => ConfigureProfile(true);
                menu.Items.Add(services);
                menu.Items.Add(tweaks);
                menu.IsOpen = true;
                return;
            }
            ConfigureProfile(IsNewProfile);
        }

        private void ConfigureProfile(bool newProfile)
        {
            if (newProfile)
                configure.ShowItems(Loc.T("Configure Performance Boost"), Loc.T("Select the settings to apply. Undo restores your previous values:"), newSystemTweaks, "selectedtweak:");
            else
                configure.ShowItems(Loc.T("Configure Optimization Services"), Loc.T("Select the services to pause during optimization:"), turboBoostServices, "selectedservice:");
        }

        private async void ActivateTurboBoost_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button btn) return;
            bool active = SelectedProfilesActive;
            var profiles = IsAllProfiles
                ? new[] { false, true }.Where(p => (p ? newBoostActive : turboBoostActive) == active).ToList()
                : new List<bool> { IsNewProfile };
            btn.IsEnabled = false;
            OptimizationProfile.IsEnabled = false;
            btn.Content = Loc.T(active ? "Undoing..." : "Applying...");

            try
            {
                var allMessages = new List<string>();
                foreach (bool newProfile in profiles)
                {
                    var messages = await RunOptimizationProfileAsync(newProfile, active);
                    string profileName = Loc.T(newProfile ? "new systems" : "old systems");
                    if (active)
                        MultronWinCleaner.Processes.Optimizer.AppendLog(new[] { Loc.F("Undone: optimization for {0}", profileName) }.Concat(messages.Select(m => "   " + m)));
                    else
                        MultronWinCleaner.Processes.Optimizer.AppendLog(new[] { Loc.F("Applied: optimization for {0}", profileName) }.Concat(messages.Select(m => "   " + m)));
                    if (profiles.Count > 1)
                        allMessages.Add(Loc.F(active ? "Undone: optimization for {0}" : "Applied: optimization for {0}", profileName));
                    allMessages.AddRange(profiles.Count > 1 ? messages.Select(m => "   " + m) : messages);
                }
                if (active && !newBoostActive && !turboBoostActive)
                    MultronWinCleaner.Processes.Optimizer.ClearLog();
                AppDialog.Show(Loc.T(active ? "Optimization undone." : "Optimization applied.") + "\n\n" + string.Join("\n", allMessages), Loc.T("System Optimization"), MessageBoxButton.OK, MessageBoxImage.Information);
                if (!active)
                    await OfferStartupAsync();
            }
            catch (Exception ex)
            {
                MultronWinCleaner.Processes.Optimizer.AppendLog(new[] { Loc.F("Optimization failed: {0}", ex.Message) });
                AppDialog.Show(Loc.F("Optimization failed:\n{0}", ex.Message), Loc.T("System Optimization"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btn.IsEnabled = true;
                OptimizationProfile.IsEnabled = true;
                RefreshOptimizationCard();
            }
        }

        private async Task<List<string>> RunOptimizationProfileAsync(bool newProfile, bool active)
        {
            List<string> messages;
            if (newProfile)
            {
                var tweaks = newSystemTweaks.ToList();
                messages = await Task.Run(() => active
                    ? MultronWinCleaner.Processes.Optimizer.UndoNewSystemTweaks()
                    : MultronWinCleaner.Processes.Optimizer.ApplyNewSystemTweaks(tweaks));
                bool memorySelected = tweaks.Any(t => t.ServiceName == "memory" && t.IsSelected);
                if (!active && memorySelected)
                {
                    bool wasOn = memcleaner?.chkEnableAutoClean.IsChecked == true;
                    newBoostAutoMemPrevious = wasOn ? 1 : 0;
                    savesettings($"newboostautomem:{newBoostAutoMemPrevious}");
                    if (memcleaner != null) memcleaner.chkEnableAutoClean.IsChecked = true;
                    if (memcleaner != null) await memcleaner.cleanmemory();
                    messages.Add(Loc.T("Applied: Automatic memory cleaning (Memory Cleaner)"));
                }
                else if (active)
                {
                    if (newBoostAutoMemPrevious == 0)
                    {
                        if (memcleaner != null) memcleaner.chkEnableAutoClean.IsChecked = false;
                        messages.Add(Loc.T("Automatic memory cleaning was turned off again."));
                    }
                    newBoostAutoMemPrevious = -1;
                    savesettings("newboostautomem:-1");
                }
                newBoostActive = !active;
                savesettings($"newboost:{(newBoostActive ? "1" : "0")}");
            }
            else
            {
                var services = turboBoostServices.ToList();
                if (active)
                {
                    var started = await Task.Run(MultronWinCleaner.Processes.Optimizer.StartStoppedServices);
                    messages = new List<string> { started.Count == 0 ? Loc.T("No paused services had to be started again.") : Loc.F("Started again: {0}", string.Join(", ", started)) };
                }
                else
                {
                    var stopped = await Task.Run(() => MultronWinCleaner.Processes.Optimizer.StopOldSystemServices(services));
                    if (memcleaner != null) await memcleaner.cleanmemory();
                    messages = new List<string> { stopped.Count == 0 ? Loc.T("No selected service was running, so nothing had to be paused. Memory was cleaned.") : Loc.F("Paused: {0}. Memory was cleaned.", string.Join(", ", stopped)) };
                }
                turboBoostActive = !active;
                savesettings($"turboboost:{(turboBoostActive ? "1" : "0")}");
            }
            return messages;
        }

        private void StopStartService(string serviceName, bool start)
        {
            try
            {
                ServiceController sc = new ServiceController(serviceName);
                if (start)
                {
                    if (sc.Status != ServiceControllerStatus.Running && sc.Status != ServiceControllerStatus.StartPending)
                    {
                        sc.Start();
                        sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(10));
                    }
                }
                else
                {
                    if (sc.Status != ServiceControllerStatus.Stopped && sc.Status != ServiceControllerStatus.StopPending)
                    {
                        sc.Stop();
                        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Service {serviceName} error: {ex.Message}");
            }
        }

        private void StartupManager_Click(object sender, RoutedEventArgs e)
        {
            if(startupmanager == null)
            {
                startupmanager = new StartupManager(this);
                window.settings.Close();
                window.settings = new Settings(memcleaner, window.utilities, window, startupmanager);
                window.settings.Show();
                window.settings.Hide();
            }
            startupmanager.BringToFront();
        }
    }
}
