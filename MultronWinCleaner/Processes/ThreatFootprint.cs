using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using Microsoft.Win32;
using Microsoft.Win32.TaskScheduler;

namespace MultronWinCleaner.Processes
{
    public static class ThreatFootprint
    {
        public enum ItemKind { FileCopy, RegistryValue, RegistryToken, StartupShortcut, ScheduledTask, Service }

        public sealed class Item
        {
            public ItemKind Kind;
            public string Display = "";

            public string? FilePath;

            public RegistryHive RegHive;
            public RegistryView RegView;
            public string? RegSubKey;
            public string? RegValueName;
            public string? RegToken;
            public string? RegDefault;

            public string? TaskPath;

            public string? ServiceName;
        }

        public sealed class Result
        {
            public string Sha256 = "";
            public List<Item> Items = new();
        }

        private static IEnumerable<string> CandidateRoots()
        {
            string?[] roots =
            {
                Path.GetTempPath(),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup),
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) is { Length: > 0 } up ? Path.Combine(up, "Downloads") : null,
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            };
            return roots.Where(r => !string.IsNullOrEmpty(r)).Select(r => r!).Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private const int MaxFilesScanned = 60000;

        public static Result Find(string primaryPath, string? sha256, long size, CancellationToken ct, Action<string>? progress = null)
        {
            var result = new Result();
            string primaryFull;
            try { primaryFull = Path.GetFullPath(primaryPath); }
            catch { return result; }

            try
            {
                if (size <= 0 && File.Exists(primaryFull)) size = new FileInfo(primaryFull).Length;
            }
            catch { }

            string hash = NormalizeHash(sha256);
            if (hash.Length != 64 && File.Exists(primaryFull))
            {
                try { hash = HashFile(primaryFull); } catch { }
            }
            result.Sha256 = hash;

            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { primaryFull };
            string fileName = Path.GetFileName(primaryFull);

            if (hash.Length == 64 && size > 0)
            {
                var roots = new List<string>(CandidateRoots());
                try { string? dir = Path.GetDirectoryName(primaryFull); if (!string.IsNullOrEmpty(dir)) roots.Add(dir); }
                catch { }

                int scanned = 0;
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    foreach (string candidate in EnumerateFilesSafe(root, ct))
                    {
                        if (++scanned > MaxFilesScanned) break;
                        ct.ThrowIfCancellationRequested();

                        if (known.Contains(candidate) || !seen.Add(candidate))
                            continue;
                        long candidateSize;
                        try { candidateSize = new FileInfo(candidate).Length; }
                        catch { continue; }
                        if (candidateSize != size)
                            continue;

                        string candidateHash;
                        try { candidateHash = HashFile(candidate); }
                        catch { continue; }
                        if (!string.Equals(candidateHash, hash, StringComparison.OrdinalIgnoreCase))
                            continue;

                        known.Add(candidate);
                        result.Items.Add(new Item
                        {
                            Kind = ItemKind.FileCopy,
                            Display = candidate,
                            FilePath = candidate
                        });
                        progress?.Invoke(Loc.F("Found another copy: {0}", candidate));
                    }
                    if (scanned > MaxFilesScanned) break;
                }
            }

            try { FindRegistryRun(known, result, ct); } catch (OperationCanceledException) { throw; } catch { }
            try { FindWinlogon(known, result, ct); } catch (OperationCanceledException) { throw; } catch { }
            try { FindAppInitDlls(known, result, ct); } catch (OperationCanceledException) { throw; } catch { }
            try { FindImageFileExecutionOptions(known, result, ct); } catch (OperationCanceledException) { throw; } catch { }
            try { FindServices(known, result, ct); } catch (OperationCanceledException) { throw; } catch { }
            try { FindStartupShortcuts(known, result, ct); } catch (OperationCanceledException) { throw; } catch { }
            try { FindScheduledTasks(known, fileName, result, ct); } catch (OperationCanceledException) { throw; } catch { }

