using MFK;
using Microsoft.VisualBasic.Logging;
using Multron_Win_Cleaner;
using MultronWinCleaner;
using Ookii.Dialogs.Wpf;
using System;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Xml.Linq;
using static Multron_Win_Cleaner.MainWindow;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.ListView;
namespace MultronWinCleaner.Processes
{
    public class Clean
    {
        public MainWindow main;
        private int shortcut;
        private long totalsize;
        int winsxs = 0;
        private volatile string dotsText = "";
        private HashSet<string> checkedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private List<string> browserTargets = new List<string>();
        private List<(string Name, string Path)> riskyTargets = new List<(string Name, string Path)>();
        private List<string> extraTargets = new List<string>();
        private readonly HashSet<string> skippedRiskyPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> skippedBrowserPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> lockedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private string currentItem = "";
        private int itemFiles;
        private long itemBytes;
        private int itemLocked;
        private int itemDenied;
        private int itemReboot;
        private int totalReboot;
        private int totalFiles;
        private long totalBytes;
        private int totalLocked;
        private int totalDenied;
        private int cleanedItems;

        private SecureDelete.Mode? shredModeCache;
        private SecureDelete.Mode ShredMode => shredModeCache ??= SecureDelete.ReadConfiguredMode();
        private bool Shredding => ShredMode != SecureDelete.Mode.Off;
        private int skippedItems;
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> exceptionLog = new();
        private const int MaxLoggedExceptions = 2000;
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> rebootFiles = new();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> rebootSet = new(StringComparer.OrdinalIgnoreCase);
        private const int MaxListedRebootFiles = 300;
        private readonly System.Collections.Concurrent.ConcurrentQueue<(string File, int Error)> deniedFiles = new();
        private readonly Stopwatch cleanTimer = new Stopwatch();
        private bool automaticRun;
        private bool runComponentCleanup;
        private bool runRestoreHealth;
        private bool runSfc;
        private bool restoreHealthDone;
        private static SolidColorBrush BLUE => ThemeColors.ReadableBrush("#1e88e5");
        private static SolidColorBrush GREEN => ThemeColors.ReadableBrush("#28A745");


