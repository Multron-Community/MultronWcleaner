using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace MultronWinCleaner.Processes
{
    public static class ShortcutFixer
    {
        public sealed class BrokenShortcut
        {
            public string ShortcutPath { get; set; } = "";
            public string TargetPath { get; set; } = "";
            public string? RepairTarget { get; set; }
            public string Location { get; set; } = "";
            public string Name => Path.GetFileNameWithoutExtension(ShortcutPath);
            public string Action => RepairTarget != null ? Loc.F("Will be repaired: {0}", RepairTarget) : Loc.T("Will be moved to the Recycle Bin");
        }

        private static readonly EnumerationOptions LinkSearch = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        private static IEnumerable<(string Folder, string Location)> ShortcutFolders()
        {
            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            yield return (Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Desktop");
            yield return (Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), "Public Desktop");
            yield return (Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Start Menu");
            yield return (Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "Start Menu (all users)");
            yield return (Path.Combine(roaming, @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar"), "Taskbar");
            yield return (Environment.GetFolderPath(Environment.SpecialFolder.SendTo), "Send To");
        }

        private static IEnumerable<string> StartMenuRoots()
        {
            yield return Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);
        }

        public static string BackupFolder => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ShortcutBackup");

        private static Dictionary<string, List<string>>? installedPrograms;

        private static Dictionary<string, List<string>> InstalledPrograms()
        {
            var found = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            void Add(string? file)
            {
                if (string.IsNullOrWhiteSpace(file))
                    return;
                file = Environment.ExpandEnvironmentVariables(file.Trim().Trim('"'));
                int comma = file.LastIndexOf(',');
                if (comma > 0 && file.Substring(comma + 1).Trim().TrimStart('-').All(char.IsDigit))
                    file = file.Substring(0, comma).Trim().Trim('"');
                if (!file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !Path.IsPathFullyQualified(file) || !File.Exists(file))
                    return;
                string name = Path.GetFileName(file);
                if (!found.TryGetValue(name, out var list))
                    found[name] = list = new List<string>();
                if (!list.Contains(file, StringComparer.OrdinalIgnoreCase))
                    list.Add(file);
            }

            foreach (var hive in new[] { Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryHive.CurrentUser })
                foreach (var view in new[] { Microsoft.Win32.RegistryView.Registry64, Microsoft.Win32.RegistryView.Registry32 })
                {
                    try
                    {
                        using var root = Microsoft.Win32.RegistryKey.OpenBaseKey(hive, view);
                        using (var appPaths = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths"))
                            if (appPaths != null)
                                foreach (string sub in appPaths.GetSubKeyNames())
                                {
                                    using var key = appPaths.OpenSubKey(sub);
                                    Add(key?.GetValue("") as string);
                                }
                        using (var uninstall = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
                            if (uninstall != null)
                                foreach (string sub in uninstall.GetSubKeyNames())
                                {
                                    using var key = uninstall.OpenSubKey(sub);
                                    Add(key?.GetValue("DisplayIcon") as string);
                                }
                    }
                    catch (Exception) { }
                }
            return found;
        }

        private static string? FindInstalled(string target)
        {
            string name = Path.GetFileName(target);
            if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                return null;
            installedPrograms ??= InstalledPrograms();
            if (!installedPrograms.TryGetValue(name, out var files) || files.Count != 1)
                return null;

            string[] generic = { "bin", "x64", "x86", "application", "app", "program", "programs" };
            string? folder = Path.GetDirectoryName(target);
            while (folder != null && generic.Contains(Path.GetFileName(folder), StringComparer.OrdinalIgnoreCase))
                folder = Path.GetDirectoryName(folder);
            string owner = folder == null ? "" : VersionlessName(Path.GetFileName(folder));
            if (owner.Length < 3)
                return null;
            bool sameProgram = (Path.GetDirectoryName(files[0]) ?? "").Split('\\').Any(f => VersionlessName(f) == owner);
            return sameProgram ? files[0] : null;
        }

        public static List<BrokenShortcut> FindBrokenShortcuts()
        {
            var result = new List<BrokenShortcut>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var shell = new IWshRuntimeLibrary.WshShell();
            installedPrograms = null;

            foreach (var (folder, location) in ShortcutFolders())
            {
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                    continue;

                foreach (string file in Directory.EnumerateFiles(folder, "*.lnk", LinkSearch))
                {
                    if (!seen.Add(file))
                        continue;

                    string target;
                    try
                    {
                        var shortcut = (IWshRuntimeLibrary.IWshShortcut)shell.CreateShortcut(file);
                        target = shortcut.TargetPath;
                    }
                    catch (Exception)
                    {
                        continue;
                    }

                    if (!IsMissing(target))
                        continue;

                    result.Add(new BrokenShortcut
                    {
                        ShortcutPath = file,
                        TargetPath = target,
                        RepairTarget = FindReplacement(target) ?? FindInstalled(Environment.ExpandEnvironmentVariables(target.Trim().Trim('"'))),
                        Location = location
                    });
                }
            }
            return result.OrderBy(r => r.Location).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

        private static bool IsMissing(string target)
        {
            if (string.IsNullOrWhiteSpace(target))
                return false;

            string path = Environment.ExpandEnvironmentVariables(target.Trim().Trim('"'));
            if (path.Contains('%') || path.StartsWith(@"\\") || !Path.IsPathFullyQualified(path))
                return false;

            try
            {
                string? root = Path.GetPathRoot(path);
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root) || Exists(path))
                    return false;

                string x86 = @"\Program Files (x86)\", x64 = @"\Program Files\";
                if (path.Contains(x86, StringComparison.OrdinalIgnoreCase) && Exists(path.Replace(x86, x64, StringComparison.OrdinalIgnoreCase)))
                    return false;
                if (path.Contains(x64, StringComparison.OrdinalIgnoreCase) && Exists(path.Replace(x64, x86, StringComparison.OrdinalIgnoreCase)))
                    return false;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string VersionlessName(string name) => Regex.Replace(name, @"[\d\.\-_ ]+", "").ToLowerInvariant();

        private static string? FindReplacement(string target)
        {
            try
            {
                string path = Environment.ExpandEnvironmentVariables(target.Trim().Trim('"'));
                string? ancestor = Path.GetDirectoryName(path);
                while (!string.IsNullOrEmpty(ancestor) && !Directory.Exists(ancestor))
                    ancestor = Path.GetDirectoryName(ancestor);
                if (string.IsNullOrEmpty(ancestor))
                    return null;

                string[] segments = path.Substring(ancestor.TrimEnd('\\').Length).TrimStart('\\').Split('\\');
                if (segments.Length < 2 || !segments[0].Any(char.IsDigit))
                    return null;

                string missingFolder = VersionlessName(segments[0]);
                string rest = Path.Combine(segments.Skip(1).ToArray());
                var candidates = Directory.EnumerateDirectories(ancestor)
                    .Where(d => VersionlessName(Path.GetFileName(d)) == missingFolder && File.Exists(Path.Combine(d, rest)))
                    .ToList();
                return candidates.Count == 1 ? Path.Combine(candidates[0], rest) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static (int Repaired, int Removed, List<BrokenShortcut> Failed) Fix(IEnumerable<BrokenShortcut> shortcuts)
        {
            int repaired = 0, removed = 0;
            var failed = new List<BrokenShortcut>();
            var shell = new IWshRuntimeLibrary.WshShell();

            foreach (var item in shortcuts)
            {
                try
                {
                    if (!File.Exists(item.ShortcutPath))
                        continue;

                    if (item.RepairTarget != null && File.Exists(item.RepairTarget))
                    {
                        string backup = Path.Combine(BackupFolder, DateTime.Now.ToString("yyyy-MM-dd"), item.Location);
                        Directory.CreateDirectory(backup);
                        File.Copy(item.ShortcutPath, Path.Combine(backup, Path.GetFileName(item.ShortcutPath)), overwrite: true);
                        var shortcut = (IWshRuntimeLibrary.IWshShortcut)shell.CreateShortcut(item.ShortcutPath);
                        string oldFolder = Path.GetDirectoryName(Environment.ExpandEnvironmentVariables(item.TargetPath)) ?? "";
                        string newFolder = Path.GetDirectoryName(item.RepairTarget) ?? "";
                        shortcut.TargetPath = item.RepairTarget;
                        if (!string.IsNullOrEmpty(shortcut.WorkingDirectory) && Environment.ExpandEnvironmentVariables(shortcut.WorkingDirectory).StartsWith(oldFolder, StringComparison.OrdinalIgnoreCase))
                            shortcut.WorkingDirectory = newFolder + Environment.ExpandEnvironmentVariables(shortcut.WorkingDirectory).Substring(oldFolder.Length);
                        if (!string.IsNullOrEmpty(shortcut.IconLocation) && Environment.ExpandEnvironmentVariables(shortcut.IconLocation).StartsWith(oldFolder, StringComparison.OrdinalIgnoreCase))
                            shortcut.IconLocation = newFolder + Environment.ExpandEnvironmentVariables(shortcut.IconLocation).Substring(oldFolder.Length);
                        shortcut.Save();
                        repaired++;
                    }
                    else
                    {
                        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(item.ShortcutPath,
                            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                        removed++;
                        RemoveEmptyStartMenuFolders(Path.GetDirectoryName(item.ShortcutPath));
                    }
                }
                catch (Exception)
                {
                    failed.Add(item);
                }
            }
            return (repaired, removed, failed);
        }

        private static void RemoveEmptyStartMenuFolders(string? folder)
        {
            string? root = StartMenuRoots().FirstOrDefault(r => !string.IsNullOrEmpty(r) && folder != null
                && folder.StartsWith(r.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase));
            if (root == null)
                return;
            string startup = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            string commonStartup = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);
            try
            {
                while (folder != null && folder.Length > root.TrimEnd('\\').Length
                    && !folder.Equals(startup, StringComparison.OrdinalIgnoreCase)
                    && !folder.Equals(commonStartup, StringComparison.OrdinalIgnoreCase)
                    && Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
                {
                    Directory.Delete(folder);
                    folder = Path.GetDirectoryName(folder);
                }
            }
            catch (Exception) { }
        }
    }
}
