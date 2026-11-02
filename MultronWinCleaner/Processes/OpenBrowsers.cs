using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace MultronWinCleaner.Processes
{
    public sealed class BrowserTag
    {
        public string Name { get; init; } = "";
        public string[] Processes { get; init; } = Array.Empty<string>();
        public bool IsBrowser { get; init; }
        public string ProfileRoot { get; set; } = "";
        public List<string> ProfileFolders { get; set; } = new List<string>();
        public List<string> ReferencePaths { get; } = new List<string>();

        public string Key => Name + "|" + ProfileRoot;

        public static BrowserTag? FromHeader(string line)
        {
            string[] parts = line.TrimStart('{').TrimStart('=').Split('=');
            if (parts.Length < 2)
                return null;
            string kind = parts[1].Trim().ToLowerInvariant();
            if (kind != "browser" && kind != "app")
                return null;
            string[] processes = parts.Length > 2
                ? parts[2].Split(',').Select(p => p.Trim()).Where(p => p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)).ToArray()
                : Array.Empty<string>();
            return new BrowserTag { Name = parts[0].Trim(), Processes = processes, IsBrowser = kind == "browser" };
        }
    }

    public static class OpenBrowsers
    {
        public enum Choice { Ask, Close, Skip }

        private const string ChoiceSettingKey = "browserclose";
        private static readonly string[] LockFileNames = { "lockfile", "parent.lock" };
        private static string SettingsPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Settings.txt");

        public static Choice SavedChoice
        {
            get
            {
                try
                {
                    string? value = File.Exists(SettingsPath)
                        ? File.ReadLines(SettingsPath).Where(l => l.StartsWith(ChoiceSettingKey + ":", StringComparison.OrdinalIgnoreCase))
                            .Select(l => l.Substring(ChoiceSettingKey.Length + 1).Trim()).LastOrDefault()
                        : null;
                    return value == "close" ? Choice.Close : value == "skip" ? Choice.Skip : Choice.Ask;
                }
                catch (Exception)
                {
                    return Choice.Ask;
                }
            }
            set
            {
                try
                {
                    var lines = File.Exists(SettingsPath) ? File.ReadAllLines(SettingsPath).ToList() : new List<string>();
                    lines.RemoveAll(l => l.StartsWith(ChoiceSettingKey + ":", StringComparison.OrdinalIgnoreCase));
                    if (value != Choice.Ask)
                        lines.Add(ChoiceSettingKey + ":" + (value == Choice.Close ? "close" : "skip"));
                    File.WriteAllLines(SettingsPath, lines);
                }
                catch (Exception) { }
            }
        }

        private static List<string> LockFiles(BrowserTag tag)
        {
            var files = new List<string>();
            if (string.IsNullOrEmpty(tag.ProfileRoot))
                return files;
            var folders = new List<string> { tag.ProfileRoot };
            folders.AddRange(tag.ProfileFolders.Select(f => Path.Combine(tag.ProfileRoot, f)));
            foreach (string folder in folders)
                foreach (string name in LockFileNames)
                {
                    string file = Path.Combine(folder, name);
                    if (File.Exists(file))
                        files.Add(file);
                }
            return files;
        }

        private static string? ExePath(Process process)
        {
            try { return process.MainModule?.FileName; }
            catch (Exception) { return null; }
        }

        private static bool InUse(string file)
        {
            try
            {
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None);
                return false;
            }
            catch (IOException ex) when ((ex.HResult & 0xFFFF) is 32 or 33)
            {
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static readonly HashSet<string> GenericFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "users", "appdata", "local", "roaming", "locallow", "programs", "program files", "program files (x86)", "programdata",
            "application", "app", "bin", "user data", "profiles", "packages", "localcache", "windowsapps", "cache", "temp"
        };

        private static List<string> NameTokens(string path)
        {
            string[] parts = path.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
            int start = Array.FindIndex(parts, p => p.Equals("AppData", StringComparison.OrdinalIgnoreCase));
            start = start >= 0 ? start + 2 : 1;
            return parts.Skip(start)
                .Where(p => p.Length >= 3 && !GenericFolders.Contains(p) && !p.Equals(Environment.UserName, StringComparison.OrdinalIgnoreCase))
                .Take(2)
                .ToList();
        }

        private static int Score(string exePath, IReadOnlyCollection<string> tokens)
        {
            string[] folders = (Path.GetDirectoryName(exePath) ?? "").Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)
                .Where(f => f.Length >= 3 && !GenericFolders.Contains(f) && !f.Equals(Environment.UserName, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            return tokens.Count(t => folders.Any(f => f.Contains(t, StringComparison.OrdinalIgnoreCase) || t.Contains(f, StringComparison.OrdinalIgnoreCase)));
        }

        public static List<Process> Running(BrowserTag tag)
        {
            bool confirmedOpen = false;
            if (!string.IsNullOrEmpty(tag.ProfileRoot) && Directory.Exists(tag.ProfileRoot))
            {
                List<string> lockFiles = LockFiles(tag);
                if (lockFiles.Count > 0)
                {
                    if (!lockFiles.Any(InUse))
                        return new List<Process>();
                    confirmedOpen = true;
                }
                else if (File.Exists(Path.Combine(tag.ProfileRoot, "Local State")))
                {
                    return new List<Process>();
                }
            }

            var byProgram = new Dictionary<string, List<Process>>(StringComparer.OrdinalIgnoreCase);
            foreach (string exe in tag.Processes)
                foreach (Process p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe)))
                {
                    if (p.Id == Environment.ProcessId || ExePath(p) is not string path)
                        continue;
                    if (!byProgram.TryGetValue(path, out var list))
                        byProgram[path] = list = new List<Process>();
                    list.Add(p);
                }
            if (byProgram.Count == 0)
                return new List<Process>();
            if (confirmedOpen && byProgram.Count == 1)
                return byProgram.Values.First();

            var tokens = (string.IsNullOrEmpty(tag.ProfileRoot) ? tag.ReferencePaths : new List<string> { tag.ProfileRoot })
                .SelectMany(NameTokens).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (tokens.Count == 0)
                return new List<Process>();
            var scored = byProgram.Select(kv => (Programs: kv.Value, Score: Score(kv.Key, tokens))).Where(s => s.Score > 0).OrderByDescending(s => s.Score).ToList();
            if (scored.Count == 0 || (scored.Count > 1 && scored[0].Score == scored[1].Score))
                return new List<Process>();
            return scored[0].Programs;
        }

        public static bool Close(IReadOnlyCollection<Process> processes, TimeSpan wait)
        {
            foreach (Process p in processes)
            {
                try
                {
                    if (!p.HasExited && p.MainWindowHandle != IntPtr.Zero)
                        p.CloseMainWindow();
                }
                catch (Exception) { }
            }

            var timer = Stopwatch.StartNew();
            foreach (Process p in processes)
            {
                try
                {
                    TimeSpan left = wait - timer.Elapsed;
                    if (left > TimeSpan.Zero)
                        p.WaitForExit(left);
                }
                catch (Exception) { }
            }

            bool closed = true;
            foreach (Process p in processes)
            {
                try
                {
                    if (p.HasExited)
                        continue;
                    p.Kill(entireProcessTree: true);
                    if (!p.WaitForExit(TimeSpan.FromSeconds(5)))
                        closed = false;
                }
                catch (Exception)
                {
                    closed = false;
                }
            }
            return closed;
        }
    }
}