        CancellationTokenSource cts = new CancellationTokenSource();
        public Clean(MainWindow main)
        {
            this.main = main;
        }
        private static SolidColorBrush CreateBrush(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();  
            return brush;
        }
        public async Task ScandotsAsync(string text, CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (main.cancelclean == 2)
                        break;

                    await main.Dispatcher.InvokeAsync(() => {
                        main.label1_Copy.Text = (dotsText ?? text) + ".";
                        main.label1_Copy.Foreground = BLUE;
                    });

                    await Task.Delay(1000, cancellationToken);

                    await main.Dispatcher.InvokeAsync(() => {
                        main.label1_Copy.Text = (dotsText ?? text) + "..";
                    });

                    await Task.Delay(1000, cancellationToken);

                    await main.Dispatcher.InvokeAsync(() => {
                        main.label1_Copy.Text = (dotsText ?? text) + "...";
                    });

                    await Task.Delay(1000, cancellationToken);
                }
              
            }
            catch (TaskCanceledException)
            {

            }
        }
     
        private async Task ProcessShortcutAsync(string shortcutString)
        {
            string[] parts = shortcutString.Split('=');

            if (parts.Length >= 2)
            {
                string targetFile = parts[0];
                string arguments = parts[1];

                await RunCommandAsync(targetFile, arguments);
            }
        }
        private async Task ProcessCommandAsync(string shortcutString)
        {
            string[] parts = shortcutString.Split('=');

            if (parts.Length >= 2)
            {
                string targetFile = parts[0];
                string arguments = parts[1];

                if (targetFile.Equals("Dism.exe", StringComparison.OrdinalIgnoreCase))
                {
                    arguments = $"/c {targetFile} {arguments}";   
                    targetFile = "cmd.exe";
                }

                await RunCommandAsync(targetFile, arguments);
            }
        }
        private async Task RunCommandAsync(string fileName, string arguments)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Normal
            };

            try
            {
 
                await Task.Run(() =>
                {
                    using (Process? process = Process.Start(startInfo))
                    {
                        if (process != null)
                        {
                            process.WaitForExit();
                        }
                    }
                });
            }
            catch (System.ComponentModel.Win32Exception)
            {
                 
                main.Dispatcher.Invoke(() =>
                {
                    AppDialog.Show(Loc.F("Administrator permission was denied for '{0}'. Skipping...", arguments), Loc.T("Cancelled"), MessageBoxButton.OK, MessageBoxImage.Warning);
                });
            }
            catch (Exception ex)
            {
        
                main.Dispatcher.Invoke(() =>
                {
                    AppDialog.Show(Loc.F("An error occurred: {0}", ex.Message), Loc.T("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
        }
        public async Task run()
        {
       
            await main.Dispatcher.InvokeAsync(() =>
            {
                main.wrapPanelDirectories.Children.Clear();
                main.wrapPanelDirectories.Visibility = Visibility.Hidden;
                main.wrapPanel1.Visibility = Visibility.Hidden;
                main.dataGridGroups.Visibility = Visibility.Hidden;
                main.wrapPanelDirectories.Visibility = Visibility.Visible;
                main.ScrollViewerDirectories.Visibility = Visibility.Visible;
                main.Datagridscroll.Visibility = Visibility.Hidden;
                main.buttonStartScan.Content = Loc.T("Cancel");
                main.buttonReset.Visibility = Visibility.Collapsed;
                main.progressBar1.Value = 0;
                main.ShowRestartRequired(0);
                main.AnimateIcon(true);
                checkedPaths = new HashSet<string>(
                    main.viewModel.Groups.Where(g => g.allFiles != null).SelectMany(g => g.allFiles).Where(f => f.IsChecked).Select(f => f.Path),
                    StringComparer.OrdinalIgnoreCase);
                browserTargets = main.viewModel.Groups
                    .Where(g => g.Path != null && g.allFiles != null && g.allFiles.Any(f => f.IsChecked) && main.browserPaths.ContainsKey(g.Path))
                    .Select(g => g.Path)
                    .ToList();
                var pathsWithFiles = new HashSet<string>(
                    main.viewModel.Groups.Where(g => g.Path != null && g.allFiles != null && g.allFiles.Any(f => f.IsChecked)).Select(g => g.Path),
                    StringComparer.OrdinalIgnoreCase);
                riskyTargets = main.database
                    .Select(item => (Name: GetToken(item, 0), Path: GetToken(item, 1)))
                    .Where(e => main.warningPaths.ContainsKey(e.Path) && (pathsWithFiles.Contains(e.Path) || checkedPaths.Contains(e.Path)))
                    .Distinct()
                    .ToList();
                extraTargets = main.database
                    .Select(item => GetToken(item, 1))
                    .Where(p => main.extraDatabasePaths.ContainsKey(p) && (pathsWithFiles.Contains(p) || checkedPaths.Contains(p)))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            });

            automaticRun = main.autoclean == 1 || main.startupclean == 1;
            var repairChoices = await main.Dispatcher.InvokeAsync(() => main.GetSystemRepairChoices());
            runComponentCleanup = repairChoices.Cleanup;
            runRestoreHealth = repairChoices.Restore;
            runSfc = repairChoices.Sfc;
            await ConfirmRiskyItemsAsync();
            if (automaticRun)
                await SkipExtraDatabaseItemsAsync();
            else
                await HandleOpenBrowsersAsync();
            cleanTimer.Restart();
            shredModeCache = null;
            var task = ScandotsAsync(Shredding ? Loc.T("Shredding & Cleaning") : Loc.T("Cleaning"), cts.Token);
            var items = main.database.Where(item => !item.EndsWith("=logscan")).ToList();
            int totalCount = items.Count;
            int cleanedCount = 0;
            totalsize = new DriveInfo("C:\\").AvailableFreeSpace;

            await UpdateStatusColor(BLUE);

            if (Shredding)
                await AddStatusTextBlock(Loc.F("Secure deletion is on ({0}): files are shredded before they are removed, so they cannot be recovered.", SecureDelete.Describe(ShredMode)));

            if (main.logfiles.Count > 0)
            {
                var status = await AddStatusTextBlock(Loc.F(Shredding ? "Shredding & Cleaning: Found Log Files in C:\\ ({0} files)" : "Cleaning: Found Log Files in C:\\ ({0} files)", main.logfiles.Count));
                StartItem(Loc.T("Deep Log Files"));
                int logTotal = main.logfiles.Count;
                await DeleteFilesAsync(main.logfiles.ToList(), Loc.T("Deep Log Scan Result"), done =>
                {
                    dotsText = Loc.F(Shredding ? "Shredding & Cleaning: Deep Log Files ({0} / {1})" : "Cleaning: Deep Log Files ({0} / {1})", done, logTotal);
                    _ = UpdateProgress(done, logTotal);
                });

                FinishItem();
                await main.Dispatcher.InvokeAsync(() =>
                {
                    status.Text = Loc.F(Shredding ? "Shredded & Cleaned: Found Log Files in C:\\ ({0})" : "Cleaned: Found Log Files in C:\\ ({0})", DetailText());
                    if (itemLocked > 0 || itemDenied > 0)
                        status.Foreground = ThemeColors.Warning;
                });
            }

            var cleanedFolders = new List<(string Path, string Name)>();
            foreach (string item in items)
            {
                if (main.cancelclean == 2) break;

                cleanedCount++;
                int iscleaned = 0;
                await UpdateProgress(cleanedCount, totalCount);

                string name = GetToken(item, 0);
                string path = GetToken(item, 1);
         

                if (name.Contains("cleanmgr.exe") && shortcut == 0)
                {
                    if (automaticRun)
                    {
                        string cleanmgrArguments = GetToken(item, 1);
                        if (cleanmgrArguments.StartsWith("/d", StringComparison.OrdinalIgnoreCase) || cleanmgrArguments.Equals("/lowdisk", StringComparison.OrdinalIgnoreCase))
                            cleanmgrArguments = "/verylowdisk";
                        StartInBackground("cleanmgr.exe", cleanmgrArguments);
                        await AddStatusTextBlock(Loc.F("Started in the background: cleanmgr.exe {0}", cleanmgrArguments));
                    }
                    else
                    {
                        await AddStatusTextBlock(Loc.T("Running cleanmgr.exe"));
                        await ProcessShortcutAsync(item);
                    }
                    shortcut = 1;
                    iscleaned = 3;
                }
                else if (name.Contains("Dism.exe"))
                {
                    bool restore = GetToken(item, 1).Contains("RestoreHealth", StringComparison.OrdinalIgnoreCase);
                    if (restore ? restoreHealthDone : winsxs == 1)
                    {
                    }
                    else if (!(restore ? runRestoreHealth : runComponentCleanup))
                    {
                        var skipBlock = await AddStatusTextBlock(restore
                            ? Loc.T("Skipped: Dism.exe Restore Health (no corruption found or not selected)")
                            : Loc.T("Skipped: Dism.exe component cleanup (not selected)"));
                        await main.Dispatcher.InvokeAsync(() => skipBlock.Foreground = GREEN);
                    }
                    else if (restore)
                    {
                        await RunSystemToolAsync(Loc.T("Dism.exe Restore Health"), false, "/English /Online /Cleanup-Image /RestoreHealth /NoRestart", "dism_restorehealth.log");
                        iscleaned = 3;
                    }
                    else
                    {
                        await RunSystemToolAsync(Loc.T("Dism.exe component cleanup"), false, "/English /Online /Cleanup-Image /StartComponentCleanup /NoRestart", "dism_cleanup.log");
                        iscleaned = 3;
                    }

                    if (restore)
                        restoreHealthDone = true;
                    else
                        winsxs = 1;
                }
                else if (name.Contains("sfc.exe", StringComparison.OrdinalIgnoreCase))
                {
                    if (!runSfc)
                    {
                        var skipBlock = await AddStatusTextBlock(Loc.T("Skipped: sfc.exe /scannow (no violations found or not selected)"));
                        await main.Dispatcher.InvokeAsync(() => skipBlock.Foreground = GREEN);
                    }
                    else
                    {
                        await RunSystemToolAsync("sfc /scannow", true, "/scannow", "sfc_scannow.log");
                        iscleaned = 3;
                    }
                    runSfc = false;
                }
                else if (Directory.Exists(path) || File.Exists(path))
                {
                    dotsText = Loc.F(Shredding ? "Shredding & Cleaning: {0}" : "Cleaning: {0}", name);
                    var statusBlock = await AddStatusTextBlock(Loc.F(Shredding ? "Shredding & Cleaning: {0}: {1}" : "Cleaning: {0}: {1}", name, path));
                    StartItem(name);
                    string parentName = "";

                    if (skippedRiskyPaths.Contains(path))
                    {
                        iscleaned = 10;
                    }
                    else if (skippedBrowserPaths.Contains(path))
                    {
                        iscleaned = 9;
                    }
                    else if (File.Exists(path))
                    {
                        if (!checkedPaths.Contains(path) || main.settings.IsExcluded(path))
                        {
                            iscleaned = 0;
                        }
                        else
                        {
                            TryDeleteFile(path, name);
                            iscleaned = itemLocked > 0 ? 5 : itemDenied > 0 ? 7 : 1;
                        }
                    }
                    else
                    {
                        var targetGroup = main.viewModel.Groups.FirstOrDefault(g => g.Path != null && g.Path.Equals(path, StringComparison.OrdinalIgnoreCase));

                        if (targetGroup == null || targetGroup.allFiles == null || targetGroup.allFiles.Count == 0)
                        {
                            parentName = cleanedFolders.FirstOrDefault(c => path.StartsWith(c.Path.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)).Name ?? "";
                            iscleaned = parentName.Length > 0 ? 8 : 2;
                        }
                        else if (targetGroup.allFiles.Any(f => f.IsChecked))
                        {
                            var groupFiles = targetGroup.allFiles.Where(f => f.IsChecked).Select(f => f.Path).ToList();
                            await DeleteFilesAsync(groupFiles, name, null);
                            await Task.Run(() => DeleteEmptyDirectories(path));
                            cleanedFolders.Add((path, name));
                            iscleaned = itemLocked > 0 || itemDenied > 0 ? 6 : 1;
                        }
                    }

                    FinishItem();
                    if (iscleaned == 0 || iscleaned == 9 || iscleaned == 10)
                        skippedItems++;
                    string detail = DetailText();

                    await main.Dispatcher.InvokeAsync(() =>
                    {
                        if (iscleaned == 0)
                        {
                            statusBlock.Text = Loc.F("Ignored: {0}: {1}", name, path);
                            statusBlock.Foreground = GREEN;
                        }
                        else if (iscleaned == 10)
                        {
                            statusBlock.Text = Loc.F("Skipped, not confirmed: {0}: {1}", name, path);
                            statusBlock.Foreground = ThemeColors.Warning;
                        }
                        else if (iscleaned == 9)
                        {
                            statusBlock.Text = Loc.F("Skipped, the program is open: {0}: {1}", name, path);
                            statusBlock.Foreground = ThemeColors.Warning;
                        }
                        else if (iscleaned == 2)
                        {
                            statusBlock.Text = Loc.F("Folder Empty: {0}: {1}", name, path);
                            statusBlock.Foreground = ThemeColors.ReadableBrush(Colors.Gray);
                        }
                        else if (iscleaned == 8)
                        {
                            statusBlock.Text = Loc.F("Cleaned with {0}: {1}: {2}", parentName, name, path);
                            statusBlock.Foreground = GREEN;
                        }
                        else if (iscleaned == 1)
                        {
                            statusBlock.Text = Loc.F(Shredding ? "Shredded & Cleaned: {0}: {1} ({2})" : "Cleaned: {0}: {1} ({2})", name, path, detail);
                            statusBlock.Foreground = BLUE;
                        }
                        else if (iscleaned == 5)
                        {
                            statusBlock.Text = Loc.F("Locked: {0}: {1}", name, path);
                            statusBlock.Foreground = MultronWinCleaner.Processes.ThemeColors.ReadableBrush(Colors.Red);
                        }
                        else if (iscleaned == 7)
                        {
                            statusBlock.Text = Loc.F("Access Denied: {0}: {1}", name, path);
                            statusBlock.Foreground = MultronWinCleaner.Processes.ThemeColors.ReadableBrush(Colors.Red);
                        }
                        else if (iscleaned == 6)
                        {
                            statusBlock.Text = Loc.F(Shredding ? "Partly Shredded & Cleaned: {0}: {1} ({2})" : "Partly Cleaned: {0}: {1} ({2})", name, path, detail);
                            statusBlock.Foreground = ThemeColors.Warning;
                        }
                    });
                }
            }

            if (main.cancelclean != 2)
                await RemoveMalwareThreatsAsync();

            cleanTimer.Stop();
            TimeSpan elapsed = cleanTimer.Elapsed;
            string summaryText = Loc.F(Shredding
                    ? "{0}: {1} files shredded and deleted ({2}) from {3} locations, {4} locked files, {5} access denied, {6} unticked locations skipped. Took {7}m {8}s"
                    : "{0}: {1} files deleted ({2}) from {3} locations, {4} locked files, {5} access denied, {6} unticked locations skipped. Took {7}m {8}s",
                Loc.T(main.cancelclean == 2 ? "Cleaning canceled" : "Summary"), totalFiles, main.formatsize(totalBytes), cleanedItems, totalLocked, totalDenied, skippedItems, (int)elapsed.TotalMinutes, elapsed.Seconds);
            if (totalReboot > 0)
                summaryText += " " + Loc.F("{0} file(s) in use by Windows will be deleted at the next restart.", totalReboot);
            if (Shredding)
                summaryText += " " + Loc.F("Secure deletion: {0}.", SecureDelete.Describe(ShredMode));
            var summaryBlock = await AddStatusTextBlock(summaryText);
            await main.Dispatcher.InvokeAsync(() =>
            {
                summaryBlock.FontWeight = FontWeights.Bold;
                summaryBlock.Foreground = totalLocked > 0 || totalDenied > 0 ? ThemeColors.Warning : GREEN;
            });
            await ShowRebootFilesAsync();
            await FinalizeCleaning();
        }
        private async Task SkipExtraDatabaseItemsAsync()
        {
            if (extraTargets.Count == 0)
                return;
            foreach (string path in extraTargets)
                skippedRiskyPaths.Add(path);
            var status = await AddStatusTextBlock(Loc.F("Skipped {0} selected item(s) from additional databases; automatic cleaning only uses the official database.", extraTargets.Count));
            await main.Dispatcher.InvokeAsync(() => status.Foreground = ThemeColors.Warning);
        }

        private async Task ConfirmRiskyItemsAsync()
        {
            if (riskyTargets.Count == 0 || (!automaticRun && !MainWindow.ConfirmRiskyItems()))
                return;

            bool clean = false;
            if (!automaticRun)
            {
                string list = string.Join("\n", riskyTargets.Select(r => $"- {r.Name}: {r.Path}\n   {Loc.T(main.warningPaths[r.Path])}"));
                clean = AppDialog.Show(main,
                    Loc.F("These selected items can harm Windows or delete your own files:\n\n{0}\n\nClean them too? If you choose No, only these items are skipped.", list),
                    Loc.T("Check before cleaning"), MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
            }
            if (clean)
                return;
            foreach (var item in riskyTargets)
                skippedRiskyPaths.Add(item.Path);
            if (automaticRun)
            {
                var status = await AddStatusTextBlock(Loc.F("Skipped {0} selected item(s) that can harm Windows or delete your own files; automatic cleaning never cleans them.", riskyTargets.Count));
                await main.Dispatcher.InvokeAsync(() => status.Foreground = ThemeColors.Warning);
            }
        }

        private async Task HandleOpenBrowsersAsync()
        {
            var decided = new Dictionary<int, OpenBrowsers.Choice>();
            foreach (var browser in browserTargets.GroupBy(p => main.browserPaths[p].Key))
            {
                BrowserTag tag = main.browserPaths[browser.First()];
                List<Process> running = await Task.Run(() => OpenBrowsers.Running(tag));
                if (running.Count == 0)
                    continue;

                OpenBrowsers.Choice choice = running.All(p => decided.ContainsKey(p.Id)) ? decided[running[0].Id] : OpenBrowsers.SavedChoice;
                if (choice == OpenBrowsers.Choice.Ask)
                {
                    var (answer, remember) = AppDialog.AskWithOption(main,
                        tag.IsBrowser
                            ? Loc.F("Close {0} to clean all of its files found by the scan? Its open tabs come back the next time you start it.", tag.Name)
                            : Loc.F("Close {0} to clean all of its files found by the scan?", tag.Name),
                        Loc.F("{0} is open", tag.Name), Loc.T("Yes, close"), Loc.T("No, skip"), Loc.T("Don't ask again"));
                    choice = answer == MessageBoxResult.Yes ? OpenBrowsers.Choice.Close : OpenBrowsers.Choice.Skip;
                    if (remember)
                        OpenBrowsers.SavedChoice = choice;
                }
                foreach (Process p in running)
                    decided[p.Id] = choice;

                if (choice == OpenBrowsers.Choice.Skip)
                {
                    foreach (string path in browser)
                        skippedBrowserPaths.Add(path);
                    continue;
                }

                var status = await AddStatusTextBlock(Loc.F("Closing {0}...", tag.Name));
                bool closed = await Task.Run(() => OpenBrowsers.Close(running, TimeSpan.FromSeconds(10)));
                await main.Dispatcher.InvokeAsync(() =>
                {
                    status.Text = closed ? Loc.F("Closed: {0}", tag.Name) : Loc.F("{0} could not be closed completely; files it still uses are listed at the end.", tag.Name);
                    status.Foreground = closed ? GREEN : ThemeColors.Warning;
                });
            }
        }

        private async Task RemoveMalwareThreatsAsync()
        {
            var malwareScan = main.utilities?.malwarescan;
            if (malwareScan == null || malwareScan.IsScanning)
                return;

            List<MalwareScan.ThreatRemoval>? removals;
            int found = 0;
            try
            {
                removals = await malwareScan.RemoveThreatsAsync(threats =>
                {
                    found = threats.Count;
                    return !automaticRun && ConfirmThreatRemoval(threats);
                });
            }
            catch (Exception ex)
            {
                catchexception(ex.Message + " " + ex.StackTrace);
                return;
            }

            if (removals == null)
            {
                var kept = await AddStatusTextBlock(Loc.F("Threats Kept: {0} malicious file(s) were not deleted. You can remove them in the Malware Scan window.", found));
                await main.Dispatcher.InvokeAsync(() => kept.Foreground = ThemeColors.Warning);
                return;
            }

            foreach (var removal in removals)
            {
                string text;
                Brush brush;
                if (removal.Skipped)
                {
                    text = Loc.F("Threat Skipped: {0}: {1} ({2})", removal.Threat, removal.FilePath, removal.Error);
                    brush = ThemeColors.Warning;
                }
                else if (removal.Outcome == ForceDelete.Outcome.Deleted)
                {
                    text = Loc.F("Threat Removed: {0}: {1}", removal.Threat, removal.FilePath);
                    brush = GREEN;
                }
                else if (removal.Outcome == ForceDelete.Outcome.ScheduledForReboot)
                {
                    text = Loc.F("Threat Locked, will be deleted at restart: {0}: {1}", removal.Threat, removal.FilePath);
                    brush = ThemeColors.Warning;
                    if (rebootSet.TryAdd(removal.FilePath, 0))
                    {
                        rebootFiles.Enqueue(removal.FilePath);
                        Interlocked.Increment(ref totalReboot);
                    }
                }
                else
                {
                    text = Loc.F("Threat Could Not Be Removed: {0}: {1} ({2})", removal.Threat, removal.FilePath, removal.Error);
                    brush = Brushes.Red;
                }

                var block = await AddStatusTextBlock(text);
                await main.Dispatcher.InvokeAsync(() => block.Foreground = brush);
            }
        }

        private bool ConfirmThreatRemoval(IReadOnlyList<CloudScanResult> threats)
        {
            const int shown = 10;
            var lines = threats.Take(shown).Select(t => "- " + t.FilePath + (string.IsNullOrEmpty(t.Threat) ? "" : " (" + t.Threat + ")")
                + (MalwareScan.IsProtectedPath(t.FilePath) ? "  ⚠ " + Loc.T("system or program folder") : ""));
            string list = string.Join("\n", lines);
            if (threats.Count > shown)
                list += "\n" + Loc.F("... and {0} more", threats.Count - shown);
            if (threats.Any(t => MalwareScan.IsProtectedPath(t.FilePath)))
                list += "\n\n" + Loc.T("Some files are in a Windows or program folder. Deleting them can break Windows or an installed program.");
            var answer = AppDialog.Show(main, Loc.F("The malware scan found {0} malicious file(s):\n\n{1}\n\nDelete them now? Programs using these files will be closed.", threats.Count, list),
                Loc.T("Malware Scan"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
            return answer == MessageBoxResult.Yes;
        }

        private void StartInBackground(string fileName, string arguments)
        {
            try
            {
                using Process? process = Process.Start(new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                });
            }
            catch (Exception ex)
            {
                catchexception(ex.Message + " " + ex.StackTrace);
            }
        }

        private async Task RunSystemToolAsync(string title, bool sfcTool, string arguments, string logName)
        {
            dotsText = Loc.F("Cleaning: {0}", title);
            var block = await AddStatusTextBlock(Loc.F("Running: {0} ({1}%)", title, 0));
            int lastPercent = -1;
            Action<int> progress = pct =>
            {
                if (pct == lastPercent) return;
                lastPercent = pct;
                dotsText = Loc.F("Cleaning: {0} ({1}%)", title, pct);
                main.Dispatcher.BeginInvoke(() => block.Text = Loc.F("Running: {0} ({1}%)", title, pct));
            };

            string output = sfcTool
                ? await Scan.RunSfcCommandAsync(arguments, main.dismcancel.Token, main.progressBar1, 120, progress)
                : await Scan.RunDismCommandWithProgressAsync(arguments, main.dismcancel.Token, main.progressBar1, 120, progress);

            try
            {
                File.WriteAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, logName), output);
            }
            catch (Exception) { }

            var result = sfcTool ? SystemChecks.DescribeSfc(output) : SystemChecks.DescribeDism(output);
            await main.Dispatcher.InvokeAsync(() =>
            {
                block.Text = Loc.F("{0}: {1}: {2} (log: {3})", Loc.T(result.Ok ? "Execute Done" : "Finished with problems"), title, result.Text, logName);
                block.Foreground = result.Ok ? BLUE : ThemeColors.Warning;
            });
        }

        private void StartItem(string name)
        {
            currentItem = name;
            itemFiles = 0;
            itemBytes = 0;
            itemLocked = 0;
            itemDenied = 0;
            itemReboot = 0;
        }
        private void FinishItem()
        {
            totalFiles += itemFiles;
            totalBytes += itemBytes;
            totalLocked += itemLocked;
            totalDenied += itemDenied;
            totalReboot += itemReboot;
            if (itemFiles > 0)
                cleanedItems++;
        }
        private string DetailText()
        {
            string text = Loc.F(itemFiles == 1 ? "{0} file, {1}" : "{0} files, {1}", itemFiles, main.formatsize(itemBytes));
            if (itemLocked > 0)
                text += Loc.F(", {0} locked", itemLocked);
            if (itemReboot > 0)
                text += Loc.F(", {0} at next restart", itemReboot);
            if (itemDenied > 0)
                text += Loc.F(", {0} access denied", itemDenied);
            return text;
        }

        private void CountUnremovable(string file)
        {
            if (ForceDelete.ScheduleDeleteAtReboot(file))
            {
                if (rebootSet.TryAdd(file, 0))
                {
                    Interlocked.Increment(ref itemReboot);
                    rebootFiles.Enqueue(file);
                }
            }
            else
            {
                Interlocked.Increment(ref itemDenied);
                int error = ForceDelete.LastScheduleError;
                deniedFiles.Enqueue((file, error));
                catchexception($"Could not delete or schedule for the next restart (Windows error {error}): {file}");
            }
        }

        private int DeleteParallelism => Shredding ? 2 : Math.Clamp(Environment.ProcessorCount, 4, 16);

        private Task DeleteFilesAsync(IReadOnlyList<string> files, string groupName, Action<int>? progress)
        {
            if (files.Count == 0)
                return Task.CompletedTask;
            return Task.Run(() =>
            {
                int done = 0;
                var throttle = Stopwatch.StartNew();
                try
                {
                    Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = DeleteParallelism }, (file, state) =>
                    {
                        if (main.cancelclean == 2)
                        {
                            state.Stop();
                            return;
                        }
                        TryDeleteFile(file, groupName);
                        int n = Interlocked.Increment(ref done);
                        if (progress != null)
                        {
                            bool report;
                            lock (throttle)
                            {
                                report = throttle.ElapsedMilliseconds >= 100 || n == files.Count;
                                if (report) throttle.Restart();
                            }
                            if (report)
                                progress(n);
                        }
                    });
                }
                catch (AggregateException ex)
                {
                    catchexception(ex.Message);
                }
                progress?.Invoke(Volatile.Read(ref done));
            });
        }
        private void TryDeleteFile(string file, string groupName)
        {
            if (!checkedPaths.Contains(file) || main.settings.IsExcluded(file))
                return;

            try
            {
                FileInfo info = new FileInfo(file);
                if (!info.Exists)
                    return;

                long length = info.Length;
                if (info.IsReadOnly)
                {
                    try { info.IsReadOnly = false; } catch { }
                }

                if (ShredMode != SecureDelete.Mode.Off)
                {
                    if (!SecureDelete.Shred(file, ShredMode))
                    {
                        if (File.Exists(file))
                        {
                            if (catchlockedfile(null, file, groupName))
                                Interlocked.Increment(ref itemLocked);
                            else
                                CountUnremovable(file);
                        }
                        return;
                    }
                }
                else
                {
                    try
                    {
                        info.Delete();
                    }
                    catch (Exception ex)
                    {
                        if (!ForceDelete.TryHardDelete(file))
                        {
                            if (File.Exists(file))
                            {
                                if (catchlockedfile(ex, file, groupName))
                                    Interlocked.Increment(ref itemLocked);
                                else
                                    CountUnremovable(file);
                            }
                            catchexception(ex.Message + " " + ex.StackTrace);
                            return;
                        }
                    }
                }

                int deleted = Interlocked.Increment(ref itemFiles);
                Interlocked.Add(ref itemBytes, length);
                if (deleted % 50 == 0)
                    dotsText = Loc.F(Shredding ? "Shredding & Cleaning: {0} ({1} files)" : "Cleaning: {0} ({1} files)", currentItem, deleted);
            }
            catch (Exception Ex)
            {
                if (File.Exists(file))
                {
                    if (catchlockedfile(Ex, file, groupName))
                        Interlocked.Increment(ref itemLocked);
                    else
                        CountUnremovable(file);
                }
                catchexception(Ex.Message + " " + Ex.StackTrace);
            }
        }
        public bool catchlockedfile(Exception? ex, string file, string groupName)
        {
            bool isLocked = WhoUsesFile.HasClosableLocker(file);

            if (isLocked)
            {
                lock (lockedFiles)
                {
                    if (lockedFiles.Add(file))
                        main.paths.Add(file + "=" + "0" + "=" + Loc.T("Unknown Process") + "=" + groupName);
                }
            }
            return isLocked;
        }
        public void catchexception(string message)
        {
            if (exceptionLog.Count < MaxLoggedExceptions)
                exceptionLog.Enqueue(message);
        }

        private async Task ShowRebootFilesAsync()
        {
            await ShowFileListAsync(
                rebootFiles.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).Select(f => (f, string.Empty)).ToList(),
                count => Loc.F("Will be deleted at the next restart ({0}): these files are in use by Windows or a Windows service, so Windows removes them when the PC restarts.", count));
            await ShowFileListAsync(
                deniedFiles.GroupBy(d => d.File, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).OrderBy(d => d.File, StringComparer.OrdinalIgnoreCase)
                    .Select(d => (d.File, d.Error != 0 ? Loc.F("Windows error {0}", d.Error) : string.Empty)).ToList(),
                count => Loc.F("Could not be deleted ({0}): access to these files is denied and Windows did not accept them for deletion at the next restart.", count));
        }

        private static Border NoticeBanner(string text, string glyph, double size)
        {
            var icon = new TextBlock
            {
                Text = glyph,
                FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = size + 2,
                Margin = new Thickness(0, 1, 12, 0),
                VerticalAlignment = VerticalAlignment.Top
            };
            icon.SetResourceReference(TextBlock.ForegroundProperty, "ResultProblemText");
            var label = new TextBlock { FontSize = size, TextWrapping = TextWrapping.Wrap };
            int split = -1;
            foreach (string mark in new[] { ". ", "! ", "。", "！" })
            {
                int at = text.IndexOf(mark, StringComparison.Ordinal);
                if (at > 0 && at < text.Length - mark.Length && (split < 0 || at < split))
                    split = at + mark.Length;
            }
            var title = new System.Windows.Documents.Run(split > 0 ? text.Substring(0, split).TrimEnd() : text) { FontWeight = FontWeights.SemiBold };
            title.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "ResultProblemText");
            label.Inlines.Add(title);
            if (split > 0)
            {
                label.Inlines.Add(new System.Windows.Documents.LineBreak());
                var body = new System.Windows.Documents.Run(text.Substring(split).Trim()) { FontSize = Math.Max(12.5, size - 1.5) };
                body.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "TextPrimary");
                label.Inlines.Add(body);
            }
            var line = new DockPanel();
            DockPanel.SetDock(icon, Dock.Left);
            line.Children.Add(icon);
            line.Children.Add(label);
            return Appear(new Border
            {
                Child = line,
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x66, 0xFF, 0x8A, 0x3D)),
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x1F, 0xFF, 0x8A, 0x3D)),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(5, 14, 5, 6)
            });
        }

        private static Border RestartBanner(Multron_Win_Cleaner.MainWindow window, Border banner)
        {
            if (banner.Child is DockPanel line)
            {
                var button = new Button { Content = Loc.T("Restart now"), Height = 32, Padding = new Thickness(14, 0, 14, 0), Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                button.SetResourceReference(FrameworkElement.StyleProperty, "UtilityPrimaryButtonStyle");
                button.Click += (s, e) => window.AskRestart();
                DockPanel.SetDock(button, Dock.Right);
                line.Children.Insert(1, button);
            }
            return banner;
        }

        private static long lastAppear;

        private static T Appear<T>(T element) where T : FrameworkElement
        {
            long now = Environment.TickCount64;
            bool busy = now - lastAppear < 160 || !Motion.Enabled;
            lastAppear = now;
            if (busy)
                return element;
            void Start(object? sender, RoutedEventArgs e)
            {
                element.Loaded -= Start;
                var ease = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
                var slide = new TranslateTransform(14, 0);
                element.RenderTransform = slide;
                var move = new System.Windows.Media.Animation.DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease };
                move.Completed += (s, a) =>
                {
                    if (ReferenceEquals(element.RenderTransform, slide))
                        element.ClearValue(UIElement.RenderTransformProperty);
                };
                slide.BeginAnimation(TranslateTransform.XProperty, move);
                element.BeginAnimation(UIElement.OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280))
                {
                    EasingFunction = ease,
                    FillBehavior = System.Windows.Media.Animation.FillBehavior.Stop
                });
            }
            element.Loaded += Start;
            return element;
        }

        private static Border FileRow(string file, string note)
        {
            var icon = new TextBlock
            {
                Text = "\uE8A5",
                FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 13,
                Margin = new Thickness(0, 2, 10, 0),
                VerticalAlignment = VerticalAlignment.Top
            };
            icon.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
            string name = System.IO.Path.GetFileName(file);
            var title = new TextBlock { Text = name.Length > 0 ? name : file, FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            title.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
            var folder = new TextBlock { Text = System.IO.Path.GetDirectoryName(file) ?? string.Empty, FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0) };
            folder.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
            var text = new StackPanel();
            text.Children.Add(title);
            text.Children.Add(folder);
            var line = new DockPanel();
            DockPanel.SetDock(icon, Dock.Left);
            line.Children.Add(icon);
            if (note.Length > 0)
            {
                var badge = new TextBlock { Text = note, FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                badge.SetResourceReference(TextBlock.ForegroundProperty, "ResultProblemText");
                DockPanel.SetDock(badge, Dock.Right);
                line.Children.Add(badge);
            }
            line.Children.Add(text);
            return Appear(new Border
            {
                Child = line,
                ToolTip = file,
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(5, 2, 5, 2),
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x14, 0x80, 0x80, 0x80))
            });
        }

        private async Task ShowFileListAsync(List<(string File, string Note)> files, Func<int, string> headerText)
        {
            if (files.Count == 0)
                return;
            await main.Dispatcher.InvokeAsync(() =>
            {
                main.wrapPanelDirectories.Children.Add(NoticeBanner(headerText(files.Count), "\uE7BA", 14));
                foreach (var (file, note) in files.Take(MaxListedRebootFiles))
                    main.wrapPanelDirectories.Children.Add(FileRow(file, note));
                if (files.Count > MaxListedRebootFiles)
                {
                    var more = new TextBlock { Text = Loc.F("...and {0} more.", files.Count - MaxListedRebootFiles), FontSize = 12.5, Margin = new Thickness(17, 4, 5, 2) };
                    more.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
                    main.wrapPanelDirectories.Children.Add(more);
                }
                main.ScrollViewerDirectories.ScrollToBottom();
            });
        }

        private void WriteExceptionLog()
        {
            if (exceptionLog.IsEmpty)
                return;
            try
            {
                File.WriteAllLines(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "exceptions.txt"), exceptionLog);
            }
            catch { }
            exceptionLog.Clear();
        }
        public class LockedFileGroupViewModel : INotifyPropertyChanged
        {
            public string GroupName { get; set; } = "";

            public string DisplayName { get; set; } = "";

            public string FilePath { get; set; } = "";



            public ObservableCollection<LockedProcessViewModel> Processes { get; set; } = new ObservableCollection<LockedProcessViewModel>();

            public event PropertyChangedEventHandler? PropertyChanged;
            protected void OnPropertyChanged([CallerMemberName] string? name = null)
                => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        public class LockedProcessViewModel : INotifyPropertyChanged
        {
            public string DisplayName { get; set; } = "";
            public string GroupName { get; set; } = "";
            public string FilePath { get; set; } = "";
            public string FilePathWithoutSize { get; set; } = "";
            public string Id { get; set; } = "";
            private bool _isChecked;
            public bool IsChecked
            {
                get => _isChecked;
                set
                {
                    if (_isChecked != value)
                    {
                        _isChecked = value;
                        OnPropertyChanged();
                        OnCheckedChanged?.Invoke(this, value);
                    }
                }
            }
            public Action<LockedProcessViewModel, bool>? OnCheckedChanged { get; set; }
            public event PropertyChangedEventHandler? PropertyChanged;
            protected void OnPropertyChanged([CallerMemberName] string? name = null)
                => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        }

        public class LockedFileGroup
        {
            public string FilePath { get; set; } = "";

        }




        public void DeleteEmptyDirectories(string parentPath)
        {
            try
            {
                foreach (string subDir in Directory.EnumerateDirectories(parentPath, "*", new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }).ToList())
                {
                    if (main.cancelclean == 2) return;
                    DeleteEmptyDirectories(subDir);

                    if (Directory.Exists(subDir) && !Directory.EnumerateFileSystemEntries(subDir).Any())
                    {
                        try
                        {
                            Directory.Delete(subDir);
                        }
                        catch (Exception ex)
                        {
                            catchexception(ex.Message + " " + ex.StackTrace);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                catchexception(ex.Message + " " + ex.StackTrace);
            }
      
          
        }



        public class LockedFileViewModel
        {
            public string FilePath { get; set; } = "";
            public string LockedByProcess { get; set; } = "";
        }
        private async Task FinalizeCleaning()
        {
            bool automatic = main.autoclean == 1 || main.startupclean == 1;
            main.AnimateIcon(false);
            string resultMessage = "";
            await cts.CancelAsync();
            WriteExceptionLog();
            long freeAfter = new DriveInfo("C:\\").AvailableFreeSpace;
            long freeDelta = freeAfter - totalsize;
            long freedSpace = Math.Max(0, freeDelta);
            string deletedLine = Loc.F("Files deleted: {0} in {1} files.", main.formatsize(totalBytes), totalFiles);
            string diskLine = Loc.F("Free space on C: before {0}, after {1} ({2})",
                main.formatsize(totalsize), main.formatsize(freeAfter),
                (freeDelta >= 0 ? "+" : "-") + main.formatsize(Math.Abs(freeDelta)));
            var diskBlock = await AddStatusTextBlock(diskLine);
            await main.Dispatcher.InvokeAsync(() => diskBlock.FontWeight = FontWeights.Bold);
            int restartFiles = rebootSet.Count;
            if (restartFiles > 0)
            {
                await main.Dispatcher.InvokeAsync(() =>
                {
                    main.wrapPanelDirectories.Children.Add(RestartBanner(main, NoticeBanner(Loc.T("Cleaning done! Restart required.") + " " +
                                   Loc.F("{0} file(s) are in use by Windows and will be deleted when the PC restarts. Restart to finish cleaning.", restartFiles), "\uE777", 15)));
                    main.ScrollViewerDirectories.ScrollToBottom();
                    main.ShowRestartRequired(restartFiles);
                });
            }
            if (main.paths.Count > 0)
            {
                await main.Dispatcher.InvokeAsync(() =>
                {
                    main.ButtonLockedFiles.Visibility = Visibility.Visible;
                });

                resultMessage = main.autoclean == 1
                    ? Loc.F("Auto Clean done! {0} cleaned. {1} Locked Files Found! {2}", main.formatsize(freedSpace), DateTime.Now, main.paths.Count) :   main.startupclean == 1   ? Loc.F("Startup Clean done! {0} cleaned. {1} Locked Files Found! {2}", main.formatsize(freedSpace), DateTime.Now, main.paths.Count)  : Loc.F("Cleaning done! {0} cleaned. {1} Locked Files Found! {2}", main.formatsize(freedSpace), DateTime.Now, main.paths.Count);
            }
            else
            {
                resultMessage = main.autoclean == 1   ? Loc.F("Auto Clean done! {0} cleaned. {1}", main.formatsize(freedSpace), DateTime.Now) : main.startupclean == 1   ? Loc.F("Startup Clean done! {0} cleaned. {1}", main.formatsize(freedSpace), DateTime.Now)    : Loc.F("Cleaning done! {0} cleaned. {1}", main.formatsize(freedSpace), DateTime.Now);
            }
        
            await main.Dispatcher.InvokeAsync(() =>
            {
                main.AppendCleanupLog(resultMessage + " " + deletedLine + " " + diskLine, false);
                string resultTitle = main.autoclean == 1 ? Loc.T("Auto Clean done") : main.startupclean == 1 ? Loc.T("Startup Clean done") : Loc.T("Cleaning done");
                main.ShowCleanResult(resultTitle, freedSpace, totalBytes, totalFiles, main.paths.Count, restartFiles);
                main.reset = 1;
               
                
                main.autoclean = 0;
                main.onclean = 0;
                main.buttonStartScan.Content = Loc.T("Scan");
                main.buttonStartScan.IsEnabled = false;
                main.buttonReset.Visibility = Visibility.Visible;

                if (main.settings.chkEnableNotifyClean.IsChecked == true && (main.Visibility != Visibility.Visible || main.WindowState == WindowState.Minimized))
                {
                    Notify notify = new Notify(Loc.T("Clean Information"), Loc.T("Your system cleaned!") + "\r\n", $"{main.formatsize(freedSpace)}");
                    notify.Styled(Notify.Kind.Success).Show();
                }
                var action = automatic ? Loc.En((main.settings.cmbPostCleanupAction.SelectedItem as ComboBoxItem)?.Content) : null;

                switch (action)
                {
                    case "Restart": SystemActions.Restart(); break;
                    case "LogOff": SystemActions.LogOff(); break;
                    case "Shutdown": SystemActions.Shutdown(); break;
                }
            });
        }



        internal static Border StatusRow(TextBlock text)
        {
            var icon = new TextBlock
            {
                FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 13,
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new ScaleTransform()
            };
            var view = new TextBlock
            {
                FontSize = text.FontSize,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            text.Visibility = Visibility.Collapsed;
            var chipText = new TextBlock { FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var chip = new Border
            {
                Child = chipText,
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 2, 8, 3),
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
                MaxWidth = 190
            };
            var chipSlot = new Grid { MinWidth = 150, VerticalAlignment = VerticalAlignment.Center };
            chipSlot.Children.Add(chip);
            var line = new Grid();
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(chipSlot, 1);
            Grid.SetColumn(view, 2);
            line.Children.Add(icon);
            line.Children.Add(text);
            line.Children.Add(chipSlot);
            line.Children.Add(view);
            var row = new Border
            {
                Child = line,
                Padding = new Thickness(12, 7, 12, 7),
                Margin = new Thickness(5, 2, 5, 2),
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x14, 0x80, 0x80, 0x80)),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            void Sync()
            {
                var color = (text.Foreground as SolidColorBrush)?.Color ?? Colors.Gray;
                bool green = color.G > color.R + 40 && color.G > color.B;
                bool warm = color.R > color.B + 60;
                bool neutral = Math.Max(color.R, Math.Max(color.G, color.B)) - Math.Min(color.R, Math.Min(color.G, color.B)) < 24;
                string glyph = green ? "\uE73E" : warm ? "\uE7BA" : neutral ? "\uE738" : "\uE895";
                if (icon.Text.Length > 0 && icon.Text != glyph && icon.RenderTransform is ScaleTransform pop)
                {
                    var bounce = new System.Windows.Media.Animation.DoubleAnimation(0.5, 1, TimeSpan.FromMilliseconds(360))
                    {
                        EasingFunction = new System.Windows.Media.Animation.BackEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut, Amplitude = 0.6 }
                    };
                    pop.BeginAnimation(ScaleTransform.ScaleXProperty, bounce);
                    pop.BeginAnimation(ScaleTransform.ScaleYProperty, bounce);
                }
                icon.Text = glyph;
                icon.Foreground = text.Foreground;
                text.ToolTip = text.Text;
                Render();
            }
            void Render()
            {
                view.Inlines.Clear();
                view.ToolTip = text.Text;
                view.FontWeight = text.FontWeight;
                var parts = SplitStatus(text.Text);
                bool shortLabel = parts.Label.Length <= 22 && parts.Label.Count(ch => ch == ' ') <= 1;
                if (!shortLabel)
                    parts = (text.Text, "", "", "");
                bool split = parts.Name.Length > 0 || parts.Path.Length > 0 || parts.Detail.Length > 0;
                chipSlot.Visibility = split ? Visibility.Visible : Visibility.Collapsed;
                view.TextWrapping = split ? TextWrapping.NoWrap : TextWrapping.Wrap;
                view.TextTrimming = split ? TextTrimming.CharacterEllipsis : TextTrimming.None;
                Grid.SetColumn(view, split ? 2 : 1);
                Grid.SetColumnSpan(view, split ? 1 : 2);
                if (!split)
                {
                    string trimmed = text.Text.TrimEnd();
                    bool working = trimmed.EndsWith("...") || trimmed.EndsWith("…");
                    if (icon.Text == "" && !working)
                        icon.Text = "";
                    view.Inlines.Add(new System.Windows.Documents.Run(parts.Label) { Foreground = text.Foreground, FontWeight = working ? FontWeights.SemiBold : FontWeights.Normal });
                    return;
                }
                var color = (text.Foreground as SolidColorBrush)?.Color ?? Colors.Gray;
                chipText.Text = parts.Label;
                chipText.Foreground = text.Foreground;
                chip.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x24, color.R, color.G, color.B));
                chip.ToolTip = parts.Label;
                if (parts.Name.Length > 0)
                {
                    string shown = !parts.Name.Contains(' ') && parts.Name.Length > 12 && parts.Name.Skip(1).Any(char.IsUpper) ? Load.Prettify(parts.Name) : parts.Name;
                    var name = new System.Windows.Documents.Run(shown) { FontWeight = FontWeights.SemiBold };
                    name.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "TextPrimary");
                    view.Inlines.Add(name);
                }
                if (parts.Detail.Length > 0)
                {
                    var detail = new System.Windows.Documents.Run((view.Inlines.Count > 0 ? "   " : "") + parts.Detail.Trim('(', ')', '（', '）'));
                    detail.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "TextSecondary");
                    view.Inlines.Add(detail);
                }
                if (parts.Path.Length > 0)
                {
                    var path = new System.Windows.Documents.Run((view.Inlines.Count > 0 ? "   " : "") + parts.Path) { FontSize = Math.Max(11.5, text.FontSize - 1.5) };
                    path.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "TextSecondary");
                    view.Inlines.Add(path);
                }
            }
            System.ComponentModel.DependencyPropertyDescriptor.FromProperty(TextBlock.ForegroundProperty, typeof(TextBlock)).AddValueChanged(text, (s, e) => Sync());
            System.ComponentModel.DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock)).AddValueChanged(text, (s, e) => { text.ToolTip = text.Text; Render(); });
            System.ComponentModel.DependencyPropertyDescriptor.FromProperty(TextBlock.FontWeightProperty, typeof(TextBlock)).AddValueChanged(text, (s, e) => Render());
            Multron_Win_Cleaner.MainWindow.KeepAccentReadable(text);
            Sync();
            return Appear(row);
        }

        private static readonly System.Text.RegularExpressions.Regex StatusLabel =
            new System.Text.RegularExpressions.Regex(@"^(?<label>[^:：\\]{1,48}?)\s?[:：]\s+(?<rest>.+)$", System.Text.RegularExpressions.RegexOptions.Singleline);

        private static readonly System.Text.RegularExpressions.Regex StatusPath =
            new System.Text.RegularExpressions.Regex(@"(?<path>(?:[A-Za-z]:\\|\\\\|%[A-Za-z]+%)[^()]*?)(?<detail>\s*[(（][^()（）]*[)）])?\s*$");

        private static (string Label, string Name, string Path, string Detail) SplitStatus(string text)
        {
            var head = StatusLabel.Match(text);
            if (!head.Success || head.Groups["label"].Value.Contains('(') || char.IsDigit(head.Groups["label"].Value.TrimStart().FirstOrDefault()))
                return (text, "", "", "");
            string label = head.Groups["label"].Value.Trim();
            string rest = head.Groups["rest"].Value.Trim();
            string extra = "";
            int comma = label.IndexOfAny(new[] { ',', '，', '、' });
            if (comma > 0 && comma < label.Length - 1)
            {
                extra = label.Substring(comma + 1).Trim();
                label = label.Substring(0, comma).Trim();
            }
            var tail = StatusPath.Match(rest);
            if (!tail.Success)
            {
                var sized = System.Text.RegularExpressions.Regex.Match(rest, @"^(?<name>.+?)\s+(?<size>\d[\d.,]*\s?(?:KB|MB|GB|TB|" + System.Text.RegularExpressions.Regex.Escape(Loc.T("Byte")) + "))$");
                if (sized.Success)
                    return (label, sized.Groups["name"].Value.Trim(), "", Join(extra, sized.Groups["size"].Value.Trim()));
                var detailOnly = System.Text.RegularExpressions.Regex.Match(rest, @"^(?<name>.*?)(?<detail>\s*[(（][^()（）]*[)）])$");
                if (detailOnly.Success && detailOnly.Groups["name"].Value.Trim().Length > 0)
                    return (label, detailOnly.Groups["name"].Value.Trim(), "", Join(extra, detailOnly.Groups["detail"].Value.Trim()));
                return (label, rest, "", extra);
            }
            string name = rest.Substring(0, tail.Index).Trim().TrimEnd(':', '：').Trim();
            return (label, name, tail.Groups["path"].Value.Trim(), Join(extra, tail.Groups["detail"].Value.Trim()));

            static string Join(string first, string second)
            {
                second = second.Trim('(', ')', '（', '）', ' ');
                return first.Length == 0 ? second : second.Length == 0 ? first : first + ", " + second;
            }
        }

        private async Task<TextBlock> AddStatusTextBlock(string text)
        {
            TextBlock tb = null!;
            await main.Dispatcher.InvokeAsync(() =>
            {
             
                tb = new TextBlock
                {
                    Text = text,
                    Foreground = BLUE,
                    FontSize = 13.5,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = text
                };
                main.wrapPanelDirectories.Children.Add(StatusRow(tb));
                main.ScrollViewerDirectories.ScrollToBottom();
            });
            return tb;
        }

        private async Task UpdateProgress(int current, int total)
        {
            double percent = (double)current / total * 100;
            await main.Dispatcher.InvokeAsync(() =>
            {
                main.progressBar1.Value = percent;

            });
        }




        private async Task UpdateStatusColor(System.Windows.Media.Brush brush)
        {
            await main.Dispatcher.InvokeAsync(() =>
            {
                main.label1_Copy.Foreground = brush;
            });
        }

        private string GetToken(string source, int index)
        {
            var parts = source.Split('=');
            return (index >= 0 && index < parts.Length) ? parts[index] : "";
        }
    }
}
