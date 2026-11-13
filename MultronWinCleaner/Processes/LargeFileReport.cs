using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MultronWinCleaner.Processes
{
    public static class LargeFileReport
    {
        private const long MegaByte = 1024 * 1024;

        public sealed record Result(List<LargeFileFinder.LargeFileInfo> Files, int TotalFound, long TotalBytes);

        private static readonly HashSet<string> SkippedFolders = new(StringComparer.OrdinalIgnoreCase)
        {
            "AppData", "node_modules", ".git", ".svn", ".vs", ".nuget", ".cargo", ".gradle", ".m2", ".cache", "$Recycle.Bin", "OneDriveTemp",
            "Program Files", "Program Files (x86)", "Windows", "ProgramData", "Virtual Machines", "Games", "My Games", "Saved Games", "Steam", "SteamLibrary",
            "Epic Games", "GOG Games", "Riot Games", "EA Games", "Ubisoft", "Battle.net", "XboxGames", "WindowsApps",
            "Recovery", "System Volume Information", "PerfLogs", "Boot", "Config.Msi", "MSOCache", "$WinREAgent", "$Windows.~BT", "$Windows.~WS", "$SysReset",
            "Default", "Default User", "All Users", "defaultuser0"
        };

        private static readonly Dictionary<string, long> MinimumSizes = BuildMinimumSizes();

        private static Dictionary<string, long> BuildMinimumSizes()
        {
            var sizes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            void Add(long megabytes, params string[] extensions)
            {
                foreach (string extension in extensions)
                    sizes[extension] = megabytes * MegaByte;
            }
            Add(25, ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tif", ".tiff", ".heic", ".webp", ".psd", ".raw", ".cr2", ".nef", ".arw", ".dng");
            Add(50, ".mp3", ".wav", ".flac", ".m4a", ".aac", ".ogg", ".wma", ".opus");
            Add(300, ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm", ".m4v", ".mpg", ".mpeg", ".ts", ".m2ts", ".flv", ".3gp");
            Add(500, ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz", ".iso", ".img", ".exe", ".msi", ".msix", ".appx", ".apk",
                ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".odt", ".ods", ".odp", ".epub", ".csv", ".txt");
            return sizes;
        }

        private const FileAttributes SkippedAttributes = FileAttributes.ReparsePoint | FileAttributes.Offline | FileAttributes.System | FileAttributes.Hidden
            | (FileAttributes)0x00040000 | (FileAttributes)0x00400000;

        public static Result Find(int unusedMonths, Func<long, string> formatSize, Func<bool> isCancelled)
        {
            var found = new List<FileInfo>();
            DateTime cutoff = DateTime.UtcNow.AddDays(-unusedMonths * 30);
            var options = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = false, AttributesToSkip = SkippedAttributes };
            var roots = PersonalRoots();
            var pending = new Stack<string>(roots.Where(r => r.Length > 0 && Directory.Exists(r)));

            while (pending.Count > 0 && !isCancelled())
            {
                string path = pending.Pop();
                try
                {
                    List<FileSystemInfo> entries = new DirectoryInfo(path).EnumerateFileSystemInfos("*", options).ToList();
                    if (!roots.Contains(path) && IsInstalledProgram(entries))
                        continue;
                    foreach (FileSystemInfo entry in entries)
                    {
                        if (entry is DirectoryInfo)
                        {
                            if (!SkippedFolders.Contains(entry.Name))
                                pending.Push(entry.FullName);
                        }
                        else if (entry is FileInfo file && MinimumSizes.TryGetValue(file.Extension, out long minimum) && file.Length >= minimum && file.LastWriteTimeUtc <= cutoff)
                        {
                            found.Add(file);
                        }
                    }
                }
                catch { }
            }

            var files = found.OrderByDescending(f => f.Length)
                .Select(f => new LargeFileFinder.LargeFileInfo
                {
                    Name = f.Name,
                    Path = f.FullName,
                    FormattedSize = formatSize(f.Length),
                    FileSizeBytes = f.Length,
                    LastModified = f.LastWriteTime,
                    LastAccessed = f.LastWriteTime
                }).ToList();
            return new Result(files, files.Count, files.Sum(f => f.FileSizeBytes));
        }

        private static HashSet<string> PersonalRoots()
        {
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { profile };
            string? users = Path.GetDirectoryName(profile);
            if (users != null)
                roots.Add(users);
            string systemDrive = Path.GetPathRoot(Environment.SystemDirectory) ?? "";
            try
            {
                foreach (DriveInfo drive in DriveInfo.GetDrives())
                    if (drive.DriveType == DriveType.Fixed && drive.IsReady && !drive.Name.Equals(systemDrive, StringComparison.OrdinalIgnoreCase))
                        roots.Add(drive.RootDirectory.FullName);
            }
            catch { }
            if (users != null)
                roots.Remove(profile);
            return roots;
        }

        private static bool IsInstalledProgram(List<FileSystemInfo> entries)
        {
            bool hasProgram = false, hasLibrary = false;
            foreach (FileSystemInfo entry in entries)
            {
                if (entry is not FileInfo)
                    continue;
                hasProgram |= entry.Extension.Equals(".exe", StringComparison.OrdinalIgnoreCase);
                hasLibrary |= entry.Extension.Equals(".dll", StringComparison.OrdinalIgnoreCase);
            }
            return hasProgram && hasLibrary;
        }
    }
}
