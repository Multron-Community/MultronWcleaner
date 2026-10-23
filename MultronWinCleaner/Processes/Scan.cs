using Multron_Win_Cleaner;
using Ookii.Dialogs.Wpf;
using System;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.TextFormatting;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Xml.Linq;

namespace MultronWinCleaner.Processes
{

    public class Scan
    {
        MainWindow main;
        long totalsize = 0;
        long deeplogscantotal;
        long currentscan = 0;
        int winsxs = 0;
        int health = 0;
        int sfc = 0;
        int logscan = 0;
        int olderindex = 0;
        int accessindex = 0;
        bool filterByAccess;
        bool filterByAge;
        int customAccessDays;
        int customAgeDays;
        static readonly EnumerationOptions ScanEnumeration = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        static readonly EnumerationOptions DeepScanEnumeration = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint };
        CancellationTokenSource cts = new CancellationTokenSource();
        List<(string file, long size, string path, int days, string date, string modified)> checkboxData = new List<(string file, long size, string path, int days, string date, string modified)>();

        public Scan(MainWindow main)
        {
            this.main = main;
        }




        public class MainViewModel : INotifyPropertyChanged
        {
            public ObservableCollection<GroupViewModel> Groups { get; set; } = new ObservableCollection<GroupViewModel>();

            private GroupViewModel? selectedGroup;
            public GroupViewModel? SelectedGroup
            {
                get => selectedGroup;
                set
                {
                    selectedGroup = value;
                    OnPropertyChanged();
                }
            }

            public event PropertyChangedEventHandler? PropertyChanged;
            protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public class PathGroup : INotifyPropertyChanged
        {
            public string Path { get; set; } = "";
            public ObservableCollection<FileItem> Files { get; set; } = new ObservableCollection<FileItem>();

            public event PropertyChangedEventHandler? PropertyChanged;
            protected void OnPropertyChanged([CallerMemberName] string? propName = null) =>
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        }

        public class FileItem : INotifyPropertyChanged
        {

            public string FileName { get; set; } = "";
            public string Path { get; set; } = "";
            public long SizeBytes { get; set; }
            public int AgeDays { get; set; }
            public string CreatedDate { get; set; } = "";
            public string ModifiedDate { get; set; } = "";
            public string File => Loc.F("File to delete={0}={1} | Created: {2} ({3} days ago) | Modified: {4}", FileName, formatsize(SizeBytes), CreatedDate, AgeDays, ModifiedDate);



            private bool isChecked;
            public bool IsChecked
            {
                get => isChecked;
                set
                {
                    isChecked = value;
                    OnPropertyChanged();
                }
            }

            public event PropertyChangedEventHandler? PropertyChanged;
            protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            private string formatsize(long size)
            {
                if ((size < 0))
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
        }

        public class GroupViewModel : INotifyPropertyChanged
        {
            private const int PageSize = 100;
            private int currentLoadedCount = 0;

            public string Path { get; private set; }

            public ObservableCollection<FileItem> Files { get; private set; } = new ObservableCollection<FileItem>();

            public List<FileItem> allFiles;

            public long TotalSizeBytes { get; private set; }
            public string TotalSizeFormatted => formatsize(TotalSizeBytes);
            public string ExpanderHeader => $"{Path} ({TotalSizeFormatted})";

            public GroupViewModel(string path, List<FileItem> allFiles)
            {
                this.Path = path;
                this.allFiles = allFiles;
                this.TotalSizeBytes = allFiles.Sum(f => f.SizeBytes);

                _ = LoadMoreFilesAsync();
            }
            private string formatsize(long size)
            {
                if ((size < 0))
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


            public async Task LoadMoreFilesAsync()
            {
                int remaining = allFiles.Count - currentLoadedCount;
                if (remaining <= 0) return;

                int toLoad = Math.Min(PageSize, remaining);

                for (int i = currentLoadedCount; i < currentLoadedCount + toLoad; i++)
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        Files.Add(allFiles[i]);
                    }, DispatcherPriority.Background);
                }

                currentLoadedCount += toLoad;
                OnPropertyChanged(nameof(Files));
            }

