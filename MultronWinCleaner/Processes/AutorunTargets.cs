using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;
using Microsoft.Win32.TaskScheduler;

namespace MultronWinCleaner.Processes
{
    public static class AutorunTargets
    {
        private static readonly (RegistryHive Hive, RegistryView View)[] Hives =
        {
            (RegistryHive.CurrentUser, RegistryView.Default),
            (RegistryHive.LocalMachine, RegistryView.Registry64),
            (RegistryHive.LocalMachine, RegistryView.Registry32),
        };

        private static readonly string[] RunKeys =
        {
            @"Software\Microsoft\Windows\CurrentVersion\Run",
            @"Software\Microsoft\Windows\CurrentVersion\RunOnce",
            @"Software\Microsoft\Windows\CurrentVersion\RunServices",
            @"Software\Microsoft\Windows\CurrentVersion\RunServicesOnce",
            @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer\Run",
        };

        private static readonly Regex PathPattern = new(@"[A-Za-z]:\\[^""<>|*?\r\n,]+?\.[A-Za-z0-9]{1,5}(?=$|[\s"",;])", RegexOptions.Compiled);

        public static List<string> Collect(CancellationToken ct)
        {
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void AddCommand(string? command)
            {
                foreach (string path in PathsIn(command))
                    found.Add(path);
            }

            Safe(() => ReadRunKeys(AddCommand, ct));
            Safe(() => ReadRunOnceEx(AddCommand, ct));
            Safe(() => ReadWinlogon(AddCommand));
            Safe(() => ReadAppInit(AddCommand));
            Safe(() => ReadIfeo(AddCommand, ct));
            Safe(() => ReadServices(AddCommand, ct));
            Safe(() => ReadActiveSetup(AddCommand, ct));
            Safe(() => ReadScheduledTasks(AddCommand, ct));
            return found.ToList();
        }

        private const uint PROCESS_QUERY_INFORMATION = 0x0400;
        private const uint PROCESS_VM_READ = 0x0010;
        private const uint LIST_MODULES_ALL = 0x03;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool EnumProcessModulesEx(IntPtr process, [Out] IntPtr[]? modules, int size, out int needed, uint filter);

        [DllImport("psapi.dll", CharSet = CharSet.Unicode)]
        private static extern uint GetModuleFileNameExW(IntPtr process, IntPtr module, StringBuilder fileName, int size);

        private static string WithoutDevicePrefix(string path)
        {
            if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
                return @"\\" + path.Substring(8);
            if (path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\??\", StringComparison.Ordinal))
                return path.Substring(4);
            return path;
        }

        public static List<string> LoadedModules(CancellationToken ct)
        {
            var found = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
            int[] ids = Process.GetProcesses().Select(p =>
            {
                int id = p.Id;
                p.Dispose();
                return id;
            }).ToArray();
            System.Threading.Tasks.Parallel.ForEach(ids,
                new System.Threading.Tasks.ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Environment.ProcessorCount },
                id =>
                {
                    IntPtr handle = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, false, id);
                    if (handle == IntPtr.Zero)
                        return;
                    try
                    {
                        if (!EnumProcessModulesEx(handle, null, 0, out int needed, LIST_MODULES_ALL) || needed <= 0)
                            return;
                        var modules = new IntPtr[needed / IntPtr.Size];
                        if (!EnumProcessModulesEx(handle, modules, modules.Length * IntPtr.Size, out needed, LIST_MODULES_ALL))
                            return;
                        var name = new StringBuilder(1024);
                        int count = Math.Min(modules.Length, needed / IntPtr.Size);
                        for (int i = 0; i < count; i++)
                        {
                            name.Clear();
                            if (GetModuleFileNameExW(handle, modules[i], name, name.Capacity) > 0)
                                found.TryAdd(WithoutDevicePrefix(name.ToString()), 0);
                        }
                    }
                    finally
                    {
                        CloseHandle(handle);
                    }
                });
            return found.Keys.ToList();
        }

        private static void Safe(System.Action action)
        {
            try { action(); }
            catch (OperationCanceledException) { throw; }
            catch { }
        }

        private static IEnumerable<string> PathsIn(string? command)
        {
            if (string.IsNullOrWhiteSpace(command)) yield break;
            string expanded = Environment.ExpandEnvironmentVariables(command.Trim());
            if (expanded.StartsWith(@"\??\", StringComparison.Ordinal)) expanded = expanded.Substring(4);
            if (expanded.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase))
                expanded = Environment.GetFolderPath(Environment.SpecialFolder.Windows) + expanded.Substring(11);
            else if (expanded.StartsWith(@"System32\", StringComparison.OrdinalIgnoreCase))
                expanded = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), expanded);

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in PathPattern.Matches(expanded))
            {
                string candidate = m.Value.Trim().Trim('"');
                string full;
                try { full = Path.GetFullPath(candidate); }
                catch { continue; }
                if (seen.Add(full) && File.Exists(full))
                    yield return full;
            }

            string first = expanded.Trim('"').Split(new[] { ' ', ',' }, 2)[0];
            if (first.Length > 0 && !first.Contains('\\'))
            {
                string? onPath = FindOnSystemPath(first);
                if (onPath != null && seen.Add(onPath))
                    yield return onPath;
            }
        }

