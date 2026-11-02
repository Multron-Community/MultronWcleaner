using Ookii.Dialogs.Wpf;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MultronWinCleaner
{
    public class DuplicateFileModel
    {
        public string FileName { get; set; } = "";
        public string FilePath { get; set; } = "";
        public long FileSize { get; set; }
        public string Hash { get; set; } = "";
    }

    public class FolderNodeModel : INotifyPropertyChanged
    {
        public string Name { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public ObservableCollection<object> Children { get; set; } = new ObservableCollection<object>();

        public IEnumerable<FolderNodeModel> SubFolders => Children.OfType<FolderNodeModel>();
        public IEnumerable<FileNodeModel> Files => Children.OfType<FileNodeModel>();

        public int TotalDuplicateCount => Files.Count() + SubFolders.Sum(s => s.TotalDuplicateCount);

        public event PropertyChangedEventHandler? PropertyChanged { add { } remove { } }
    }

    public class FileNodeModel : INotifyPropertyChanged
    {
        private string _fileName = string.Empty;
        private string _filePath = string.Empty;
        private string _fileIcon = "📄";
        private string _formattedSize = string.Empty;
        private bool _isSelectedForDeletion;
        private bool _isHashVisible;

        public string FileName { get => _fileName; set { if (_fileName != value) { _fileName = value; OnPropertyChanged(nameof(FileName)); } } }
        public string FilePath { get => _filePath; set { if (_filePath != value) { _filePath = value; OnPropertyChanged(nameof(FilePath)); } } }
        public string FileIcon { get => _fileIcon; set { if (_fileIcon != value) { _fileIcon = value; OnPropertyChanged(nameof(FileIcon)); } } }
        public string FormattedSize { get => _formattedSize; set { if (_formattedSize != value) { _formattedSize = value; OnPropertyChanged(nameof(FormattedSize)); } } }
        public bool IsSelectedForDeletion { get => _isSelectedForDeletion; set { if (_isSelectedForDeletion != value) { _isSelectedForDeletion = value; OnPropertyChanged(nameof(IsSelectedForDeletion)); } } }
        public bool IsHashVisible { get => _isHashVisible; set { if (_isHashVisible != value) { _isHashVisible = value; OnPropertyChanged(nameof(IsHashVisible)); } } }

        public string? Hash { get; set; }
        public long FileSize { get; set; }
        public DateTime Created { get; set; }
        public string FolderPath => Path.GetDirectoryName(FilePath) ?? string.Empty;
        public bool IsExactDuplicate => Hash != null && !Hash.StartsWith(Duplicate_File_Finder.SimilarMediaPrefix);
        public string? DisplayHash => IsExactDuplicate || Hash == null ? Hash : Loc.T(Duplicate_File_Finder.SimilarMediaPrefix) + Hash.Substring(Duplicate_File_Finder.SimilarMediaPrefix.Length);
        public string CopyBadge => Loc.T(IsExactDuplicate ? "COPY" : "SIMILAR");
        public string CopyBadgeColor => IsExactDuplicate ? "#FFFF9800" : "#FF8E44AD";
        public string CreatedText => Created == default || Created == DateTime.MaxValue ? string.Empty : Loc.F("Created {0:g}", Created);

        public List<string> MatchedFilePaths { get; } = new List<string>();

        public int MatchCount => MatchedFilePaths.Count;
        public bool HasMatches => MatchCount > 0;

        public string MatchSummary => MatchCount switch
        {
            0 => string.Empty,
            1 => Loc.F("1 match found — {0}", Path.GetFileName(MatchedFilePaths[0])),
            _ => Loc.F("{0} matches found — view", MatchCount)
        };

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
    }

    public class DuplicatePairModel
    {
        public FileNodeModel Copy { get; set; } = null!;
        public FileNodeModel Original { get; set; } = null!;
        public string RelationText => Loc.T(Copy != null && Copy.IsExactDuplicate ? "copy of" : "similar to");
    }

    public partial class Duplicate_File_Finder : Window
    {
        public ObservableCollection<FolderNodeModel> ExplorerTree { get; set; } = new ObservableCollection<FolderNodeModel>();
        private List<DuplicateFileModel> allDuplicates = new List<DuplicateFileModel>();

        private Dictionary<string, List<FileNodeModel>> _duplicateGroupIndex = new Dictionary<string, List<FileNodeModel>>(StringComparer.OrdinalIgnoreCase);
        private List<DuplicatePairModel> duplicatePairs = new List<DuplicatePairModel>();
        public const string SimilarMediaPrefix = "Similar Media: ";
        private static readonly EnumerationOptions DuplicateFolderOptions = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System };
        private static readonly EnumerationOptions DuplicateFileOptions = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System };

        [StructLayout(LayoutKind.Sequential)]
        private struct BY_HANDLE_FILE_INFORMATION
        {
            public uint FileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
            public uint VolumeSerialNumber;
            public uint FileSizeHigh;
            public uint FileSizeLow;
            public uint NumberOfLinks;
            public uint FileIndexHigh;
            public uint FileIndexLow;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetFileInformationByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle hFile, out BY_HANDLE_FILE_INFORMATION info);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint dwFlags, IntPtr hToken, out string pszPath);

        private static readonly Guid DownloadsFolderId = new Guid("374DE290-123F-4565-9164-39C4925E467B");

        private static readonly HashSet<string> SkippedFolderNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "AppData", "Windows", "Program Files", "Program Files (x86)", "ProgramData", "$Recycle.Bin", "System Volume Information",
            "Recovery", "PerfLogs", "$WinREAgent", "WindowsApps", "SteamLibrary", "steamapps", "XboxGames", "Epic Games", "Riot Games",
            "EA Games", "GOG Games", "Ubisoft Game Launcher", "node_modules", ".git"
        };

        private static string DownloadsFolder()
        {
            try
            {
                if (SHGetKnownFolderPath(DownloadsFolderId, 0, IntPtr.Zero, out string path) == 0 && Directory.Exists(path))
                    return path;
            }
            catch (Exception) { }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }

        private static IEnumerable<string> PersonalFolders()
        {
            yield return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            yield return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            yield return DownloadsFolder();
            yield return Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            yield return Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            yield return Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
            yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
            yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments);
            yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonPictures);
            yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonVideos);
            yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonMusic);
            foreach (string variable in new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" })
                if (Environment.GetEnvironmentVariable(variable) is string oneDrive)
                    yield return oneDrive;
            string systemRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                bool usable;
                try { usable = drive.DriveType == DriveType.Fixed && drive.IsReady; }
                catch (Exception) { usable = false; }
                if (usable && !drive.RootDirectory.FullName.Equals(systemRoot, StringComparison.OrdinalIgnoreCase))
                    yield return drive.RootDirectory.FullName;
            }
        }

        private string duplicatesFilePath = Path.Combine(Environment.CurrentDirectory, "duplicates.json");

        private bool _isScanning = false;
        private bool _isScanned = false;
        private CancellationTokenSource? _cts;

        public Duplicate_File_Finder()
        {
            InitializeComponent();
            ExtensionCheckBox_CheckedChanged(this, new RoutedEventArgs());

            DuplicatesTreeView.ItemsSource = ExplorerTree;
            this.Loaded += Window_Loaded;

            var availabilityTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            availabilityTimer.Tick += (s, e) => UpdateScanAvailability();
            IsVisibleChanged += (s, e) =>
            {
                if (IsVisible)
                {
                    UpdateScanAvailability();
                    availabilityTimer.Start();
                }
                else
                {
                    availabilityTimer.Stop();
                }
            };
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(userProfile) && FolderListBox.Items.Count == 0)
            {
                FolderListBox.Items.Add(userProfile);
            }

            await LoadDuplicatesDataAsync();
        }

        #region Centralized Data Management (Save & Load)

        private async Task SaveDuplicatesDataAsync()
        {
            try
            {
       
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(allDuplicates, options);
                await File.WriteAllTextAsync(duplicatesFilePath, json);
            }
            catch (Exception ex)
            {
                AppDialog.Show(Loc.F("Could not save duplicates.json data:\n{0}", ex.Message), Loc.T("Save Error"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task LoadDuplicatesDataAsync()
        {
            if (_isScanning) return;

            if (!File.Exists(duplicatesFilePath))
            {
                StatusText.Text = Loc.T("Ready to scan target directories.");
                return;
            }

            LoadingOverlay.Visibility = Visibility.Visible;

            try
            {
                List<DuplicateFileModel>? loaded = await Task.Run(() =>
                {
                    using (var stream = new FileStream(duplicatesFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        return JsonSerializer.Deserialize<List<DuplicateFileModel>>(
                            stream,
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                        );
                    }
                });

                if (loaded == null || loaded.Count == 0 || _isScanning) return;

                allDuplicates = loaded;

                bool isHashVisible = ShowHashCheckBox?.IsChecked == true;

                var (newRoots, groupIndex) = await Task.Run(() => BuildFolderTreeNodes(allDuplicates, isHashVisible));
                SetGroupIndex(groupIndex);

                DuplicatesTreeView.ItemsSource = null;
                ExplorerTree.Clear();

                foreach (var root in newRoots)
                {
                    ExplorerTree.Add(root);
                }

                DuplicatesTreeView.ItemsSource = ExplorerTree;

                StatusText.Text = Loc.F("Successfully loaded {0} duplicate files from previous scan.", allDuplicates.Count);

                await CalculateTotalDuplicateSizeAsync();

                if (allDuplicates.Count > 0)
                {
                    _isScanned = true;
                    MainActionButton.Content = Loc.T("CLEAN");
                    MainActionButton.Background = new SolidColorBrush(Colors.Crimson);
                }
            }
            catch
            {
                StatusText.Text = Loc.T("Failed to load previous scan data.");
            }
            finally
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        #endregion

        private static Multron_Win_Cleaner.MainWindow? MainStatus => Application.Current?.MainWindow as Multron_Win_Cleaner.MainWindow;

        private async void MainActionButton_Click(object sender, RoutedEventArgs e)
        {
            if (_mainCancelContent != null)
            {
                (Application.Current?.MainWindow as Multron_Win_Cleaner.MainWindow)?.CancelScanOrClean();
                return;
            }
            if (_isScanning)
            {
                _cts?.Cancel();
                MainActionButton.IsEnabled = false;
                StatusText.Text = Loc.T("Canceling scan...");
                return;
            }

            if (!_isScanned)
            {
                if (FolderListBox.Items.Count == 0)
                {
                    AppDialog.Show(Loc.T("Please select at least one folder."), Loc.T("Warning"), MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                _isScanning = true;
                MainActionButton.Content = Loc.T("Cancel");
                TotalSizeText.Visibility = Visibility.Collapsed;
                if (SearchCountText != null) SearchCountText.Text = "";

                MainStatus?.MirrorToolStatus(Loc.T("Duplicate File Finder"), StatusText);
                await RunScanAsync();

                _isScanning = false;
                MainActionButton.IsEnabled = true;

                if (_cts != null && _cts.IsCancellationRequested)
                {
                    StatusText.Text = Loc.F("Scan Canceled. Found {0} duplicate files.", allDuplicates.Count);
                    MainStatus?.EndMirrorToolStatus(StatusText, true);
                    if (allDuplicates.Count == 0) ResetToScanState();
                }
                else if (allDuplicates.Count > 0)
                {
                    _isScanned = true;
                    MainActionButton.Content = Loc.T("CLEAN");
                    MainActionButton.Background = new SolidColorBrush(Colors.Crimson);
                    MainStatus?.EndMirrorToolStatus(StatusText);
                }
                else
                {
                    StatusText.Text = Loc.T("Scan Complete. No duplicate files found.");
                    MainStatus?.EndMirrorToolStatus(StatusText);
                    AppDialog.Show(Loc.T("Scan complete. No duplicate files were found in the selected directories."), Loc.T("Scan Results"), MessageBoxButton.OK, MessageBoxImage.Information);
                    ResetToScanState();
                }
            }
            else
            {
                CleanDuplicates();
            }
        }

        private bool _isDeleting;

        private object? _mainCancelContent;

        private void UpdateScanAvailability()
        {
            if (_isScanning || _isDeleting)
                return;
            bool busy = (Application.Current?.MainWindow as Multron_Win_Cleaner.MainWindow)?.IsScanOrCleanBusy == true;
            if (busy)
            {
                if (!Equals(MainActionButton.Content, Loc.T("Cancel")))
                    _mainCancelContent = MainActionButton.Content;
                MainActionButton.Content = Loc.T("Cancel");
                MainActionButton.IsEnabled = true;
                MainActionButton.ToolTip = Loc.T("Cancel the scan or clean running on the main screen.");
            }
            else if (_mainCancelContent != null)
            {
                MainActionButton.Content = _mainCancelContent;
                _mainCancelContent = null;
                MainActionButton.IsEnabled = true;
                MainActionButton.ToolTip = null;
            }
        }

        public bool IsBusy => _isScanning;

        public string CurrentStatus => StatusText?.Text ?? "";

        public bool WasCanceled => _cts?.IsCancellationRequested == true;

        public void CancelScan()
        {
            if (_isScanning)
                _cts?.Cancel();
        }

        public void ShowSideBySide()
        {
            SideBySideRadio.IsChecked = true;
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
            Activate();
        }

        public async Task<(int Copies, int Groups, long Bytes)> RunReportScanAsync()
        {
            if (_isScanning)
                return (0, 0, 0);

            var folders = PersonalFolders().ToList();
            FolderListBox.Items.Clear();
            foreach (string folder in folders.Where(f => !string.IsNullOrEmpty(f) && Directory.Exists(f)).Distinct(StringComparer.OrdinalIgnoreCase))
                FolderListBox.Items.Add(folder);

            _isScanning = true;
            MainActionButton.Content = Loc.T("Cancel");
            TotalSizeText.Visibility = Visibility.Collapsed;
            try
            {
                await RunScanAsync();
            }
            finally
            {
                _isScanning = false;
                MainActionButton.IsEnabled = true;
            }

            if (allDuplicates.Count > 0)
            {
                _isScanned = true;
                MainActionButton.Content = Loc.T("CLEAN");
                MainActionButton.Background = new SolidColorBrush(Colors.Crimson);
            }
            else
            {
                ResetToScanState();
            }

            var groups = allDuplicates.Where(d => d.Hash != null && !d.Hash.StartsWith(SimilarMediaPrefix)).GroupBy(d => d.Hash).Where(g => g.Count() > 1).ToList();
            return (groups.Sum(g => g.Count() - 1), groups.Count, groups.Sum(g => g.Skip(1).Sum(d => d.FileSize)));
        }

        private async Task RunScanAsync()
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            await Dispatcher.InvokeAsync(() =>
            {
                allDuplicates.Clear();
                ExplorerTree.Clear();
                if (FilesScannedText != null) FilesScannedText.Text = "";
                StatusText.Text = Loc.T("Scanning directories...");
            });

            try
            {
                await Scan(FolderListBox.Items.Cast<object>().Select(i => i.ToString() ?? "").ToList());

                await SaveDuplicatesDataAsync();

                await Dispatcher.InvokeAsync(() =>
                {
                    BuildFolderTree(allDuplicates);
                    _ = CalculateTotalDuplicateSizeAsync();

                    if (allDuplicates.Count > 0)
                    {
                        StatusText.Text = _cts.IsCancellationRequested
                            ? Loc.F("Scan Canceled. Found {0} duplicate files.", allDuplicates.Count)
                            : Loc.F("Scan Complete. Found {0} duplicate files.", allDuplicates.Count);
                    }
                });
            }
            catch (OperationCanceledException)
            {
                await Dispatcher.InvokeAsync(() => StatusText.Text = Loc.T("Scan Canceled."));
            }
            catch (Exception ex)
            {
                await Dispatcher.InvokeAsync(() => StatusText.Text = Loc.F("Scan Error: {0}", ex.Message));
            }
        }

        private static string? GetFileId(FileStream stream)
        {
            return GetFileInformationByHandle(stream.SafeFileHandle, out BY_HANDLE_FILE_INFORMATION info)
                ? $"{info.VolumeSerialNumber:X8}-{info.FileIndexHigh:X8}{info.FileIndexLow:X8}"
                : null;
        }

        private static bool FilesAreEqual(string first, string second, CancellationToken token)
        {
            const int BufferSize = 1024 * 1024;
            using var a = new FileStream(first, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, BufferSize, FileOptions.SequentialScan);
            using var b = new FileStream(second, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, BufferSize, FileOptions.SequentialScan);
            if (a.Length != b.Length)
                return false;

            byte[] bufferA = new byte[BufferSize];
            byte[] bufferB = new byte[BufferSize];
            while (true)
            {
                token.ThrowIfCancellationRequested();
                int readA = a.ReadAtLeast(bufferA, BufferSize, throwOnEndOfStream: false);
                int readB = b.ReadAtLeast(bufferB, BufferSize, throwOnEndOfStream: false);
                if (readA != readB)
                    return false;
                if (readA == 0)
                    return true;
                if (!bufferA.AsSpan(0, readA).SequenceEqual(bufferB.AsSpan(0, readB)))
                    return false;
            }
        }

        private async Task Scan(List<string> rootFolders)
        {
            var token = (_cts ??= new CancellationTokenSource()).Token;
            var exactResults = new ConcurrentBag<DuplicateFileModel>();
            var similarResults = new List<DuplicateFileModel>();

            int scannedCount = 0;
            long lastUpdateTicks = 0;
            const int updateIntervalMs = 200;

            long minSizeBytes = 0;
            if (MinSizeTextBox != null && long.TryParse(MinSizeTextBox.Text, out long minKb))
                minSizeBytes = minKb * 1024;

            var selectedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool allowAllExtensions = false;

            string filterText = ExtensionTextBox?.Text?.Trim() ?? "*.*";

            if (string.IsNullOrEmpty(filterText) || filterText == "*.*" || filterText == "*")
            {
                allowAllExtensions = true;
            }
            else
            {
                var parts = filterText.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var part in parts)
                {
                    string cleanExt = part.Trim();
                    if (cleanExt == "*.*" || cleanExt == "*")
                    {
                        allowAllExtensions = true;
                        break;
                    }
                    cleanExt = cleanExt.Replace("*", "");
                    if (!cleanExt.StartsWith("."))
                    {
                        cleanExt = "." + cleanExt;
                    }

                    if (cleanExt.Length > 1)
                    {
                        selectedExtensions.Add(cleanExt);
                    }
                }

                if (selectedExtensions.Count == 0)
                {
                    allowAllExtensions = true;
                }
            }

            bool checkQualities = CheckQualityCheckBox?.IsChecked == true;

            void MaybeReportStatus(string currentFile, string phase)
            {
                var now = Environment.TickCount64;
                if (now - Interlocked.Read(ref lastUpdateTicks) < updateIntervalMs) return;
                Interlocked.Exchange(ref lastUpdateTicks, now);

                Dispatcher.BeginInvoke(() =>
                {
                    StatusText.Text = Loc.F("{0}... {1} files scanned, {2} identical files found.", Loc.T(phase), scannedCount, exactResults.Count);
                    if (FilesScannedText != null) FilesScannedText.Text = currentFile;
                });
            }

            try
            {
                await Task.Run(() =>
                {
                    string windowsFolder = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\') + "\\";

                    IEnumerable<string> EnumerateAllFiles()
                    {
                        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        var stack = new Stack<string>();
                        foreach (string root in rootFolders)
                        {
                            if (Directory.Exists(root)) stack.Push(Path.GetFullPath(root));
                        }

                        while (stack.Count > 0)
                        {
                            token.ThrowIfCancellationRequested();
                            string dir = stack.Pop();
                            string key = dir.TrimEnd('\\') + "\\";
                            if (!visited.Add(key) || key.StartsWith(windowsFolder, StringComparison.OrdinalIgnoreCase))
                                continue;
                            if (!rootFolders.Any(r => key.Equals(r.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
                                && SkippedFolderNames.Contains(Path.GetFileName(dir.TrimEnd('\\'))))
                                continue;

                            List<string> subDirs = new List<string>();
                            List<string> filesHere = new List<string>();
                            try
                            {
                                var info = new DirectoryInfo(dir);
                                subDirs = info.EnumerateDirectories("*", DuplicateFolderOptions).Select(d => d.FullName).ToList();
                                filesHere = info.EnumerateFiles("*", DuplicateFileOptions).Select(fi => fi.FullName).ToList();
                            }
                            catch { }

                            foreach (var d in subDirs) stack.Push(d);
                            foreach (var file in filesHere) yield return file;
                        }
                    }

                    var sizeGroups = new ConcurrentDictionary<long, ConcurrentBag<string>>();
                    var mediaNameGroups = new ConcurrentDictionary<string, ConcurrentBag<string>>();

                    Parallel.ForEach(
                        EnumerateAllFiles(),
                        new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = token },
                        file =>
                        {
                            string ext = Path.GetExtension(file);

                            if (!allowAllExtensions && (string.IsNullOrEmpty(ext) || !selectedExtensions.Contains(ext)))
                            {
                                return;
                            }

                            long size;
                            try { size = new FileInfo(file).Length; }
                            catch { return; }

                            if (size > 0 && size >= minSizeBytes)
                            {
                                sizeGroups.GetOrAdd(size, _ => new ConcurrentBag<string>()).Add(file);

                                string lowerExt = ext.ToLowerInvariant();
                                bool isMedia = ImageExtensions.Contains(lowerExt) || VideoExtensions.Contains(lowerExt);

                                if (checkQualities && isMedia)
                                {
                                    string baseName = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                                    baseName = System.Text.RegularExpressions.Regex.Replace(baseName, @"\b(1080p|720p|1440p|4k|8k|480p|360p|copy)\b", "");
                                    baseName = System.Text.RegularExpressions.Regex.Replace(baseName, @"\(\d+\)", "");
                                    baseName = System.Text.RegularExpressions.Regex.Replace(baseName, @"\s+", " ").Trim(' ', '-', '_');

                                    if (baseName.Length > 0)
                                        mediaNameGroups.GetOrAdd(baseName, _ => new ConcurrentBag<string>()).Add(file);
                                }
                            }
                            Interlocked.Increment(ref scannedCount);
                            MaybeReportStatus(file, "Scanning files");
                        });

                    var candidateGroups = sizeGroups.Where(g => g.Value.Count > 1).ToList();

                    Parallel.ForEach(
                        candidateGroups,
                        new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = token },
                        group =>
                        {
                            long exactSize = group.Key;
                            var hashDict = new Dictionary<string, List<string>>();
                            var seenFileIds = new HashSet<string>();

                            foreach (var file in group.Value.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                            {
                                if (token.IsCancellationRequested) return;

                                string hash;
                                try
                                {
                                    using var stream = new FileStream(
                                        file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                                        bufferSize: 1024 * 1024, FileOptions.SequentialScan);

                                    if (stream.Length != exactSize)
                                        continue;

                                    string? fileId = GetFileId(stream);
                                    if (fileId != null && !seenFileIds.Add(fileId))
                                        continue;

                                    hash = Convert.ToHexString(SHA256.HashData(stream));
                                }
                                catch { continue; }

                                if (!hashDict.TryGetValue(hash, out var list))
                                {
                                    list = new List<string>();
                                    hashDict[hash] = list;
                                }
                                list.Add(file);

                                MaybeReportStatus(file, "Hashing");
                            }

                            foreach (var hashGroup in hashDict.Where(hg => hg.Value.Count > 1))
                            {
                                string reference = hashGroup.Value[0];
                                var identical = new List<string> { reference };

                                foreach (var other in hashGroup.Value.Skip(1))
                                {
                                    if (token.IsCancellationRequested) return;
                                    try
                                    {
                                        if (FilesAreEqual(reference, other, token))
                                            identical.Add(other);
                                    }
                                    catch (OperationCanceledException) { return; }
                                    catch { }
                                    MaybeReportStatus(other, "Verifying byte by byte");
                                }

                                if (identical.Count < 2)
                                    continue;

                                foreach (var exactDuplicate in identical)
                                {
                                    exactResults.Add(new DuplicateFileModel
                                    {
                                        FileName = Path.GetFileName(exactDuplicate),
                                        FilePath = exactDuplicate,
                                        FileSize = exactSize,
                                        Hash = hashGroup.Key
                                    });
                                }
                            }
                        });

                    if (checkQualities && !token.IsCancellationRequested)
                    {
                        var exactPaths = new HashSet<string>(exactResults.Select(r => r.FilePath), StringComparer.OrdinalIgnoreCase);
                        foreach (var group in mediaNameGroups)
                        {
                            var files = group.Value.Where(p => !exactPaths.Contains(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                            if (files.Count < 2)
                                continue;

                            string displayHash = SimilarMediaPrefix + group.Key.ToUpperInvariant();
                            foreach (var file in files)
                            {
                                long fSize = 0;
                                try { fSize = new FileInfo(file).Length; } catch { }

                                similarResults.Add(new DuplicateFileModel
                                {
                                    FileName = Path.GetFileName(file),
                                    FilePath = file,
                                    FileSize = fSize,
                                    Hash = displayHash
                                });
                            }
                        }
                    }

                }, token);
            }
            catch (OperationCanceledException) { }

            var finalList = exactResults.OrderBy(r => r.Hash).ThenBy(r => r.FilePath, StringComparer.OrdinalIgnoreCase).Concat(similarResults).ToList();
            await Dispatcher.InvokeAsync(() =>
            {
                var paths = new HashSet<string>(allDuplicates.Select(d => d.FilePath), StringComparer.OrdinalIgnoreCase);
                foreach (var item in finalList)
                {
                    if (paths.Add(item.FilePath))
                        allDuplicates.Add(item);
                }
                if (FilesScannedText != null) FilesScannedText.Text = "";
            });
        }
        private void ExtensionCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
       
            if (ExtensionTextBox == null) return;

            List<string> selectedExtensions = new List<string>();

            if (QuickExtensionGrid != null)
            {
                foreach (var box in QuickExtensionGrid.Children.OfType<CheckBox>())
                {
                    if (box.IsChecked == true)
                        selectedExtensions.Add("*" + box.Content);
                }
            }

            if (selectedExtensions.Count > 0)
            {
                ExtensionTextBox.Text = string.Join(", ", selectedExtensions);
            }
            else
            {
                ExtensionTextBox.Text = "*.*";
            }
        }

        private static readonly string[] ImageExtensions =
        {
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".heic", ".heif", ".tif", ".tiff", ".svg", ".ico",
            ".raw", ".cr2", ".cr3", ".nef", ".arw", ".dng", ".orf", ".rw2", ".psd", ".avif", ".jfif"
        };

        private static readonly string[] VideoExtensions =
        {
            ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".mpg", ".mpeg", ".3gp", ".ts",
            ".m2ts", ".mts", ".vob", ".ogv", ".rmvb", ".divx"
        };

        private static readonly string[] MusicExtensions =
        {
            ".mp3", ".wav", ".flac", ".aac", ".ogg", ".wma", ".m4a", ".opus", ".aiff", ".alac", ".ape", ".mid", ".midi", ".amr"
        };

        private static readonly string[] DocumentExtensions =
        {
            ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".rtf", ".csv", ".odt", ".ods", ".odp",
            ".epub", ".mobi", ".md", ".xps", ".one", ".pages", ".key", ".numbers"
        };

        private static readonly string[] ArchiveExtensions =
        {
            ".zip", ".rar", ".7z", ".tar", ".gz", ".tgz", ".bz2", ".xz", ".zst", ".cab", ".iso", ".img", ".vhd", ".vhdx", ".vmdk", ".wim", ".esd"
        };

        private static readonly string[] ProgramExtensions =
        {
            ".exe", ".msi", ".msix", ".msixbundle", ".appx", ".appxbundle", ".apk", ".xapk", ".dmg", ".deb", ".rpm", ".jar"
        };

        private static string ToFilter(IEnumerable<string> extensions) => string.Join(", ", extensions.Select(e => "*" + e));

        private void ExtensionPresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ExtensionTextBox == null || ExtensionPresetComboBox.SelectedItem == null) return;

            var selectedItem = (ComboBoxItem)ExtensionPresetComboBox.SelectedItem;
            string content = Loc.En(selectedItem.Content);

            if (content.Contains("All Files"))
            {
                ExtensionTextBox.Text = "*.*";
            }
            else if (content.Contains("Images Only"))
            {
                ExtensionTextBox.Text = ToFilter(ImageExtensions);
            }
            else if (content.Contains("Videos Only"))
            {
                ExtensionTextBox.Text = ToFilter(VideoExtensions);
            }
            else if (content.Contains("Music Only"))
            {
                ExtensionTextBox.Text = ToFilter(MusicExtensions);
            }
            else if (content.Contains("Documents Only"))
            {
                ExtensionTextBox.Text = ToFilter(DocumentExtensions);
            }
            else if (content.Contains("Archives"))
            {
                ExtensionTextBox.Text = ToFilter(ArchiveExtensions);
            }
            else if (content.Contains("Programs"))
            {
                ExtensionTextBox.Text = ToFilter(ProgramExtensions);
            }
            else if (content.Contains("Custom"))
            {
                ExtensionCheckBox_CheckedChanged(this, new RoutedEventArgs());
            }
        }
        private void BuildFolderTree(List<DuplicateFileModel> duplicates)
        {
            var (nodes, groupIndex) = BuildFolderTreeNodes(duplicates, ShowHashCheckBox?.IsChecked == true);
            SetGroupIndex(groupIndex);

            ExplorerTree.Clear();
            foreach (var root in nodes)
            {
                ExplorerTree.Add(root);
            }

            DuplicatesTreeView.ItemsSource = null;
            DuplicatesTreeView.ItemsSource = ExplorerTree;
        }

        private (List<FolderNodeModel> Roots, Dictionary<string, List<FileNodeModel>> GroupIndex) BuildFolderTreeNodes(
            List<DuplicateFileModel> duplicates, bool isHashVisible)
        {
            var rootNodes = new List<FolderNodeModel>();
            var folderLookup = new Dictionary<string, FolderNodeModel>(StringComparer.OrdinalIgnoreCase);
            var groupIndex = new Dictionary<string, List<FileNodeModel>>(StringComparer.OrdinalIgnoreCase);

            var groupedByHash = duplicates.GroupBy(d => d.Hash);
            var fileCheckStates = new Dictionary<string, bool>();
            var createdTimes = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            var siblingPathsByHash = new Dictionary<string, List<string>>();

            foreach (var group in groupedByHash)
            {
                var orderedFiles = group.OrderBy(f => GetCreated(f.FilePath, createdTimes)).ThenBy(f => f.FilePath.Length).ThenBy(f => f.FilePath, StringComparer.OrdinalIgnoreCase).ToList();
                siblingPathsByHash[group.Key ?? ""] = orderedFiles.Select(f => f.FilePath).ToList();

                bool isFirst = true;
                bool isSimilarGroup = group.Key != null && group.Key.StartsWith(SimilarMediaPrefix);
                foreach (var file in orderedFiles)
                {
                    fileCheckStates[file.FilePath] = !isFirst && !isSimilarGroup;
                    isFirst = false;
                }
            }

            foreach (var file in duplicates)
            {
                string? directory = Path.GetDirectoryName(file.FilePath);
                if (string.IsNullOrEmpty(directory)) continue;

                string[] pathParts = directory.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

                FolderNodeModel? currentNode = null;
                string currentPath = "";

                for (int i = 0; i < pathParts.Length; i++)
                {
                    string part = pathParts[i];
                    currentPath = (i == 0) ? part + Path.DirectorySeparatorChar.ToString() : Path.Combine(currentPath, part);

                    if (!folderLookup.TryGetValue(currentPath, out FolderNodeModel? node))
                    {
                        node = new FolderNodeModel { Name = part, FullPath = currentPath };
                        folderLookup[currentPath] = node;

                        if (currentNode == null)
                        {
                            rootNodes.Add(node);
                        }
                        else
                        {
                            currentNode.Children.Add(node);
                        }
                    }
                    currentNode = node;
                }

                if (currentNode != null)
                {
                    var fileNode = new FileNodeModel
                    {
                        FileName = file.FileName,
                        FilePath = file.FilePath,
                        FileSize = file.FileSize,
                        Created = GetCreated(file.FilePath, createdTimes),
                        Hash = file.Hash,
                        FileIcon = GetFileIcon(file.FileName),
                        FormattedSize = FormatSize(file.FileSize),
                        IsSelectedForDeletion = fileCheckStates.GetValueOrDefault(file.FilePath, true),
                        IsHashVisible = isHashVisible
                    };

                    if (!string.IsNullOrEmpty(file.Hash) && siblingPathsByHash.TryGetValue(file.Hash, out var siblingPaths))
                    {
                        foreach (var p in siblingPaths)
                        {
                            if (!string.Equals(p, file.FilePath, StringComparison.OrdinalIgnoreCase))
                                fileNode.MatchedFilePaths.Add(p);
                        }
                    }

                    currentNode.Children.Add(fileNode);

                    if (!string.IsNullOrEmpty(file.Hash))
                    {
                        if (!groupIndex.TryGetValue(file.Hash, out var list))
                        {
                            list = new List<FileNodeModel>();
                            groupIndex[file.Hash] = list;
                        }
                        list.Add(fileNode);
                    }
                }
            }

            return (rootNodes, groupIndex);
        }

        private static DateTime GetCreated(string path, Dictionary<string, DateTime> cache)
        {
            if (!cache.TryGetValue(path, out DateTime value))
            {
                try
                {
                    value = File.Exists(path) ? File.GetCreationTime(path) : DateTime.MaxValue;
                }
                catch (Exception)
                {
                    value = DateTime.MaxValue;
                }
                cache[path] = value;
            }
            return value;
        }

        private void SetGroupIndex(Dictionary<string, List<FileNodeModel>> groupIndex)
        {
            _duplicateGroupIndex = groupIndex;
            var pairs = new List<DuplicatePairModel>();
            foreach (var group in groupIndex.Values)
            {
                if (group.Count < 2) continue;
                var ordered = group.OrderBy(n => n.Created).ThenBy(n => n.FilePath.Length).ThenBy(n => n.FilePath, StringComparer.OrdinalIgnoreCase).ToList();
                foreach (var copy in ordered.Skip(1))
                    pairs.Add(new DuplicatePairModel { Copy = copy, Original = ordered[0] });
            }
            duplicatePairs = pairs.OrderBy(p => p.Original.FilePath, StringComparer.OrdinalIgnoreCase).ToList();
            ApplyPairFilter();
        }

        private void ApplyPairFilter()
        {
            if (PairsListBox == null) return;
            string query = SearchBox?.Text.Trim() ?? string.Empty;
            if (query.Length == 0)
            {
                PairsListBox.ItemsSource = duplicatePairs;
                return;
            }
            PairsListBox.ItemsSource = duplicatePairs.Where(p => MatchesQuery(p.Copy, query) || MatchesQuery(p.Original, query)).ToList();
        }

        private static bool MatchesQuery(FileNodeModel node, string query)
        {
            return node.FilePath.Contains(query, StringComparison.OrdinalIgnoreCase)
                || (node.Hash != null && node.Hash.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        private void ViewMode_Changed(object sender, RoutedEventArgs e)
        {
            if (DuplicatesTreeView == null || PairsListBox == null) return;
            bool sideBySide = SideBySideRadio.IsChecked == true;
            DuplicatesTreeView.Visibility = sideBySide ? Visibility.Collapsed : Visibility.Visible;
            PairsListBox.Visibility = sideBySide ? Visibility.Visible : Visibility.Collapsed;
        }

        private string GetFileIcon(string fileName)
        {
            string ext = Path.GetExtension(fileName).ToLowerInvariant();
            if (ImageExtensions.Contains(ext)) return "🖼️";
            if (VideoExtensions.Contains(ext)) return "🎥";
            if (MusicExtensions.Contains(ext)) return "🎥";
            if (ArchiveExtensions.Contains(ext)) return "🎥";
            if (ProgramExtensions.Contains(ext)) return "🎥";
            return "🎵";
        }

        private string FormatSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;

            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }

            return $"{len:0.##} {sizes[order]}";
        }

        private async Task CalculateTotalDuplicateSizeAsync()
        {
            long totalRecoverableBytes = await Task.Run(() =>
            {
                long bytes = 0;
                var groups = allDuplicates.Where(d => d.Hash != null && !d.Hash.StartsWith(SimilarMediaPrefix)).GroupBy(d => d.Hash);

                foreach (var group in groups)
                {
                    var items = group.ToList();
                    if (items.Count > 1)
                    {
                        bytes += items.Skip(1).Sum(f => f.FileSize);
                    }
                }

                return bytes;
            });

            if (totalRecoverableBytes > 0)
            {
                TotalSizeText.Text = Loc.F("Recoverable Space: {0}", FormatSize(totalRecoverableBytes));
                TotalSizeText.Visibility = Visibility.Visible;
            }
            else
            {
                TotalSizeText.Visibility = Visibility.Collapsed;
            }
        }

        private async void CleanDuplicates()
        {
            MessageBoxResult result = AppDialog.Show(
                Loc.T("All CHECKED files in the list will be permanently deleted. Are you sure?"),
                Loc.T("Confirm Cleanup"), MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                MainStatus?.MirrorToolStatus(Loc.T("Duplicate File Finder"), StatusText);
                var keptFileOf = new Dictionary<FileNodeModel, FileNodeModel>();
                foreach (var group in _duplicateGroupIndex.Values)
                {
                    var ordered = group.OrderBy(n => n.Created).ThenBy(n => n.FilePath.Length).ThenBy(n => n.FilePath, StringComparer.OrdinalIgnoreCase).ToList();
                    var kept = ordered.FirstOrDefault(n => !n.IsSelectedForDeletion && File.Exists(n.FilePath))
                               ?? ordered.FirstOrDefault(n => File.Exists(n.FilePath));
                    if (kept == null)
                        continue;
                    foreach (var node in ordered.Where(n => n.IsSelectedForDeletion && !ReferenceEquals(n, kept)))
                        keptFileOf[node] = kept;
                }
                var filesToDelete = keptFileOf.Keys.ToList();
                var verifyToken = CancellationToken.None;

                int totalFiles = filesToDelete.Count;

                if (totalFiles == 0)
                {
                    AppDialog.Show(Loc.T("No files were selected for deletion."), Loc.T("Info"), MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                int deletedCount = 0;
                long freedBytes = 0;

                var failedFilesList = new List<FileNodeModel>();

                _isDeleting = true;

                MainActionButton.IsEnabled = false;

                var progress = new Progress<string>(statusMessage =>
                {
                    StatusText.Text = statusMessage;
                });

                var progressReporter = (IProgress<string>)progress;

                await Task.Run(() =>
                {
                    int currentProcessed = 0;

                    foreach (var fileNode in filesToDelete)
                    {
                        currentProcessed++;
                        string filePath = fileNode.FilePath;

                        try
                        {
                            var keptFile = keptFileOf[fileNode];
                            if (File.Exists(filePath) && fileNode.IsExactDuplicate && !FilesAreEqual(filePath, keptFile.FilePath, verifyToken))
                            {
                                failedFilesList.Add(CreateErrorNode(fileNode, Loc.F("Not deleted: its content no longer matches {0}", keptFile.FilePath)));
                            }
                            else if (File.Exists(filePath))
                            {
                                long fileSize = new FileInfo(filePath).Length;

                                File.Delete(filePath);
                                deletedCount++;

                                freedBytes += fileSize;
                            }
                            else
                            {
                                failedFilesList.Add(CreateErrorNode(fileNode, Loc.T("File missing on disk")));
                            }
                        }
                        catch (Exception ex)
                        {
                            failedFilesList.Add(CreateErrorNode(fileNode, ex.Message));
                        }

                        progressReporter.Report(Loc.F("Deleting... ({0}/{1}) - Freed: {2}", currentProcessed, totalFiles, FormatSize(freedBytes)));
                    }
                });

                MainActionButton.IsEnabled = true;

                _isDeleting = false;

                ClearListButton_Click(this, new RoutedEventArgs());

                StatusText.Text = Loc.F("Cleanup Complete! Successfully deleted {0} of {1} files. Total freed space: {2}", deletedCount, totalFiles, FormatSize(freedBytes));
                MainStatus?.EndMirrorToolStatus(StatusText, failedFilesList.Count > 0);
                MainStatus?.ShowDuplicatesDeleted(deletedCount, totalFiles, freedBytes, failedFilesList.Count);

                if (failedFilesList.Count > 0)
                {
                    var errorFolder = new FolderNodeModel
                    {
                        Name = Loc.F("⚠️ Failed to Delete ({0} files)", failedFilesList.Count),
                        FullPath = "ErrorList",
                        Children = new ObservableCollection<object>(failedFilesList)
                    };

                    TreeViewRadio.IsChecked = true;
                    DuplicatesTreeView.ItemsSource = new List<object> { errorFolder };
                }
            }
        }

        private FileNodeModel CreateErrorNode(FileNodeModel originalNode, string errorMessage)
        {
            return new FileNodeModel
            {
                FileName = Loc.F("{0}  [ERROR: {1}]", originalNode.FileName, errorMessage),
                FilePath = originalNode.FilePath,
                FileIcon = "❌",
                FormattedSize = originalNode.FormattedSize,
                IsSelectedForDeletion = false,
            };
        }

        private List<FileNodeModel> GetCheckedFiles(IEnumerable<FolderNodeModel> folders)
        {
            var result = new List<FileNodeModel>();
            if (folders == null) return result;

            foreach (var folder in folders)
            {
                result.AddRange(folder.Files.Where(f => f.IsSelectedForDeletion));
                result.AddRange(GetCheckedFiles(folder.SubFolders));
            }
            return result;
        }

        private void ResetToScanState()
        {
            _isScanning = false;
            _isScanned = false;
            MainActionButton.IsEnabled = true;
            MainActionButton.Content = Loc.T("SCAN");
            MainActionButton.Background = (Brush)FindResource("AccentColor");
            StatusText.Text = Loc.T("Ready to scan target directories.");
            TotalSizeText.Visibility = Visibility.Collapsed;
            if (SearchCountText != null) SearchCountText.Text = "";
            if (FilesScannedText != null) FilesScannedText.Text = "";
        }

        private System.Windows.Threading.DispatcherTimer? searchTimer;

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            bool empty = SearchBox.Text.Length == 0;
            SearchHint.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            SearchClear.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
            if (searchTimer == null)
            {
                searchTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
                searchTimer.Tick += (s, args) =>
                {
                    searchTimer.Stop();
                    ApplySearch();
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

        private void ApplySearch()
        {
            ApplyPairFilter();
            string query = SearchBox.Text.Trim();

            if (query.Length == 0)
            {
                DuplicatesTreeView.ItemsSource = ExplorerTree;
                if (SearchCountText != null) SearchCountText.Text = "";
                return;
            }

            var filteredTree = new ObservableCollection<FolderNodeModel>();
            int matchCount = 0;

            foreach (var rootNode in ExplorerTree)
            {
                var filteredNode = FilterFolderTree(rootNode, query, ref matchCount);
                if (filteredNode != null)
                {
                    filteredTree.Add(filteredNode);
                }
            }

            DuplicatesTreeView.ItemsSource = filteredTree;
            if (SearchCountText != null) SearchCountText.Text = Loc.F("{0} matches found", matchCount);
        }

        private FolderNodeModel? FilterFolderTree(FolderNodeModel? node, string query, ref int matchCount)
        {
            if (node == null) return null;

            var filteredNode = new FolderNodeModel
            {
                Name = node.Name,
                FullPath = node.FullPath
            };

            foreach (var subFolder in node.SubFolders)
            {
                var matchedSub = FilterFolderTree(subFolder, query, ref matchCount);
                if (matchedSub != null && matchedSub.Children.Count > 0)
                {
                    filteredNode.Children.Add(matchedSub);
                }
            }

            foreach (var file in node.Files)
            {
                if (file.FileName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    file.FilePath.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    (file.Hash != null && file.Hash.Contains(query, StringComparison.OrdinalIgnoreCase)))
                {
                    filteredNode.Children.Add(file);
                    matchCount++;
                }
            }

            if (filteredNode.Children.Count > 0) return filteredNode;

            return null;
        }

        private void ShowHashCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            ToggleHashVisibility(ExplorerTree, true);
        }

        private void ShowHashCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            ToggleHashVisibility(ExplorerTree, false);
        }

        private void ToggleHashVisibility(IEnumerable<FolderNodeModel> folders, bool isVisible)
        {
            if (folders == null) return;

            foreach (var folder in folders)
            {
                foreach (var file in folder.Files)
                {
                    file.IsHashVisible = isVisible;
                }
                ToggleHashVisibility(folder.SubFolders, isVisible);
            }
        }

        #region Show Matching Files

        private void ContextMenu_ShowMatchingFiles_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.Tag is FileNodeModel node)
                ShowMatchingFiles(node);
        }

        private void MatchSummary_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.Tag is FileNodeModel node)
                ShowMatchingFiles(node);
        }

        private void ShowMatchingFiles(FileNodeModel currentNode)
        {
            if (currentNode == null || string.IsNullOrEmpty(currentNode.Hash)) return;
            if (!_duplicateGroupIndex.TryGetValue(currentNode.Hash, out var siblings) || siblings.Count < 2) return;

            var others = siblings.Where(n => !ReferenceEquals(n, currentNode)).ToList();
            if (others.Count == 0) return;

            var matchesWindow = new MatchingFilesWindow(currentNode, others) { Owner = this };
            matchesWindow.ShowDialog();
        }

        #endregion

        #region Folder Duplicates Popup

        private void FolderBadge_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement element || element.Tag is not FolderNodeModel folder) return;

            e.Handled = true;

            var files = GetAllFilesRecursive(folder);
            if (files.Count == 0) return;

            var window = new FolderDuplicatesWindow(folder.Name, files) { Owner = this };
            window.ShowDialog();
        }

        private List<FileNodeModel> GetAllFilesRecursive(FolderNodeModel folder)
        {
            var result = new List<FileNodeModel>();
            result.AddRange(folder.Files);

            foreach (var subFolder in folder.SubFolders)
                result.AddRange(GetAllFilesRecursive(subFolder));

            return result;
        }

        #endregion

        private void ContextMenu_OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.Tag is string folderPath && Directory.Exists(folderPath))
            {
                Process.Start(new ProcessStartInfo { FileName = folderPath, UseShellExecute = true });
            }
        }

        private void ContextMenu_OpenFileLocation_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.Tag is string filePath && File.Exists(filePath))
            {
                Process.Start("explorer.exe", $"/select,\"{filePath}\"");
            }
        }

        private void ContextMenu_OpenFile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.Tag is string filePath && File.Exists(filePath))
            {
                Process.Start(new ProcessStartInfo { FileName = filePath, UseShellExecute = true });
            }
        }

        private void BtnOpenJson_Click(object sender, RoutedEventArgs e)
        {
            if (File.Exists(duplicatesFilePath))
            {
                try { Process.Start(new ProcessStartInfo(duplicatesFilePath) { UseShellExecute = true }); }
                catch (Exception ex) { AppDialog.Show(Loc.F("Could not open the file: {0}", ex.Message), Loc.T("Error"), MessageBoxButton.OK, MessageBoxImage.Error); }
            }
            else AppDialog.Show(Loc.T("The duplicates data file does not exist yet."), Loc.T("Information"), MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnDeleteJson_Click(object sender, RoutedEventArgs e)
        {
            if (File.Exists(duplicatesFilePath))
            {
                MessageBoxResult result = AppDialog.Show(
                    Loc.T("Are you sure you want to delete the saved duplicates data file?"),
                    Loc.T("Confirm Deletion"), MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        File.Delete(duplicatesFilePath);
                        ClearListButton_Click(this, new RoutedEventArgs());
                        AppDialog.Show(Loc.T("Data file successfully deleted."), Loc.T("Success"), MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch (Exception ex) { AppDialog.Show(Loc.F("Could not delete the file: {0}", ex.Message), Loc.T("Error"), MessageBoxButton.OK, MessageBoxImage.Error); }
                }
            }
            else AppDialog.Show(Loc.T("The duplicates data file does not exist."), Loc.T("Information"), MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ClearListButton_Click(object sender, RoutedEventArgs e)
        {
            allDuplicates.Clear();
            ExplorerTree.Clear();
            SetGroupIndex(new Dictionary<string, List<FileNodeModel>>(StringComparer.OrdinalIgnoreCase));
            StatusText.Text = Loc.T("List cleared.");
            if (File.Exists(duplicatesFilePath)) File.Delete(duplicatesFilePath);
            ResetToScanState();
        }

        private ToolSettingsWindow? settingsWindow;

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            AdvancedPanel.Visibility = Visibility.Visible;
            settingsWindow ??= new ToolSettingsWindow(this, Loc.T("Duplicate File Finder Settings"), AdvancedPanel, 960);
            settingsWindow.ShowOrActivate();
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new VistaFolderBrowserDialog { Description = Loc.T("Select folder") };
            if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.SelectedPath))
                FolderListBox.Items.Add(dlg.SelectedPath);
        }

        private void BtnAddDefault_Click(object sender, RoutedEventArgs e)
        {
            FolderListBox.Items.Clear();
            string? osDrive = Path.GetPathRoot(Environment.SystemDirectory);
            if (!string.IsNullOrEmpty(osDrive)) FolderListBox.Items.Add(osDrive);
        }

        private void NumberValidationTextBox(object sender, TextCompositionEventArgs e)
        {
            System.Text.RegularExpressions.Regex regex = new System.Text.RegularExpressions.Regex("[^0-9]+");
            e.Handled = regex.IsMatch(e.Text);
        }

        private void RemoveButton_Click(object sender, RoutedEventArgs e)
        {
            if (FolderListBox.SelectedItem != null)
                FolderListBox.Items.Remove(FolderListBox.SelectedItem);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Visibility = Visibility.Collapsed;
        private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private bool isMax = false;
        private double prevW, prevH, prevX, prevY;

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (!isMax)
            {
                prevW = Width; prevH = Height; prevX = Left; prevY = Top;
                Left = SystemParameters.WorkArea.Left; Top = SystemParameters.WorkArea.Top;
                Width = SystemParameters.WorkArea.Width; Height = SystemParameters.WorkArea.Height;
            }
            else
            {
                Width = prevW; Height = prevH; Left = prevX; Top = prevY;
            }
            isMax = !isMax;
        }

        private void TopBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) this.DragMove();
        }
    }
}
