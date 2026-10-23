using IWshRuntimeLibrary;
using Microsoft.VisualBasic.ApplicationServices;
using Microsoft.Win32;
using Multron_Win_Cleaner;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.DirectoryServices.AccountManagement;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using static Multron_Win_Cleaner.MainWindow;

namespace MultronWinCleaner
{
    public partial class Settings : Window
    {
        public HashSet<string> excludedfiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private string[] excludedFolders = Array.Empty<string>();
        private readonly object exceptionsFileLock = new object();
        public string logfilepath = "";
        string excludedfilesdir = Environment.CurrentDirectory + "\\excluded.txt";

        MemCleaner? memcleaner;
        Utilities Utilities;
        MainWindow mainWindow;
        StartupManager? manager;

        public Settings(MemCleaner? memcleaner, Utilities utilities, MainWindow mainWindow, StartupManager? manager)
        {
            InitializeComponent();
            this.memcleaner = memcleaner;
            this.Utilities = utilities;
            this.mainWindow = mainWindow;
            this.manager = manager;

            foreach (var box in new[] { cmbAccessPreset, cmbAgePreset, cmbScheduleType, cmbPostCleanupAction })
                defaultComboIndexes[box] = box.SelectedIndex;

            LoadLanguageOptions();
            _ = getusers();
        }

        private readonly Dictionary<ComboBox, int> defaultComboIndexes = new Dictionary<ComboBox, int>();

        private static readonly string[] ResettableKeys =
        {
            "customday", "starttime", "customaccess", "endtime", "minutes", "loglocation", "logpath", "autoclean", "trayicon",
            MainWindow.OfflineModeSettingKey, MainWindow.AutoSaveSelectionsSettingKey, MainWindow.AllBrowserProfilesSettingKey,
            MainWindow.AdvancedInterfaceSettingKey, MainWindow.MrtScanSettingKey, MainWindow.MrtModeSettingKey, MainWindow.MrtRemoveSettingKey,
            MultronWinCleaner.MalwareScan.WithMainScanSettingKey, MultronWinCleaner.MalwareScan.ModeSettingKey,
            MultronWinCleaner.Processes.SecurityCheck.IgnoredSettingKey,
            "oldscan", "access_scan", "onlylowcpu", "runifactive", "pluggedin", "batterylow", "enablelog", "showlastlog",
            "cleanallusers", "cleanselectedusers", "startupscan", "startupclean", "startupnotifyscan", "startupnotifyclean",
            MainWindow.NotifySecuritySettingKey, MainWindow.NotifyFirewallSettingKey, MainWindow.NotifyDuplicatesSettingKey,
            MainWindow.NotifyMalwareSettingKey, Notify.PositionSettingKey, Notify.LayoutSettingKey,
            "accessscanindex", "oldscanindex", "scheduletype", "postaction", "shredmode",
            MultronWinCleaner.Processes.DeepLogOptions.ExtensionsKey, MultronWinCleaner.Processes.DeepLogOptions.MinAgeKey, MultronWinCleaner.Processes.DeepLogOptions.MinSizeKey,
            "day1", "day2", "day3", "day4", "day5", "day6", "day7", "limitweeks", "week1", "week2", "week3", "week4", "week5",
            MultronWinCleaner.Processes.Updater.AppUpdateSettingKey, MultronWinCleaner.Processes.Updater.AutoUpdateSettingKey,
            MultronWinCleaner.Processes.Updater.AutoUpdateHoursSettingKey, MultronWinCleaner.Processes.Updater.AutoReloadSettingKey,
            MultronWinCleaner.Processes.Updater.StatusSettingKey, MultronWinCleaner.Processes.Updater.ButtonSettingKey,
            MainWindow.ThemeFollowsWindowsSettingKey,
            Appearance.EnabledSettingKey, Appearance.ColorSettingKey, Appearance.LevelSettingKey
        };

