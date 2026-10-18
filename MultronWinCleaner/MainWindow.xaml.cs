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
        public List<StackPanel> stackpanels = new List<StackPanel>();
        public List<Expander> expanders = new List<Expander>();
        public List<ListBox> listboxes = new List<ListBox>();
        public List<ComboBox> comboboxlist = new List<ComboBox>();
        public Dictionary<string, bool> databaseDefaults = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        public List<string> paths = new List<string>();
        public CancellationTokenSource dismcancel = new CancellationTokenSource();
        public Utilities utilities = null!;
        public byte autoclean = 0;
        public byte startupclean = 0;
        public byte startupscan = 0;
        public byte onclean = 0;
        public byte killer = 0;
        public readonly TrayIconAnimator _trayAnimator ;
        public string extensions = ".log.etl.dmp.trace.tmp.temp.bak.swp";
        string settingsPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Settings.txt");
        private DispatcherTimer diskSpaceTimer;
        public MainWindow()
        {
       

            InitializeComponent();
            _trayAnimator = new TrayIconAnimator(TrayIcon, "pack://application:,,,/MultronWinCleaner;component/Assets/mwc_icon.ico", "pack://application:,,,/MultronWinCleaner;component/Assets/TrayFrames/frame{0:00}.ico", frameCount: 12);

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

            label1_Copy.Text = Loc.T("Ready for the scan");

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
                    double freeSpaceGB = drive.AvailableFreeSpace / (1024.0 * 1024 * 1024);
                    double totalSizeGB = drive.TotalSize / (1024.0 * 1024 * 1024);

                    string model = string.IsNullOrEmpty(diskModel) ? Loc.T("Disk") : diskModel;
                    labelDiskSize.Text = Loc.F("[{0}] Drive {1} Free: {2:F2} GB / Total: {3:F2} GB", model, driveLetter, freeSpaceGB, totalSizeGB);
                }
            }
            catch (Exception)
            {
                labelDiskSize.Text = Loc.T("Disk size could not be read.");
            }
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
                LoadingSpinnerRotation.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty,
                    new System.Windows.Media.Animation.DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.8)) { RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever });
                StatusLoad.BeginAnimation(UIElement.OpacityProperty,
                    new System.Windows.Media.Animation.DoubleAnimation(1.0, 0.3, TimeSpan.FromSeconds(0.8)) { AutoReverse = true, RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever });
            }
            else
            {
                LoadingSpinnerRotation.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, null);
                StatusLoad.BeginAnimation(UIElement.OpacityProperty, null);
            }
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

        private static bool IsSettingOnByDefault(string key)
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

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
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

            bool offlineMode = IsOfflineModeEnabled();
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
            }

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

            LoadingOverlay.Visibility = Visibility.Collapsed;

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
                    Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(WarningColor))
                });
                return;
            }

            CheckBox mrt = new CheckBox
            {
                Content = Loc.T("Microsoft MRT Scan") + "=" + Loc.T("Scans with the Malicious Software Removal Tool built into Windows. It runs hidden, before the cloud scan.") + "=" + MrtOptionKey,
                Margin = new Thickness(10),
                IsChecked = IsSettingOnByDefault(MrtScanSettingKey),
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
                Margin = new Thickness(10, 0, 10, 6),
                FontSize = 12,
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                Foreground = new SolidColorBrush(Colors.Black),
                Background = new SolidColorBrush(Colors.White),
                BorderBrush = new SolidColorBrush(Colors.Gray),
                BorderThickness = new Thickness(1),
                ItemsSource = new List<string> { Loc.T("Quick Scan"), Loc.T("Full Scan") },
                SelectedIndex = string.Equals(ReadSettingValue(MrtModeSettingKey), "full", StringComparison.OrdinalIgnoreCase) ? 1 : 0,
                IsEnabled = mrt.IsChecked == true,
                ToolTip = Loc.T("Quick: the places malicious software usually uses, a few minutes.\nFull: every fixed drive, this can take hours.")
            };

            CheckBox remove = new CheckBox
            {
                Content = Loc.T("Remove found malicious software right away"),
                Margin = new Thickness(10, 0, 10, 10),
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

            if (automatic && IsSettingOnByDefault(NotifyMalwareSettingKey)
                && outcome is MultronWinCleaner.Processes.MrtScanner.Outcome.Clean or MultronWinCleaner.Processes.MrtScanner.Outcome.Threats)
            {
                bool threats = outcome == MultronWinCleaner.Processes.MrtScanner.Outcome.Threats;
                var notify = new Notify(Loc.T(threats ? "Microsoft MRT found malicious software!" : "Microsoft MRT scan finished."), result.Message, null, BringToFront);
                (threats ? notify.AsWarning() : notify).Show();
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
            public MalwareListRow(string source, string verdictText, string fileName, string threatName, string filePath)
            {
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
                Content = Loc.T("Multron Malware Scan Cloud") + " (" + Loc.T("Testing") + ")=" + Loc.T("Scans your system for malicious software in the cloud with the viruskov.com OPEN-EDR engine") + "=malscan",
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
                Margin = new Thickness(10, 0, 10, 10),
                FontSize = 12,
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                Foreground = new SolidColorBrush(Colors.Black),
                Background = new SolidColorBrush(Colors.White),
                BorderBrush = new SolidColorBrush(Colors.Gray),
                BorderThickness = new Thickness(1),
                IsEnabled = malscan.IsChecked == true,
                ItemsSource = new List<string> { Loc.T("Quick Scan"), Loc.T("Full Scan"), Loc.T("Custom Scan") },
                SelectedIndex = (int)MultronWinCleaner.MalwareScan.GetSavedMode(),
                ToolTip = Loc.T("Quick: running processes, startup items, Downloads, Desktop and temp folders.\nFull: Quick + the whole Windows drive.\nCustom: the drives, folders and files set in the Malware Scan window.")
            };

            TextBlock malwareSettingsLink = new TextBlock { Margin = new Thickness(12, 0, 10, 10), FontSize = 12 };
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
                label1_Copy.Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(InfoColor));
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
                    buttonStartScan.Content = Loc.T("Clean");
                    buttonStartScan.IsEnabled = wasEnabled;
                    buttonStartScan.ToolTip = null;
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
                ShortcutResultsList.ItemsSource = foundShortcuts;
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
            ShortcutResultsList.ItemsSource = foundShortcuts;
            ShortcutResultsStatus.Text = Loc.F("{0} repaired, {1} moved to the Recycle Bin", repaired, removed) + (remaining.Count > 0 ? Loc.F(", {0} could not be changed.", remaining.Count) : ".");
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
            FirewallRulesList.ItemsSource = foundFirewallRules;
            FirewallResultsStatus.Text = remaining.Count == 0
                ? Loc.F(removed == 1 ? "{0} firewall rule removed." : "{0} firewall rules removed.", removed)
                : Loc.F(removed == 1 ? "{0} firewall rule removed." : "{0} firewall rules removed.", removed) + " " + Loc.F("{0} could not be removed (they may be managed by your organization).", remaining.Count);
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
                FirewallRulesList.ItemsSource = foundFirewallRules;
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
                DuplicateCancelButton.Visibility = Visibility.Collapsed;
                bool wasWaiting = duplicateCancelMode;
                LeaveDuplicateCancelMode();
                if (wasWaiting && Equals(buttonStartScan.Content, Loc.T("Clean")))
                {
                    progressBar1.Value = 100;
                    label1_Copy.Text = DuplicateResultsStatus.Text;
                    label1_Copy.Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(InfoColor));
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
            buttonStartScan.Content = Loc.T("Clean");
            buttonStartScan.IsEnabled = duplicateCancelWasEnabled;
            buttonStartScan.ToolTip = null;
        }

        private void ShowDuplicateProgress(string status)
        {
            if (!duplicateCancelMode)
                return;
            label1_Copy.Text = Loc.F("Duplicate files: {0}", status);
            label1_Copy.Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(InfoColor));
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

        private static string? ReadSettingValue(string key)
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
                "malscan" => (Loc.T("Multron Malware Scan Cloud (Testing)"), Loc.T("Scans your system for malicious software in the cloud with the viruskov.com OPEN-EDR engine")),
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

        private Expander? optionsSection;
        private WrapPanel? optionsSectionPanel;

        private void AddOptionExpander(Expander expander)
        {
            if (!IsSimpleInterface())
            {
                wrapPanel1.Children.Add(expander);
                return;
            }

            if (optionsSection == null || optionsSection.Parent != wrapPanel1)
            {
                optionsSectionPanel = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(5) };
                optionsSection = new Expander
                {
                    Header = MultronWinCleaner.Processes.Load.SectionHeader("\uE90F " + Loc.T("Extra Scans & Tools")),
                    Tag = Loc.T("Extra Scans & Tools"),
                    Margin = new Thickness(5, 10, 5, 5),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(5, 5, 5, 10),
                    IsExpanded = true,
                    FontSize = 15,
                    FontWeight = FontWeights.Bold,
                    Content = optionsSectionPanel
                };
                optionsSection.SetResourceReference(Control.ForegroundProperty, "Text");
                optionsSection.SetResourceReference(Control.BorderBrushProperty, "Text");
                wrapPanel1.Children.Add(optionsSection);
            }
            optionsSectionPanel!.Children.Add(expander);
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
            ApplyOptionTemplate(checkbox);
            checkboxes2.Add(checkbox);

            Expander? existingExpander = expanders.FirstOrDefault(e => e.Header != null && e.Header.ToString() == expander);

            if (existingExpander != null)
            {
                
                if (existingExpander.Content is StackPanel existingPanel)
                {
                    existingPanel.Children.Add(checkbox);
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
        }

        private System.Windows.Shapes.Path? _arcPath;
        public void UpdateArc(double percent)
        {

            percent = Math.Max(0, Math.Min(100, percent));

            double angle = 360 * (percent / 100);
            double radius = progressBar1.ActualWidth / 2 - 7.5;
            var center = new System.Windows.Point(progressBar1.ActualWidth / 2, progressBar1.ActualHeight / 2);

            if (_arcPath == null)
                _arcPath = (System.Windows.Shapes.Path)progressBar1.Template.FindName("PART_Indicator", progressBar1);

            if (_arcPath == null) return;

            Geometry g;

            if (percent <= 0)
            {
                g = Geometry.Empty;
            }
            else if (percent >= 99.999)
            {
                g = new EllipseGeometry(center, radius, radius);
            }
            else
            {
                var start = new System.Windows.Point(center.X, center.Y - radius);

                double rad = (Math.PI / 180) * (angle - 90);
                var end = new System.Windows.Point(
                    center.X + radius * Math.Cos(rad),
                    center.Y + radius * Math.Sin(rad));

                bool largeArc = angle > 180;

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
        }

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
            if (!ConfirmExitIfBusy())
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

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            if (settings.chkTrayIcon.IsChecked == true && TrayIcon.Visibility == Visibility.Visible)
            {
                this.Hide();
                settings.Hide();
            }
            else
            {
                if (!ConfirmExitIfBusy())
                    return;
                ExitApplication();
            }

        }
       
     

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
                buttonMaximize.Content = "↔";
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
                buttonMaximize.Content = "↔";
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
                            main.label1_Copy.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1e88e5"));
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
            void HideChildrenOfAllDockPanels(Panel parent)
            {
                foreach (UIElement child in parent.Children)
                {
                    if (child is DockPanel dockPanel)
                    {
                        foreach (UIElement dpChild in dockPanel.Children)
                        {
                            dpChild.Visibility = Visibility.Hidden;
                        }
                    }

                    if (child is Panel nestedPanel)
                    {
                        HideChildrenOfAllDockPanels(nestedPanel);
                    }
                }
            }
            public async Task run()
            {
                await main.Dispatcher.InvokeAsync(() =>
                {
                    HideChildrenOfAllDockPanels(main.Panel);
                    main.ScrollViewerDirectories.Visibility = Visibility.Visible;
                    main.wrapPanelDirectories.Visibility = Visibility.Visible;
                    main.progressBar1.Minimum = 0;
                    main.progressBar1.Maximum = 100;
                    main.progressBar1.Value = 0;

                    main.wrapPanelDirectories.Children.Add(new TextBlock
                    {
                        Text = Loc.T("Started..."),
                        Foreground = System.Windows.Media.Brushes.Black,
                        Margin = new Thickness(5)
                    });
                });

                var dotsTask = ScandotsAsync(Loc.T("Killing"), cts);
                int progress = 0;
                int killed = 0;
                int deleted = 0;
                List<string> lockedPaths = await main.Dispatcher.InvokeAsync(() =>
                    main.paths.Select(p => main.stringtokenizer(p, "=", 0) ?? "").Where(p => p.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList());

                for (int i = 0; i < lockedPaths.Count; i++)
                {
                    if (main.cancelstatus.IsCancellationRequested) break;

                    string path = lockedPaths[i];

                    if (System.IO.File.Exists(path))
                    {
                        long fileSize = 0;
                        string? error = TryDeleteLocked(path, ref fileSize, out bool inUse);

                        if (error != null && inUse)
                        {
                            foreach (var locker in WhoUsesFile.GetLockers(path))
                            {
                                if (WhoUsesFile.IsProtectedProcess(locker.Id, locker.Name))
                                {
                                    await AddKillMessage(Loc.F("Skipped system process: {0} (PID {1}) for {2}", locker.Name, locker.Id, path), System.Windows.Media.Brushes.Goldenrod);
                                    continue;
                                }

                                try
                                {
                                    await AddKillMessage(Loc.F("Killing: {0} (PID {1}) for {2}", locker.Name, locker.Id, path), System.Windows.Media.Brushes.Red);
                                    using (Process proc = Process.GetProcessById(locker.Id))
                                    {
                                        proc.Kill();
                                        proc.WaitForExit(5000);
                                    }
                                    killed++;
                                }
                                catch (ArgumentException)
                                {
                                }
                                catch (Exception ex)
                                {
                                    await AddKillMessage(Loc.F("Cannot Kill: {0} (PID {1}) - {2}", locker.Name, locker.Id, ex.Message), System.Windows.Media.Brushes.Goldenrod);
                                }
                            }

                            for (int attempt = 0; attempt < 3 && error != null && inUse; attempt++)
                            {
                                await Task.Delay(300);
                                error = TryDeleteLocked(path, ref fileSize, out inUse);
                            }
                        }

                        if (error == null)
                        {
                            deleted++;
                            totalsize += fileSize;
                            await AddKillMessage(Loc.F("Deleted: {0} ({1})", path, main.formatsize(fileSize)), System.Windows.Media.Brushes.Green);
                        }
                        else
                        {
                            await AddKillMessage(Loc.F("Cannot Delete: {0} - {1}", path, error), System.Windows.Media.Brushes.Goldenrod);
                        }
                    }
                    else
                    {
                        await AddKillMessage(Loc.F("File no longer exists: {0}", path), InfoBrush);
                    }

                    progress++;
                    await main.Dispatcher.InvokeAsync(() =>
                    {
                        double percent = (progress * 100.0) / lockedPaths.Count;
                        main.progressBar1.Value = percent;
                        main.UpdateArc(percent);
                    });
                }

                await cts.CancelAsync();
                await dotsTask;

                await main.Dispatcher.InvokeAsync(() =>
                {
                    string message;
                    System.Windows.Media.Brush labelColor;

                    if (main.cancelstatus.IsCancellationRequested)
                    {
                        message = Loc.F("Killing Canceled. {0} Process Killed, {1} Files Deleted. Freed Space: {2}", killed, deleted, main.formatsize(totalsize));
                        labelColor = System.Windows.Media.Brushes.Goldenrod;
                    }
                    else
                    {
                        message = Loc.F("Killing Done! {0} Process Killed, {1} Files Deleted. Freed Space: {2}", killed, deleted, main.formatsize(totalsize));
                        labelColor = System.Windows.Media.Brushes.Red;
                    }

                    main.label1_Copy.Text = message;
                    main.label1_Copy.Foreground = labelColor;
                    main.buttonReset.Visibility = Visibility.Visible;
                    main.buttonStartScan.Content = Loc.T("Scan");
                    main.buttonStartScan.IsEnabled = false;
                    main.paths.Clear();
                });
            }

            private static readonly System.Windows.Media.SolidColorBrush InfoBrush = CreateFrozenBrush("#1e88e5");

            private static System.Windows.Media.SolidColorBrush CreateFrozenBrush(string hex)
            {
                var brush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
                brush.Freeze();
                return brush;
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

            private async Task AddKillMessage(string text, System.Windows.Media.Brush brush)
            {
                if (brush.CanFreeze && !brush.IsFrozen)
                    brush.Freeze();
                await main.Dispatcher.InvokeAsync(() =>
                {
                    main.wrapPanelDirectories.Children.Add(new TextBlock
                    {
                        Text = text,
                        Foreground = brush,
                        FontSize = 16,
                        Margin = new Thickness(5)
                    });
                });
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

                wrapPanelDirectories.Children.Clear();
                MultronWinCleaner.Processes.Clean clean = new MultronWinCleaner.Processes.Clean(this);
                await Task.Run(() => clean.run());
            }
            else if (buttonStartScan.Content.Equals(Loc.T("Scan")))
            {
                DuplicateResultsPanel.Visibility = Visibility.Collapsed;
                FirewallResultsPanel.Visibility = Visibility.Collapsed;
                ShortcutResultsPanel.Visibility = Visibility.Collapsed;
                SecurityResultsPanel.Visibility = Visibility.Collapsed;
                HideSystemChecks();
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
                    label1_Copy.Foreground = System.Windows.Media.Brushes.Goldenrod;
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
        private MalwareScanProgress.ScanState? lastMalwareState;
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
                if (automatic && IsSettingOnByDefault(NotifyMalwareSettingKey) && lastMalwareState != null)
                {
                    bool threatsFound = lastMalwareThreats > 0 || lastMalwareState == MalwareScanProgress.ScanState.Threats;
                    if (threatsFound || lastMalwareState == MalwareScanProgress.ScanState.Clean)
                    {
                        var notify = new Notify(Loc.T(threatsFound ? "Malware scan finished: threats found!" : "Malware scan finished."),
                            lastMalwareStatus, null, () => utilities?.malwarescan?.ShowResults());
                        (threatsFound ? notify.AsWarning() : notify).Show();
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

        public void ResetMalwareResultsPanel()
        {
            MalwareResultsPanel.Visibility = Visibility.Collapsed;
            MalwareResultsStatus.Text = "";
            MalwareThreatsList.ItemsSource = null;
            MalwareThreatsList.Visibility = Visibility.Collapsed;
            mrtRows.Clear();
            cloudRows = new List<MalwareListRow>();
            mrtPanelStatus = null;
            cloudPanelStatus = null;
            mrtThreatCount = 0;
            lastMrtResult = null;
        }

        public void UpdateMalwareResultsPanel(MalwareScanProgress.ScanState state, string status,
            IEnumerable<MultronWinCleaner.Processes.CloudScanResult>? threats = null, int threatCount = 0)
        {
            var threatList = threats?.ToList();
            int threatsFound = Math.Max(threatCount, threatList?.Count ?? 0);
            lastMalwareState = state;
            lastMalwareStatus = status;
            lastMalwareThreats = threatsFound;
            string color = state switch
            {
                _ when threatsFound > 0 => ProblemColor,
                MalwareScanProgress.ScanState.Clean => OkColor,
                MalwareScanProgress.ScanState.Threats => ProblemColor,
                MalwareScanProgress.ScanState.Stopped or MalwareScanProgress.ScanState.Failed => WarningColor,
                _ => InfoColor
            };
            bool scanning = state == MalwareScanProgress.ScanState.Scanning;
            cloudPanelStatus = status;
            cloudPanelColor = color;
            if (threatList != null || !scanning)
                cloudRows = (threatList ?? new List<MultronWinCleaner.Processes.CloudScanResult>())
                    .Select(t => new MalwareListRow(Loc.T("Multron Cloud"), t.VerdictText, t.FileName, t.ThreatName, t.FilePath))
                    .ToList();
            MalwareResultsButton.Content = Loc.T(state == MalwareScanProgress.ScanState.Threats || threatsFound > 0 ? "Show Threats" : "Open Results");
            RefreshMalwarePanel(scanning);
            if (scanning)
            {
                bool starting = malwareCancelRestore == null;
                EnterMalwareCancelMode("Clean");
                if (Equals(malwareCancelRestore, "Clean"))
                {
                    if (starting)
                        progressBar1.Value = 0;
                    label1_Copy.Text = Loc.F("Malware scan: {0}", status);
                    label1_Copy.Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(InfoColor));
                }
            }
            else if (Equals(malwareCancelRestore, "Clean"))
                LeaveMalwareCancelMode();

            if (state != MalwareScanProgress.ScanState.Scanning && Equals(buttonStartScan.Content, Loc.T("Clean")))
            {
                bool anyThreats = state == MalwareScanProgress.ScanState.Threats || threatsFound > 0 || mrtThreatCount > 0;
                progressBar1.Value = 100;
                label1_Copy.Text = Loc.T(anyThreats ? "Malware scan finished: threats found!" : "Malware scan finished.");
                label1_Copy.Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(anyThreats ? ProblemColor : color));
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
            utilities?.malwarescan?.ShowResults();
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
            label1_Copy.Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(InfoColor));
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
            buttonStartScan.Content = Loc.T(malwareCancelRestore);
            buttonStartScan.IsEnabled = malwareCancelWasEnabled;
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
            label1_Copy.Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
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
            if (percent.HasValue)
                progressBar1.Value = Math.Clamp(percent.Value, 0, 100);
            SetStatusText(Loc.F("{0}: {1}", tool, text), warning ? WarningColor : InfoColor);
        }

        public void FinishToolStatus(string tool, string text, bool problem)
        {
            if (activeTool != tool)
                return;
            activeTool = null;
            if (IsScanOrCleanBusy)
                return;
            progressBar1.Value = toolSavedProgress;
            if (!string.IsNullOrWhiteSpace(text))
                SetStatusText(Loc.F("{0}: {1}", tool, text), problem ? WarningColor : OkColor);
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

        public void SetStandaloneMalwareProgress(int done, int total, int threats)
        {
            if (total <= 0 || activeTool != Loc.T("Malware Scan"))
                return;
            ShowToolStatus(Loc.T("Malware Scan"), Loc.F("{0:N0} / {1:N0} files checked, {2} threat(s) found", done, total, threats),
                done * 100.0 / total, threats > 0);
        }

        public void SetStandaloneMalwareStatus(string text, bool problem) => FinishToolStatus(Loc.T("Malware Scan"), text, problem);

        private async void ButtonStartScan_Click(object sender, RoutedEventArgs e)
        {
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
                label1_Copy.Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(WarningColor));
                CancelDuplicateReport();
                return;
            }
            if (malwareCancelRestore != null)
            {
                buttonStartScan.IsEnabled = false;
                if (Equals(malwareCancelRestore, "Clean"))
                    MalwareCancelButton.IsEnabled = false;
                label1_Copy.Text = Loc.T("Stopping the malware scan...");
                label1_Copy.Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(WarningColor));
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
                label1_Copy.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1e88e5"));
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
        private void OpenSettings_Click(object sender, RoutedEventArgs e)
        {

            settings.Show();
            settings.WindowState = WindowState.Normal;
        }

        private void UtilitiesButton_Click(object sender, RoutedEventArgs e)
        {

            utilities.Show();
            settings.WindowState = WindowState.Normal;

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
                    main.label1_Copy.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1e88e5"));
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
            ButtonLockedFiles.Visibility = Visibility.Hidden;
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

        private void ButtonReset_Click(object sender, RoutedEventArgs e)
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
            buttonReset.Visibility = Visibility.Hidden;
            ScrollViewerDetectedFiles.Visibility = Visibility.Visible;
            wrapPanel1.Visibility = Visibility.Visible;
            ButtonLockedFiles.Visibility = Visibility.Hidden;
            buttonStartScan.Content = Loc.T("Scan");
            buttonStartScan.IsEnabled = true;

            progressBar1.Value = 0;
            startupscan = 0;
            startupclean = 0;
            autoclean = 0;
            cancelclean = 0;
            scanstatus = 0;
            reset = 0;
        }
        int backuped = 0;

        private async void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is not TextBox textBox) return;
            string searchText = textBox.Text.Trim().ToLower();

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
                    viewModel.Groups.Add(group);

                backuped = 0;
                return;
            }

            var filteredList = await Task.Run(() =>
                viewModelbac.Groups
                    .Where(g => !string.IsNullOrEmpty(g.Path) && g.Path.ToLower().Contains(searchText))
                    .ToList());

            viewModel.Groups.Clear();
            foreach (var group in filteredList)
                viewModel.Groups.Add(group);
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

        private void ApplyLibrarySearch(string query)
        {
            foreach (var expander in searchExpanded)
                expander.IsExpanded = false;
            searchExpanded.Clear();

            foreach (var expander in ChildExpanders(wrapPanel1))
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

        private async void SelectAll_wpanel_Click(object sender, RoutedEventArgs e)
        {
            var dialogResult = await ShowCustomDialogAsync(
      Loc.T("Warning"),
      Loc.T("Selecting all items across all groups may cause critical like browser history & downloads and recent files to be permanently deleted during cleanup.\n\nAre you sure you want to select everything?"),
      CustomDialogIcon.Warning,
      CustomDialogButtons.YesNo);

            if (dialogResult != CustomDialogResult.Yes)
            {
                return;
            }
            int i = 0;
            foreach (CheckBox box in checkboxes2)
            {

                box.IsChecked = true;
                i++;
            }

        }

        private void UnselectAll_wpanel_Click(object sender, RoutedEventArgs e)
        {
       
            foreach (CheckBox box in checkboxes2)
            {

                box.IsChecked = false;
            }

        }
        private void SelectAll_AllGroups_Click(object sender, RoutedEventArgs e)
        {
           
       

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
                DialogIconText.Foreground = new System.Windows.Media.SolidColorBrush(iconColor);
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

        private static System.Drawing.Icon? warningTrayIcon;

        private static System.Drawing.Icon CreateWarningTrayIcon()
        {
            var info = Application.GetResourceStream(new Uri("pack://application:,,,/MultronWinCleaner;component/Assets/mwc_icon.ico"));
            using var baseIcon = new System.Drawing.Icon(info.Stream, 32, 32);
            using var bitmap = new System.Drawing.Bitmap(32, 32);
            using (var g = System.Drawing.Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                g.DrawIcon(baseIcon, new System.Drawing.Rectangle(0, 0, 32, 32));
                var badge = new System.Drawing.Rectangle(15, 15, 17, 17);
                using (var fill = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 255, 152, 0)))
                    g.FillEllipse(fill, badge);
                using (var border = new System.Drawing.Pen(System.Drawing.Color.White, 1.5f))
                    g.DrawEllipse(border, badge);
                using var font = new System.Drawing.Font("Segoe UI", 10f, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Pixel);
                using var format = new System.Drawing.StringFormat { Alignment = System.Drawing.StringAlignment.Center, LineAlignment = System.Drawing.StringAlignment.Center };
                g.DrawString("!", font, System.Drawing.Brushes.White, new System.Drawing.RectangleF(15, 15, 17, 17), format);
            }
            return System.Drawing.Icon.FromHandle(bitmap.GetHicon());
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
                if (warnings.Count > 0)
                    warningTrayIcon ??= CreateWarningTrayIcon();
                _trayAnimator?.SetWarningIcon(warnings.Count > 0 ? warningTrayIcon : null);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Tray warning icon failed: " + ex.Message);
            }
            string tip = "Multron Win Cleaner" + (warnings.Count > 0 ? "\n⚠ " + string.Join("\n⚠ ", warnings) : "");
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
                new Notify(Loc.T("Update Available"), text, "", () => _ = InstallAppUpdateAsync()).Show();
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
            label1_Copy.Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
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
                }

                if (databaseReloadPending)
                {
                    if (beforeScan || (MultronWinCleaner.Processes.Updater.IsAutoReloadEnabled() && IsIdleForDatabaseReload()))
                    {
                        ShowDatabaseStatus(Loc.T("Loading the new database..."), InfoColor, force);
                        await ReloadDatabaseAsync(true);
                        databaseReloadPending = false;
                        ShowDatabaseStatus(Loc.F("The database was updated to version {0}.", downloadedDatabaseVersion ?? ""), OkColor, force);
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
            wrapPanelDirectories.Children.Clear();
            ScrollViewerDirectories.Content = null;
            checkboxes2.Clear();
            listboxes.Clear();
            expanders.Clear();
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
            if (!ConfirmExitIfBusy())
            {
                e.Cancel = true;
                return;
            }
            ExitApplication();
        }
    }
}
