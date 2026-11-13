using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace MultronWinCleaner.Processes
{
    public enum BootOperationType
    {
        Delete,
        Move,
        Rename,
        ManualCommand,
        RunProgram,
        EmptyFolder,
        RepairSystemFiles,
        RepairWindowsImage,
        ClearTempFiles,
        ClearUpdateCache,
        ResetNetwork,
        EmptyRecycleBin
    }

    public enum BootOperationState
    {
        Waiting,
        Scheduled,
        Failed
    }

    public sealed class BootOperation : INotifyPropertyChanged
    {
        private BootOperationState state;

        public BootOperationType Type { get; set; }
        public string SourcePath { get; set; } = string.Empty;
        public string DestinationPath { get; set; } = string.Empty;
        public string ManualCommand { get; set; } = string.Empty;
        public string RunOnceName { get; set; } = string.Empty;
        public List<string> ScheduledPaths { get; set; } = new();

        public BootOperationState State
        {
            get => state;
            set
            {
                state = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(State)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StatusText)));
            }
        }

        [JsonIgnore]
        public string TypeText => Loc.T(Type switch
        {
            BootOperationType.Delete => "Delete",
            BootOperationType.Move => "Move",
            BootOperationType.Rename => "Rename",
            BootOperationType.RunProgram => "Run a program",
            BootOperationType.EmptyFolder => "Empty a folder",
            BootOperationType.RepairSystemFiles => "Repair system files (SFC)",
            BootOperationType.RepairWindowsImage => "Repair Windows image (DISM)",
            BootOperationType.ClearTempFiles => "Clear temporary files",
            BootOperationType.ClearUpdateCache => "Clear Windows Update cache",
            BootOperationType.ResetNetwork => "Reset network settings",
            BootOperationType.EmptyRecycleBin => "Empty Recycle Bin",
            _ => "Manual command"
        });

        [JsonIgnore]
        public bool IsCommand => BootOperations.IsCommandType(Type);

        [JsonIgnore]
        public string DetailsText =>
            IsCommand ? ManualCommand :
            Type == BootOperationType.Delete || Type == BootOperationType.EmptyFolder ? SourcePath :
            $"{SourcePath} → {DestinationPath}";

        [JsonIgnore]
        public string StatusText => Loc.T(State switch
        {
            BootOperationState.Scheduled => "Scheduled for next restart",
            BootOperationState.Failed => "Failed",
            _ => "Waiting"
        });

        public bool SameAs(BootOperation other) =>
            Type == other.Type &&
            string.Equals(SourcePath, other.SourcePath, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(DestinationPath, other.DestinationPath, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(ManualCommand, other.ManualCommand, StringComparison.Ordinal);

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public static class BootOperations
    {
        private const string RunOnceKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce";
        private const string CommandPrefix = "cmd.exe /c ";
        private const int MaxRunOnceLength = 260;

        private static readonly Dictionary<BootOperationType, string> Presets = new()
        {
            [BootOperationType.RepairSystemFiles] = "sfc /scannow",
            [BootOperationType.RepairWindowsImage] = "DISM /Online /Cleanup-Image /RestoreHealth",
            [BootOperationType.ClearTempFiles] = "del /f /s /q \"%TEMP%\\*\" & del /f /s /q \"%SystemRoot%\\Temp\\*\"",
            [BootOperationType.ClearUpdateCache] = "net stop wuauserv & rd /s /q \"%SystemRoot%\\SoftwareDistribution\\Download\" & net start wuauserv",
            [BootOperationType.ResetNetwork] = "ipconfig /flushdns & netsh winsock reset & netsh int ip reset",
            [BootOperationType.EmptyRecycleBin] = "rd /s /q %SystemDrive%\\$Recycle.Bin"
        };

        public static bool IsPreset(BootOperationType type) => Presets.ContainsKey(type);

        public static string PresetCommand(BootOperationType type) => Presets.TryGetValue(type, out string? command) ? command : string.Empty;

        public static bool IsCommandType(BootOperationType type) =>
            type == BootOperationType.ManualCommand || type == BootOperationType.RunProgram || IsPreset(type);

        private static string StorePath => Path.Combine(AppContext.BaseDirectory, "BootOperations.json");

        public static BootOperation? Create(BootOperationType type, string source, string destination, string command, out string error, out bool insideWindows)
        {
            error = string.Empty;
            insideWindows = false;

            if (IsPreset(type))
                return new BootOperation { Type = type, ManualCommand = Presets[type] };

            if (type == BootOperationType.RunProgram)
                return CreateProgramOperation(source, command, out error);

            if (type == BootOperationType.ManualCommand)
            {
                string text = command.Trim();
                if (text.Length == 0)
                {
                    error = Loc.T("Please enter a manual command.");
                    return null;
                }
                int limit = MaxRunOnceLength - CommandPrefix.Length;
                if (text.Length > limit)
                {
                    error = Loc.F("The command is too long. The limit is {0} characters.", limit);
                    return null;
                }
                return new BootOperation { Type = type, ManualCommand = text };
            }

            string sourcePath = Clean(source);
            if (sourcePath.Length == 0)
            {
                error = Loc.T("Please enter a source path.");
                return null;
            }
            if (!TryFullPath(sourcePath, out sourcePath))
            {
                error = Loc.T("Enter a full path, for example C:\\Folder\\File.txt.");
                return null;
            }
            if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))
            {
                error = Loc.F("This path does not exist: {0}", sourcePath);
                return null;
            }
            if (IsProtected(sourcePath))
            {
                error = Loc.F("This location is protected and cannot be changed: {0}", sourcePath);
                return null;
            }

            insideWindows = IsInsideWindows(sourcePath);
            if (type == BootOperationType.EmptyFolder && !Directory.Exists(sourcePath))
            {
                error = Loc.F("This path does not exist: {0}", sourcePath);
                return null;
            }
            if (type == BootOperationType.Delete || type == BootOperationType.EmptyFolder)
                return new BootOperation { Type = type, SourcePath = sourcePath };

            string target = ResolveTarget(type, sourcePath, Clean(destination), out error);
            if (target.Length == 0)
                return null;
            insideWindows |= IsInsideWindows(target);
            return new BootOperation { Type = type, SourcePath = sourcePath, DestinationPath = target };
        }

        public static bool Schedule(BootOperation operation, out string error)
        {
            error = string.Empty;
            try
            {
                if (operation.IsCommand)
                {
                    string name = "MultronBootOp_" + Guid.NewGuid().ToString("N").Substring(0, 8);
                    using var key = Registry.LocalMachine.CreateSubKey(RunOnceKey, writable: true);
                    key.SetValue(name, CommandPrefix + operation.ManualCommand, RegistryValueKind.String);
                    operation.RunOnceName = name;
                    operation.State = BootOperationState.Scheduled;
                    return true;
                }

                bool isFile = File.Exists(operation.SourcePath);
                if (!isFile && !Directory.Exists(operation.SourcePath))
                {
                    error = Loc.F("This path does not exist: {0}", operation.SourcePath);
                    operation.State = BootOperationState.Failed;
                    return false;
                }

                var paths = new List<string>();
                bool removes = operation.Type == BootOperationType.Delete || operation.Type == BootOperationType.EmptyFolder;
                string? destination = removes ? null : operation.DestinationPath;
                if (removes && !isFile)
                    CollectForDelete(operation.SourcePath, paths, operation.Type == BootOperationType.Delete);
                else
                    paths.Add(operation.SourcePath);

                var done = new List<string>();
                foreach (string path in paths)
                {
                    if (!ForceDelete.ScheduleFileOperation(path, destination))
                    {
                        error = Loc.F("Could not schedule the operation (error {0}).", ForceDelete.LastScheduleError);
                        ForceDelete.CancelScheduledOperations(done);
                        operation.State = BootOperationState.Failed;
                        return false;
                    }
                    done.Add(path);
                }

                operation.ScheduledPaths = done;
                operation.State = BootOperationState.Scheduled;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                operation.State = BootOperationState.Failed;
                return false;
            }
        }

        public static bool Cancel(BootOperation operation)
        {
            try
            {
                if (operation.IsCommand)
                {
                    using var key = Registry.LocalMachine.OpenSubKey(RunOnceKey, writable: true);
                    key?.DeleteValue(operation.RunOnceName, false);
                    return true;
                }
                return ForceDelete.CancelScheduledOperations(operation.ScheduledPaths);
            }
            catch
            {
                return false;
            }
        }

        public static async Task<(bool Ok, string Message)> RunNowAsync(BootOperation operation)
        {
            try
            {
                if (operation.IsCommand)
                    return await RunCommandAsync(operation.ManualCommand);

                await Task.Run(() =>
                {
                    bool isFile = File.Exists(operation.SourcePath);
                    if (!isFile && !Directory.Exists(operation.SourcePath))
                        throw new FileNotFoundException(Loc.F("This path does not exist: {0}", operation.SourcePath));

                    if (operation.Type == BootOperationType.EmptyFolder)
                    {
                        EmptyFolder(operation.SourcePath);
                    }
                    else if (operation.Type == BootOperationType.Delete)
                    {
                        if (isFile)
                            File.Delete(operation.SourcePath);
                        else
                            Directory.Delete(operation.SourcePath, true);
                    }
                    else if (isFile)
                    {
                        File.Move(operation.SourcePath, operation.DestinationPath);
                    }
                    else
                    {
                        Directory.Move(operation.SourcePath, operation.DestinationPath);
                    }
                });
                return (true, Loc.T("Operation completed."));
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        public static List<BootOperation> Load()
        {
            try
            {
                if (!File.Exists(StorePath))
                    return new List<BootOperation>();
                var saved = JsonSerializer.Deserialize<List<BootOperation>>(File.ReadAllText(StorePath));
                return saved?.Where(StillPending).ToList() ?? new List<BootOperation>();
            }
            catch
            {
                return new List<BootOperation>();
            }
        }

        public static void Save(IEnumerable<BootOperation> operations)
        {
            try
            {
                var scheduled = operations.Where(o => o.State == BootOperationState.Scheduled).ToList();
                if (scheduled.Count == 0)
                    File.Delete(StorePath);
                else
                    File.WriteAllText(StorePath, JsonSerializer.Serialize(scheduled));
            }
            catch { }
        }

        private static bool StillPending(BootOperation operation)
        {
            if (operation.State != BootOperationState.Scheduled)
                return false;
            if (!operation.IsCommand)
                return operation.ScheduledPaths.Count > 0 && ForceDelete.IsScheduled(operation.ScheduledPaths[0]);
            using var key = Registry.LocalMachine.OpenSubKey(RunOnceKey);
            return key?.GetValue(operation.RunOnceName) != null;
        }

        private static string ResolveTarget(BootOperationType type, string source, string destination, out string error)
        {
            error = string.Empty;
            if (destination.Length == 0)
            {
                error = Loc.T("Please enter a destination path.");
                return string.Empty;
            }

            string target;
            if (type == BootOperationType.Rename && destination.IndexOfAny(new[] { '\\', '/' }) < 0)
            {
                if (destination.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                {
                    error = Loc.T("The new name contains characters that are not allowed.");
                    return string.Empty;
                }
                target = Path.Combine(Path.GetDirectoryName(source) ?? string.Empty, destination);
            }
            else if (!TryFullPath(destination, out target))
            {
                error = Loc.T("Enter a full path, for example C:\\Folder\\File.txt.");
                return string.Empty;
            }
            else if (type == BootOperationType.Move && Directory.Exists(target))
            {
                target = Path.Combine(target, Path.GetFileName(source));
            }

            string? parent = Path.GetDirectoryName(target);
            if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
            {
                error = Loc.F("The destination folder does not exist: {0}", parent ?? target);
                return string.Empty;
            }
            if (File.Exists(target) || Directory.Exists(target))
            {
                error = Loc.F("The destination already exists: {0}", target);
                return string.Empty;
            }
            if (!string.Equals(Path.GetPathRoot(source), Path.GetPathRoot(target), StringComparison.OrdinalIgnoreCase))
            {
                error = Loc.T("The destination must be on the same drive as the source.");
                return string.Empty;
            }
            if (IsProtected(target))
            {
                error = Loc.F("This location is protected and cannot be changed: {0}", target);
                return string.Empty;
            }
            return target;
        }

        private static BootOperation? CreateProgramOperation(string source, string arguments, out string error)
        {
            error = string.Empty;
            string path = Clean(source);
            if (path.Length == 0)
            {
                error = Loc.T("Please enter a source path.");
                return null;
            }
            if (!TryFullPath(path, out path))
            {
                error = Loc.T("Enter a full path, for example C:\\Folder\\File.txt.");
                return null;
            }
            if (!File.Exists(path))
            {
                error = Loc.F("This path does not exist: {0}", path);
                return null;
            }
            if (!new[] { ".exe", ".bat", ".cmd" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            {
                error = Loc.T("Choose a program file (.exe, .bat or .cmd).");
                return null;
            }

            string text = $"start \"\" \"{path}\"";
            if (arguments.Trim().Length > 0)
                text += " " + arguments.Trim();
            int limit = MaxRunOnceLength - CommandPrefix.Length;
            if (text.Length > limit)
            {
                error = Loc.F("The command is too long. The limit is {0} characters.", limit);
                return null;
            }
            return new BootOperation { Type = BootOperationType.RunProgram, SourcePath = path, ManualCommand = text };
        }

        private static readonly EnumerationOptions AllEntries = new() { AttributesToSkip = 0, IgnoreInaccessible = false, RecurseSubdirectories = false };

        private static void CollectForDelete(string folder, List<string> ordered, bool includeFolder)
        {
            foreach (var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos("*", AllEntries))
            {
                if (entry is DirectoryInfo child && !entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    CollectForDelete(child.FullName, ordered, true);
                else
                    ordered.Add(entry.FullName);
            }
            if (includeFolder)
                ordered.Add(folder);
        }

        private static void EmptyFolder(string folder)
        {
            foreach (var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos("*", AllEntries).ToList())
            {
                if (entry is DirectoryInfo child && !entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    child.Delete(true);
                else
                    entry.Delete();
            }
        }

        private static async Task<(bool Ok, string Message)> RunCommandAsync(string command)
        {
            using var process = Process.Start(new ProcessStartInfo("cmd.exe", "/c " + command)
            {
                CreateNoWindow = true,
                UseShellExecute = false
            });
            if (process == null)
                return (false, Loc.F("Error while running the command: {0}", command));

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
                return (true, Loc.F("Command finished with exit code {0}.", process.ExitCode));
            }
            catch (OperationCanceledException)
            {
                return (true, Loc.T("The command is still running in the background."));
            }
        }

        private static string Clean(string value) => value.Trim().Trim('"').Trim();

        private static bool TryFullPath(string value, out string fullPath)
        {
            fullPath = string.Empty;
            try
            {
                if (!Path.IsPathFullyQualified(value))
                    return false;
                fullPath = Path.GetFullPath(value);
                string root = Path.GetPathRoot(fullPath) ?? string.Empty;
                if (fullPath.Length > root.Length)
                    fullPath = fullPath.TrimEnd('\\', '/');
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string Folder(Environment.SpecialFolder folder) => Environment.GetFolderPath(folder).TrimEnd('\\');

        private static bool IsSameOrInside(string path, string folder) =>
            folder.Length > 0 &&
            (string.Equals(path, folder, StringComparison.OrdinalIgnoreCase) ||
             path.StartsWith(folder + "\\", StringComparison.OrdinalIgnoreCase));

        private static bool IsInsideWindows(string path) => IsSameOrInside(path, Folder(Environment.SpecialFolder.Windows));

        private static bool IsProtected(string path)
        {
            string root = (Path.GetPathRoot(path) ?? string.Empty).TrimEnd('\\');
            if (string.Equals(path.TrimEnd('\\'), root, StringComparison.OrdinalIgnoreCase))
                return true;

            string windows = Folder(Environment.SpecialFolder.Windows);
            string[] closed =
            {
                Folder(Environment.SpecialFolder.System),
                Folder(Environment.SpecialFolder.SystemX86),
                Path.Combine(windows, "WinSxS"),
                Path.Combine(windows, "Boot")
            };
            if (closed.Any(folder => IsSameOrInside(path, folder)))
                return true;

            string profile = Folder(Environment.SpecialFolder.UserProfile);
            string[] exact =
            {
                windows,
                Folder(Environment.SpecialFolder.ProgramFiles),
                Folder(Environment.SpecialFolder.ProgramFilesX86),
                Folder(Environment.SpecialFolder.CommonApplicationData),
                profile,
                Path.GetDirectoryName(profile) ?? string.Empty
            };
            return exact.Any(folder => string.Equals(path, folder, StringComparison.OrdinalIgnoreCase));
        }
    }
}
