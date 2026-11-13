using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Ookii.Dialogs.Wpf;

namespace MultronWinCleaner
{
    public partial class LargeFileFinder : Window
    {
        private volatile bool isScanning = false;
        private byte cancel = 0;
        private bool sortDescending = true;
        private double previousWidth, previousHeight, previousLeft, previousTop;
        private bool isMaximized = false;
         
        private readonly List<string> excludedPaths = new();
         
        private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".mpg", ".mpeg", ".3gp", ".ts",
            ".m2ts", ".mts", ".vob", ".ogv", ".rmvb", ".divx", ".f4v", ".asf"
        };

        private static readonly HashSet<string> MusicExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp3", ".wav", ".flac", ".aac", ".ogg", ".wma", ".m4a", ".alac", ".aiff", ".opus", ".ape", ".mid",
            ".midi", ".amr", ".m4b", ".dsf"
        };

        private static readonly HashSet<string> PhotoExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".svg", ".tif", ".tiff", ".raw", ".ico", ".heic",
            ".heif", ".avif", ".jfif", ".cr2", ".cr3", ".nef", ".arw", ".dng", ".orf", ".rw2", ".psd", ".xcf"
        };

        private static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".zip", ".rar", ".7z", ".tar", ".gz", ".cab", ".bz2", ".xz", ".tgz", ".zst", ".lz", ".lzma", ".z"
        };

        private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".rtf", ".csv", ".odt", ".ods", ".odp",
            ".epub", ".mobi", ".md", ".xps", ".one", ".pst", ".ost"
        };

        private static readonly HashSet<string> ProgramExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".msi", ".msix", ".msixbundle", ".appx", ".appxbundle", ".msu", ".apk", ".xapk", ".dmg", ".deb", ".rpm", ".jar"
        };

        private static readonly HashSet<string> DiskImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".iso", ".img", ".vhd", ".vhdx", ".vmdk", ".vdi", ".qcow2", ".ova", ".wim", ".esd", ".swm", ".avhdx"
        };

        private static readonly HashSet<string> BackupExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".bak", ".backup", ".old", ".tmp", ".dmp", ".mdmp", ".log", ".etl", ".db", ".sqlite", ".mdf", ".ldf", ".sql"
        };

        private static readonly HashSet<string> KnownExtensions = new(
            VideoExtensions.Concat(MusicExtensions).Concat(PhotoExtensions).Concat(ArchiveExtensions)
                .Concat(DocumentExtensions).Concat(ProgramExtensions).Concat(DiskImageExtensions).Concat(BackupExtensions),
            StringComparer.OrdinalIgnoreCase);

        private readonly List<CheckBox> extensionBoxes = new();

        private void BuildExtensionChoices()
        {
            var groups = new (CheckBox Category, IEnumerable<string> Extensions)[]
            {
                (ChkVideos, VideoExtensions), (ChkMusic, MusicExtensions), (ChkPhotos, PhotoExtensions), (ChkArchives, ArchiveExtensions),
                (ChkDocuments, DocumentExtensions), (ChkPrograms, ProgramExtensions), (ChkDiskImages, DiskImageExtensions), (ChkBackups, BackupExtensions)
            };
            foreach (var (category, extensions) in groups)
            {
                var boxes = new List<CheckBox>();
                foreach (string extension in extensions)
                {
                    var box = new CheckBox { Content = extension, IsChecked = category.IsChecked, Margin = new Thickness(0, 0, 10, 4), MinWidth = 72 };
                    box.SetResourceReference(ForegroundProperty, "TextPrimary");
                    box.Click += (s, e) => category.IsChecked = boxes.Any(b => b.IsChecked == true);
                    boxes.Add(box);
                    extensionBoxes.Add(box);
                    ExtensionChoices.Children.Add(box);
                }
                category.Click += (s, e) =>
                {
                    foreach (var box in boxes)
                        box.IsChecked = category.IsChecked == true;
                };
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplySearch();

        private void ApplySearch()
        {
            string query = SearchBox.Text.Trim();
            var source = LargeFilesDataGrid.ItemsSource;
            if (source == null)
            {
                SearchCountText.Text = string.Empty;
                EmptyHint.Text = Loc.T("Results will appear here after a scan.");
                return;
            }

            var view = CollectionViewSource.GetDefaultView(source);
            view.Filter = query.Length == 0 ? null : item =>
                item is LargeFileInfo file &&
                (file.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || file.Path.Contains(query, StringComparison.OrdinalIgnoreCase));

            int shown = view.Cast<object>().Count();
            SearchCountText.Text = query.Length == 0 ? string.Empty : Loc.F("{0} matches found", shown);
            EmptyHint.Text = Loc.T(query.Length > 0 && shown == 0 ? "No matching files." : "Results will appear here after a scan.");
        }

        public LargeFileFinder()
        {
            InitializeComponent();
            BuildExtensionChoices();
            DependencyPropertyDescriptor.FromProperty(ItemsControl.ItemsSourceProperty, typeof(DataGrid))
                .AddValueChanged(LargeFilesDataGrid, (s, e) => ApplySearch());

            FolderListBox.Items.Clear();
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            FolderListBox.Items.Add(userProfile);

            LargeFilesDataGrid.ItemsSource = null;

            SizeThresholdTextBox.Text = "1";
            SetDefaultUnitToGB();
        }

        public void ShowReport(List<LargeFileInfo> files)
        {
            foreach (DataGridColumn column in LargeFilesDataGrid.Columns)
                column.SortDirection = null;
            SizeColumn.Header = Loc.T("Size");
            SizeColumn.SortDirection = System.ComponentModel.ListSortDirection.Descending;
            sortDescending = false;
            LargeFilesDataGrid.ItemsSource = files;
            ScanResultLabel.Text = FoundText(files.Count, files.Sum(f => f.FileSizeBytes));
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
            Activate();
        }

        private void LargeFilesDataGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            DependencyObject? node = e.OriginalSource as DependencyObject;
            while (node != null && node is not DataGridRow)
                node = node is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
            if (node is DataGridRow row)
                LargeFilesDataGrid.SelectedItem = row.Item;
        }

        private void SetDefaultUnitToGB()
        {
            foreach (ComboBoxItem item in SizeUnitComboBox.Items)
            {
                if (item.Content?.ToString() == "GB")
                {
                    SizeUnitComboBox.SelectedItem = item;
                    break;
                }
            }
        }

        private void TopBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                this.DragMove();
            }
        }

        private ToolSettingsWindow? settingsWindow;

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            AdvancedPanel.Visibility = Visibility.Visible;
            settingsWindow ??= new ToolSettingsWindow(this, Loc.T("Large File Finder Settings"), AdvancedPanel, 960, 720, windowButtons: true);
            settingsWindow.ShowOrActivate();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
               Visibility = Visibility.Collapsed;
        }

        public class LargeFileInfo
        {
            public string Name { get; set; } = "";
            public string Path { get; set; } = "";
            public string FormattedSize { get; set; } = "";
            public long FileSizeBytes { get; set; }
            public DateTime LastModified { get; set; }
            public DateTime LastAccessed { get; set; }

            public string FormattedLastModified => LastModified.ToString("g", Loc.Culture);
            public string FormattedLastAccessed => LastAccessed.ToString("g", Loc.Culture);

            public string TimeAgo
            {
                get
                {
                    TimeSpan span = DateTime.Now - LastAccessed;
                    if (span.TotalDays >= 365)
                        return Ago((int)(span.TotalDays / 365), "1 year ago", "{0} years ago");
                    if (span.TotalDays >= 30)
                        return Ago((int)(span.TotalDays / 30), "1 month ago", "{0} months ago");
                    if (span.TotalDays >= 1)
                        return Ago((int)span.TotalDays, "1 day ago", "{0} days ago");
                    if (span.TotalHours >= 1)
                        return Ago((int)span.TotalHours, "1 hour ago", "{0} hours ago");
                    return Loc.T("Recently");
                }
            }

            private static string Ago(int value, string one, string many) => value == 1 ? Loc.T(one) : Loc.F(many, value);
        } 
        private static string FormatBytes(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double value = bytes;
            int unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }
            return value.ToString(unit == 0 ? "0" : "0.##", Loc.Culture) + " " + units[unit];
        }

        private static string FoundText(int count, long bytes) =>
            count == 0 ? Loc.F("{0} large files found.", count) : Loc.F("{0} large files found ({1}).", count, FormatBytes(bytes));

        private void MoveToRecycleBin_Click(object sender, RoutedEventArgs e)
        {
            var selected = LargeFilesDataGrid.SelectedItems.OfType<LargeFileInfo>().ToList();
            if (selected.Count == 0)
                return;
            long bytes = selected.Sum(f => f.FileSizeBytes);
            if (AppDialog.Show(Loc.F("Move {0} file(s) ({1}) to the Recycle Bin?", selected.Count, FormatBytes(bytes)), Loc.T("Move to Recycle Bin"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            var moved = new List<LargeFileInfo>();
            int failed = 0;
            foreach (var file in selected)
            {
                try
                {
                    if (File.Exists(file.Path))
                        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(file.Path,
                            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                    moved.Add(file);
                }
                catch
                {
                    failed++;
                }
            }
            var remaining = (LargeFilesDataGrid.ItemsSource as IEnumerable<LargeFileInfo>)?.Except(moved).ToList() ?? new List<LargeFileInfo>();
            LargeFilesDataGrid.ItemsSource = remaining;
            string text = FoundText(remaining.Count, remaining.Sum(f => f.FileSizeBytes)) + " " + Loc.F("{0} file(s) moved to the Recycle Bin.", moved.Count);
            if (failed > 0)
                text += " " + Loc.F("{0} file(s) could not be moved.", failed);
            ScanResultLabel.Text = text;
        }

        private bool IsPathExcluded(string targetPath)
        {
            if (string.IsNullOrWhiteSpace(targetPath) || excludedPaths.Count == 0)
                return false;

            foreach (var excluded in excludedPaths)
            {
            
                if (targetPath.Equals(excluded, StringComparison.OrdinalIgnoreCase) ||
                    targetPath.StartsWith(excluded.EndsWith(Path.DirectorySeparatorChar.ToString()) ? excluded : excluded + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private async void StartScan_Click(object sender, RoutedEventArgs e)
        {
            if (isScanning)
            {
                cancel = 1;
                ScanResultLabel.Text = Loc.T("Cancelling scan...");
                StartScan.IsEnabled = false;
                return;
            }

            if (!double.TryParse(SizeThresholdTextBox.Text, out double minSize))
            {
                ScanResultLabel.Text = Loc.T("Please enter a valid size threshold.");
                return;
            }

            bool applyDateFilter = EnableDateFilterCheckBox.IsChecked == true;
            int minDaysOld = 0;
            if (applyDateFilter && !int.TryParse(DaysOldTextBox.Text, out minDaysOld))
            {
                ScanResultLabel.Text = Loc.T("Please enter a valid number of days for the date filter.");
                return;
            }
            string selectedDateType = Loc.En((DateTypeComboBox.SelectedItem as ComboBoxItem)?.Content ?? "Last Accessed");
            bool allowOthers = ChkOthers.IsChecked == true;
            var allowedExtensions = new HashSet<string>(extensionBoxes.Where(box => box.IsChecked == true).Select(box => (string)box.Content), StringComparer.OrdinalIgnoreCase);

            if (allowedExtensions.Count == 0 && !allowOthers)
            {
                ScanResultLabel.Text = Loc.T("Please select at least one file type category to scan.");
                return;
            }

            isScanning = true;
            cancel = 0;
            UpdateScanButtonUI(true);

            ScanResultLabel.Text = Loc.T("Calculating...");
            ScanProgressBar.Visibility = Visibility.Visible;
            ScanProgressBar.Value = 0;
            LargeFilesDataGrid.ItemsSource = null;
            (Application.Current?.MainWindow as Multron_Win_Cleaner.MainWindow)?.MirrorToolStatus(Loc.T("Large File Finder"), ScanResultLabel, ScanProgressBar);

            string selectedUnit = (SizeUnitComboBox.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "GB";

            if (SizeColumn != null)
            {
                SizeColumn.Header = Loc.F("Size ({0})", selectedUnit);
            }

            long multiplier = selectedUnit switch
            {
                "KB" => 1024L,
                "MB" => 1024L * 1024L,
                "GB" => 1024L * 1024L * 1024L,
                _ => 1024L * 1024L * 1024L
            };

            long minBytes = (long)(minSize * multiplier);

            try
            {
                var foundFiles = new List<LargeFileInfo>();
                var sw = Stopwatch.StartNew();

                var allFiles = await Task.Run(() =>
                {
                    var files = new List<string>();
                    foreach (string folder in FolderListBox.Items)
                    {
                        if (cancel == 1) break;

                        if (Directory.Exists(folder) && !IsPathExcluded(folder))
                        {
                            try
                            {
                                files.AddRange(SafeFileEnumerator(folder));
                            }
                            catch { }
                        }
                    }
                    return files;
                });

                int totalFiles = allFiles.Count;
                if (totalFiles == 0)
                {
                    ScanResultLabel.Text = Loc.T("No files found in the selected folders.");
                    return;
                }

                int processed = 0;

                await Task.Run(() =>
                {
                    foreach (var file in allFiles)
                    {
                        if (cancel == 1) break;
                         
                        if (IsPathExcluded(file))
                        {
                            processed++;
                            continue;
                        }

                        try
                        {
                            var info = new FileInfo(file);

                            string ext = info.Extension;
                            bool matchesCategory = KnownExtensions.Contains(ext) ? allowedExtensions.Contains(ext) : allowOthers;

                            if (!matchesCategory)
                            {
                                processed++;
                                continue;
                            }

                            if (info.Length >= minBytes)
                            {
                                if (applyDateFilter)
                                {
                                    DateTime checkDate = (selectedDateType == "Last Modified")
                                        ? info.LastWriteTime
                                        : info.LastAccessTime;

                                    double fileAgeInDays = (DateTime.Now - checkDate).TotalDays;

                                    if (fileAgeInDays < minDaysOld)
                                    {
                                        processed++;
                                        continue;
                                    }
                                }

                                double displaySize = selectedUnit switch
                                {
                                    "KB" => info.Length / 1024d,
                                    "MB" => info.Length / (1024d * 1024d),
                                    "GB" => info.Length / (1024d * 1024d * 1024d),
                                    _ => info.Length / (1024d * 1024d * 1024d)
                                };

                                lock (foundFiles)
                                {
                                    foundFiles.Add(new LargeFileInfo
                                    {
                                        Name = info.Name,
                                        Path = info.FullName,
                                        FormattedSize = displaySize.ToString("F2", Loc.Culture) + " " + selectedUnit,
                                        FileSizeBytes = info.Length,
                                        LastModified = info.LastWriteTime,
                                        LastAccessed = info.LastAccessTime
                                    });
                                }
                            }
                        }
                        catch { }

                        processed++;

                        if (processed % 25 == 0 || processed == totalFiles)
                        {
                            double progress = (double)processed / totalFiles * 100;
                            TimeSpan elapsed = sw.Elapsed;
                            TimeSpan estimatedTotal = TimeSpan.FromMilliseconds(elapsed.TotalMilliseconds * totalFiles / processed);
                            TimeSpan remaining = estimatedTotal - elapsed;

                            Dispatcher.Invoke(() =>
                            {
                                ScanProgressBar.Value = progress;
                                ScanResultLabel.Text = Loc.F("Scanning... {0:F1}% - ETA: {1:mm\\:ss}", progress, remaining);
                            });
                        }
                    }
                });

                Dispatcher.Invoke(() =>
                {
                    var sortedList = foundFiles.OrderByDescending(f => f.FileSizeBytes).ToList();
                    LargeFilesDataGrid.ItemsSource = sortedList;

                    if (cancel == 1)
                    {
                        ScanResultLabel.Text = Loc.F("{0} large files found. Scan Cancelled!", sortedList.Count);
                    }
                    else
                    {
                        ScanResultLabel.Text = FoundText(sortedList.Count, sortedList.Sum(f => f.FileSizeBytes));
                    }
                });
            }
            catch (Exception ex)
            {
                ScanResultLabel.Text = Loc.F("An error occurred: {0}", ex.Message);
            }
            finally
            {
                (Application.Current?.MainWindow as Multron_Win_Cleaner.MainWindow)?.EndMirrorToolStatus(ScanResultLabel, cancel == 1);
                isScanning = false;
                UpdateScanButtonUI(false);
                ScanProgressBar.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateScanButtonUI(bool scanning)
        {
            StartScan.IsEnabled = true;
            if (StartScan.Content is TextBlock tb)
            {
                tb.Text = Loc.T(scanning ? "CANCEL" : "SCAN");
            }
            else
            {
                StartScan.Content = Loc.T(scanning ? "CANCEL" : "SCAN");
            }
        }

        private IEnumerable<string> SafeFileEnumerator(string root)
        {
            var pending = new Stack<string>();
            pending.Push(root);

            while (pending.Count > 0)
            {
                if (cancel == 1) break;
                string currentDir = pending.Pop();
                 
                if (IsPathExcluded(currentDir)) continue;

                string[] subDirs;
                try
                {
                    subDirs = Directory.GetDirectories(currentDir);
                }
                catch { continue; }

                foreach (var dir in subDirs)
                {
                    if (cancel == 1) break;
                    if (!IsPathExcluded(dir))
                    {
                        pending.Push(dir);
                    }
                }

                string[] files;
                try
                {
                    files = Directory.GetFiles(currentDir);
                }
                catch { continue; }

                foreach (var file in files)
                {
                    if (cancel == 1) break;
                    if (!IsPathExcluded(file))
                    {
                        yield return file;
                    }
                }
            }
        }
         
        private void AddFolderToExclusions_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new VistaFolderBrowserDialog
            {
                Description = Loc.T("Select a folder to exclude"),
                UseDescriptionForTitle = true
            };

            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
            {
                AddExclusionPath(dialog.SelectedPath);
            }
        }

        private void AddExclusionPath(string path)
        {
            if (!excludedPaths.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                excludedPaths.Add(path);

            
                var listBox = (FindName("ExcludedListBox") ??
                               FindName("ExcludedFolderListBox") ??
                               FindName("ExcludedFolderList")) as ListBox;

                if (listBox != null && !listBox.Items.Contains(path))
                {
                    listBox.Items.Add(path);
                }
            }
        }

        private void RemoveExclusion_Click(object sender, RoutedEventArgs e)
        {
            if (FindName("ExcludedListBox") is ListBox excludedListBox && excludedListBox.SelectedItem != null)
            {
                string pathToRemove = excludedListBox.SelectedItem.ToString() ?? "";
                excludedPaths.RemoveAll(p => p.Equals(pathToRemove, StringComparison.OrdinalIgnoreCase));
                excludedListBox.Items.Remove(excludedListBox.SelectedItem);
            }
        }
 
        private void ExcludeSelectedFile_Click(object sender, RoutedEventArgs e)
        {
            if (LargeFilesDataGrid.SelectedItem is LargeFileInfo selectedFile && !string.IsNullOrEmpty(selectedFile.Path))
            {
                AddExclusionPath(selectedFile.Path);
                AppDialog.Show(Loc.F("File added to exclusions:\n{0}", selectedFile.Path), Loc.T("Excluded"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void ExcludeSelectedFolder_Click(object sender, RoutedEventArgs e)
        {
            if (LargeFilesDataGrid.SelectedItem is LargeFileInfo selectedFile && !string.IsNullOrEmpty(selectedFile.Path))
            {
                string? directory = Path.GetDirectoryName(selectedFile.Path);
                if (!string.IsNullOrEmpty(directory))
                {
                    AddExclusionPath(directory);
                    AppDialog.Show(Loc.F("Folder added to exclusions:\n{0}", directory), Loc.T("Excluded"), MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        private void LargeFilesDataGrid_Sorting(object sender, DataGridSortingEventArgs e)
        {
            if (e.Column == SizeColumn)
            {
                e.Handled = true;
                var list = LargeFilesDataGrid.ItemsSource as List<LargeFileInfo>;
                if (list == null) return;

                list = sortDescending
                    ? list.OrderByDescending(f => f.FileSizeBytes).ToList()
                    : list.OrderBy(f => f.FileSizeBytes).ToList();

                var direction = sortDescending ? System.ComponentModel.ListSortDirection.Descending : System.ComponentModel.ListSortDirection.Ascending;
                sortDescending = !sortDescending;

                LargeFilesDataGrid.ItemsSource = null;
                LargeFilesDataGrid.ItemsSource = list;
                foreach (DataGridColumn column in LargeFilesDataGrid.Columns)
                    column.SortDirection = null;
                SizeColumn.SortDirection = direction;
            }
        }

        private void SelectAllCategories_Click(object sender, RoutedEventArgs e)
        {
            SetCategoriesState(true);
        }

        private void DeselectAllCategories_Click(object sender, RoutedEventArgs e)
        {
            SetCategoriesState(false);
        }

        private void SetCategoriesState(bool isChecked)
        {
            ChkVideos.IsChecked = isChecked;
            ChkMusic.IsChecked = isChecked;
            ChkPhotos.IsChecked = isChecked;
            ChkArchives.IsChecked = isChecked;
            ChkDocuments.IsChecked = isChecked;
            ChkPrograms.IsChecked = isChecked;
            ChkDiskImages.IsChecked = isChecked;
            ChkBackups.IsChecked = isChecked;
            ChkOthers.IsChecked = isChecked;
            foreach (var box in extensionBoxes)
                box.IsChecked = isChecked;
        }

        private void OpenFile_Click(object sender, RoutedEventArgs e)
        {
            if (LargeFilesDataGrid.SelectedItem is LargeFileInfo selectedFile && !string.IsNullOrEmpty(selectedFile.Path))
            {
                try
                {
                    if (File.Exists(selectedFile.Path))
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = selectedFile.Path,
                            UseShellExecute = true
                        });
                    }
                    else
                    {
                        AppDialog.Show(Loc.T("File no longer exists."), Loc.T("Warning"), MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                catch (Exception ex)
                {
                    AppDialog.Show(Loc.F("Could not open file: {0}", ex.Message), Loc.T("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void OpenFileLocation_Click(object sender, RoutedEventArgs e)
        {
            if (LargeFilesDataGrid.SelectedItem is LargeFileInfo selectedFile && !string.IsNullOrEmpty(selectedFile.Path))
            {
                try
                {
                    if (File.Exists(selectedFile.Path))
                    {
                        string argument = "/select, \"" + selectedFile.Path + "\"";
                        Process.Start("explorer.exe", argument);
                    }
                }
                catch (Exception ex)
                {
                    AppDialog.Show(Loc.F("Could not open folder: {0}", ex.Message), Loc.T("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void CopyFilePath_Click(object sender, RoutedEventArgs e)
        {
            if (LargeFilesDataGrid.SelectedItem is LargeFileInfo selectedFile && !string.IsNullOrEmpty(selectedFile.Path))
            {
                Clipboard.SetText(selectedFile.Path);
            }
        }
 

        private void RemoveSelectedFolder_Click(object sender, RoutedEventArgs e)
        {
            if (FolderListBox.SelectedItem != null)
            {
                FolderListBox.Items.Remove(FolderListBox.SelectedItem);
            }
        }

        private void AddFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new VistaFolderBrowserDialog
            {
                Description = Loc.T("Select a folder"),
                UseDescriptionForTitle = true
            };

            bool? result = dialog.ShowDialog();

            if (result == true && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
            {
                if (!FolderListBox.Items.Contains(dialog.SelectedPath))
                {
                    FolderListBox.Items.Add(dialog.SelectedPath);
                }
            }
        }

        public static readonly string[] ExportHeader = { "File Name", "Path", "Size", "Bytes", "Last Used / Access", "Last Modified" };

        public IEnumerable<string[]> ExportRows()
        {
            var view = LargeFilesDataGrid.ItemsSource == null ? null : CollectionViewSource.GetDefaultView(LargeFilesDataGrid.ItemsSource);
            if (view == null)
                yield break;
            foreach (var file in view.OfType<LargeFileInfo>())
                yield return new[] { file.Name, file.Path, file.FormattedSize, file.FileSizeBytes.ToString(System.Globalization.CultureInfo.InvariantCulture), file.FormattedLastAccessed, file.FormattedLastModified };
        }

        private void ExportListButton_Click(object sender, RoutedEventArgs e)
        {
            if (!ExportRows().Any())
            {
                AppDialog.Show(this, Loc.T("There is nothing to export yet. Run a scan first."), Loc.T("Export list"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            MultronWinCleaner.Processes.CsvExport.Save(this, "MultronWinCleaner_large_files", ExportHeader.Select(Loc.T), ExportRows());
        }

        private void ClearListButton_Click(object sender, RoutedEventArgs e)
        {
            LargeFilesDataGrid.ItemsSource = null;
            SearchBox.Clear();
            ScanResultLabel.Text = Loc.T("List Cleared!");
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }
        private void AddExcludedFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new VistaFolderBrowserDialog
            {
                Description = Loc.T("Select a folder to exclude"),
                UseDescriptionForTitle = true
            };

            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
            {
                AddExclusionPath(dialog.SelectedPath);
            }
        }

        private void RemoveSelectedExcludedFolder_Click(object sender, RoutedEventArgs e)
        {
            var listBox = (FindName("ExcludedListBox") ?? FindName("ExcludedFolderListBox")) as ListBox;
            if (listBox?.SelectedItem != null)
            {
                string pathToRemove = listBox.SelectedItem.ToString() ?? "";
                excludedPaths.RemoveAll(p => p.Equals(pathToRemove, StringComparison.OrdinalIgnoreCase));
                listBox.Items.Remove(listBox.SelectedItem);
            }
        }
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
            }
        }
    }
}