        private void ResetToDefaults_Click(object sender, RoutedEventArgs e)
        {
            var answer = AppDialog.Show(this,
                Loc.T("Reset all settings in this window to their defaults?") + "\n\n" +
                Loc.T("Your language, exceptions, right-click menu, Windows startup and saved cleaning selections are kept."),
                Loc.T("Reset to defaults"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes)
                return;

            autoSaveTimer?.Stop();
            autoSaveReady = false;
            try
            {
                string path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Settings.txt");
                if (System.IO.File.Exists(path))
                {
                    var keys = new HashSet<string>(ResettableKeys, StringComparer.OrdinalIgnoreCase);
                    var kept = System.IO.File.ReadAllLines(path)
                        .Where(l => { int i = l.IndexOf(':'); return i <= 0 || !keys.Contains(l.Substring(0, i).Trim()); })
                        .ToList();
                    System.IO.File.WriteAllLines(path, kept);
                }

                foreach (var pair in defaultComboIndexes)
                    pair.Key.SelectedIndex = pair.Value;
                rbCleanAllUsers.IsChecked = true;
                LoadSettingsFromFile();
            }
            catch (Exception ex)
            {
                AppDialog.Show(Loc.F("Could not reset the settings: {0}", ex.Message), Loc.T("Settings"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                autoSaveReady = true;
            }

            mainWindow?.SetAutoSaveSelections(chkAutoSaveSelections.IsChecked == true);
            mainWindow?.ApplyOfflineModeToMalwareScan();
            mainWindow?.ApplyDatabaseUpdateSettings();
            mainWindow?.ReloadDatabaseIfIdle();
            mainWindow?.SetThemeFollowsWindows(true);
            Appearance.Apply();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void MaximizeButton_Click(object sender, RoutedEventArgs e) =>
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        private void Window_StateChanged(object? sender, EventArgs e)
        {
            bool maximized = WindowState == WindowState.Maximized;
            MaxHeight = SystemParameters.WorkArea.Height;
            MaxWidth = SystemParameters.WorkArea.Width;
            WindowFrame.Margin = new Thickness(maximized ? 0 : 24);
            WindowFrame.CornerRadius = new CornerRadius(maximized ? 0 : 22);
            if (System.Windows.Shell.WindowChrome.GetWindowChrome(this) is System.Windows.Shell.WindowChrome chrome)
                chrome.ResizeBorderThickness = new Thickness(maximized ? 0 : 24);
            MaximizeButton.Content = maximized ? "❐" : "□";
        }

        private bool languageLoading;

        private void LoadLanguageOptions()
        {
            languageLoading = true;
            string saved = Loc.ReadSavedLanguage();
            string windowsName = Loc.Languages.First(l => l.Code == Loc.DetectWindowsLanguage()).Name;
            var autoItem = new ComboBoxItem { Content = Loc.F("Automatic (Windows language: {0})", windowsName), Tag = Loc.AutoLanguage };
            cmbLanguage.Items.Add(autoItem);
            if (saved == Loc.AutoLanguage)
                cmbLanguage.SelectedItem = autoItem;
            foreach (var (code, name) in Loc.Languages)
            {
                var item = new ComboBoxItem { Content = name, Tag = code };
                cmbLanguage.Items.Add(item);
                if (code == saved)
                    cmbLanguage.SelectedItem = item;
            }
            languageLoading = false;
        }

        private void cmbLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (languageLoading || cmbLanguage.SelectedItem is not ComboBoxItem item || item.Tag is not string code) return;

            try
            {
                Loc.SaveLanguage(code);
            }
            catch (Exception ex)
            {
                AppDialog.Show(Loc.F("Could not save {0}: {1}", Loc.T("language"), ex.Message), Loc.T("Settings"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (Loc.Resolve(code) == Loc.Language) return;

            var answer = AppDialog.Show(Loc.T("The new language is applied after the app restarts. Restart now?"), Loc.T("Language"), MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Yes)
                Loc.Restart();
        }

        public void defaultloglocation()
        {
            if (logfilepath == "")
            {
                txtLogPath.Text = Environment.CurrentDirectory + "\\mwc_cleanlog.txt";
            }
        }
         
        public async Task getusers()
        {
            string currentUser = WindowsIdentity.GetCurrent().Name;
            string currentUserShort = currentUser.Contains("\\") ? currentUser.Split('\\')[1] : currentUser;

           
            List<string> foundUsers = new List<string>();
             
            await Task.Run(() =>
            {
                using (PrincipalContext ctx = new PrincipalContext(ContextType.Machine))
                {
                    UserPrincipal userPrincipal = new UserPrincipal(ctx);
                    using (PrincipalSearcher searcher = new PrincipalSearcher(userPrincipal))
                    {
                        foreach (var result in searcher.FindAll())
                        {
                            if (result is UserPrincipal user && !string.IsNullOrEmpty(user.SamAccountName))
                            {
                                foundUsers.Add(user.SamAccountName);
                            }
                        }
                    }
                }
            });
             
            comboBoxUserSelection.Items.Clear();
            foreach (var username in foundUsers)
            {
                comboBoxUserSelection.Items.Add(username);
                if (username.Equals(currentUserShort, StringComparison.OrdinalIgnoreCase))
                {
                    comboBoxUserSelection.SelectedItem = username;
                }
            }
        }

 
        private bool isClosed;

        protected override void OnClosed(EventArgs e)
        {
            isClosed = true;
            base.OnClosed(e);
        }

        private async Task StatusUpdateLoop()
        {
            while (!isClosed)
            {
                if (!IsVisible)
                {
                    await Task.Delay(1000);
                    continue;
                }
                string status = mainWindow?.AutoCleanStatus ?? "";
                txtStat.Text = Loc.F("Status: {0}", status.Length > 0 ? status : Loc.T(chkAutoClean.IsChecked == true ? "starting..." : "Automatic cleaning is off"));
                await Task.Delay(1000);
            }
        }

        public void LoadServiceSettings()
        {
            string settingsPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Settings.txt");
            if (!System.IO.File.Exists(settingsPath)) return;

            var lines = System.IO.File.ReadAllLines(settingsPath);
            var settingsDict = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
             
             
            foreach (var line in lines)
            {
                if (line.StartsWith("selectedservice:", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = line.Split(':');
                    if (parts.Length >= 3)
                    {
                        string serviceName = parts[1].Trim();
                        bool isSelected = parts[2].Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
                        settingsDict[serviceName] = isSelected;
                    }
                }
            }
             
            foreach (var svc in Utilities.turboBoostServices)
            {
                if (settingsDict.TryGetValue(svc.ServiceName, out bool isSelected))
                {
                    svc.IsSelected = isSelected;
                }
                else
                {
                    svc.IsSelected = true;  
                }
            }
        }
        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            LoadServiceSettings();
            _ = StatusUpdateLoop();

            if (!System.IO.File.Exists(excludedfilesdir))
            {
                System.IO.File.Create(excludedfilesdir).Close();
            }
            else
            {
                var excludeLines = await System.IO.File.ReadAllLinesAsync(excludedfilesdir);
                foreach (string line in excludeLines.Select(NormalizeExceptionPath).Where(l => l.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
                    lstExceptions.Items.Add(line);
                RebuildExceptions();
            }

            LoadSettingsFromFile();
        }

        private void LoadSettingsFromFile()
        {
            string settingsPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Settings.txt");
            var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (!System.IO.File.Exists(settingsPath))
            {
                System.IO.File.Create(settingsPath).Close();
            }
            else
            {
                var lines = System.IO.File.ReadAllLines(settingsPath);
                settings = lines
                    .Where(l => l.Contains(":") && !l.StartsWith("selectedservice:", StringComparison.OrdinalIgnoreCase))
                    .Select(l => l.Split(new[] { ':' }, 2))
                    .ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim(), StringComparer.OrdinalIgnoreCase);
            }

            string GetString(string key, string fallback = "") => settings.TryGetValue(key, out string? val) ? val ?? fallback : fallback;
            bool GetBool(string key) => GetString(key) == "1";

            void SetComboBoxSelection(ComboBox comboBox, string value)
            {
                if (string.IsNullOrEmpty(value)) return;
                foreach (ComboBoxItem item in comboBox.Items)
                {
                    if (Loc.En(item.Content).Equals(value, StringComparison.OrdinalIgnoreCase))
                    {
                        comboBox.SelectedItem = item;
                        break;
                    }
                }
            }
       

            chkAutoClean.IsChecked = GetBool("autoclean");
            chkTrayIcon.IsChecked = GetBool("trayicon");
            offlineModeLoading = true;
            chkOfflineMode.IsChecked = GetBool(MainWindow.OfflineModeSettingKey);
            offlineModeLoading = false;
            LoadDatabaseUpdateSettings();
            autoSaveSelectionsLoading = true;
            chkAutoSaveSelections.IsChecked = GetString(MainWindow.AutoSaveSelectionsSettingKey, "1") != "0";
            autoSaveSelectionsLoading = false;
            allBrowserProfilesLoading = true;
            chkCleanAllBrowserProfiles.IsChecked = GetString(MainWindow.AllBrowserProfilesSettingKey, "1") != "0";
            allBrowserProfilesLoading = false;
            interfaceModeLoading = true;
            if (GetBool(MainWindow.AdvancedInterfaceSettingKey))
                rbAdvancedInterface.IsChecked = true;
            else
                rbSimpleInterface.IsChecked = true;
            interfaceModeLoading = false;
            OnlyLowCPU.IsChecked = GetBool("onlylowcpu");
            RunIfInactive.IsChecked = GetBool("runifactive");
            SkipBattery.IsChecked = GetBool("batterylow");
            startupScanLoading = true;
            chkEnableStartupScan.IsChecked = GetBool("startupscan");
            startupScanLoading = false;
            startupCleanLoading = true;
            chkEnableStartupClean.IsChecked = GetBool("startupclean");
            startupCleanLoading = false;
            chkEnableNotifyScan.IsChecked = GetString("startupnotifyscan", "1") != "0";
            chkEnableNotifyClean.IsChecked = GetString("startupnotifyclean", "1") != "0";
            chkNotifySecurity.IsChecked = GetString(MainWindow.NotifySecuritySettingKey, "1") != "0";
            chkNotifyFirewall.IsChecked = GetString(MainWindow.NotifyFirewallSettingKey, "1") != "0";
            chkNotifyDuplicates.IsChecked = GetString(MainWindow.NotifyDuplicatesSettingKey, "1") != "0";
            chkNotifyMalware.IsChecked = GetString(MainWindow.NotifyMalwareSettingKey, "1") != "0";
            cmbNotifyPosition.SelectedIndex = (int)Notify.ReadPosition();
            cmbNotifyLayout.SelectedIndex = (int)Notify.ReadLayout();
            LoadContextMenuSettings();
            SetThemeFollowsWindowsBox(MainWindow.ThemeFollowsWindows);
            LoadTransparencySettings();
            _ = LoadRunAtStartupAsync();
            if (GetBool("cleanallusers"))
            {
                 rbCleanAllUsers.IsChecked = GetBool("cleanallusers");
            } 
          
            rbCleanSelectedUsers.IsChecked = GetBool("cleanselectedusers");
 

            AccessScan.IsChecked = GetBool("access_scan");
            OnlyBattery.IsChecked = GetBool("pluggedin");
            chkEnableLog.IsChecked = GetBool("enablelog");
            chkShowLastLog.IsChecked = GetBool("showlastlog");
            OldScan.IsChecked = GetBool("oldscan");
             if(memcleaner != null)
            {
                memcleaner.chkEnableAutoClean.IsChecked = GetBool("automemclean");
                memcleaner.chkSmartRAM.IsChecked = GetBool("mon");
                memcleaner.chkShowNotification.IsChecked = GetBool("sendnotify");
                memcleaner.chkSkipOnLowBattery.IsChecked = GetBool("skipiflow");
                memcleaner.chkTopMostMonitor.IsChecked = GetBool("topmost");

                memcleaner.chkClearTemp.IsChecked = GetBool("cleartemp");
                memcleaner.chkClearClipboard.IsChecked = GetBool("clearclip");
                memcleaner.chkStartWithWinCleaner.IsChecked = GetBool("startwithwincleaner");

                memcleaner.rbDeepClean.IsChecked = GetBool("deepclean");
                memcleaner.rbDeepCleanAggresive.IsChecked = GetBool("aggdeepclean");
                if (memcleaner.rbDeepClean.IsChecked == true)
                {
                    memcleaner.rbDeepCleanAggresive.IsChecked = false;
                }
                else if (memcleaner.rbDeepCleanAggresive.IsChecked == true)
                {
                    memcleaner.rbDeepClean.IsChecked = false;
                }
                else if (memcleaner.rbDeepClean.IsChecked == false && memcleaner.rbDeepCleanAggresive.IsChecked == false)
                {
                    memcleaner.rbQuickClean.IsChecked = true;
                }
            }
          


            if(manager != null)
               manager.tglShowNotifications.IsChecked = GetBool("startupwarning");

            txtStartTime.Text = GetString("starttime", "08:00");
            txtEndTime.Text = GetString("endtime", "22:00");
            txtCleaningInterval.Text = GetString("minutes", "0");
            txtCustomAccess.Text = GetString("customaccess");
            txtCustomDay.Text = GetString("customday");


          
            string logPath = GetString("logpath");
            if (!string.IsNullOrEmpty(logPath))
            {
                txtLogPath.Text = logPath;
                logfilepath = logPath;
            }
            else
            {
                defaultloglocation();
            }
            if (rbCleanAllUsers.IsChecked == true)
            {
                comboBoxUserSelection.IsEnabled = false;
            }
            else
            {
                comboBoxUserSelection.IsEnabled = true;
            }
            if (memcleaner != null) SetComboBoxSelection(memcleaner.cbCleanInterval, GetString("mcminutes"));
            SetComboBoxSelection(cmbAccessPreset, GetString("accessscanindex"));
            SetComboBoxSelection(cmbAgePreset, GetString("oldscanindex"));
            SetComboBoxSelection(cmbScheduleType, GetString("scheduletype"));
            SetComboBoxSelection(cmbPostCleanupAction, GetString("postaction"));

            string logExtensions = GetString(MultronWinCleaner.Processes.DeepLogOptions.ExtensionsKey);
            txtNewExtension.Text = MultronWinCleaner.Processes.DeepLogOptions.IsValidExtensionList(logExtensions)
                ? logExtensions : MultronWinCleaner.Processes.DeepLogOptions.DefaultExtensions;
            if (mainWindow != null)
                mainWindow.extensions = txtNewExtension.Text;
            txtLogMinAge.Text = int.TryParse(GetString(MultronWinCleaner.Processes.DeepLogOptions.MinAgeKey), out int logAge) ? Math.Max(0, logAge).ToString() : "0";
            txtLogMinSize.Text = int.TryParse(GetString(MultronWinCleaner.Processes.DeepLogOptions.MinSizeKey), out int logKb) ? Math.Max(0, logKb).ToString() : "0";

            shredModeLoading = true;
            cmbShredMode.SelectedIndex = int.TryParse(GetString("shredmode"), out int shredMode) ? Math.Clamp(shredMode, 0, 4) : 0;
            shredModeLoading = false;

            for (int i = 1; i <= 7; i++)
            {
                if (FindName($"Day{i}") is CheckBox dayBox)
                    dayBox.IsChecked = GetBool($"day{i}");
            }
            chkLimitWeeks.IsChecked = GetBool("limitweeks");
            for (int i = 1; i <= 5; i++)
            {
                if (FindName($"Week{i}") is CheckBox weekBox)
                    weekBox.IsChecked = GetString($"week{i}") != "0";
            }

            if (autoSaveTimer == null)
                StartAutoSave();
        }

        private bool offlineModeLoading;

        private void chkOfflineMode_Changed(object sender, RoutedEventArgs e)
        {
            if (offlineModeLoading) return;
            SaveSettingNow(MainWindow.OfflineModeSettingKey, chkOfflineMode.IsChecked == true, "offline mode");
            mainWindow?.ApplyOfflineModeToMalwareScan();
            mainWindow?.ApplyDatabaseUpdateSettings();
        }

        private bool autoSaveSelectionsLoading;

        private void chkAutoSaveSelections_Changed(object sender, RoutedEventArgs e)
        {
            if (autoSaveSelectionsLoading) return;
            bool enabled = chkAutoSaveSelections.IsChecked == true;
            mainWindow?.SetAutoSaveSelections(enabled);
            SaveSettingNow(MainWindow.AutoSaveSelectionsSettingKey, enabled, "auto-save selections");
        }

        private bool allBrowserProfilesLoading;

        private void chkCleanAllBrowserProfiles_Changed(object sender, RoutedEventArgs e)
        {
            if (allBrowserProfilesLoading) return;
            SaveSettingNow(MainWindow.AllBrowserProfilesSettingKey, chkCleanAllBrowserProfiles.IsChecked == true, "browser profiles");
            mainWindow?.ReloadDatabaseIfIdle();
        }

        private bool interfaceModeLoading;

        private void InterfaceMode_Changed(object sender, RoutedEventArgs e)
        {
            if (interfaceModeLoading || rbAdvancedInterface == null) return;
            bool advanced = rbAdvancedInterface.IsChecked == true;
            if (advanced == MainWindow.IsAdvancedInterface()) return;
            SaveSettingNow(MainWindow.AdvancedInterfaceSettingKey, advanced, "interface mode");
            mainWindow?.ApplyInterfaceMode();
        }

        private bool databaseUpdateLoading = true;

        private MultronWinCleaner.Processes.Updater.AppUpdate? latestAppRelease;

        private void ShowAppUpdateState(string status)
        {
            txtAppVersion.Text = Loc.F("Installed version: {0}", MultronWinCleaner.Processes.Updater.CurrentAppVersion);
            txtAppUpdateStatus.Text = status;
            bool newer = latestAppRelease != null && latestAppRelease.Version > MultronWinCleaner.Processes.Updater.CurrentAppVersion;
            btnInstallAppUpdate.Content = newer ? Loc.F("Update to {0}", latestAppRelease!.Version) : "";
            btnInstallAppUpdate.Visibility = newer ? Visibility.Visible : Visibility.Collapsed;
            btnAppReleasePage.Visibility = latestAppRelease != null && latestAppRelease.PageUrl.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void CheckAppUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (IsOfflineModeEnabled())
            {
                ShowAppUpdateState(Loc.T("Turn off offline mode to check for updates."));
                return;
            }
            btnCheckAppUpdate.IsEnabled = false;
            btnInstallAppUpdate.IsEnabled = false;
            ShowAppUpdateState(Loc.T("Checking for updates..."));
            try
            {
                latestAppRelease = await Task.Run(MultronWinCleaner.Processes.Updater.FindLatestAppReleaseAsync);
                if (latestAppRelease == null)
                    ShowAppUpdateState(Loc.T("No release with an update package was found."));
                else if (latestAppRelease.Version > MultronWinCleaner.Processes.Updater.CurrentAppVersion)
                    ShowAppUpdateState(Loc.F("Version {0} is available.", latestAppRelease.Version));
                else
                    ShowAppUpdateState(Loc.F("You have the latest version ({0}).", latestAppRelease.Version));
            }
            catch (Exception ex)
            {
                ShowAppUpdateState(Loc.F("Could not check for updates: {0}", MultronWinCleaner.Processes.Updater.DescribeError(ex)));
            }
            finally
            {
                btnCheckAppUpdate.IsEnabled = true;
                btnInstallAppUpdate.IsEnabled = true;
            }
        }

        private async void InstallAppUpdate_Click(object sender, RoutedEventArgs e)
        {
            var update = latestAppRelease;
            if (update == null || mainWindow == null)
                return;
            if (!mainWindow.IsIdleForDatabaseReload() || mainWindow.IsAppUpdateBusy)
            {
                AppDialog.Show(this, Loc.T("Please wait until the current scan or clean has finished."), Loc.T("App Updates"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (AppDialog.Show(this, Loc.F("Install Multron Win Cleaner {0} now? The app closes and starts again during the update.", update.Version),
                    Loc.T("App Updates"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            btnCheckAppUpdate.IsEnabled = false;
            btnInstallAppUpdate.IsEnabled = false;
            var progress = new Progress<int>(percent => txtAppUpdateStatus.Text = Loc.F("Downloading Update {0}%", percent));
            bool installed = await mainWindow.InstallAppUpdateNowAsync(update, this, progress);
            if (!installed)
            {
                btnCheckAppUpdate.IsEnabled = true;
                btnInstallAppUpdate.IsEnabled = true;
                ShowAppUpdateState(Loc.T("The update was not installed."));
            }
        }

        private void AppReleasePage_Click(object sender, RoutedEventArgs e)
        {
            if (latestAppRelease?.PageUrl is { Length: > 0 } url)
            {
                try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
                catch (Exception ex) { Debug.WriteLine("Could not open the release page: " + ex.Message); }
            }
        }

        private void LoadDatabaseUpdateSettings()
        {
            ShowAppUpdateState(Loc.T("Click Check for Updates to look for a newer version."));
            databaseUpdateLoading = true;
            chkAppAutoUpdate.IsChecked = MultronWinCleaner.Processes.Updater.IsAppAutoUpdateEnabled();
            chkDatabaseAutoUpdate.IsChecked = MultronWinCleaner.Processes.Updater.IsAutoUpdateEnabled();
            chkDatabaseAutoReload.IsChecked = MultronWinCleaner.Processes.Updater.IsAutoReloadEnabled();
            chkDatabaseUpdateStatus.IsChecked = MultronWinCleaner.Processes.Updater.IsStatusEnabled();
            chkDatabaseUpdateButton.IsChecked = MultronWinCleaner.Processes.Updater.IsButtonEnabled();

            cmbDatabaseUpdateHours.Items.Clear();
            int saved = MultronWinCleaner.Processes.Updater.GetUpdateHours();
            foreach (int hours in MultronWinCleaner.Processes.Updater.UpdateIntervals)
            {
                var item = new ComboBoxItem { Content = Loc.F(hours == 1 ? "{0} hour" : "{0} hours", hours), Tag = hours };
                cmbDatabaseUpdateHours.Items.Add(item);
                if (hours == saved)
                    cmbDatabaseUpdateHours.SelectedItem = item;
            }
            UpdateDatabaseSettingStates();
            databaseUpdateLoading = false;
        }

        private void UpdateDatabaseSettingStates()
        {
            bool on = chkDatabaseAutoUpdate.IsChecked == true;
            cmbDatabaseUpdateHours.IsEnabled = on;
            chkDatabaseAutoReload.IsEnabled = on;
        }

        private void DatabaseUpdateSetting_Changed(object sender, RoutedEventArgs e)
        {
            if (databaseUpdateLoading) return;
            UpdateDatabaseSettingStates();
            SaveSettingNow(MultronWinCleaner.Processes.Updater.AppUpdateSettingKey, chkAppAutoUpdate.IsChecked == true, "app updates");
            SaveSettingNow(MultronWinCleaner.Processes.Updater.AutoUpdateSettingKey, chkDatabaseAutoUpdate.IsChecked == true, "database updates");
            SaveSettingNow(MultronWinCleaner.Processes.Updater.AutoReloadSettingKey, chkDatabaseAutoReload.IsChecked == true, "database updates");
            SaveSettingNow(MultronWinCleaner.Processes.Updater.StatusSettingKey, chkDatabaseUpdateStatus.IsChecked == true, "database updates");
            SaveSettingNow(MultronWinCleaner.Processes.Updater.ButtonSettingKey, chkDatabaseUpdateButton.IsChecked == true, "database updates");
            mainWindow?.ApplyDatabaseUpdateSettings();
        }

        private void DatabaseUpdateHours_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (databaseUpdateLoading || cmbDatabaseUpdateHours.SelectedItem is not ComboBoxItem item || item.Tag is not int hours) return;
            MultronWinCleaner.Processes.Updater.SaveSetting(MultronWinCleaner.Processes.Updater.AutoUpdateHoursSettingKey, hours.ToString());
        }

        private void SaveSettingNow(string key, bool enabled, string displayName)
        {
            try
            {
                string path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Settings.txt");
                var lines = System.IO.File.Exists(path) ? System.IO.File.ReadAllLines(path).ToList() : new List<string>();
                string entry = $"{key}:{(enabled ? "1" : "0")}";
                int index = lines.FindIndex(l => l.StartsWith(key + ":", StringComparison.OrdinalIgnoreCase));
                if (index != -1)
                    lines[index] = entry;
                else
                    lines.Add(entry);
                System.IO.File.WriteAllLines(path, lines);
            }
            catch (Exception ex)
            {
                AppDialog.Show(Loc.F("Could not save {0}: {1}", Loc.T(displayName), ex.Message), Loc.T("Settings"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private bool autoSaveReady;
        private System.Windows.Threading.DispatcherTimer? autoSaveTimer;

        private void StartAutoSave()
        {
            autoSaveTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            autoSaveTimer.Tick += (s, e) =>
            {
                autoSaveTimer.Stop();
                SaveAllSettings();
            };
            AddHandler(System.Windows.Controls.Primitives.ToggleButton.CheckedEvent, new RoutedEventHandler(QueueAutoSave));
            AddHandler(System.Windows.Controls.Primitives.ToggleButton.UncheckedEvent, new RoutedEventHandler(QueueAutoSave));
            AddHandler(System.Windows.Controls.Primitives.Selector.SelectionChangedEvent, new SelectionChangedEventHandler((s, e) => QueueAutoSave(s, e)));
            AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((s, e) => QueueAutoSave(s, e)));
            IsVisibleChanged += (s, e) => { if (!IsVisible) FlushAutoSave(); };
            Closing += (s, e) => FlushAutoSave();
            Application.Current.Exit += (s, e) => FlushAutoSave();
            autoSaveReady = true;
        }

        private void QueueAutoSave(object sender, RoutedEventArgs e)
        {
            if (!autoSaveReady || autoSaveTimer == null)
                return;
            autoSaveTimer.Stop();
            autoSaveTimer.Start();
        }

        private void FlushAutoSave()
        {
            if (autoSaveTimer == null || !autoSaveTimer.IsEnabled)
                return;
            autoSaveTimer.Stop();
            SaveAllSettings();
        }

        private void SaveAllSettings()
        {
            try
            {
                string path = AppDomain.CurrentDomain.BaseDirectory + "\\" + "Settings.txt";
                var lines = System.IO.File.Exists(path) ? System.IO.File.ReadAllLines(path).ToList() : new List<string>();

                void Upsert(string key, string value)
                {
                    int index = lines.FindIndex(l => l.StartsWith(key + ":", StringComparison.OrdinalIgnoreCase));
                    if (index != -1)
                        lines[index] = $"{key}:{value}";
                    else
                        lines.Add($"{key}:{value}");
                }

                Upsert("customday", txtCustomDay.Text.Trim());
                Upsert("starttime", txtStartTime.Text.Trim());
                Upsert("customaccess", txtCustomAccess.Text.Trim());
                Upsert("endtime", txtEndTime.Text.Trim());
                Upsert("minutes", txtCleaningInterval.Text.Trim());
                Upsert("loglocation", txtLogPath.Text.Trim());
                Upsert("autoclean", chkAutoClean.IsChecked == true ? "1" : "0");
                Upsert("trayicon", chkTrayIcon.IsChecked == true ? "1" : "0");
                Upsert(MainWindow.OfflineModeSettingKey, chkOfflineMode.IsChecked == true ? "1" : "0");
                Upsert(MainWindow.AutoSaveSelectionsSettingKey, chkAutoSaveSelections.IsChecked == true ? "1" : "0");
                Upsert(MainWindow.AllBrowserProfilesSettingKey, chkCleanAllBrowserProfiles.IsChecked == true ? "1" : "0");
                Upsert(MainWindow.AdvancedInterfaceSettingKey, rbAdvancedInterface.IsChecked == true ? "1" : "0");
                Upsert("oldscan", OldScan.IsChecked == true ? "1" : "0");
                Upsert("access_scan", AccessScan.IsChecked == true ? "1" : "0");
                Upsert("onlylowcpu", OnlyLowCPU.IsChecked == true ? "1" : "0");
                Upsert("runifactive", RunIfInactive.IsChecked == true ? "1" : "0");
                Upsert("pluggedin", OnlyBattery.IsChecked == true ? "1" : "0");
                Upsert("batterylow", SkipBattery.IsChecked == true ? "1" : "0");
                Upsert("enablelog", chkEnableLog.IsChecked == true ? "1" : "0");
                Upsert("showlastlog", chkShowLastLog.IsChecked == true ? "1" : "0");
                Upsert("logpath", logfilepath);
                Upsert("cleanallusers", rbCleanAllUsers.IsChecked == true ? "1" : "0");
                Upsert("cleanselectedusers", rbCleanSelectedUsers.IsChecked == true ? "1" : "0");
                Upsert("startupscan", chkEnableStartupScan.IsChecked == true ? "1" : "0");
                Upsert("startupclean", chkEnableStartupClean.IsChecked == true ? "1" : "0");
                Upsert("startupnotifyscan", chkEnableNotifyScan.IsChecked == true ? "1" : "0");
                Upsert("startupnotifyclean", chkEnableNotifyClean.IsChecked == true ? "1" : "0");
                Upsert(MainWindow.NotifySecuritySettingKey, chkNotifySecurity.IsChecked == true ? "1" : "0");
                Upsert(MainWindow.NotifyFirewallSettingKey, chkNotifyFirewall.IsChecked == true ? "1" : "0");
                Upsert(MainWindow.NotifyDuplicatesSettingKey, chkNotifyDuplicates.IsChecked == true ? "1" : "0");
                Upsert(MainWindow.NotifyMalwareSettingKey, chkNotifyMalware.IsChecked == true ? "1" : "0");
                Upsert(Notify.PositionSettingKey, Math.Max(0, cmbNotifyPosition.SelectedIndex).ToString());
                Upsert(Notify.LayoutSettingKey, Math.Max(0, cmbNotifyLayout.SelectedIndex).ToString());
                if (comboBoxUserSelection != null)
                {
                    comboBoxUserSelection.IsEnabled = rbCleanAllUsers.IsChecked != true;
                }
                if (cmbAccessPreset.SelectedItem is ComboBoxItem selectedaccess)
                    Upsert("accessscanindex", Loc.En(selectedaccess.Content));

                if (cmbAgePreset.SelectedItem is ComboBoxItem selectedindex)
                    Upsert("oldscanindex", Loc.En(selectedindex.Content));

                if (cmbScheduleType.SelectedItem is ComboBoxItem selectedSchedule)
                    Upsert("scheduletype", Loc.En(selectedSchedule.Content));

                if (cmbPostCleanupAction.SelectedItem is ComboBoxItem selectedAction)
                    Upsert("postaction", Loc.En(selectedAction.Content));

                for (int i = 1; i <= 7; i++)
                {
                    string controlName = $"Day{i}";
                    string keyName = $"day{i}";
                    if (this.FindName(controlName) is CheckBox dayBox)
                        Upsert(keyName, dayBox.IsChecked == true ? "1" : "0");
                }

                Upsert("limitweeks", chkLimitWeeks.IsChecked == true ? "1" : "0");
                for (int i = 1; i <= 5; i++)
                {
                    if (this.FindName($"Week{i}") is CheckBox weekBox)
                        Upsert($"week{i}", weekBox.IsChecked == true ? "1" : "0");
                }

                System.IO.File.WriteAllLines(path, lines);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Could not save the settings: " + ex.Message);
            }
        }

        private void chkCleanAllUsers_Checked(object sender, RoutedEventArgs e)
        { 
            if (comboBoxUserSelection != null)
            {
                comboBoxUserSelection.IsEnabled = false;
            }
        }

        private void chkCleanAllUsers_Unchecked(object sender, RoutedEventArgs e)
        { 
            if (comboBoxUserSelection != null)
            {
                comboBoxUserSelection.IsEnabled = true;
            }
        }

        private void txtCleaningInterval_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !IsNumeric(e.Text);
        }

        private bool IsNumeric(string text)
        {
            return int.TryParse(text, out _);
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Hide();
        }

        public static string NormalizeExceptionPath(string path)
        {
            string value = (path ?? "").Trim().Trim('"');
            if (value.Length > 3)
                value = value.TrimEnd('\\', '/');
            return value;
        }

        public bool IsExcluded(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;
            if (excludedfiles.Contains(path))
                return true;
            foreach (string folder in excludedFolders)
            {
                if (path.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private void RebuildExceptions()
        {
            var items = lstExceptions.Items.Cast<string>().ToList();
            excludedfiles = new HashSet<string>(items, StringComparer.OrdinalIgnoreCase);
            excludedFolders = items.Where(System.IO.Directory.Exists).Select(p => p.EndsWith("\\") ? p : p + "\\").ToArray();
        }

        private void SaveExceptions()
        {
            var items = lstExceptions.Items.Cast<string>().ToList();
            lock (exceptionsFileLock)
            {
                try
                {
                    System.IO.File.WriteAllLines(excludedfilesdir, items);
                }
                catch (Exception ex)
                {
                    AppDialog.Show(Loc.F("Could not save the exceptions list:\n{0}", ex.Message), Loc.T("Exceptions"), MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        public void addexception(string path)
        {
            path = NormalizeExceptionPath(path);
            if (path.Length == 0 || lstExceptions.Items.Cast<string>().Any(i => i.Equals(path, StringComparison.OrdinalIgnoreCase)))
                return;

            lstExceptions.Items.Add(path);
            txtExceptionPath.Clear();
            RebuildExceptions();
            SaveExceptions();
        }

        public void removeexception(string path)
        {
            path = NormalizeExceptionPath(path);
            var item = lstExceptions.Items.Cast<string>().FirstOrDefault(i => i.Equals(path, StringComparison.OrdinalIgnoreCase));
            if (item == null)
                return;

            lstExceptions.Items.Remove(item);
            RebuildExceptions();
            SaveExceptions();
        }

        private void AddException_Click(object sender, RoutedEventArgs e)
        {
            string path = NormalizeExceptionPath(txtExceptionPath.Text);
            if (System.IO.File.Exists(path) || System.IO.Directory.Exists(path))
            {
                addexception(path);
            }
            else
            {
                AppDialog.Show(Loc.T("The file or folder does not exist."), Loc.T("Exceptions"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void RemoveException_Click(object sender, RoutedEventArgs e)
        {
            if (lstExceptions.SelectedItem != null)
                removeexception(lstExceptions.SelectedItem.ToString() ?? "");
        }

        private void RemoveExceptionAll_Click(object sender, RoutedEventArgs e)
        {
            lstExceptions.Items.Clear();
            RebuildExceptions();
            SaveExceptions();
        }

        private void allusersChecked(object sender, RoutedEventArgs e)
        {
            comboBoxUserSelection.IsEnabled = false;
        }

        private void allusersUnchecked(object sender, RoutedEventArgs e)
        {
            comboBoxUserSelection.IsEnabled = true;
        }

        private async void ApplyUserSelection_Click(object sender, RoutedEventArgs e)
        {
     
            mainWindow.ReloadDb.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        }


        public async Task<bool> IsInStartup_ActAsync()
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = "/query /tn \"MultronWCleaner\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                  
                };

                using var proc = Process.Start(startInfo);
                if (proc == null) return false;

                
                await Task.Run(() => proc.WaitForExit());
                return proc.ExitCode == 0;
            }
            catch { return false; }
        }

        public async Task<bool> RemoveFromStartup_ActAsync()
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = "/delete /tn \"MultronWCleaner\" /f",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(startInfo);
                if (proc == null) return false;

                await Task.Run(() => proc.WaitForExit());
                return proc.ExitCode == 0;
            }
            catch { return false; }
        }

        public async Task<bool> CreateStartupTaskAsync()
        {
            try
            {
                string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? Environment.ProcessPath ?? "";

                var startInfo = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                   
                    Arguments = $"/create /tn \"MultronWCleaner\" /tr \"\\\"{exePath}\\\" -startup\" /sc onlogon /rl highest /f",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(startInfo);
                if (proc == null) return false;

                await Task.Run(() => proc.WaitForExit());
                return proc.ExitCode == 0;
            }
            catch { return false; }
        }

        private bool runAtStartupLoading;

        private async Task LoadRunAtStartupAsync()
        {
            bool inStartup = await IsInStartup_ActAsync();
            SetRunAtStartupBox(inStartup);
        }

        public void SetRunAtStartupBox(bool on)
        {
            runAtStartupLoading = true;
            try { chkRunAtStartup.IsChecked = on; }
            finally { runAtStartupLoading = false; }
        }

        private async void ChkRunAtStartup_Changed(object sender, RoutedEventArgs e)
        {
            if (runAtStartupLoading)
                return;
            bool on = chkRunAtStartup.IsChecked == true;
            chkRunAtStartup.IsEnabled = false;
            try
            {
                bool success = on ? await CreateStartupTaskAsync() : await RemoveFromStartup_ActAsync() || !await IsInStartup_ActAsync();
                if (!success)
                {
                    SetRunAtStartupBox(!on);
                    AppDialog.Show(Loc.T("Could not change Windows startup. Make sure the program runs as administrator."), Loc.T("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                if (on)
                    EnsureTrayIconForStartup();
                if (!on)
                {
                    chkEnableStartupScan.IsChecked = false;
                    chkEnableStartupClean.IsChecked = false;
                    mainWindow?.utilities?.savesettings("startupscan:0");
                    mainWindow?.utilities?.savesettings("startupclean:0");
                }
            }
            finally
            {
                chkRunAtStartup.IsEnabled = true;
            }
        }

        private void EnsureTrayIconForStartup()
        {
            if (chkTrayIcon.IsChecked == true || mainWindow?.utilities?.chkTrayIconUtil.IsChecked == true)
                return;
            chkTrayIcon.IsChecked = true;
            SaveSettingNow("trayicon", true, "tray icon");
        }

        private async Task<bool> EnsureRunAtStartupAsync()
        {
            if (await IsInStartup_ActAsync())
            {
                EnsureTrayIconForStartup();
                return true;
            }
            var answer = AppDialog.Show(Loc.T("This option needs Multron Win Cleaner to start with Windows. Turn that on now?"),
                Loc.T("Multron Win Cleaner"), MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes)
                return false;
            if (!await CreateStartupTaskAsync())
            {
                AppDialog.Show(Loc.T("Could not change Windows startup. Make sure the program runs as administrator."), Loc.T("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
            SetRunAtStartupBox(true);
            EnsureTrayIconForStartup();
            return true;
        }

        private void OldScan_Checked(object sender, RoutedEventArgs e)
        {
            AccessScan.IsChecked = false;
        }

        private bool startupScanLoading;
        private bool startupCleanLoading;

        private bool transparencyLoading;
        private bool shredModeLoading;

        private void ShredMode_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (shredModeLoading || cmbShredMode == null) return;
            Utilities?.savesettings($"shredmode:{Math.Max(0, cmbShredMode.SelectedIndex)}");
            mainWindow?.UpdateShredBadge();
        }

        private void LoadTransparencySettings()
        {
            transparencyLoading = true;
            try
            {
                if (cmbTransparencyColor.Items.Count == 0)
                {
                    foreach (var (name, color) in Appearance.Colors)
                    {
                        var swatch = new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(7), Margin = new Thickness(0, 0, 8, 0), Background = new SolidColorBrush(color), VerticalAlignment = VerticalAlignment.Center };
                        var label = new TextBlock { Text = Loc.T(name), VerticalAlignment = VerticalAlignment.Center };
                        var row = new StackPanel { Orientation = Orientation.Horizontal };
                        row.Children.Add(swatch);
                        row.Children.Add(label);
                        cmbTransparencyColor.Items.Add(new ComboBoxItem { Content = row });
                    }
                }
                chkTransparency.IsChecked = Appearance.Enabled;
                cmbTransparencyColor.SelectedIndex = Appearance.ColorIndex;
                cmbTransparencyLevel.SelectedIndex = Appearance.LevelIndex;
                UpdateTransparencyControls();
            }
            finally
            {
                transparencyLoading = false;
            }
        }

        private void UpdateTransparencyControls()
        {
            bool on = chkTransparency.IsChecked == true;
            cmbTransparencyColor.IsEnabled = on;
            cmbTransparencyLevel.IsEnabled = on;
        }

        private void Transparency_Changed(object sender, RoutedEventArgs e)
        {
            if (transparencyLoading || cmbTransparencyColor == null || cmbTransparencyLevel == null || chkTransparency == null)
                return;
            UpdateTransparencyControls();
            Utilities?.savesettings(Appearance.EnabledSettingKey + (chkTransparency.IsChecked == true ? ":1" : ":0"));
            Utilities?.savesettings(Appearance.ColorSettingKey + ":" + Math.Max(0, cmbTransparencyColor.SelectedIndex));
            Utilities?.savesettings(Appearance.LevelSettingKey + ":" + Math.Max(0, cmbTransparencyLevel.SelectedIndex));
            Appearance.Apply();
        }

        private bool themeFollowsWindowsLoading;

        public void SetThemeFollowsWindowsBox(bool follow)
        {
            themeFollowsWindowsLoading = true;
            try { chkThemeFollowsWindows.IsChecked = follow; }
            finally { themeFollowsWindowsLoading = false; }
        }

        private void ChkThemeFollowsWindows_Changed(object sender, RoutedEventArgs e)
        {
            if (themeFollowsWindowsLoading || !IsLoaded)
                return;
            mainWindow?.SetThemeFollowsWindows(chkThemeFollowsWindows.IsChecked == true);
        }

        private bool contextMenuLoading;

        private void LoadContextMenuSettings()
        {
            contextMenuLoading = true;
            try
            {
                chkContextMenuScan.IsChecked = MultronWinCleaner.Processes.ShellContextMenu.IsScanRegistered();
                bool win11 = MultronWinCleaner.Processes.ShellContextMenu.IsWindows11;
                chkClassicContextMenu.Visibility = win11 ? Visibility.Visible : Visibility.Collapsed;
                txtClassicContextMenuHint.Visibility = chkClassicContextMenu.Visibility;
                chkClassicContextMenu.IsChecked = win11 && MultronWinCleaner.Processes.ShellContextMenu.IsClassicMenuOn();
                MultronWinCleaner.Processes.ShellContextMenu.RefreshScan();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Could not read the right-click menu settings: " + ex.Message);
            }
            finally
            {
                contextMenuLoading = false;
            }
        }

        private void ChkContextMenuScan_Changed(object sender, RoutedEventArgs e)
        {
            if (contextMenuLoading)
                return;
            try
            {
                if (chkContextMenuScan.IsChecked == true)
                    MultronWinCleaner.Processes.ShellContextMenu.RegisterScan();
                else
                    MultronWinCleaner.Processes.ShellContextMenu.UnregisterScan();
            }
            catch (Exception ex)
            {
                AppDialog.Show(Loc.F("Could not change the right-click menu: {0}", ex.Message), Loc.T("Right-Click Menu"), MessageBoxButton.OK, MessageBoxImage.Warning);
                LoadContextMenuSettings();
            }
        }

        private void ChkClassicContextMenu_Changed(object sender, RoutedEventArgs e)
        {
            if (contextMenuLoading)
                return;
            try
            {
                MultronWinCleaner.Processes.ShellContextMenu.SetClassicMenu(chkClassicContextMenu.IsChecked == true);
            }
            catch (Exception ex)
            {
                AppDialog.Show(Loc.F("Could not change the right-click menu: {0}", ex.Message), Loc.T("Right-Click Menu"), MessageBoxButton.OK, MessageBoxImage.Warning);
                LoadContextMenuSettings();
                return;
            }

            var answer = AppDialog.Show(Loc.T("The change takes effect after File Explorer restarts. Restart File Explorer now? Open Explorer windows will close."),
                Loc.T("Right-Click Menu"), MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Yes)
                MultronWinCleaner.Processes.ShellContextMenu.RestartExplorer();
        }

        private async void ChkEnableStartupScan_Checked(object sender, RoutedEventArgs e)
        {
            if (startupScanLoading) return;
            if (!await EnsureRunAtStartupAsync())
            {
                startupScanLoading = true;
                chkEnableStartupScan.IsChecked = false;
                startupScanLoading = false;
            }
        }

        private async void ChkEnableStartupClean_Checked(object sender, RoutedEventArgs e)
        {
            if (startupCleanLoading) return;
            if (!await EnsureRunAtStartupAsync())
            {
                startupCleanLoading = true;
                chkEnableStartupClean.IsChecked = false;
                startupCleanLoading = false;
            }
        }

        private void btnAutoCleanHelp_Click(object sender, RoutedEventArgs e)
        {
            AppDialog.Show(Loc.T("Once you configure your settings, automatic cleaning will start if the checkmark above is enabled. However, if you close and reopen the software, it will reset. It needs to keep running continuously, this way it will no longer create files or registry in the system. I may think about improving this later, but for now this approach seemed better."), Loc.T("Auto Clean Help"), MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void btnScannerHelp_Click(object sender, RoutedEventArgs e)
        {
            AppDialog.Show(Loc.T("These settings are intended for high-end systems. If you are using an older system, there is no need to modify these settings. You don't need to use in old systems because cache files cause slowdowns and fill up storage space on older systems. However, high-end systems can reduce power consumption and allow applications to open faster by using cache files and similar optimizations. The recommended setting is 30 days, but you can adjust it according to your own knowledge or situation."), Loc.T("Scanner Help"), MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void AccessScan_Checked(object sender, RoutedEventArgs e)
        {
            OldScan.IsChecked = false;
        }

        private void ResetExtensions_Click(object sender, RoutedEventArgs e)
        {
            txtNewExtension.Text = MultronWinCleaner.Processes.DeepLogOptions.DefaultExtensions;
            mainWindow.extensions = txtNewExtension.Text;
            Utilities?.savesettings($"{MultronWinCleaner.Processes.DeepLogOptions.ExtensionsKey}:{txtNewExtension.Text}");
        }

        private void LogFilter_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox box) return;
            if (!int.TryParse(box.Text.Trim(), out int value) || value < 0)
                value = 0;
            box.Text = value.ToString();
            string key = box == txtLogMinAge ? MultronWinCleaner.Processes.DeepLogOptions.MinAgeKey : MultronWinCleaner.Processes.DeepLogOptions.MinSizeKey;
            Utilities?.savesettings($"{key}:{value}");
        }

        private void AddExtension_Click(object sender, RoutedEventArgs e)
        {
            if (mainWindow.extensions == txtNewExtension.Text)
            {
                AppDialog.Show(Loc.F("No change detected. {0} > {1}", txtNewExtension.Text, mainWindow.extensions), Loc.T("Multron Win Cleaner"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string input = txtNewExtension.Text.Trim();

            if (input.Contains(" "))
            {
                AppDialog.Show(Loc.T("Extensions cannot contain spaces."), Loc.T("Error"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!System.Text.RegularExpressions.Regex.IsMatch(input, @"^(\.[a-zA-Z0-9]+)+$"))
            {
                AppDialog.Show(Loc.T("Invalid format. Example: .log.etl.dmp.tmp"), Loc.T("Error"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = AppDialog.Show(Loc.F("Are you sure you want to add {0}?", txtNewExtension.Text), Loc.T("Confirm"), System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);

            if (result == System.Windows.MessageBoxResult.Yes)
            {
                mainWindow.extensions = txtNewExtension.Text;
                Utilities?.savesettings($"{MultronWinCleaner.Processes.DeepLogOptions.ExtensionsKey}:{txtNewExtension.Text}");
                AppDialog.Show(Loc.F("Changes applied. {0} > {1}", mainWindow.extensions, txtNewExtension.Text), Loc.T("Information"), System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
        }

        private void OpenLogFile_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(logfilepath) || !System.IO.File.Exists(logfilepath))
            {
                AppDialog.Show(this, Loc.T("The log file was not found. No log has been written yet, or its location has changed."),
                    Loc.T("Open Log File"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            Process.Start("explorer.exe", logfilepath);
        }

        private void OpenAppFolder_Click(object sender, RoutedEventArgs e)
        {
            string folderPath;

            if (!string.IsNullOrEmpty(logfilepath) && System.IO.File.Exists(logfilepath))
            {
                folderPath = System.IO.Path.GetDirectoryName(logfilepath) ?? Environment.CurrentDirectory;
            }
            else
            {
                folderPath = Environment.CurrentDirectory;
            }

            Process.Start("explorer.exe", folderPath);
        }

        private async void ClearLog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await System.IO.File.WriteAllTextAsync(logfilepath, "");
                AppDialog.Show(Loc.F("Log file {0}: all logs cleaned!", logfilepath), Loc.T("Success"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AppDialog.Show(Loc.F("Error clearing log: {0}", ex.Message), Loc.T("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        }

        private void BrowseLogPath_Click(object sender, RoutedEventArgs e)
        {
            OpenFolderDialog dialog = new OpenFolderDialog();
            dialog.Title = Loc.T("Select Folder For Log File.");

            dialog.DefaultDirectory = Environment.CurrentDirectory;
            dialog.InitialDirectory = Environment.CurrentDirectory;
            if (dialog.ShowDialog() == true)
            {
                txtLogPath.Text = dialog.FolderName;
            }
        }

        private async void ApplyLog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string logEntry = DateTime.Now + " Log path applied successfully!" + Environment.NewLine;

                if (!txtLogPath.Text.EndsWith(".txt"))
                {
                    string path = txtLogPath.Text + "\\mwc_cleanlog.txt";
                    await System.IO.File.AppendAllTextAsync(path, logEntry);
                    logfilepath = path;
                    txtLogPath.Text = path;
                }
                else
                {
                    await System.IO.File.AppendAllTextAsync(txtLogPath.Text, logEntry);
                    logfilepath = txtLogPath.Text;
                }

                AppDialog.Show(Loc.T("Log path applied successfully!"), Loc.T("Multron Win Cleaner"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AppDialog.Show(Loc.F("Error applying log path: {0}", ex.Message), Loc.T("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
