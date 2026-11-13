using IWshRuntimeLibrary;
using Microsoft.Win32;
using Microsoft.Win32.TaskScheduler;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace MultronWinCleaner
{
    public partial class StartupManager : Window, INotifyPropertyChanged
    {
        public ObservableCollection<StartupApp> StartupApps { get; set; } = new();
        Utilities utilities;
        private static readonly ConcurrentDictionary<string, BitmapImage> IconCache = new();

        private StartupApp? selectedApp;
        public StartupApp? SelectedApp
        {
            get => selectedApp;
            set { selectedApp = value; OnPropertyChanged(); }
        }

        private bool _isLoading = false;
        private HashSet<string> _knownItems = new();
        private static bool _notificationsEnabled;

        public StartupManager(Utilities utilities)
        {
            InitializeComponent();
            DataContext = this;

            this.utilities = utilities;
            tglShowNotifications.IsChecked = _notificationsEnabled;

            Loaded += async (s, e) => await LoadStartupAppsFastAsync(isSilentRefresh: false);

            IsVisibleChanged += async (s, e) =>
            {
                _isShown = IsVisible;
                if (IsVisible && IsLoaded)
                    await LoadStartupAppsFastAsync(isSilentRefresh: true);
            };
            currentInstance?._monitoringCts?.Cancel();
            currentInstance = this;
            StartRealTimeMonitoring();
        }

        private static StartupManager? currentInstance;

        private volatile bool _isShown;

        private static readonly TimeSpan TaskSchedulerRefreshInterval = TimeSpan.FromSeconds(60);
        private DateTime _lastTaskSchedulerScan = DateTime.MinValue;
        private List<StartupApp> _cachedTaskApps = new();

        private CancellationTokenSource? _monitoringCts;
        protected override void OnClosed(EventArgs e)
        {
            _monitoringCts?.Cancel();
            base.OnClosed(e);
        }

        private void StartRealTimeMonitoring()
        {
            _monitoringCts = new CancellationTokenSource();
            var token = _monitoringCts.Token;
            StartChangeWatchers(token);

            System.Threading.Tasks.Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        if (_isShown || _notificationsEnabled)
                            await LoadStartupAppsFastAsync(true);
                        await WaitForChangeAsync(token);
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex) { Debug.WriteLine($"Real-time monitoring error: {ex.Message}"); }
                }
            }, token);
        }

        private async System.Threading.Tasks.Task LoadStartupAppsFastAsync(bool isSilentRefresh)
        {
            if (_isLoading) return;
            try
            {
                if (!isSilentRefresh) LoadingOverlay.Visibility = Visibility.Visible;
                _isLoading = true;

                await System.Threading.Tasks.Task.Delay(50);

                var allApps = new ConcurrentBag<StartupApp>();

                await System.Threading.Tasks.Task.Run(() =>
                {
                    var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>();
                    var staThread = new Thread(() =>
                    {
                        try
                        {
                            GetWinlogonEntries(allApps);
                            GetRegistryStartupApps(allApps);
                            GetPolicyAndTerminalApps(allApps);
                            GetWindowsValueEntries(allApps);

                            if (!isSilentRefresh || _taskListChanged || DateTime.UtcNow - _lastTaskSchedulerScan > TaskSchedulerRefreshInterval)
                            {
                                _taskListChanged = false;
                                var taskApps = new ConcurrentBag<StartupApp>();
                                GetTaskSchedulerApps(taskApps);
                                _cachedTaskApps = taskApps.ToList();
                                _lastTaskSchedulerScan = DateTime.UtcNow;
                            }
                            foreach (var taskApp in _cachedTaskApps)
                                allApps.Add(taskApp);

                            GetServiceApps(allApps);
                            GetPackagedStartupTasks(allApps);
                            GetActiveSetupEntries(allApps);
                            GetExtendedLocations(allApps);
                            GetStartupFolderApps(allApps);
                            tcs.SetResult(true);
                        }
                        catch (Exception ex) { tcs.SetException(ex); }
                    });

                    staThread.SetApartmentState(ApartmentState.STA);
                    staThread.IsBackground = true;
                    staThread.Start();

                    return tcs.Task;
                });

                var liveAdvanced = new HashSet<string>(allApps.Where(a => a.Advanced != null && !a.IsAdvancedRecord).Select(a => a.Advanced!.Id));
                var sortedApps = allApps
                    .Where(a => !a.IsAdvancedRecord || !liveAdvanced.Contains(a.Advanced!.Id))
                    .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(a => a.UniqueId, StringComparer.Ordinal)
                    .ToList();
                var currentHashes = new HashSet<string>(sortedApps.Select(a => a.UniqueId));

                if (_knownItems == null) _knownItems = new HashSet<string>();

                var newItems = currentHashes.Except(_knownItems).ToList();
                bool isInitialScan = _knownItems.Count == 0;

                if (isSilentRefresh && !isInitialScan && newItems.Any() && _notificationsEnabled)
                {
                    foreach (var hash in newItems)
                    {
                        var newApp = sortedApps.First(a => a.UniqueId == hash);
                        Application.Current.Dispatcher.Invoke(() => {
                            try
                            {
                                var type = Type.GetType("MultronWinCleaner.NewStartupItemDetected");
                                if (type != null)
                                {
                                    var detect = Activator.CreateInstance(type, newApp.Name, newApp.Path, this) as Window;
                                    detect?.Show();
                                }
                            }
                            catch (Exception ex) { Debug.WriteLine($"Notification Error: {ex.Message}"); }
                        });
                    }
                }

                _knownItems = currentHashes;

                Application.Current.Dispatcher.Invoke(() =>
                {
                    var itemsToRemove = StartupApps.Where(a => !currentHashes.Contains(a.UniqueId)).ToList();
                    foreach (var item in itemsToRemove) StartupApps.Remove(item);

                    foreach (var app in sortedApps)
                    {
                        var existing = StartupApps.FirstOrDefault(a => a.UniqueId == app.UniqueId);
                        if (existing != null)
                        {
                            if (!existing.HasPendingChange && existing.IsEnabled != app.IsEnabled)
                                existing.IsEnabled = app.IsEnabled;
                            continue;
                        }

                        app.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(StartupApp.HasPendingChange)) CheckPendingChanges(); };
                        app.HighImpactWarningAction = (msg) => ShowInAppWarning(msg);

                        int index = 0;
                        while (index < StartupApps.Count && string.Compare(StartupApps[index].Name, app.Name, StringComparison.OrdinalIgnoreCase) <= 0)
                            index++;
                        StartupApps.Insert(index, app);
                    }

                    CheckPendingChanges();
                    RefreshFilter();
                     
                    if (!isSilentRefresh)
                    {
                        var highImpactApps = StartupApps.Where(a => a.IsEnabled && a.Impact == "High" && !a.IsWindowsTask).Select(a => a.Name).ToList();
                        if (highImpactApps.Any())
                        {
                            string appNames = string.Join(", ", highImpactApps.Take(3));
                            if (highImpactApps.Count > 3) appNames += Loc.T(" and others");
                            ShowInAppWarning(Loc.F("Warning: You have high-impact apps ({0}) enabled. This may slow down your boot time.", appNames));
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                AppDialog.Show(Loc.F("A critical error occurred while loading the list:\n\n{0}", ex.Message), Loc.T("System Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isLoading = false;
                if (!isSilentRefresh) LoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }


        private DispatcherTimer? searchTimer;

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            bool empty = SearchBox.Text.Length == 0;
            SearchHint.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            SearchClear.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
            if (searchTimer == null)
            {
                searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
                searchTimer.Tick += (s, args) =>
                {
                    searchTimer.Stop();
                    ApplySearch(SearchBox.Text.Trim());
                };
            }
            searchTimer.Stop();
            searchTimer.Start();
        }

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
                SearchBox.Clear();
        }

        private void SearchClear_Click(object sender, RoutedEventArgs e)
        {
            SearchBox.Clear();
            SearchBox.Focus();
        }

        private string _searchQuery = "";
        private string _statusFilter = "All";

        private void ApplySearch(string query)
        {
            _searchQuery = query;
            RefreshFilter();
        }

        public async void RevealItem(string name, string path)
        {
            Show();
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
            Activate();
            SearchBox.Text = "";
            FilterAll.IsChecked = true;

            StartupApp? item = null;
            for (int attempt = 0; attempt < 20 && item == null; attempt++)
            {
                item = StartupApps.FirstOrDefault(a => a.Name == name && a.Path == path);
                if (item == null)
                    await System.Threading.Tasks.Task.Delay(300);
            }
            if (item == null)
                return;

            if (item.IsWindowsTask)
                chkWindowsTasks.IsChecked = true;
            RefreshFilter();
            StartupAppsDataGrid.SelectedItem = item;
            StartupAppsDataGrid.ScrollIntoView(item);
            StartupAppsDataGrid.UpdateLayout();
            await System.Threading.Tasks.Task.Delay(200);
            StartupAppsDataGrid.ScrollIntoView(item);
            var brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Transparent);
            item.FlashBrush = brush;
            brush.BeginAnimation(System.Windows.Media.SolidColorBrush.ColorProperty, new System.Windows.Media.Animation.ColorAnimation
            {
                From = System.Windows.Media.Color.FromArgb(0x00, 0xFF, 0x98, 0x00),
                To = System.Windows.Media.Color.FromArgb(0xE0, 0xFF, 0x98, 0x00),
                Duration = TimeSpan.FromMilliseconds(350),
                AutoReverse = true,
                RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
                SpeedRatio = 1
            });
            await System.Threading.Tasks.Task.Delay(5000);
            item.FlashBrush = null;
        }

        private void Filter_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is string tag)
                _statusFilter = tag;
            RefreshFilter();
        }

        private void WindowsTasks_Changed(object sender, RoutedEventArgs e) => RefreshFilter();

        private void RefreshFilter()
        {
            if (!IsInitialized) return;
            var view = CollectionViewSource.GetDefaultView(StartupApps);
            if (view == null) return;
            bool showWindowsTasks = chkWindowsTasks.IsChecked == true;
            view.Filter = item => item is StartupApp app
                && (showWindowsTasks || !app.IsWindowsTask)
                && (_statusFilter switch
                {
                    "Enabled" => app.IsEnabled,
                    "Disabled" => !app.IsEnabled,
                    "High" => app.IsEnabled && app.Impact == "High",
                    _ => true
                })
                && (_searchQuery.Length == 0
                    || app.Name.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase)
                    || app.Path.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase));
            UpdateSummary();
        }

        private void UpdateSummary()
        {
            int enabled = StartupApps.Count(a => a.IsEnabled && a.IsUserApp);
            int high = StartupApps.Count(a => a.IsEnabled && a.IsUserApp && a.Impact == "High");
            SummaryText.Text = Loc.F("{0} apps start with Windows", enabled);
            HighImpactText.Text = Loc.F("{0} high impact", high);
            HighImpactBadge.Visibility = high > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void tglShowNotifications_Checked(object sender, RoutedEventArgs e)
        {
            _notificationsEnabled = true;
            utilities.savesettings("startupwarning:1");
        }

        private void tglShowNotifications_Unchecked(object sender, RoutedEventArgs e)
        {
            _notificationsEnabled = false;
            utilities.savesettings("startupwarning:0");
        }

        private void GetRegistryStartupApps(ConcurrentBag<StartupApp> apps)
        {
            string[] keys = {
                @"Software\Microsoft\Windows\CurrentVersion\Run",
                @"Software\Microsoft\Windows\CurrentVersion\RunOnce",
                @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run",
                @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\RunOnce"
            };

            foreach (var keyPath in keys)
            {
                ReadRegKey(Registry.CurrentUser, keyPath, apps);
                ReadRegKey(Registry.LocalMachine, keyPath, apps);
            }
        }

        private void GetPolicyAndTerminalApps(ConcurrentBag<StartupApp> apps)
        {
            const string policy = @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer\Run";
            const string terminal = @"Software\Microsoft\Windows NT\CurrentVersion\Terminal Server\Install\Software\Microsoft\Windows\CurrentVersion\";
            ReadRegKey(Registry.CurrentUser, policy, apps, "Group Policy");
            ReadRegKey(Registry.LocalMachine, policy, apps, "Group Policy");
            ReadRegKey(Registry.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Policies\Explorer\Run", apps, "Group Policy");
            ReadRegKey(Registry.LocalMachine, terminal + "Run", apps, "Terminal Server");
            ReadRegKey(Registry.LocalMachine, terminal + "RunOnce", apps, "Terminal Server");
        }

        private void AddReadOnlyEntry(ConcurrentBag<StartupApp> apps, string name, string command, string type, AdvancedSource? source = null)
        {
            apps.Add(new StartupApp
            {
                Name = name,
                Path = command,
                ValueName = name,
                StartupType = type,
                Advanced = source,
                IsEnabled = true,
                IsPresent = true,
                Icon = GetIcon(command),
                Impact = CalculateImpactAutomatically(command, name)
            });
        }

        private static string DisplayName(string command, string fallback)
        {
            string? exe = SplitCommandLine(command).Exe;
            return string.IsNullOrEmpty(exe) ? fallback : Path.GetFileNameWithoutExtension(exe);
        }

        private void GetWindowsValueEntries(ConcurrentBag<StartupApp> apps)
        {
            const string windows = @"Software\Microsoft\Windows NT\CurrentVersion\Windows";
            const string winlogon = @"Software\Microsoft\Windows NT\CurrentVersion\Winlogon";

            foreach (string valueName in new[] { "Load", "Run" })
            {
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(windows, false);
                    foreach (string part in (key?.GetValue(valueName) as string ?? "").Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                        AddReadOnlyEntry(apps, DisplayName(part, valueName), part, "Windows Load", ListSource(Registry.CurrentUser, windows, valueName, part, " "));
                }
                catch { }
            }

            foreach (var root in new[] { Registry.LocalMachine, Registry.CurrentUser })
            {
                try
                {
                    using var key = root.OpenSubKey(winlogon, false);
                    foreach (string part in (key?.GetValue("Shell") as string ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        if (!part.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase))
                            AddReadOnlyEntry(apps, DisplayName(part, "Shell"), part, "Winlogon Shell", ListSource(root, winlogon, "Shell", part, ",", "explorer.exe"));
                }
                catch { }
            }

            foreach (string subKey in new[] { @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Windows", @"SOFTWARE\WOW6432Node\Microsoft\Windows NT\CurrentVersion\Windows" })
            {
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey(subKey, false);
                    foreach (string part in (key?.GetValue("AppInit_DLLs") as string ?? "").Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                        AddReadOnlyEntry(apps, DisplayName(part, "AppInit_DLLs"), part, "AppInit DLL", ListSource(Registry.LocalMachine, subKey, "AppInit_DLLs", part, " "));
                }
                catch { }
            }

            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager", false);
                if (key?.GetValue("BootExecute") is string[] entries)
                    foreach (string entry in entries.Where(e => !string.IsNullOrWhiteSpace(e) && !e.StartsWith("autocheck ", StringComparison.OrdinalIgnoreCase)))
                        AddReadOnlyEntry(apps, entry.Split(' ')[0], entry, "Boot Execute", ListSource(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager", "BootExecute", entry, "\0"));
            }
            catch { }
        }

        private void GetActiveSetupEntries(ConcurrentBag<StartupApp> apps)
        {
            foreach (string subKey in new[] { @"SOFTWARE\Microsoft\Active Setup\Installed Components", @"SOFTWARE\WOW6432Node\Microsoft\Active Setup\Installed Components" })
            {
                try
                {
                    using var root = Registry.LocalMachine.OpenSubKey(subKey, false);
                    if (root == null) continue;
                    foreach (string id in root.GetSubKeyNames())
                    {
                        try
                        {
                            using var item = root.OpenSubKey(id, false);
                            string stub = item?.GetValue("StubPath") as string ?? "";
                            if (string.IsNullOrWhiteSpace(stub) || (item?.GetValue("IsInstalled") is int installed && installed == 0)) continue;
                            string? target = ResolveLaunchTarget(stub);
                            if (string.IsNullOrEmpty(target) || target.StartsWith(WindowsFolderPrefix(), StringComparison.OrdinalIgnoreCase)) continue;
                            string title = item?.GetValue(null) as string ?? "";
                            AddReadOnlyEntry(apps, string.IsNullOrWhiteSpace(title) ? DisplayName(stub, id) : title, stub, "Active Setup", ValueSource(Registry.LocalMachine, $@"{subKey}\{id}", "StubPath"));
                        }
                        catch { }
                    }
                }
                catch { }
            }
        }

        private void GetServiceApps(ConcurrentBag<StartupApp> apps)
        {
            try
            {
                using var services = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services", false);
                if (services == null) return;
                foreach (string name in services.GetSubKeyNames())
                {
                    try
                    {
                        using var service = services.OpenSubKey(name, false);
                        if (service?.GetValue("Start") is not int start || (start != 2 && start != 4)) continue;
                        if (service.GetValue("Type") is not int type || (type & 0x30) == 0) continue;
                        string image = service.GetValue("ImagePath") as string ?? "";
                        if (string.IsNullOrWhiteSpace(image)) continue;
                        string? target = ResolveLaunchTarget(image);
                        bool thirdParty = IsThirdPartyFile(target);
                        if (!thirdParty)
                        {
                            using var parameters = service.OpenSubKey("Parameters", false);
                            string dll = parameters?.GetValue("ServiceDll") as string ?? service.GetValue("ServiceDll") as string ?? "";
                            thirdParty = dll.Length > 0 && IsThirdPartyFile(ProbeFile(NormalizeSystemPath(dll)));
                        }
                        if (!thirdParty) continue;
                        string display = service.GetValue("DisplayName") as string ?? name;
                        if (display.StartsWith("@", StringComparison.Ordinal)) display = name;

                        apps.Add(new StartupApp
                        {
                            Name = display,
                            Path = image,
                            ValueName = name,
                            StartupType = "Service",
                            IsServiceEntry = true,
                            IsEnabled = start == 2,
                            IsPresent = true,
                            Icon = GetIcon(image),
                            Impact = CalculateImpactAutomatically(image, display)
                        });
                    }
                    catch { }
                }
            }
            catch { }
        }

        private const string PackagedTasksKey = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\SystemAppData";

        private void GetPackagedStartupTasks(ConcurrentBag<StartupApp> apps)
        {
            try
            {
                using var root = Registry.CurrentUser.OpenSubKey(PackagedTasksKey, false);
                if (root == null) return;
                foreach (string package in root.GetSubKeyNames())
                {
                    try
                    {
                        using var packageKey = root.OpenSubKey(package, false);
                        if (packageKey == null) continue;
                        foreach (string taskId in packageKey.GetSubKeyNames())
                        {
                            using var task = packageKey.OpenSubKey(taskId, false);
                            if (task?.GetValue("State") is not int state) continue;
                            string title = package.Split('_')[0];
                            int dot = title.IndexOf('.');
                            apps.Add(new StartupApp
                            {
                                Name = dot > 0 && dot < title.Length - 1 ? title.Substring(dot + 1) : title,
                                Path = package,
                                ValueName = "State",
                                RegistryRoot = Registry.CurrentUser,
                                RegistryPath = PackagedTasksKey + "\\" + package + "\\" + taskId,
                                IsPackagedTask = true,
                                StartupType = "Store App",
                                IsEnabled = state == 2 || state == 4,
                                IsPresent = true,
                                Impact = "Low"
                            });
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        private void ReadRegKey(RegistryKey root, string subKey, ConcurrentBag<StartupApp> apps, string? forcedType = null)
        {
            try
            {
                using var key = root.OpenSubKey(subKey, false);
                if (key == null) return;

                foreach (var name in key.GetValueNames())
                {
                    try
                    {
                        string path = key.GetValue(name)?.ToString() ?? "";
                        string? approvedKey = forcedType == null ? ApprovedKeyFor(subKey) : null;
                        bool enabled = approvedKey == null || !IsApprovedDisabled(root, approvedKey, name);

                        apps.Add(new StartupApp
                        {
                            Name = name,
                            Path = path,
                            ValueName = name,
                            RegistryRoot = root,
                            RegistryPath = subKey,
                            ApprovedKey = approvedKey,
                            StartupType = forcedType ?? (root == Registry.CurrentUser ? "Registry (User)" : "Registry (Machine)"),
                            IsEnabled = enabled,
                            IsPresent = true,
                            Icon = GetIcon(path),
                            Impact = CalculateImpactAutomatically(path, name)
                        });
                    }
                    catch { }
                }
            }
            catch { }
        }

        private void GetTaskSchedulerApps(ConcurrentBag<StartupApp> apps)
        {
            try
            {
                using var ts = new TaskService();
                foreach (var task in ts.AllTasks)
                {
                    try
                    {
                        if (task.Definition.Triggers.Any(tr => tr.Enabled && tr.TriggerType is TaskTriggerType.Logon or TaskTriggerType.Boot))
                        {
                            var action = task.Definition.Actions.OfType<ExecAction>().FirstOrDefault();
                            string actionPath = action != null ? $"{action.Path} {action.Arguments}".Trim() : Loc.T("Task Action");

                            apps.Add(new StartupApp
                            {
                                Name = task.Name,
                                Path = actionPath,
                                ValueName = task.Path,
                                StartupType = "Scheduled Task",
                                IsEnabled = task.Enabled,
                                IsPresent = true,
                                CanToggleTask = true,
                                IsWindowsTask = task.Path.StartsWith(@"\Microsoft\Windows\", StringComparison.OrdinalIgnoreCase),
                                Impact = action != null ? CalculateImpactAutomatically($"\"{action.Path}\" {action.Arguments}", task.Name) : "Low",
                                Icon = action != null ? GetIcon($"\"{action.Path}\" {action.Arguments}") : null
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[Error]: {task.Name} -> {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TaskService Error]: {ex.Message}");
            }
        }

        private void GetStartupFolderApps(ConcurrentBag<StartupApp> apps)
        {
            var folders = new[]
            {
                (Path: Environment.GetFolderPath(Environment.SpecialFolder.Startup), Root: Registry.CurrentUser),
                (Path: Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), Root: Registry.LocalMachine)
            };

            foreach (var folder in folders)
            {
                try
                {
                    if (!Directory.Exists(folder.Path)) continue;

                    foreach (var file in Directory.GetFiles(folder.Path))
                    {
                        try
                        {
                            string fileName = Path.GetFileName(file);
                            if (fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                            string title = Path.GetFileNameWithoutExtension(file);

                            apps.Add(new StartupApp
                            {
                                Name = title,
                                Path = file,
                                ValueName = fileName,
                                RegistryRoot = folder.Root,
                                ApprovedKey = ApprovedStartupFolderKey,
                                StartupType = "Startup Folder",
                                IsEnabled = !IsApprovedDisabled(folder.Root, ApprovedStartupFolderKey, fileName),
                                IsPresent = true,
                                Icon = GetIcon(file),
                                Impact = CalculateImpactAutomatically(file, title)
                            });
                        }
                        catch { }
                    }
                }
                catch { }
            }
        }

        private void GetWinlogonEntries(ConcurrentBag<StartupApp> apps)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon");
                string raw = key?.GetValue("Userinit") as string ?? "";

                foreach (var part in raw.Split(',').Select(p => p.Trim()).Where(p => !string.IsNullOrEmpty(p) && !p.EndsWith("userinit.exe", StringComparison.OrdinalIgnoreCase)))
                {
                    try
                    {
                        apps.Add(new StartupApp
                        {
                            Name = Path.GetFileNameWithoutExtension(part),
                            Path = part,
                            ValueName = Path.GetFileNameWithoutExtension(part),
                            StartupType = "Userinit (WinLogon)",
                            Advanced = ListSource(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon", "Userinit", part, ",", Path.Combine(Environment.SystemDirectory, "userinit.exe")),
                            IsEnabled = true,
                            IsPresent = true,
                            Icon = GetIcon(part),
                            Impact = CalculateImpactAutomatically(part, Path.GetFileNameWithoutExtension(part))
                        });
                    }
                    catch { }
                }
            }
            catch { }
        }

        private const long HighThresholdBytes = 40L * 1024 * 1024;
        private const long MediumThresholdBytes = 10L * 1024 * 1024;
        private const long ManagedRuntimeOverheadBytes = 8L * 1024 * 1024;

        private const string ApprovedBaseKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";
        private const string ApprovedStartupFolderKey = ApprovedBaseKey + "StartupFolder";

        private static readonly HashSet<string> LauncherHosts = new(StringComparer.OrdinalIgnoreCase)
        {
            "rundll32.exe", "regsvr32.exe", "cmd.exe", "powershell.exe", "pwsh.exe", "wscript.exe", "cscript.exe", "mshta.exe", "conhost.exe", "explorer.exe"
        };

        private static readonly HashSet<string> LaunchableExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".dll", ".ocx", ".com", ".bat", ".cmd", ".ps1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".hta"
        };

        private static string? ApprovedKeyFor(string runSubKey)
        {
            if (!runSubKey.EndsWith(@"\Run", StringComparison.OrdinalIgnoreCase)) return null;
            return ApprovedBaseKey + (runSubKey.Contains("WOW6432Node", StringComparison.OrdinalIgnoreCase) ? "Run32" : "Run");
        }

        private static bool IsApprovedDisabled(RegistryKey root, string approvedKey, string valueName)
        {
            try
            {
                using var key = root.OpenSubKey(approvedKey, false);
                return key?.GetValue(valueName) is byte[] data && data.Length > 0 && (data[0] & 1) != 0;
            }
            catch { return false; }
        }

        private static string? ProbeFile(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate)) return null;
            candidate = candidate.Trim().Trim('"');
            var names = System.IO.Path.HasExtension(candidate) ? new[] { candidate } : new[] { candidate, candidate + ".exe" };
            foreach (string name in names)
            {
                try
                {
                    if (System.IO.Path.IsPathRooted(name))
                    {
                        if (System.IO.File.Exists(name)) return System.IO.Path.GetFullPath(name);
                        continue;
                    }
                    var folders = new List<string> { Environment.SystemDirectory, Environment.GetFolderPath(Environment.SpecialFolder.Windows) };
                    folders.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries));
                    foreach (string folder in folders)
                    {
                        string full;
                        try { full = System.IO.Path.Combine(folder.Trim(), name); } catch { continue; }
                        if (System.IO.File.Exists(full)) return System.IO.Path.GetFullPath(full);
                    }
                }
                catch { }
            }
            return null;
        }

        private static (string? Exe, string Args) SplitCommandLine(string commandLine)
        {
            string line = Environment.ExpandEnvironmentVariables(commandLine ?? "").Trim();
            if (line.Length == 0) return (null, "");
            if (line[0] == '"')
            {
                int end = line.IndexOf('"', 1);
                if (end < 0) return (ProbeFile(line) ?? line.Trim('"'), "");
                string quoted = line.Substring(1, end - 1);
                return (ProbeFile(quoted) ?? quoted, line.Substring(end + 1).Trim());
            }
            int space = -1;
            do
            {
                space = line.IndexOf(' ', space + 1);
                string found = space < 0 ? line : line.Substring(0, space);
                string? file = ProbeFile(found);
                if (file != null) return (file, space < 0 ? "" : line.Substring(space + 1).Trim());
            } while (space >= 0);
            int first = line.IndexOf(' ');
            return (first < 0 ? line : line.Substring(0, first), first < 0 ? "" : line.Substring(first + 1).Trim());
        }

        private static List<string> SplitArguments(string args)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(args)) return result;
            IntPtr argv = NativeMethods.CommandLineToArgvW("x " + args, out int argc);
            if (argv == IntPtr.Zero) return result;
            try
            {
                for (int i = 1; i < argc; i++)
                {
                    string? value = Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size));
                    if (!string.IsNullOrEmpty(value)) result.Add(value);
                }
            }
            finally { NativeMethods.LocalFree(argv); }
            return result;
        }

        private string? SelectedStartupTarget()
        {
            if (StartupAppsDataGrid.SelectedItem is not StartupApp app || string.IsNullOrWhiteSpace(app.Path))
                return null;
            string? target = null;
            try
            {
                target = ResolveLaunchTarget(app.Path);
            }
            catch
            {
            }
            if (!string.IsNullOrWhiteSpace(target) && (System.IO.File.Exists(target) || System.IO.Directory.Exists(target)))
                return target;
            return LongestExistingPath(app.Path) ?? app.Path.Trim().Trim('"');
        }

        private static string? LongestExistingPath(string commandLine)
        {
            string text = Environment.ExpandEnvironmentVariables(commandLine.Trim());
            if (text.StartsWith("\""))
            {
                int end = text.IndexOf('"', 1);
                if (end > 1)
                {
                    string quoted = text.Substring(1, end - 1);
                    if (System.IO.File.Exists(quoted))
                        return quoted;
                }
            }
            string[] parts = text.Split(' ');
            for (int count = parts.Length; count > 0; count--)
            {
                string candidate = string.Join(" ", parts, 0, count).Trim().Trim('"').TrimEnd(',');
                if (candidate.Length > 2 && System.IO.File.Exists(candidate))
                    return candidate;
            }
            return null;
        }

        private void StartupOpenLocation_Click(object sender, RoutedEventArgs e)
        {
            string? target = SelectedStartupTarget();
            if (target == null)
                return;
            try
            {
                if (System.IO.File.Exists(target))
                    System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + target + "\"");
                else if (System.IO.Directory.Exists(target))
                    System.Diagnostics.Process.Start("explorer.exe", "\"" + target + "\"");
                else
                    AppDialog.Show(Loc.T("The file could not be found."), Loc.T("Open file location"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AppDialog.Show(Loc.F("Could not open folder: {0}", ex.Message), Loc.T("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void StartupCopyPath_Click(object sender, RoutedEventArgs e)
        {
            string? target = SelectedStartupTarget();
            if (target == null)
                return;
            try
            {
                Clipboard.SetText(target);
            }
            catch
            {
            }
        }

        private static string? ResolveLaunchTarget(string commandLine, int depth = 0)
        {
            var (exe, args) = SplitCommandLine(commandLine);
            if (string.IsNullOrEmpty(exe) || depth > 3) return exe;

            if (exe.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                string? target = ResolveShortcutTargetSta(exe);
                return string.IsNullOrEmpty(target) ? null : ResolveLaunchTarget(target, depth + 1);
            }

            string fileName = System.IO.Path.GetFileName(exe);
            var tokens = SplitArguments(args);

            if (LauncherHosts.Contains(fileName))
            {
                string windows = WindowsFolderPrefix();
                foreach (string raw in tokens)
                {
                    string token = Environment.ExpandEnvironmentVariables(raw);
                    int comma = token.IndexOf(',');
                    if (comma > 0) token = token.Substring(0, comma);
                    if (!LaunchableExtensions.Contains(System.IO.Path.GetExtension(token))) continue;
                    string? file = ProbeFile(token);
                    if (file != null && !file.StartsWith(windows, StringComparison.OrdinalIgnoreCase))
                        return file;
                }
                return exe;
            }

            if (fileName.Equals("Update.exe", StringComparison.OrdinalIgnoreCase))
            {
                int flag = tokens.FindIndex(t => t.Equals("--processStart", StringComparison.OrdinalIgnoreCase) || t.Equals("--processStartAndWait", StringComparison.OrdinalIgnoreCase));
                string? folder = System.IO.Path.GetDirectoryName(exe);
                if (flag >= 0 && flag + 1 < tokens.Count && !string.IsNullOrEmpty(folder))
                {
                    string app = tokens[flag + 1];
                    string? latest = Directory.GetDirectories(folder, "app-*")
                        .OrderByDescending(d => Version.TryParse(System.IO.Path.GetFileName(d).Substring(4), out var v) ? v : new Version(0, 0))
                        .Select(d => System.IO.Path.Combine(d, app))
                        .FirstOrDefault(System.IO.File.Exists);
                    if (latest != null) return latest;
                    string beside = System.IO.Path.Combine(folder, app);
                    if (System.IO.File.Exists(beside)) return beside;
                }
            }

            return exe;
        }

        private static string WindowsFolderPrefix() =>
            System.IO.Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.Windows)).TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;

        private string CalculateImpactAutomatically(string path, string appName)
        {
            if (string.IsNullOrWhiteSpace(path)) return "Low";

            try
            {
                string? exePath = ResolveLaunchTarget(path);
                if (string.IsNullOrEmpty(exePath) || !System.IO.File.Exists(exePath)) return "Low";

                string fullPathNormalized = Path.GetFullPath(exePath);
                if (fullPathNormalized.StartsWith(WindowsFolderPrefix(), StringComparison.OrdinalIgnoreCase))
                    return "Low";

                var info = new FileInfo(fullPathNormalized);
                var stamp = (info.LastWriteTimeUtc, info.Length);
                if (ImpactCache.TryGetValue(fullPathNormalized, out var cached) && cached.Stamp == stamp)
                    return cached.Impact;

                long weight = MeasureImportWeightOnce(fullPathNormalized);
                string impact = weight > HighThresholdBytes ? "High" : weight > MediumThresholdBytes ? "Medium" : "Low";
                ImpactCache[fullPathNormalized] = (stamp, impact);
                return impact;
            }
            catch { }

            return "Low";
        }

        private static readonly ConcurrentDictionary<string, ((DateTime, long) Stamp, string Impact)> ImpactCache =
            new(StringComparer.OrdinalIgnoreCase);

        private static long MeasureImportWeightOnce(string exePath)
        {
            long ownSize = 0;
            try { ownSize = new FileInfo(exePath).Length; } catch { }

            var info = PeImportAnalyzer.Analyze(exePath);

            if (!info.IsValidPe)
                return ownSize;
             
            long weight = ownSize + ResolvePrivateImportWeight(exePath, info);
            if (info.IsManaged) weight += ManagedRuntimeOverheadBytes;
            return weight;
        }
         
        private static long ResolvePrivateImportWeight(string exePath, PeImportAnalyzer.PeInfo rootInfo)
        {
            string? exeDir = Path.GetDirectoryName(exePath);
            if (string.IsNullOrEmpty(exeDir)) return 0;

            long totalWeight = 0;
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Path.GetFileName(exePath)
    };
            var queue = new Queue<(string DllName, int Depth)>();
            foreach (var dll in rootInfo.ImportedDllNames)
                queue.Enqueue((dll, 0));

            const int maxDepth = 2;
            const int maxScanned = 200;
            int scanned = 0;

            while (queue.Count > 0 && scanned < maxScanned)
            {
                var (dllName, depth) = queue.Dequeue();

                if (!visited.Add(dllName)) continue;
                scanned++;

                string candidatePath = Path.Combine(exeDir, dllName);
                if (!System.IO.File.Exists(candidatePath)) continue; 

                try { totalWeight += new FileInfo(candidatePath).Length; }
                catch { continue; }

                if (depth >= maxDepth) continue;

                var childInfo = PeImportAnalyzer.Analyze(candidatePath);
                if (!childInfo.IsValidPe) continue;

                foreach (var childDll in childInfo.ImportedDllNames)
                    if (!visited.Contains(childDll))
                        queue.Enqueue((childDll, depth + 1));
            }

            return totalWeight;
        }

        private static string? ResolveShortcutTargetSta(string lnkPath)
        {
            string? target = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var shell = new IWshRuntimeLibrary.WshShell();
                    var shortcut = (IWshRuntimeLibrary.IWshShortcut)shell.CreateShortcut(lnkPath);
                    if (!string.IsNullOrEmpty(shortcut.TargetPath))
                        target = $"\"{shortcut.TargetPath}\" {shortcut.Arguments}".Trim();
                }
                catch { }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            return target;
        }

        private static class PeImportAnalyzer
        {
            private const ushort IMAGE_DOS_SIGNATURE = 0x5A4D;
            private const uint IMAGE_NT_SIGNATURE = 0x00004550;
            private const int IMAGE_DIRECTORY_ENTRY_IMPORT = 1;
            private const int IMAGE_DIRECTORY_ENTRY_COM_DESCRIPTOR = 14; 

            public sealed class PeInfo
            {
                public bool IsValidPe;
                public bool IsManaged;
                public List<string> ImportedDllNames = new();
            }

            public static PeInfo Analyze(string filePath)
            {
                var info = new PeInfo();
                try
                {
                    using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var br = new BinaryReader(fs);

                    if (fs.Length < 0x40) return info;
                    if (br.ReadUInt16() != IMAGE_DOS_SIGNATURE) return info;

                    fs.Seek(0x3C, SeekOrigin.Begin);
                    int peHeaderOffset = br.ReadInt32();
                    if (peHeaderOffset <= 0 || peHeaderOffset >= fs.Length - 4) return info;

                    fs.Seek(peHeaderOffset, SeekOrigin.Begin);
                    if (br.ReadUInt32() != IMAGE_NT_SIGNATURE) return info;

                    br.ReadUInt16();                     
                    ushort numberOfSections = br.ReadUInt16();
                    br.ReadUInt32(); br.ReadUInt32(); br.ReadUInt32(); 
                    ushort sizeOfOptionalHeader = br.ReadUInt16();
                    br.ReadUInt16();                  

                    long optionalHeaderStart = fs.Position;
                    if (sizeOfOptionalHeader == 0) return info;

                    ushort magic = br.ReadUInt16();
                    bool isPe32Plus = magic == 0x20B;
                    if (!isPe32Plus && magic != 0x10B) return info;

                    info.IsValidPe = true;

                    long dataDirectoryOffset = optionalHeaderStart + (isPe32Plus ? 112 : 96);
                    fs.Seek(dataDirectoryOffset, SeekOrigin.Begin);

                    var dataDirectories = new (uint Rva, uint Size)[16];
                    for (int i = 0; i < 16; i++)
                        dataDirectories[i] = (br.ReadUInt32(), br.ReadUInt32());

                    long sectionHeadersOffset = optionalHeaderStart + sizeOfOptionalHeader;
                    fs.Seek(sectionHeadersOffset, SeekOrigin.Begin);

                    var sections = new List<(uint Va, uint Vs, uint RawSize, uint RawPtr)>();
                    for (int i = 0; i < numberOfSections; i++)
                    {
                        fs.Seek(8, SeekOrigin.Current); 
                        uint vs = br.ReadUInt32();
                        uint va = br.ReadUInt32();
                        uint rawSize = br.ReadUInt32();
                        uint rawPtr = br.ReadUInt32();
                        fs.Seek(16, SeekOrigin.Current);
                        sections.Add((va, vs, rawSize, rawPtr));
                    }

                    long RvaToOffset(uint rva)
                    {
                        foreach (var s in sections)
                        {
                            uint size = s.Vs != 0 ? s.Vs : s.RawSize;
                            if (rva >= s.Va && rva < s.Va + size)
                                return s.RawPtr + (rva - s.Va);
                        }
                        return -1;
                    }

                    info.IsManaged = dataDirectories[IMAGE_DIRECTORY_ENTRY_COM_DESCRIPTOR].Rva != 0;

                    var (importRva, importSize) = dataDirectories[IMAGE_DIRECTORY_ENTRY_IMPORT];
                    if (importRva == 0 || importSize == 0) return info;

                    long importTableOffset = RvaToOffset(importRva);
                    if (importTableOffset < 0) return info;

                    fs.Seek(importTableOffset, SeekOrigin.Begin);
                    while (true)
                    {
                        uint originalFirstThunk = br.ReadUInt32();
                        uint timeDateStamp = br.ReadUInt32();
                        uint forwarderChain = br.ReadUInt32();
                        uint nameRva = br.ReadUInt32();
                        uint firstThunk = br.ReadUInt32();

                        if (originalFirstThunk == 0 && timeDateStamp == 0 && forwarderChain == 0
                            && nameRva == 0 && firstThunk == 0)
                            break;

                        if (nameRva != 0)
                        {
                            long nameOffset = RvaToOffset(nameRva);
                            if (nameOffset >= 0)
                            {
                                long returnPos = fs.Position;
                                fs.Seek(nameOffset, SeekOrigin.Begin);
                                string dllName = ReadAsciiZ(br);
                                if (!string.IsNullOrEmpty(dllName))
                                    info.ImportedDllNames.Add(dllName);
                                fs.Seek(returnPos, SeekOrigin.Begin);
                            }
                        }

                        if (info.ImportedDllNames.Count > 256) break;
                    }
                }
                catch {  }

                return info;
            }

            private static string ReadAsciiZ(BinaryReader br)
            {
                var bytes = new List<byte>(64);
                while (true)
                {
                    byte b = br.ReadByte();
                    if (b == 0) break;
                    bytes.Add(b);
                    if (bytes.Count > 260) break;
                }
                return Encoding.ASCII.GetString(bytes.ToArray());
            }
        }

        private static class NativeMethods
        {
            [DllImport("shell32.dll", SetLastError = true)]
            public static extern IntPtr CommandLineToArgvW(
                [MarshalAs(UnmanagedType.LPWStr)] string cmdLine, out int numArgs);

            [DllImport("kernel32.dll")]
            public static extern IntPtr LocalFree(IntPtr hMem);
        }

        private BitmapImage? GetIcon(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            string? target = ResolveLaunchTarget(path);
            string? exe = target != null && target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? target : SplitCommandLine(path).Exe;
            if (string.IsNullOrEmpty(exe)) return null;

            if (IconCache.TryGetValue(exe, out var cachedIcon)) return cachedIcon;
            if (!System.IO.File.Exists(exe)) return null;

            try
            {
                var sysIcon = System.Drawing.Icon.ExtractAssociatedIcon(exe);
                using var ms = new MemoryStream();
                sysIcon!.ToBitmap().Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                ms.Position = 0;

                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();

                IconCache[exe] = bmp;
                return bmp;
            }
            catch { return null; }
        }

        private void CheckPendingChanges()
        {
            int pending = StartupApps.Count(a => a.HasPendingChange);
            btnApply.IsEnabled = pending > 0;
            ApplyText.Text = pending > 0 ? Loc.F("Apply changes ({0})", pending) : Loc.T("Apply changes");
        }

        private bool isMaximized = false;
        private double previousWidth, previousHeight, previousLeft, previousTop;

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (isMaximized)
            {
                WindowState = WindowState.Normal;
                Width = previousWidth;
                Height = previousHeight;
                Left = previousLeft;
                Top = previousTop;
                isMaximized = false;
            }
            else
            {
                previousWidth = Width;
                previousHeight = Height;
                previousLeft = Left;
                previousTop = Top;

                WindowState = WindowState.Normal;
                Left = SystemParameters.WorkArea.Left;
                Top = SystemParameters.WorkArea.Top;
                Width = SystemParameters.WorkArea.Width;
                Height = SystemParameters.WorkArea.Height;
                isMaximized = true;
            }
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            var pending = StartupApps.Where(a => a.HasPendingChange).ToList();
            int failed = pending.Count(item => !item.ApplyPendingChange());
            _lastTaskSchedulerScan = DateTime.MinValue;

            if (failed == 0)
                AppDialog.Show(Loc.T("Changes applied successfully!"), Loc.T("Success"), MessageBoxButton.OK, MessageBoxImage.Information);
            CheckPendingChanges();
            RefreshFilter();
        }

        private void RemoveButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not StartupApp app || !app.CanRemove) return;
            if (AppDialog.Show(Loc.F("Are you sure you want to remove '{0}'?", app.Name), Loc.T("Confirm Removal"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;
            if (app.Remove())
            {
                StartupApps.Remove(app);
                _knownItems.Remove(app.UniqueId);
                _lastTaskSchedulerScan = DateTime.MinValue;
                CheckPendingChanges();
                UpdateSummary();
            }
        }

        private void StartupAppsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => SelectedApp = StartupAppsDataGrid.SelectedItem as StartupApp;

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Visibility = Visibility.Collapsed;
        private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void TopBar_MouseDown(object sender, MouseButtonEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }

        private async void AddButton_Click(object sender, RoutedEventArgs e)
        {
            new AddStartup { Owner = this }.ShowDialog();
            await LoadStartupAppsFastAsync(isSilentRefresh: true);
        }

        private void AddOperationButton_Click(object sender, RoutedEventArgs e)
        {
            new AddBootOperationWindow { Owner = this }.ShowDialog();
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string p = "") => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));

        private Visibility _warningVisibility = Visibility.Collapsed;
        public Visibility WarningVisibility
        {
            get => _warningVisibility;
            set { _warningVisibility = value; OnPropertyChanged(); }
        }

        private string _warningMessage = "";
        public string WarningMessage
        {
            get => _warningMessage;
            set { _warningMessage = value; OnPropertyChanged(); }
        }

        private DispatcherTimer? _warningTimer;
         
        private void ShowInAppWarning(string message)
        {
            Application.Current.Dispatcher.BeginInvoke(new System.Action(() =>
            {
                WarningMessage = message;
                WarningVisibility = Visibility.Visible;

                if (_warningTimer == null)
                {
                    _warningTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(7) };
                    _warningTimer.Tick += (s, e) =>
                    {
                        WarningVisibility = Visibility.Collapsed;
                        _warningTimer.Stop();
                    };
                }

                _warningTimer.Stop();
                _warningTimer.Start();
            }), DispatcherPriority.Normal);
        }

        private void CloseWarning_Click(object sender, RoutedEventArgs e)
        {
            WarningVisibility = Visibility.Collapsed;
            _warningTimer?.Stop();
        }

        public class StartupApp : INotifyPropertyChanged
        {
            public string UniqueId => $"{StartupType}|{RegistryPath}|{ValueName}|{Name}|{Path}";

            public BitmapImage? Icon { get; set; }
            public string Name { get; set; } = "";
            public string Path { get; set; } = "";
            public string ValueName { get; set; } = "";
            public string StartupType { get; set; } = "";
            public RegistryKey? RegistryRoot { get; set; }
            public string RegistryPath { get; set; } = "";
            public string? ApprovedKey { get; set; }
            public bool CanToggleTask { get; set; }
            public bool IsWindowsTask { get; set; }
            public bool IsServiceEntry { get; set; }
            public bool IsPackagedTask { get; set; }
            public bool IsUserApp => !IsWindowsTask && !IsServiceEntry && !StartupType.EndsWith("Provider") && StartupType is not ("Driver" or "Explorer Extension" or "Print Monitor");
            private System.Windows.Media.SolidColorBrush? _flashBrush;
            public System.Windows.Media.SolidColorBrush? FlashBrush { get => _flashBrush; set { _flashBrush = value; OnPropertyChanged(); } }
            public AdvancedSource? Advanced { get; set; }
            public bool IsAdvancedRecord { get; set; }
            public bool CanToggle => Advanced != null || (ApprovedKey != null && RegistryRoot != null) || CanToggleTask || IsServiceEntry || IsPackagedTask;
            private bool IsRegistryValueEntry => StartupType.StartsWith("Registry") || StartupType == "Group Policy" || StartupType == "Terminal Server";
            public bool CanRemove => IsRegistryValueEntry || StartupType == "Scheduled Task" || StartupType == "Startup Folder";
            public string? ToggleHint => Advanced != null ? Loc.T("Advanced item. Turning it off can stop Windows or other programs from working. Only change it if you know what it does.")
                : CanToggle ? null : Loc.T("Windows manages this item, so it cannot be turned off here.");

            private bool _isEnabled, _isPresent, _pendingEnabled;
            public bool IsPresent { get => _isPresent; set { _isPresent = value; OnPropertyChanged(); OnPropertyChanged(nameof(Status)); OnPropertyChanged(nameof(StatusText)); OnPropertyChanged(nameof(IsCheckboxEnabled)); } }
            public bool IsEnabled { get => _isEnabled; set { _isEnabled = value; _pendingEnabled = value; OnPropertyChanged(); OnPropertyChanged(nameof(Status)); OnPropertyChanged(nameof(StatusText)); OnPropertyChanged(nameof(EffectiveImpact)); OnPropertyChanged(nameof(ImpactScore)); OnPropertyChanged(nameof(ImpactText)); } }
            public bool IsCheckboxEnabled => IsPresent && CanToggle;
            public string Impact { get; set; } = "Low";
            public string EffectiveImpact => IsEnabled ? Impact : "None";
            public int ImpactScore => EffectiveImpact switch { "High" => 3, "Medium" => 2, "None" => 0, _ => 1 };
            public string ImpactText => Loc.T(EffectiveImpact);
            public string StartupTypeText => Loc.T(StartupType);
            public string StatusText => Loc.T(Status);

            public Action<string>? HighImpactWarningAction { get; set; }

            public bool PendingEnabled
            {
                get => _pendingEnabled;
                set
                {
                    if (!value && Advanced != null && _isEnabled && _pendingEnabled
                        && AppDialog.Show($"{Name}\n\n{Loc.T("Advanced item. Turning it off can stop Windows or other programs from working. Only change it if you know what it does.")}",
                            Loc.T("Turn off this item?"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                    {
                        Application.Current.Dispatcher.BeginInvoke(new System.Action(() => OnPropertyChanged(nameof(PendingEnabled))), DispatcherPriority.Background);
                        return;
                    }

                    _pendingEnabled = value; 
                    if (value == true && Impact == "High")
                    {
                        HighImpactWarningAction?.Invoke(Loc.F("Warning: '{0}' may consume significant system resources and slow down your startup time.", Name));
                    }

                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasPendingChange));
                }
            }

            public bool HasPendingChange => _isEnabled != _pendingEnabled;

            public string Status
            {
                get => !IsPresent ? "Removed" : (IsEnabled ? "Enabled" : "Disabled");
                set
                {
                    if (value == Status) return;

                    switch (value)
                    {
                        case "Enabled":
                            IsPresent = true;
                            IsEnabled = true;
                            break;
                        case "Disabled":
                            IsPresent = true;
                            IsEnabled = false;
                            break;
                        case "Removed":
                            IsPresent = false;
                            break;
                        default:
                            if (bool.TryParse(value, out var b))
                            {
                                IsPresent = true;
                                IsEnabled = b;
                            }
                            break;
                    }
                    OnPropertyChanged(nameof(Status)); OnPropertyChanged(nameof(StatusText));
                }
            }

            public bool ApplyPendingChange()
            {
                if (!HasPendingChange) return true;
                try
                {
                    if (Advanced != null)
                    {
                        if (_pendingEnabled) Advanced.TurnOn();
                        else Advanced.TurnOff(Name, Path, StartupType);
                    }
                    else if (ApprovedKey != null && RegistryRoot != null)
                    {
                        using var key = RegistryRoot.CreateSubKey(ApprovedKey, true);
                        key.SetValue(ValueName, new byte[] { (byte)(_pendingEnabled ? 0x02 : 0x03), 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, RegistryValueKind.Binary);
                    }
                    else if (IsServiceEntry)
                    {
                        using var service = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + ValueName, true)
                            ?? throw new InvalidOperationException(ValueName);
                        service.SetValue("Start", _pendingEnabled ? 2 : 4, RegistryValueKind.DWord);
                    }
                    else if (IsPackagedTask && RegistryRoot != null)
                    {
                        using var task = RegistryRoot.OpenSubKey(RegistryPath, true) ?? throw new InvalidOperationException(RegistryPath);
                        task.SetValue(ValueName, _pendingEnabled ? 2 : 1, RegistryValueKind.DWord);
                    }
                    else if (CanToggleTask)
                    {
                        using var ts = new TaskService();
                        var task = ts.GetTask(ValueName) ?? throw new InvalidOperationException(Loc.T("The task no longer exists."));
                        task.Enabled = _pendingEnabled;
                    }
                    else
                    {
                        PendingEnabled = _isEnabled;
                        return false;
                    }
                    IsEnabled = _pendingEnabled;
                    return true;
                }
                catch (Exception ex)
                {
                    AppDialog.Show(Loc.F("Error applying changes: {0}", ex.Message), Loc.T("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }
            }

            public bool Remove()
            {
                try
                {
                    if (IsRegistryValueEntry && RegistryRoot != null)
                    {
                        using var key = RegistryRoot.OpenSubKey(RegistryPath, true) ?? throw new InvalidOperationException(RegistryPath);
                        key.DeleteValue(ValueName, false);
                    }
                    else if (StartupType == "Scheduled Task")
                    {
                        using var ts = new TaskService();
                        var task = ts.GetTask(ValueName);
                        task?.Folder.DeleteTask(task.Name);
                    }
                    else if (StartupType == "Startup Folder")
                    {
                        if (System.IO.File.Exists(Path)) System.IO.File.Delete(Path);
                    }
                    else
                    {
                        return false;
                    }

                    if (ApprovedKey != null && RegistryRoot != null)
                    {
                        using var approved = RegistryRoot.OpenSubKey(ApprovedKey, true);
                        approved?.DeleteValue(ValueName, false);
                    }
                    IsPresent = false;
                    return true;
                }
                catch (Exception ex)
                {
                    AppDialog.Show(Loc.F("Could not remove '{0}': {1}", Name, ex.Message), Loc.T("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }
            }

            public event PropertyChangedEventHandler? PropertyChanged;
            protected void OnPropertyChanged([CallerMemberName] string p = "") => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
        }
    }
}