        private static string? FindOnSystemPath(string name)
        {
            if (!Path.HasExtension(name)) name += ".exe";
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            foreach (string dir in new[] { Path.Combine(windows, "System32"), windows })
            {
                string path = Path.Combine(dir, name);
                if (File.Exists(path)) return path;
            }
            return null;
        }

        private static void ReadRunKeys(System.Action<string?> add, CancellationToken ct)
        {
            foreach (var (hive, view) in Hives)
            {
                using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view);
                foreach (string sub in RunKeys)
                {
                    ct.ThrowIfCancellationRequested();
                    using RegistryKey? key = baseKey.OpenSubKey(sub);
                    if (key == null) continue;
                    foreach (string name in key.GetValueNames())
                        add(key.GetValue(name)?.ToString());
                }
            }
        }

        private static void ReadRunOnceEx(System.Action<string?> add, CancellationToken ct)
        {
            foreach (var (hive, view) in Hives)
            {
                using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view);
                using RegistryKey? key = baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\RunOnceEx");
                if (key == null) continue;
                foreach (string sub in key.GetSubKeyNames())
                {
                    ct.ThrowIfCancellationRequested();
                    using RegistryKey? entry = key.OpenSubKey(sub);
                    if (entry == null) continue;
                    foreach (string name in entry.GetValueNames())
                        add(entry.GetValue(name)?.ToString());
                }
            }
        }

        private static void ReadWinlogon(System.Action<string?> add)
        {
            foreach (var (hive, view) in Hives)
            {
                using RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view);
                using RegistryKey? key = baseKey.OpenSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\Winlogon");
                if (key == null) continue;
                foreach (string name in new[] { "Shell", "Userinit", "Taskman", "AppSetup" })
                    add(key.GetValue(name)?.ToString());
            }
        }

        private static void ReadAppInit(System.Action<string?> add)
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                using RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using RegistryKey? key = baseKey.OpenSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\Windows");
                string? dlls = key?.GetValue("AppInit_DLLs")?.ToString();
                if (string.IsNullOrWhiteSpace(dlls)) continue;
                foreach (string dll in dlls.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries))
                    add(dll);
            }
        }

        private static void ReadIfeo(System.Action<string?> add, CancellationToken ct)
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                using RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using RegistryKey? key = baseKey.OpenSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\Image File Execution Options");
                if (key == null) continue;
                foreach (string sub in key.GetSubKeyNames())
                {
                    ct.ThrowIfCancellationRequested();
                    using RegistryKey? entry = key.OpenSubKey(sub);
                    add(entry?.GetValue("Debugger")?.ToString());
                    add(entry?.GetValue("VerifierDlls")?.ToString());
                }
            }
        }

        private static void ReadServices(System.Action<string?> add, CancellationToken ct)
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default);
            using RegistryKey? services = baseKey.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
            if (services == null) return;
            foreach (string name in services.GetSubKeyNames())
            {
                ct.ThrowIfCancellationRequested();
                using RegistryKey? svc = services.OpenSubKey(name);
                if (svc == null) continue;
                if (svc.GetValue("Start") is int start && start == 4) continue;
                add(svc.GetValue("ImagePath")?.ToString());
                using RegistryKey? pars = svc.OpenSubKey("Parameters");
                add(pars?.GetValue("ServiceDll")?.ToString());
            }
        }

        private static void ReadActiveSetup(System.Action<string?> add, CancellationToken ct)
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                using RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using RegistryKey? key = baseKey.OpenSubKey(@"Software\Microsoft\Active Setup\Installed Components");
                if (key == null) continue;
                foreach (string sub in key.GetSubKeyNames())
                {
                    ct.ThrowIfCancellationRequested();
                    using RegistryKey? entry = key.OpenSubKey(sub);
                    add(entry?.GetValue("StubPath")?.ToString());
                }
            }
        }

        private static void ReadScheduledTasks(System.Action<string?> add, CancellationToken ct)
        {
            using var service = new TaskService();
            foreach (Microsoft.Win32.TaskScheduler.Task task in service.AllTasks)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    if (!task.Enabled) continue;
                    foreach (var action in task.Definition.Actions)
                    {
                        if (action is ExecAction exec && !string.IsNullOrEmpty(exec.Path))
                        {
                            string path = exec.Path.Trim();
                            add(path.Contains(' ') && !path.StartsWith("\"") ? "\"" + path + "\"" : path);
                            add(exec.Arguments);
                        }
                    }
                }
                catch { }
            }
        }
    }
}