            return result;
        }

        private static readonly (RegistryHive Hive, RegistryView View)[] RunHives =
        {
            (RegistryHive.CurrentUser, RegistryView.Default),
            (RegistryHive.LocalMachine, RegistryView.Registry64),
            (RegistryHive.LocalMachine, RegistryView.Registry32),
        };

        private static readonly string[] RunSubKeys =
        {
            @"Software\Microsoft\Windows\CurrentVersion\Run",
            @"Software\Microsoft\Windows\CurrentVersion\RunOnce",
            @"Software\Microsoft\Windows\CurrentVersion\RunServices",
            @"Software\Microsoft\Windows\CurrentVersion\RunServicesOnce",
            @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer\Run",
        };

        private static void FindRegistryRun(HashSet<string> known, Result result, CancellationToken ct)
        {
            foreach (var (hive, view) in RunHives)
            {
                using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view);
                foreach (string sub in RunSubKeys)
                {
                    ct.ThrowIfCancellationRequested();
                    using RegistryKey? key = baseKey.OpenSubKey(sub);
                    if (key == null) continue;
                    foreach (string valueName in key.GetValueNames())
                    {
                        string command = key.GetValue(valueName)?.ToString() ?? "";
                        if (!CommandReferences(command, known))
                            continue;
                        result.Items.Add(new Item
                        {
                            Kind = ItemKind.RegistryValue,
                            Display = Loc.F("Startup registry: {0}\\{1} -> {2}", HiveName(hive, view), sub, valueName),
                            RegHive = hive,
                            RegView = view,
                            RegSubKey = sub,
                            RegValueName = valueName
                        });
                    }
                }
            }
        }

        private const string WinlogonKey = @"Software\Microsoft\Windows NT\CurrentVersion\Winlogon";

        private static void FindWinlogon(HashSet<string> known, Result result, CancellationToken ct)
        {
            (string Name, string Default)[] values = { ("Userinit", @"C:\Windows\system32\userinit.exe,"), ("Shell", "explorer.exe") };
            foreach (var (hive, view) in new[] { (RegistryHive.LocalMachine, RegistryView.Registry64), (RegistryHive.CurrentUser, RegistryView.Default) })
            {
                ct.ThrowIfCancellationRequested();
                using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view);
                using RegistryKey? key = baseKey.OpenSubKey(WinlogonKey);
                if (key == null) continue;
                foreach (var (name, def) in values)
                {
                    string data = key.GetValue(name)?.ToString() ?? "";
                    string? token = MatchedToken(data, known);
                    if (token == null) continue;
                    result.Items.Add(new Item
                    {
                        Kind = ItemKind.RegistryToken,
                        Display = Loc.F("Winlogon {0}: {1}", name, token),
                        RegHive = hive,
                        RegView = view,
                        RegSubKey = WinlogonKey,
                        RegValueName = name,
                        RegToken = token,
                        RegDefault = def
                    });
                }
            }
        }

        private static readonly string[] WindowsNtKeys =
        {
            @"Software\Microsoft\Windows NT\CurrentVersion\Windows",
            @"Software\Wow6432Node\Microsoft\Windows NT\CurrentVersion\Windows",
        };

        private static void FindAppInitDlls(HashSet<string> known, Result result, CancellationToken ct)
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            foreach (string sub in WindowsNtKeys)
            {
                ct.ThrowIfCancellationRequested();
                using RegistryKey? key = baseKey.OpenSubKey(sub);
                string data = key?.GetValue("AppInit_DLLs")?.ToString() ?? "";
                string? token = MatchedToken(data, known);
                if (token == null) continue;
                result.Items.Add(new Item
                {
                    Kind = ItemKind.RegistryToken,
                    Display = Loc.F("AppInit_DLLs: {0}", token),
                    RegHive = RegistryHive.LocalMachine,
                    RegView = RegistryView.Registry64,
                    RegSubKey = sub,
                    RegValueName = "AppInit_DLLs",
                    RegToken = token,
                    RegDefault = ""
                });
            }
        }

        private const string IfeoKey = @"Software\Microsoft\Windows NT\CurrentVersion\Image File Execution Options";

        private static void FindImageFileExecutionOptions(HashSet<string> known, Result result, CancellationToken ct)
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                using RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using RegistryKey? ifeo = baseKey.OpenSubKey(IfeoKey);
                if (ifeo == null) continue;
                foreach (string name in ifeo.GetSubKeyNames())
                {
                    ct.ThrowIfCancellationRequested();
                    using RegistryKey? sub = ifeo.OpenSubKey(name);
                    string debugger = sub?.GetValue("Debugger")?.ToString() ?? "";
                    if (!CommandReferences(debugger, known)) continue;
                    result.Items.Add(new Item
                    {
                        Kind = ItemKind.RegistryValue,
                        Display = Loc.F("Image File Execution Options debugger for {0}", name),
                        RegHive = RegistryHive.LocalMachine,
                        RegView = view,
                        RegSubKey = IfeoKey + "\\" + name,
                        RegValueName = "Debugger"
                    });
                }
            }
        }

        private const string ServicesKey = @"SYSTEM\CurrentControlSet\Services";

        private static void FindServices(HashSet<string> known, Result result, CancellationToken ct)
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default);
            using RegistryKey? services = baseKey.OpenSubKey(ServicesKey);
            if (services == null) return;
            foreach (string name in services.GetSubKeyNames())
            {
                ct.ThrowIfCancellationRequested();
                using RegistryKey? svc = services.OpenSubKey(name);
                if (svc == null) continue;
                string imagePath = svc.GetValue("ImagePath")?.ToString() ?? "";
                bool match = CommandReferences(StripServicePrefix(imagePath), known);
                if (!match)
                {
                    using RegistryKey? pars = svc.OpenSubKey("Parameters");
                    string serviceDll = pars?.GetValue("ServiceDll")?.ToString() ?? "";
                    match = CommandReferences(serviceDll, known);
                }
                if (!match) continue;
                result.Items.Add(new Item
                {
                    Kind = ItemKind.Service,
                    Display = Loc.F("Service: {0}", name),
                    ServiceName = name
                });
            }
        }

        private static string StripServicePrefix(string path)
        {
            path = path.Trim();
            if (path.StartsWith(@"\??\", StringComparison.Ordinal)) path = path.Substring(4);
            return path;
        }

        private static string? MatchedToken(string data, HashSet<string> known)
        {
            if (string.IsNullOrWhiteSpace(data)) return null;
            foreach (string part in data.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string candidate = part.Trim().Trim('"');
                string expanded;
                try { expanded = Environment.ExpandEnvironmentVariables(candidate); } catch { expanded = candidate; }
                try { if (known.Contains(Path.GetFullPath(expanded))) return part.Trim(); } catch { }
                foreach (string k in known)
                    if (expanded.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0)
                        return part.Trim();
            }
            return null;
        }

        private static void FindStartupShortcuts(HashSet<string> known, Result result, CancellationToken ct)
        {
            string[] folders =
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup),
            };
            foreach (string folder in folders.Where(f => !string.IsNullOrEmpty(f) && Directory.Exists(f)))
            {
                foreach (string lnk in SafeFiles(folder, "*.lnk"))
                {
                    ct.ThrowIfCancellationRequested();
                    string? target = ResolveShortcut(lnk);
                    if (string.IsNullOrEmpty(target))
                        continue;
                    string targetFull;
                    try { targetFull = Path.GetFullPath(target); } catch { continue; }
                    if (!known.Contains(targetFull))
                        continue;
                    result.Items.Add(new Item
                    {
                        Kind = ItemKind.StartupShortcut,
                        Display = Loc.F("Startup shortcut: {0}", lnk),
                        FilePath = lnk
                    });
                }
            }
        }

        private static void FindScheduledTasks(HashSet<string> known, string fileName, Result result, CancellationToken ct)
        {
            using var service = new TaskService();
            foreach (Microsoft.Win32.TaskScheduler.Task task in service.AllTasks)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    foreach (var action in task.Definition.Actions)
                    {
                        if (action is not ExecAction exec || string.IsNullOrEmpty(exec.Path))
                            continue;
                        string candidate = exec.Path.Trim().Trim('"');
                        string candidateFull;
                        try { candidateFull = Path.GetFullPath(Environment.ExpandEnvironmentVariables(candidate)); }
                        catch { continue; }
                        bool match = known.Contains(candidateFull)
                            || CommandReferences(exec.Arguments ?? "", known);
                        if (!match)
                            continue;
                        result.Items.Add(new Item
                        {
                            Kind = ItemKind.ScheduledTask,
                            Display = Loc.F("Scheduled task: {0}", task.Path),
                            TaskPath = task.Path
                        });
                        break;
                    }
                }
                catch { }
            }
        }

        public static bool Remove(Item item, Action<string>? progress = null)
        {
            try
            {
                switch (item.Kind)
                {
                    case ItemKind.FileCopy:
                    case ItemKind.StartupShortcut:
                        if (string.IsNullOrEmpty(item.FilePath) || !File.Exists(item.FilePath))
                            return true;
                        var del = ForceDelete.Delete(item.FilePath, progress);
                        return del.Outcome != ForceDelete.Outcome.Failed;

                    case ItemKind.RegistryValue:
                        using (RegistryKey baseKey = RegistryKey.OpenBaseKey(item.RegHive, item.RegView))
                        using (RegistryKey? key = baseKey.OpenSubKey(item.RegSubKey!, writable: true))
                        {
                            if (key == null) return true;
                            if (key.GetValue(item.RegValueName) != null)
                                key.DeleteValue(item.RegValueName!, throwOnMissingValue: false);
                            progress?.Invoke(Loc.F("Removed startup entry {0}", item.RegValueName));
                            return true;
                        }

                    case ItemKind.RegistryToken:
                        using (RegistryKey baseKey = RegistryKey.OpenBaseKey(item.RegHive, item.RegView))
                        using (RegistryKey? key = baseKey.OpenSubKey(item.RegSubKey!, writable: true))
                        {
                            if (key == null) return true;
                            string data = key.GetValue(item.RegValueName)?.ToString() ?? "";
                            var kept = data
                                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                .Where(p => !string.Equals(p.Trim(), item.RegToken?.Trim(), StringComparison.OrdinalIgnoreCase))
                                .ToList();
                            string rebuilt = string.Join(",", kept);
                            if (rebuilt.Length == 0 && !string.IsNullOrEmpty(item.RegDefault))
                                rebuilt = item.RegDefault!;
                            key.SetValue(item.RegValueName!, rebuilt, RegistryValueKind.String);
                            progress?.Invoke(Loc.F("Cleaned the malicious entry out of {0}", item.RegValueName));
                            return true;
                        }

                    case ItemKind.Service:
                        try
                        {
                            using var sc = new System.ServiceProcess.ServiceController(item.ServiceName!);
                            if (sc.Status != System.ServiceProcess.ServiceControllerStatus.Stopped)
                            {
                                sc.Stop();
                                try { sc.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10)); } catch { }
                            }
                        }
                        catch { }
                        using (RegistryKey hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default))
                        {
                            try { hklm.DeleteSubKeyTree(ServicesKey + "\\" + item.ServiceName, throwOnMissingSubKey: false); }
                            catch { }
                        }
                        progress?.Invoke(Loc.F("Stopped and removed service {0}", item.ServiceName));
                        return true;

                    case ItemKind.ScheduledTask:
                        using (var service = new TaskService())
                        {
                            string folderPath = GetTaskFolder(item.TaskPath!);
                            string name = item.TaskPath!.TrimStart('\\');
                            int slash = name.LastIndexOf('\\');
                            if (slash >= 0) name = name.Substring(slash + 1);
                            TaskFolder folder = service.GetFolder(folderPath);
                            folder.DeleteTask(name, exceptionOnNotExists: false);
                            progress?.Invoke(Loc.F("Removed scheduled task {0}", item.TaskPath));
                            return true;
                        }
                }
            }
            catch (Exception ex)
            {
                progress?.Invoke(Loc.F("Could not remove {0}: {1}", item.Display, ex.Message));
                return false;
            }
            return false;
        }

        private static string GetTaskFolder(string taskPath)
        {
            int slash = taskPath.LastIndexOf('\\');
            if (slash <= 0) return "\\";
            return taskPath.Substring(0, slash);
        }

        private static bool CommandReferences(string command, HashSet<string> known)
        {
            if (string.IsNullOrWhiteSpace(command))
                return false;
            string expanded;
            try { expanded = Environment.ExpandEnvironmentVariables(command); }
            catch { expanded = command; }

            foreach (string path in known)
                if (expanded.IndexOf(path, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

            string? exe = ExtractExecutable(expanded);
            if (!string.IsNullOrEmpty(exe))
            {
                try { if (known.Contains(Path.GetFullPath(exe))) return true; }
                catch { }
            }
            return false;
        }

        private static string? ExtractExecutable(string command)
        {
            command = command.Trim();
            if (command.Length == 0) return null;
            if (command[0] == '"')
            {
                int end = command.IndexOf('"', 1);
                return end > 1 ? command.Substring(1, end - 1) : null;
            }
            int space = command.IndexOf(' ');
            return space < 0 ? command : command.Substring(0, space);
        }

        private static string? ResolveShortcut(string lnkPath)
        {
            object? shell = null;
            object? shortcut = null;
            try
            {
                Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return null;
                shell = Activator.CreateInstance(shellType);
                if (shell == null) return null;
                shortcut = shellType.InvokeMember("CreateShortcut",
                    System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { lnkPath });
                if (shortcut == null) return null;
                object? target = shortcut.GetType().InvokeMember("TargetPath",
                    System.Reflection.BindingFlags.GetProperty, null, shortcut, null);
                return target?.ToString();
            }
            catch
            {
                return null;
            }
            finally
            {
                if (shortcut != null) try { Marshal.ReleaseComObject(shortcut); } catch { }
                if (shell != null) try { Marshal.ReleaseComObject(shell); } catch { }
            }
        }

        private static IEnumerable<string> SafeFiles(string folder, string pattern)
        {
            string[] files;
            try { files = Directory.GetFiles(folder, pattern, SearchOption.TopDirectoryOnly); }
            catch { yield break; }
            foreach (string f in files) yield return f;
        }

        private static IEnumerable<string> EnumerateFilesSafe(string root, CancellationToken ct)
        {
            var stack = new Stack<string>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                string dir = stack.Pop();

                string[] files = Array.Empty<string>();
                try { files = Directory.GetFiles(dir); }
                catch { }
                foreach (string f in files) yield return f;

                string[] subs = Array.Empty<string>();
                try { subs = Directory.GetDirectories(dir); }
                catch { }
                foreach (string sub in subs)
                {
                    try
                    {
                        var attr = new DirectoryInfo(sub).Attributes;
                        if ((attr & FileAttributes.ReparsePoint) != 0)
                            continue;
                    }
                    catch { continue; }
                    stack.Push(sub);
                }
            }
        }

        private static string HashFile(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 20);
            return Convert.ToHexString(SHA256.HashData(stream));
        }

        private static string NormalizeHash(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            var sb = new System.Text.StringBuilder(value.Length);
            foreach (char c in value)
                if (Uri.IsHexDigit(c)) sb.Append(char.ToUpperInvariant(c));
            return sb.ToString();
        }

        private static string HiveName(RegistryHive hive, RegistryView view)
        {
            string h = hive == RegistryHive.CurrentUser ? "HKCU" : "HKLM";
            return view == RegistryView.Registry32 ? h + " (32-bit)" : h;
        }
    }
}
