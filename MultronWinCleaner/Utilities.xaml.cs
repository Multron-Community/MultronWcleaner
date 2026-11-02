using Microsoft.Win32;
using Multron_Win_Cleaner;
using MultronWinCleaner;
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

        public Utilities(MainWindow window)
        {
            InitializeComponent();
            this.window = window;
            memcleaner = new MemCleaner(window);
            configure = new Configure(this);
            startupmanager = new StartupManager(this);

            WatchToolProgress(FirewallProgress, "Firewall Rule Cleaner", () => (FirewallScanResultLabel.Content?.ToString() ?? "", FirewallScanResultLabel.Foreground));
            WatchToolProgress(ShortcutProgress, "Shortcut Fixer", () => (ShortcutResultLabel.Content?.ToString() ?? "", ShortcutResultLabel.Foreground));
            WatchToolProgress(SecurityProgress, "Security Check", () => (SecurityResultLabel.Text, SecurityResultLabel.Foreground));

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

            malwarescan.Show();
            if (malwarescan.WindowState == WindowState.Minimized)
                malwarescan.WindowState = WindowState.Normal;
            malwarescan.Activate();
            malwarescan.Topmost = true;
            malwarescan.Topmost = false;
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
                    Uri iconUri = new Uri("pack://application:,,,/Assets/mwc_utilities.ico", UriKind.Absolute);
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

                var contextMenu = new System.Windows.Forms.ContextMenuStrip();
                contextMenu.Items.Add(Loc.T("Show Utilities"), null, (s, e) => { this.Show(); this.WindowState = WindowState.Normal; });
                contextMenu.Items.Add(Loc.T("Hide Utilities"), null, (s, e) => { this.Hide(); });

                trayIcon.ContextMenuStrip = contextMenu;
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
                        (solid.Color == Colors.LimeGreen || solid.Color == (Color)ColorConverter.ConvertFromString(MainWindow.OkColor));
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
                        ShortcutList.Items.Add($"{Loc.T(item.Location)}: {item.Name} → {item.TargetPath}  ({item.Action})");
                    if (brokenShortcuts.Count > 0)
                    {
                        ShortcutResultLabel.Content = Loc.F("{0} broken shortcut(s) found.", brokenShortcuts.Count);
                        ShortcutResultLabel.Foreground = Brushes.OrangeRed;
                        ShortcutScan.Content = Loc.T("Fix");
                    }
                    else
                    {
                        ShortcutResultLabel.Content = Loc.T("No broken shortcuts found.");
                        ShortcutResultLabel.Foreground = Brushes.LimeGreen;
                        ShortcutScan.Content = Loc.T("ReScan");
                    }
                }
                ShortcutList.Visibility = ShortcutList.Items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                ShortcutResultLabel.Content = Loc.F("Shortcut Fixer failed: {0}", ex.Message);
                ShortcutResultLabel.Foreground = Brushes.OrangeRed;
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
                ShortcutList.Items.Add(Loc.T(result.Failed.Contains(item) ? "Could not fix: " : item.RepairTarget != null ? "Repaired: " : "Moved to the Recycle Bin: ") + item.Name);
            brokenShortcuts = new List<MultronWinCleaner.Processes.ShortcutFixer.BrokenShortcut>();
            ShortcutResultLabel.Content = Loc.F("{0} repaired, {1} moved to the Recycle Bin", result.Repaired, result.Removed) + (result.Failed.Count > 0 ? Loc.F(", {0} failed.", result.Failed.Count) : ".");
            ShortcutResultLabel.Foreground = result.Failed.Count == 0 ? Brushes.LimeGreen : Brushes.OrangeRed;
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
            SecurityResultLabel.Text = suffix.Length > 0 ? status + " " + suffix : status;
            SecurityResultLabel.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
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
            FirewallScanResultLabel.Foreground = Brushes.DodgerBlue;
            FirewallScanResultLabel.Visibility = Visibility.Visible;
            try
            {
                invalidrules = await Task.Run(MultronWinCleaner.Processes.FirewallRules.FindInvalidRules);
                foreach (var rule in invalidrules)
                    FirewallInvalidRulesList.Items.Add(Loc.F("Invalid Path: {0}  ({1}, {2})", rule.ApplicationPath, rule.Name, Loc.T(rule.Direction)));
                FirewallInvalidRulesList.Visibility = invalidrules.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                FirewallScanResultLabel.Content = invalidrules.Count > 0 ? Loc.F("{0} invalid firewall rule(s) found.", invalidrules.Count) : Loc.T("No invalid firewall rules found.");
                FirewallScanResultLabel.Foreground = invalidrules.Count > 0 ? Brushes.OrangeRed : Brushes.LimeGreen;
                FirewallScan.Content = Loc.T(invalidrules.Count > 0 ? "Clean" : "ReScan");
                return invalidrules;
            }
            catch (Exception ex)
            {
                FirewallScanResultLabel.Content = Loc.F("Failed to read firewall rules: {0}", ex.Message);
                FirewallScanResultLabel.Foreground = Brushes.OrangeRed;
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
            ShortcutResultLabel.Foreground = Brushes.DodgerBlue;
            ShortcutResultLabel.Visibility = Visibility.Visible;
            try
            {
                brokenShortcuts = await Task.Run(MultronWinCleaner.Processes.ShortcutFixer.FindBrokenShortcuts);
                foreach (var item in brokenShortcuts)
                    ShortcutList.Items.Add($"{Loc.T(item.Location)}: {item.Name} → {item.TargetPath}  ({item.Action})");
                ShortcutList.Visibility = brokenShortcuts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                ShortcutResultLabel.Content = brokenShortcuts.Count > 0 ? Loc.F("{0} broken shortcut(s) found.", brokenShortcuts.Count) : Loc.T("No broken shortcuts found.");
                ShortcutResultLabel.Foreground = brokenShortcuts.Count > 0 ? Brushes.OrangeRed : Brushes.LimeGreen;
                ShortcutScan.Content = Loc.T(brokenShortcuts.Count > 0 ? "Fix" : "ReScan");
                return brokenShortcuts;
            }
            catch (Exception ex)
            {
                ShortcutResultLabel.Content = Loc.F("Shortcut Fixer failed: {0}", ex.Message);
                ShortcutResultLabel.Foreground = Brushes.OrangeRed;
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
                SecurityResultLabel.Text = suffix.Length > 0 ? status + " " + suffix : status;
                SecurityResultLabel.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
                SecurityFix.Visibility = securityIssues.Any(i => i.CanFix) ? Visibility.Visible : Visibility.Collapsed;
                SecurityScan.Content = Loc.T("ReScan");
                UpdateShowIgnoredButton();
            }
            catch (Exception ex)
            {
                SecurityResultLabel.Text = Loc.F("Security Check failed: {0}", ex.Message);
                SecurityResultLabel.Foreground = Brushes.OrangeRed;
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
                SecurityResultLabel.Text = Loc.T("Tick the settings you want to fix.");
                SecurityResultLabel.Foreground = Brushes.OrangeRed;
                SecurityResultLabel.Visibility = Visibility.Visible;
                return;
            }

            string message = "";
            bool done = await window.FixSecurityIssuesAsync(selected, SecurityFix, text =>
            {
                message = text;
                SecurityResultLabel.Text = text;
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
            SecurityResultLabel.Text = "";
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
            FirewallProgress.Value = 100;
            foreach (var rule in invalidrules)
                FirewallInvalidRulesList.Items.Add(Loc.F("Invalid Path: {0}  ({1}, {2})", rule.ApplicationPath, rule.Name, Loc.T(rule.Direction)));

            FirewallScanResultLabel.Visibility = Visibility.Visible;
            if (invalidrules.Count > 0)
            {
                FirewallInvalidRulesList.Visibility = Visibility.Visible;
                FirewallScanResultLabel.Content = Loc.F("{0} invalid firewall rule(s) found.", invalidrules.Count);
                FirewallScanResultLabel.Foreground = Brushes.OrangeRed;
            }
            else
            {
                FirewallProgress.Visibility = Visibility.Collapsed;
                FirewallScanResultLabel.Content = Loc.T("No invalid firewall rules found.");
                FirewallScanResultLabel.Foreground = Brushes.LimeGreen;
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
            var result = await Task.Run(() => MultronWinCleaner.Processes.FirewallRules.RemoveRules(rules));
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
            foreach (var rule in rules)
                FirewallInvalidRulesList.Items.Add(Loc.T(result.Failed.Contains(rule) ? "could not delete: " : "deleted: ") + rule.ApplicationPath);
            invalidrules = new List<MultronWinCleaner.Processes.FirewallRules.InvalidRule>();

            FirewallProgress.IsIndeterminate = false;
            FirewallProgress.Visibility = Visibility.Collapsed;
            FirewallReset.Visibility = Visibility.Visible;
            FirewallInvalidRulesList.Visibility = Visibility.Visible;
            FirewallScanResultLabel.Content = result.Failed.Count == 0
                ? Loc.F("{0} invalid firewall rule(s) deleted.", result.Removed)
                : Loc.F("{0} invalid firewall rule(s) deleted, {1} could not be deleted.", result.Removed, result.Failed.Count);
            FirewallScanResultLabel.Foreground = result.Failed.Count == 0 ? Brushes.LimeGreen : Brushes.OrangeRed;
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
                
            
            
            largefilefinder.Show();
        }
        private void TopPanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (e.ButtonState == MouseButtonState.Pressed) this.DragMove(); }
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
        
            
            Duplicate_File_Finder.Show();  
        
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
         
            
            memcleaner.Show();  
        
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
            startupmanager.Show();
        }
    }
}