            public event PropertyChangedEventHandler? PropertyChanged;
            protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }


        }
        public async Task ScandotsAsync(string text, CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (main.cancelstatus.IsCancellationRequested)
                        break;
                    await main.Dispatcher.InvokeAsync(() => {
                        main.label1_Copy.Text = text + ".";
                        main.label1_Copy.Foreground = System.Windows.Media.Brushes.Blue;
                    });

                    await Task.Delay(1000, cancellationToken);

                    await main.Dispatcher.InvokeAsync(() => {
                        main.label1_Copy.Text = text + "..";
                    });

                    await Task.Delay(1000, cancellationToken);

                    await main.Dispatcher.InvokeAsync(() => {
                        main.label1_Copy.Text = text + "...";
                    });

                    await Task.Delay(1000, cancellationToken);
                }

            }
            catch (TaskCanceledException)
            {

            }
        }
        private static readonly Regex PercentRx = new Regex(@"(\d{1,3})(?:[\.,]\d+)?\s?%|%\s?(\d{1,3})", RegexOptions.Compiled);

        public static int ParsePercent(string line)
        {
            var match = PercentRx.Match(line ?? "");
            if (!match.Success) return -1;
            string value = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
            return int.TryParse(value, out int pct) ? Math.Clamp(pct, 0, 100) : -1;
        }

        public static async Task<string> RunSfcCommandAsync(
    string arguments,
    CancellationToken externalToken,
    ProgressBar progressBar,
    int timeoutMinutes = 20,
    Action<int>? onProgress = null)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "sfc.exe",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.Unicode, 
                StandardErrorEncoding = Encoding.Unicode,
                UseShellExecute = false,
                Verb = "runas",
                CreateNoWindow = true
            };

            using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            var outputBuilder = new StringBuilder();

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
            linkedCts.CancelAfter(TimeSpan.FromMinutes(timeoutMinutes));

            proc.OutputDataReceived += (s, e) =>
            {
                if (e.Data == null) return;
                outputBuilder.AppendLine(e.Data);
                int pct = ParsePercent(e.Data);
                if (pct < 0) return;
                progressBar.Dispatcher.BeginInvoke(() => progressBar.Value = pct);
                onProgress?.Invoke(pct);
            };

            proc.ErrorDataReceived += (s, e) => { };

            try
            {
                if (!proc.Start())
                    throw new InvalidOperationException(Loc.T("sfc.exe could not be started."));

                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                Task<int> waitForExitTask = Task.Run(() =>
                {
                    proc.WaitForExit();
                    return proc.ExitCode;
                });

                Task completed = await Task.WhenAny(waitForExitTask, Task.Delay(Timeout.Infinite, linkedCts.Token));

                if (completed != waitForExitTask)
                {
                    try
                    {
                        if (!proc.HasExited)
                        {
                            proc.Kill(entireProcessTree: true);
                            await Task.Delay(500);
                        }
                    }
                    catch { }
                    linkedCts.Token.ThrowIfCancellationRequested();
                }

                await waitForExitTask;

                await progressBar.Dispatcher.InvokeAsync(() =>
                {
                    progressBar.Value = 100;
                });

                return outputBuilder.ToString();
            }
            catch (OperationCanceledException oce)
            {
                await progressBar.Dispatcher.InvokeAsync(() =>
                {
                    progressBar.Value = 0;
                });
                return oce.Message;
            }
            catch (Exception ex)
            {
                await progressBar.Dispatcher.InvokeAsync(() =>
                {
                    progressBar.Value = 0;
                });
                return ex.Message;
            }
        }
        public static async Task<string> RunDismCommandWithProgressAsync(
   string arguments,
   CancellationToken externalToken,
   ProgressBar progressBar,
   int timeoutMinutes = 10,
   Action<int>? onProgress = null)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dism.exe",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                Verb = "runas",
                CreateNoWindow = true
            };

            using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
            linkedCts.CancelAfter(TimeSpan.FromMinutes(timeoutMinutes));

            proc.OutputDataReceived += (s, e) =>
            {
                if (e.Data == null) return;
                outputBuilder.AppendLine(e.Data);
                int pct = ParsePercent(e.Data);
                if (pct < 0) return;
                progressBar.Dispatcher.BeginInvoke(() => progressBar.Value = pct);
                onProgress?.Invoke(pct);
            };

            proc.ErrorDataReceived += (s, e) =>
            {
                if (e.Data != null)
                    errorBuilder.AppendLine(e.Data);
            };

            try
            {
                if (!proc.Start())
                    throw new InvalidOperationException(Loc.T("DISM process could not be started."));

                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                Task<int> waitForExitTask = Task.Run(() =>
                {
                    proc.WaitForExit();
                    return proc.ExitCode;
                });

                Task completed = await Task.WhenAny(waitForExitTask, Task.Delay(Timeout.Infinite, linkedCts.Token));

                if (completed != waitForExitTask)
                {
                    try
                    {
                        if (!proc.HasExited)
                        {
                            proc.Kill(entireProcessTree: true);
                            await Task.Delay(500);
                        }
                    }
                    catch { }
                    linkedCts.Token.ThrowIfCancellationRequested();
                }

                var exitCode = await waitForExitTask;

                await progressBar.Dispatcher.InvokeAsync(() =>
                {
                    progressBar.Value = 100;
                });

                return outputBuilder.ToString() + "#=#" + exitCode;
            }
            catch (OperationCanceledException oce)
            {
                await progressBar.Dispatcher.InvokeAsync(() =>
                {
                    progressBar.Value = 0;
                });
                return oce.Message;
            }
            catch (Exception ex)
            {
                await progressBar.Dispatcher.InvokeAsync(() =>
                {
                    progressBar.Value = 0;
                });
                return ex.Message;
            }
        }

        
        public static Task<string> RunDismAnalyzeComponentStoreAsync(
            CancellationToken externalToken, ProgressBar progressBar, int timeoutMinutes = 10)
            => RunDismCommandWithProgressAsync("/English /Online /Cleanup-Image /AnalyzeComponentStore /NoRestart", externalToken, progressBar, timeoutMinutes);


        private static readonly Regex ExtraSizeRx = new Regex(
      @"^\s*(?<label>Backups and Disabled Features|Cache and Temporary Data)\s*:\s*(?<value>[\d\.]+)\s*(?<unit>KB|MB|GB|TB)",
      RegexOptions.Multiline | RegexOptions.Compiled | RegexOptions.IgnoreCase);


        private static (long BackupsAndDisabledFeatures, long CacheAndTemporaryData) ParseSizes(string text)
        {
            long backups = -1;
            long cache = -1;

            foreach (Match match in ExtraSizeRx.Matches(text))
            {
                string label = match.Groups["label"].Value;
                string value = match.Groups["value"].Value;
                string unit = match.Groups["unit"].Value.ToUpperInvariant();

                if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
                    continue;

                long bytes = unit switch
                {
                    "KB" => (long)(number * 1024),
                    "MB" => (long)(number * 1024 * 1024),
                    "GB" => (long)(number * 1024 * 1024 * 1024),
                    "TB" => (long)(number * 1024L * 1024L * 1024L * 1024L),
                    _ => -1
                };

                if (label.Contains("Backups", StringComparison.OrdinalIgnoreCase))
                    backups = bytes;
                else if (label.Contains("Cache", StringComparison.OrdinalIgnoreCase))
                    cache = bytes;
            }

            return (backups, cache);
        }
        public async void dropscanmessage(string name, string Color)
        {
            await main.Dispatcher.InvokeAsync(() =>
            {
               
                var converter = new System.Windows.Media.BrushConverter();
                var brush = (System.Windows.Media.Brush?)converter.ConvertFromString(Color);

                TextBlock directorytextblock = new TextBlock
                {
                    Text = name,
                    Foreground = brush,  
                    FontSize = 16,
                    Margin = new Thickness(5)
                };
                main.wrapPanelDirectories.Children.Add(directorytextblock);
                main.ScrollViewerDirectories.ScrollToBottom();
            });
        }
        
        public async Task run()
        {
            try
            {
                long size = main.database.Count();
                long nowscanning = 0;
                MainWindow.scanstatus = 2;
                main.onclean = 1;
             

                await main.Dispatcher.InvokeAsync(() =>
                {

                    olderindex = main.settings.cmbAgePreset.SelectedIndex;
                    accessindex = main.settings.cmbAccessPreset.SelectedIndex;
                    filterByAccess = main.settings.AccessScan.IsChecked == true;
                    filterByAge = main.settings.OldScan.IsChecked == true;
                    int.TryParse(main.settings.txtCustomAccess.Text, out customAccessDays);
                    int.TryParse(main.settings.txtCustomDay.Text, out customAgeDays);
                    var task = ScandotsAsync(Loc.T("Scanning"), cts.Token);
                    main.wrapPanelDirectories.Visibility = Visibility.Visible;
                    main.ScrollViewerDirectories.Visibility = Visibility.Visible;
                    main.progressBar1.Value = 0;
                    main.AnimateIcon(true);
                });
   
                foreach (string directory in main.database)
                {
                    if (main.cancelstatus.IsCancellationRequested) break;

                    string? name = main.stringtokenizer(directory, "=", 0);
                    string? path = main.stringtokenizer(directory, "=", 1);
                    if (name == null || path == null) continue;

 


                    if (name.Contains("cleanmgr.exe"))
                    {
                        dropscanmessage(Loc.T("The cleanmgr.exe command will run after the scan is completed and you click the Clean button!"), "#1e88e5");
                        continue;
                    }
                    else if (name.Contains("Dism.exe") && !path.Contains("RestoreHealth", StringComparison.OrdinalIgnoreCase) && winsxs == 0)
                    {
                        try
                        {
                            dropscanmessage(Loc.T("Analyzing the Windows component store (Dism.exe /AnalyzeComponentStore)..."), "#1e88e5");
                            await main.Dispatcher.InvokeAsync(() => main.ShowSystemCheckRunning("store"));
                            string output = await RunDismAnalyzeComponentStoreAsync(main.dismcancel.Token, main.progressBar1);

                            if (!main.cancelstatus.IsCancellationRequested)
                            {
                                await System.IO.File.WriteAllTextAsync(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dism_scan.log"), output);
                                var info = SystemChecks.ParseAnalyze(output);
                                if (info.Parsed)
                                    totalsize += info.Reclaimable;
                                await main.Dispatcher.InvokeAsync(() => main.ShowComponentStoreResult(info));
                                dropscanmessage(info.Parsed
                                    ? Loc.F("Component store analyzed: {0} can be reclaimed.", main.formatsize(info.Reclaimable))
                                    : Loc.T("Dism.exe did not report the component store size (see dism_scan.log)."), info.Parsed ? "#28A745" : "#FF9800");
                            }
                            winsxs = 1;
                        }
                        catch (OperationCanceledException)
                        {
                            winsxs = 1;
                            main.cancelstatus.Cancel();
                            break;
                        }
                    }

                    else if (name.Contains("Dism.exe") && path.Contains("RestoreHealth", StringComparison.OrdinalIgnoreCase) && health == 0)
                    {
                        try
                        {
                            dropscanmessage(Loc.T("Checking the component store for corruption (Dism.exe /ScanHealth)..."), "#1e88e5");
                            await main.Dispatcher.InvokeAsync(() => main.ShowSystemCheckRunning("health"));
                            string output = await RunDismCommandWithProgressAsync(
                                "/English /Online /Cleanup-Image /ScanHealth /NoRestart",
                                main.dismcancel.Token,
                                main.progressBar1,
                                timeoutMinutes: 30);

                            if (!main.cancelstatus.IsCancellationRequested)
                            {
                                await System.IO.File.WriteAllTextAsync(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dism_health.log"), output);
                                var state = SystemChecks.ParseScanHealth(output);
                                string detail = SystemChecks.LastLine(output);
                                await main.Dispatcher.InvokeAsync(() => main.ShowComponentHealthResult(state, detail));
                                dropscanmessage(state == SystemChecks.HealthState.Healthy
                                    ? Loc.T("No component store corruption detected.")
                                    : state == SystemChecks.HealthState.Unknown ? Loc.F("Component store check: {0}", detail) : Loc.T("Component store corruption detected. Restore Health is recommended."),
                                    state == SystemChecks.HealthState.Healthy ? "#28A745" : "#FF9800");
                            }
                            health = 1;
                        }
                        catch (OperationCanceledException)
                        {
                            main.cancelstatus.Cancel();
                            break;
                        }
                    }
                    else if (name.Contains("Deep Log Files Scan") && logscan == 0)
                    {
                        dropscanmessage(Loc.T("Running Deep Log Files Scan"), "#1e88e5");
                        cts.Cancel();
                        current = 0;
                        await main.Dispatcher.InvokeAsync(() =>
                        {
                            var animation = new DoubleAnimation
                            {
                                From = 0,
                                To = 100,
                                Duration = TimeSpan.FromSeconds(2),
                                RepeatBehavior = RepeatBehavior.Forever
                            };
                            main.progressBar1.BeginAnimation(ProgressBar.ValueProperty, animation);
                        });

                        await ScanCDirectoryAsync(path);
                        await main.Dispatcher.InvokeAsync(() =>
                        {
                            main.progressBar1.BeginAnimation(ProgressBar.ValueProperty, null);
                            main.progressBar1.Value = 100;
                        });
                        dropscanmessage(Loc.T("Deep Log Files Scan Completed!"), "#28A745");

                        logscan = 1;




                    }
                    else if (name.ToLower().Contains("sfc.exe") && sfc == 0)
                    {
                        try
                        {
                            dropscanmessage(Loc.T("Checking Windows system files (sfc /verifyonly)..."), "#1e88e5");
                            await main.Dispatcher.InvokeAsync(() => main.ShowSystemCheckRunning("sfc"));
                            string output = await RunSfcCommandAsync(
                                "/verifyonly",
                                main.dismcancel.Token,
                                main.progressBar1,
                                timeoutMinutes: 30);

                            if (!main.cancelstatus.IsCancellationRequested)
                            {
                                await System.IO.File.WriteAllTextAsync(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sfc_verify.log"), output);
                                var state = SystemChecks.ParseSfc(output);
                                string detail = SystemChecks.LastLine(output);
                                await main.Dispatcher.InvokeAsync(() => main.ShowSystemFilesResult(state, detail));
                                dropscanmessage(state == SystemChecks.SfcState.Clean
                                    ? Loc.T("No system file integrity violations detected.")
                                    : state == SystemChecks.SfcState.Unknown ? Loc.F("System file check: {0}", detail) : Loc.T("System file integrity violations detected. sfc /scannow is recommended."),
                                    state == SystemChecks.SfcState.Clean ? "#28A745" : "#FF9800");
                            }
                            sfc = 1;
                        }
                        catch (OperationCanceledException)
                        {
                            main.cancelstatus.Cancel();
                            break;
                        }
                    }
                    else
                    {
                        dropscanmessage(Loc.F("Scanning: {0}: {1}", name, path), "#1e88e5");
                        if (System.IO.File.Exists(path) && !main.settings.IsExcluded(path))
                        {
                            await addtocheckbox(path, name, "Direct Files");
                        }

                        else
                        {
                            currentscan = 0;

                            await ScanDirectoryAsync(path, name);
                        }


                        nowscanning++;
                        double percent = (double)nowscanning / size * 100;
                        await main.Dispatcher.InvokeAsync(() => main.progressBar1.Value = percent);
                    }

                }
               
                await main.Dispatcher.InvokeAsync(async () =>
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();


                    main.wrapPanelDirectories.Children.Clear();
                    main.wrapPanelDirectories.Visibility = Visibility.Hidden;
                    main.wrapPanel1.Visibility = Visibility.Hidden;
                    main.dataGridGroups.Visibility = Visibility.Visible;
                    main.ScrollViewerDetectedFiles.Visibility = Visibility.Hidden;
                    main.Datagridscroll.Visibility = Visibility.Visible;
                    var task = ScandotsAsync(Loc.T("Scanning"), cts.Token);


                 
                    var groupedByPath = await Task.Run(() => checkboxData.GroupBy(x => x.path).ToList());

                    int totalGroups = groupedByPath.Count;
                    int currentGroup = 0;
                    cts.Cancel();
                    foreach (var group in groupedByPath)
                    {
                        if (main.cancelstatus.IsCancellationRequested) break;

                        string path = group.Key;
                        var items = group.ToList();

                        var allFiles = items.Select(item => new FileItem
                        {
                            FileName = Loc.F("{0}={1} | Created: {2} ({3} days) | Last Access: {4}", item.file, main.formatsize(item.size), item.date, item.days, item.modified),
                            SizeBytes = item.size,
                            IsChecked = true,
                            Path = item.file,
                            AgeDays = item.days,
                            CreatedDate = item.date,
                            ModifiedDate = item.modified
                        }).ToList();

                        GroupViewModel groupVm = new GroupViewModel(path, allFiles);

                        await main.Dispatcher.InvokeAsync(() =>
                        {
                                main.viewModel.Groups.Add(groupVm);

                                currentGroup++;
                                main.label1_Copy.Text = Loc.F("Loading group {0} / {1}", currentGroup, totalGroups);
                                main.progressBar1.Value = (double)currentGroup / totalGroups * 100;
                        });

                        await Task.Delay(10);

                    }

                    main.DataContext = main.viewModel;
                    main.label1_Copy.Text = Loc.T("Loading Completed!");
                    main.progressBar1.Value = 100;
                    main.label1_Copy.Foreground = System.Windows.Media.Brushes.Goldenrod;
                    main.buttonReset.Visibility = Visibility.Visible;
                    main.buttonStartScan.IsEnabled = true;
                    main.buttonStartScan.Content = Loc.T("Clean");

                    main.AnimateIcon(false);
                    if (!main.cancelstatus.IsCancellationRequested)
                        await main.RunFirewallReportAsync();

                    if (!main.cancelstatus.IsCancellationRequested)
                        await main.RunShortcutReportAsync();

                    if (!main.cancelstatus.IsCancellationRequested)
                        await main.RunSecurityReportAsync();

                    if (!main.cancelstatus.IsCancellationRequested)
                        await main.RunDuplicateReportAsync();

                    if (!main.cancelstatus.IsCancellationRequested)
                        await main.RunQueuedMalwareScanAsync();

                    if (main.startupscan == 1)
                    {
                        if (main.cancelstatus.IsCancellationRequested == true)
                        {
                           main.label1_Copy.Text = Loc.F("Startup scan canceled! + {0}  Useless file found! {1}", main.formatsize(totalsize), DateTime.Now);

                        }
                        else
                        {
                          
                            main.label1_Copy.Text = Loc.F("Startup scan completed! + {0}  Useless file found! {1}", main.formatsize(totalsize), DateTime.Now);
                            main.Dispatcher.Invoke(() =>
                            {
                                if (main.settings.chkEnableNotifyScan.IsChecked == true && (main.Visibility != Visibility.Visible || main.WindowState == WindowState.Minimized))
                                {
                                    Notify notify = new Notify(Loc.T("Scan Information"), Loc.T("Your startup system scan done!") + "\r\n", $"{main.formatsize(totalsize)}");
                                    notify.Show();
                                }
                            });
                            if(main.startupclean == 1)
                              main.buttonStartScan.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        }
                    } else
                    {
                        if (main.autoclean == 1)
                        {
                            if (main.cancelstatus.IsCancellationRequested == true)
                            {
                             
                                main.label1_Copy.Text = Loc.F("Auto scan canceled! + {0}  Useless file found! {1}", main.formatsize(totalsize), DateTime.Now);

                            }
                            else
                            {
                           
                                main.label1_Copy.Text = Loc.F("Auto scan completed! + {0}  Useless file found! {1}", main.formatsize(totalsize), DateTime.Now);
                                main.Dispatcher.Invoke(() =>
                                {
                                    if (main.settings.chkEnableNotifyScan.IsChecked == true && main.Visibility == Visibility.Hidden)
                                    {
                                        Notify notify = new Notify(Loc.T("Scan Information"), Loc.T("Your auto system scan done!") + "\r\n", $"{main.formatsize(totalsize)}");
                                        notify.Show();
                                    }
                                });
                                main.buttonStartScan.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            }

                        }
                        else
                        {
                            if (main.cancelstatus.IsCancellationRequested == true)
                            {
                                main.label1_Copy.Text = Loc.F("Scan canceled! + {0}  Useless file found! {1}", main.formatsize(totalsize), DateTime.Now);

                            }
                            else
                            {
                                main.label1_Copy.Text = Loc.F("Scan completed! + {0}  Useless file found! {1}", main.formatsize(totalsize), DateTime.Now);
                                await main.Dispatcher.InvokeAsync(() =>
                                {
                                    if (main.settings.chkEnableNotifyScan.IsChecked == true && main.Visibility == Visibility.Hidden)
                                    {
                                        Notify notify = new Notify(Loc.T("Scan Information"), Loc.T("Your system scan done!") + "\r\n", $"{main.formatsize(totalsize)}");
                                        notify.Show();
                                    }
                                });
                            }

                        }
                    }
                 
                  


                }).Task.Unwrap();

                MainWindow.scanstatus = 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                File.AppendAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "load_error.log"),   $"{DateTime.Now}: {ex.Message}\n{ex.StackTrace}\n\n");
            }
        }


        public async Task addtocheckbox(string file, string path, string process)
        {
            try
            {
         
                if (!System.IO.File.Exists(file))
                {
                    return;
                }

                FileInfo fileinfo = new FileInfo(file);

                DateTime creationTime = fileinfo.CreationTime;
                DateTime accessDate = fileinfo.LastAccessTime;
                DateTime modifiedDate = fileinfo.LastWriteTime; 
                long fSize = fileinfo.Length;
                 
                double modifiedDays = (DateTime.Now - modifiedDate).TotalDays;
                double accessDays = (DateTime.Now - accessDate).TotalDays;
                 
                bool isAccessChecked = filterByAccess;
                bool isOldChecked = filterByAge;
                int customAccess = customAccessDays;
                int customOld = customAgeDays;

                bool shouldAdd = false;
                 
                if (isAccessChecked || isOldChecked)
                {
                    if (isAccessChecked)
                    {
                        shouldAdd = accessindex switch
                        {
                            0 => accessDays > 7,
                            1 => accessDays > 30,
                            2 => accessDays > 90,
                            3 => customAccess > 0 && accessDays > customAccess,  
                            _ => false
                        };
                    }
                    else if (isOldChecked)  
                    {
                        shouldAdd = olderindex switch
                        {
                            0 => modifiedDays > 7,
                            1 => modifiedDays > 30,
                            2 => modifiedDays > 90,
                            3 => customOld > 0 && modifiedDays > customOld,
                            _ => false
                        };
                    }
                }
                else
                { 
                    shouldAdd = true;
                } 
                if (shouldAdd)
                {
                    totalsize += fSize;
                    currentscan += fSize;

                    if (process.Contains("Deep Log Scanner Result"))
                    {
                        main.logfiles.Add(file);
                        deeplogscantotal += fSize;
                    }
                     
                    checkboxData.Add((file, fSize, path, (int)modifiedDays, creationTime.ToString(), modifiedDate.ToString()));
                }
            }
            catch (Exception ex)
            {
          
                await main.Dispatcher.InvokeAsync(() =>
                {
                    catchexception(ex.Message + " " + ex.StackTrace);
                });
            }
        }

        public void catchexception(string message)
        {
            using (StreamWriter writer = new StreamWriter(Environment.CurrentDirectory + "\\exceptions.txt"))
            {
                writer.WriteLine(message);
            }
        }


        public static long total = 0;
        public static long current = 0;
        public async Task ScanCDirectoryAsync(string path)
        {
         
            try
            {
                if (Directory.Exists(path))
                {
                    foreach (string dir in Directory.EnumerateDirectories(path, "*", DeepScanEnumeration))
                    {
                        if (main.cancelstatus.IsCancellationRequested) break;
                        await ScanCDirectoryAsync(dir);
                    }
                    foreach (string file in Directory.EnumerateFiles(path, "*", DeepScanEnumeration))
                    {
                        if (main.cancelstatus.IsCancellationRequested) break;

                        if (!main.settings.IsExcluded(file))
                        {
                            if (main.extensions.Split('.').Any(ext => file.EndsWith($".{ext}", StringComparison.OrdinalIgnoreCase)))
                            {
                                await addtocheckbox(file, path, "Deep Log Scanner Result");
                            }
                        }

                        current++;
                        if (current % 100 == 0)
                        {
                            await main.Dispatcher.InvokeAsync(() =>
                            {
                                main.label1_Copy.Text = Loc.F("Scanning C:\\ — {0} files checked, {1} found", current, main.formatsize(deeplogscantotal));
                                 
                                var converter = new System.Windows.Media.BrushConverter();
                                main.label1_Copy.Foreground = (System.Windows.Media.Brush)converter.ConvertFromString("#1e88e5")!;
                            });
                        }
                    }

                }
            }
            catch (Exception) {
             
            }

           
        }

        public async Task ScanDirectoryAsync(string path, string name)
        {
            try
            {
                if (Directory.Exists(path))
                {
                     
                    foreach (string file in Directory.EnumerateFiles(path, "*", ScanEnumeration))
                    {
                        if (main.cancelstatus.IsCancellationRequested) break;
                        if (!main.settings.IsExcluded(file))
                        {
                            await addtocheckbox(file, path, name);
                           
                     
                        }
                    }
                }
            }
            catch { }
            dropscanmessage(Loc.F("Completed: {0} {1}", name, main.formatsize(currentscan)), "#28A745");
          
          
        }

     


    }
}
