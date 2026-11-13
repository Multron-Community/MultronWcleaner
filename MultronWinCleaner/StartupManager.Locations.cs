using Microsoft.Win32;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Text;
using System.Text.RegularExpressions;

namespace MultronWinCleaner
{
    public partial class StartupManager
    {
        private const string CurrentVersionKey = @"Software\Microsoft\Windows\CurrentVersion";
        private const string Wow64CurrentVersionKey = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion";
        private const string NtCurrentVersionKey = @"Software\Microsoft\Windows NT\CurrentVersion";
        private const string Wow64NtCurrentVersionKey = @"Software\WOW6432Node\Microsoft\Windows NT\CurrentVersion";
        private const string ControlKey = @"SYSTEM\CurrentControlSet\Control";
        private const string ServicesKey = @"SYSTEM\CurrentControlSet\Services";

        private static readonly Regex ClsidPattern = new(@"\{[0-9A-Fa-f]{8}(-[0-9A-Fa-f]{4}){3}-[0-9A-Fa-f]{12}\}", RegexOptions.Compiled);
        private static readonly ConcurrentDictionary<string, bool> MicrosoftFileCache = new(StringComparer.OrdinalIgnoreCase);

        private static (RegistryKey Root, string Path)[] Hives(string subKey, string? wowSubKey = null)
        {
            var list = new List<(RegistryKey, string)> { (Registry.LocalMachine, subKey), (Registry.CurrentUser, subKey) };
            if (wowSubKey != null)
                list.Add((Registry.LocalMachine, wowSubKey));
            return list.ToArray();
        }

        private static void Safely(Action action)
        {
            try { action(); } catch { }
        }

        private static IEnumerable<string> SubKeyNames(RegistryKey key)
        {
            try { return key.GetSubKeyNames(); } catch { return Array.Empty<string>(); }
        }

        private static string NormalizeSystemPath(string value)
        {
            string text = Environment.ExpandEnvironmentVariables(value ?? "").Trim();
            if (text.StartsWith(@"\??\", StringComparison.Ordinal))
                text = text.Substring(4);
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (text.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase))
                text = Path.Combine(windows, text.Substring(12));
            else if (text.StartsWith(@"System32\", StringComparison.OrdinalIgnoreCase))
                text = Path.Combine(windows, text);
            return text;
        }

        private static bool IsMicrosoftFile(string file) => MicrosoftFileCache.GetOrAdd(file, f =>
        {
            try { return (FileVersionInfo.GetVersionInfo(f).CompanyName ?? "").Contains("Microsoft", StringComparison.OrdinalIgnoreCase); }
            catch { return false; }
        });

        private static bool IsThirdPartyFile(string? file)
        {
            if (string.IsNullOrEmpty(file) || !File.Exists(file)) return false;
            return !file.StartsWith(WindowsFolderPrefix(), StringComparison.OrdinalIgnoreCase) || !IsMicrosoftFile(file);
        }

        private void AddIfThirdParty(ConcurrentBag<StartupApp> apps, string name, string command, string type, AdvancedSource? source = null)
        {
            if (string.IsNullOrWhiteSpace(command)) return;
            string normalized = NormalizeSystemPath(command);
            if (IsThirdPartyFile(ResolveLaunchTarget(normalized)))
                AddReadOnlyEntry(apps, name, normalized, type, source);
        }

        private static IEnumerable<string> ListValue(object? value, params char[] separators)
        {
            if (value is string[] many) return many.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim());
            if (value is string one) return one.Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return Array.Empty<string>();
        }

        private static string? ClsidServer(string clsid, out string title)
        {
            title = clsid;
            foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                foreach (string classes in new[] { @"Software\Classes\CLSID\", @"Software\Classes\WOW6432Node\CLSID\" })
                {
                    using var key = root.OpenSubKey(classes + clsid, false);
                    using var server = key?.OpenSubKey("InprocServer32", false);
                    if (server?.GetValue(null) is not string path || path.Length == 0) continue;
                    if (key!.GetValue(null) is string name && name.Length > 0) title = name;
                    return path;
                }
            }
            return null;
        }

        private void GetExtendedLocations(ConcurrentBag<StartupApp> apps)
        {
            Safely(() => GetRunOnceEx(apps));
            Safely(() => GetWinlogonValues(apps));
            Safely(() => GetLogonScripts(apps));
            Safely(() => GetCommandProcessor(apps));
            Safely(() => GetSessionManager(apps));
            Safely(() => GetExplorerExtensions(apps));
            Safely(() => GetDrivers(apps));
            Safely(() => GetWinsockProviders(apps));
            Safely(() => GetSystemProviders(apps));
            Safely(() => GetImageHijacks(apps));
            Safely(() => GetRemoteDesktopStartup(apps));
            Safely(() => GetWmiSubscriptions(apps));
            Safely(() => AddTurnedOffRecords(apps));
        }

        private void GetRunOnceEx(ConcurrentBag<StartupApp> apps)
        {
            foreach (var (root, path) in Hives(CurrentVersionKey + @"\RunOnceEx", Wow64CurrentVersionKey + @"\RunOnceEx"))
            {
                using var key = root.OpenSubKey(path, false);
                if (key == null) continue;
                foreach (string section in SubKeyNames(key))
                {
                    using var item = key.OpenSubKey(section, false);
                    if (item == null) continue;
                    foreach (string valueName in item.GetValueNames().Where(n => n.Length > 0 && !n.Equals("Flags", StringComparison.OrdinalIgnoreCase)))
                    {
                        string command = (item.GetValue(valueName) as string ?? "").Split('|', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
                        if (command.Length > 0)
                            AddReadOnlyEntry(apps, DisplayName(command, valueName), command, "RunOnceEx", ValueSource(root, $@"{path}\{section}", valueName));
                    }
                }
            }
        }

        private void GetWinlogonValues(ConcurrentBag<StartupApp> apps)
        {
            string[] extraValues = { "Taskman", "AppSetup", "System", "VmApplet", "UIHost", "GinaDLL" };
            foreach (var (root, path) in Hives(NtCurrentVersionKey + @"\Winlogon", Wow64NtCurrentVersionKey + @"\Winlogon"))
            {
                using var key = root.OpenSubKey(path, false);
                if (key == null) continue;
                var names = new List<string>(extraValues);
                if (root == Registry.CurrentUser || path.Contains("WOW6432Node", StringComparison.OrdinalIgnoreCase))
                    names.Add("Userinit");
                if (path.Contains("WOW6432Node", StringComparison.OrdinalIgnoreCase))
                    names.Add("Shell");
                foreach (string valueName in names)
                    foreach (string part in ListValue(key.GetValue(valueName), ','))
                        AddIfThirdParty(apps, DisplayName(part, valueName), part, "Winlogon", ListSource(root, path, valueName, part));

                foreach (string group in new[] { "Notify", "GPExtensions" })
                {
                    using var parent = key.OpenSubKey(group, false);
                    if (parent == null) continue;
                    foreach (string name in SubKeyNames(parent))
                    {
                        using var item = parent.OpenSubKey(name, false);
                        string dll = item?.GetValue("DLLName") as string ?? item?.GetValue("DllName") as string ?? "";
                        AddIfThirdParty(apps, item?.GetValue(null) as string is { Length: > 0 } title ? title : name, dll, "Winlogon", TreeSource(root, $@"{path}\{group}\{name}"));
                    }
                }
            }

            foreach (var (root, path) in Hives(CurrentVersionKey + @"\Policies\System"))
            {
                using var key = root.OpenSubKey(path, false);
                if (key?.GetValue("Shell") is string shell && shell.Length > 0)
                    AddReadOnlyEntry(apps, DisplayName(shell, "Shell"), shell, "Winlogon", ValueSource(root, path, "Shell"));
            }

            foreach (string valueName in new[] { "Load", "Run" })
            {
                using var key = Registry.LocalMachine.OpenSubKey(NtCurrentVersionKey + @"\Windows", false);
                foreach (string part in ListValue(key?.GetValue(valueName), ',', ' '))
                    AddReadOnlyEntry(apps, DisplayName(part, valueName), part, "Windows Load", ListSource(Registry.LocalMachine, NtCurrentVersionKey + @"\Windows", valueName, part, " "));
            }
        }

        private void GetLogonScripts(ConcurrentBag<StartupApp> apps)
        {
            using (var environment = Registry.CurrentUser.OpenSubKey("Environment", false))
            {
                if (environment?.GetValue("UserInitMprLogonScript") is string script && script.Length > 0)
                    AddReadOnlyEntry(apps, DisplayName(script, "Logon script"), script, "Logon Script", ValueSource(Registry.CurrentUser, "Environment", "UserInitMprLogonScript"));
            }

            string[] bases = { @"Software\Policies\Microsoft\Windows\System\Scripts\", CurrentVersionKey + @"\Group Policy\Scripts\" };
            foreach (string basePath in bases)
            {
                foreach (string phase in new[] { "Logon", "Startup" })
                {
                    foreach (var (root, path) in Hives(basePath + phase))
                    {
                        using var key = root.OpenSubKey(path, false);
                        if (key == null) continue;
                        foreach (string policy in SubKeyNames(key))
                        {
                            using var policyKey = key.OpenSubKey(policy, false);
                            if (policyKey == null) continue;
                            foreach (string index in SubKeyNames(policyKey))
                            {
                                using var item = policyKey.OpenSubKey(index, false);
                                string script = item?.GetValue("Script") as string ?? "";
                                if (script.Length == 0) continue;
                                string arguments = item?.GetValue("Parameters") as string ?? "";
                                AddReadOnlyEntry(apps, Path.GetFileNameWithoutExtension(script), $"\"{script}\" {arguments}".Trim(), "Logon Script", TreeSource(root, $@"{path}\{policy}\{index}"));
                            }
                        }
                    }
                }
            }
        }

        private void GetCommandProcessor(ConcurrentBag<StartupApp> apps)
        {
            foreach (var (root, path) in Hives(@"Software\Microsoft\Command Processor", @"Software\WOW6432Node\Microsoft\Command Processor"))
            {
                using var key = root.OpenSubKey(path, false);
                if (key?.GetValue("AutoRun") is string command && command.Length > 0)
                    AddReadOnlyEntry(apps, DisplayName(command, "AutoRun"), command, "Command Processor", ValueSource(root, path, "AutoRun"));
            }
        }

        private void GetSessionManager(ConcurrentBag<StartupApp> apps)
        {
            using (var key = Registry.LocalMachine.OpenSubKey(ControlKey + @"\Session Manager", false))
            {
                foreach (string valueName in new[] { "SetupExecute", "Execute", "S0InitialCommand" })
                    foreach (string entry in ListValue(key?.GetValue(valueName), '\0'))
                        AddReadOnlyEntry(apps, entry.Split(' ')[0], entry, "Boot Execute", ListSource(Registry.LocalMachine, ControlKey + @"\Session Manager", valueName, entry, "\0"));
            }

            using (var key = Registry.LocalMachine.OpenSubKey(ControlKey + @"\Session Manager\AppCertDlls", false))
            {
                if (key != null)
                    foreach (string valueName in key.GetValueNames())
                        if (key.GetValue(valueName) is string dll && dll.Length > 0)
                            AddReadOnlyEntry(apps, valueName, dll, "AppInit DLL", ValueSource(Registry.LocalMachine, ControlKey + @"\Session Manager\AppCertDlls", valueName));
            }

            using (var key = Registry.LocalMachine.OpenSubKey(ControlKey + @"\SafeBoot", false))
            {
                if (key?.GetValue("AlternateShell") is string shell && shell.Length > 0 && !shell.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase))
                    AddReadOnlyEntry(apps, DisplayName(shell, "AlternateShell"), shell, "Safe Mode Shell", ValueSource(Registry.LocalMachine, ControlKey + @"\SafeBoot", "AlternateShell", "cmd.exe"));
            }
        }

        private void AddClsid(ConcurrentBag<StartupApp> apps, string text, string fallback, AdvancedSource source)
        {
            var match = ClsidPattern.Match(text ?? "");
            if (!match.Success) return;
            string? server = ClsidServer(match.Value, out string title);
            if (server != null)
                AddIfThirdParty(apps, title == match.Value ? fallback : title, server, "Explorer Extension", source);
        }

        private void GetExplorerExtensions(ConcurrentBag<StartupApp> apps)
        {
            foreach (string baseKey in new[] { CurrentVersionKey, Wow64CurrentVersionKey })
            {
                foreach (var (root, path) in Hives(baseKey + @"\ShellServiceObjectDelayLoad"))
                {
                    using var key = root.OpenSubKey(path, false);
                    if (key != null)
                        foreach (string valueName in key.GetValueNames())
                            AddClsid(apps, key.GetValue(valueName) as string ?? "", valueName, ValueSource(root, path, valueName));
                }

                foreach (string list in new[] { @"\Explorer\SharedTaskScheduler", @"\Explorer\ShellExecuteHooks" })
                {
                    foreach (var (root, path) in Hives(baseKey + list))
                    {
                        using var key = root.OpenSubKey(path, false);
                        if (key != null)
                            foreach (string valueName in key.GetValueNames())
                                AddClsid(apps, valueName, valueName, ValueSource(root, path, valueName));
                    }
                }

                foreach (string list in new[] { @"\Explorer\Browser Helper Objects", @"\Explorer\ShellServiceObjects", @"\Explorer\ShellIconOverlayIdentifiers" })
                {
                    foreach (var (root, path) in Hives(baseKey + list))
                    {
                        using var key = root.OpenSubKey(path, false);
                        if (key == null) continue;
                        foreach (string name in SubKeyNames(key))
                        {
                            using var item = key.OpenSubKey(name, false);
                            AddClsid(apps, ClsidPattern.IsMatch(name) ? name : item?.GetValue(null) as string ?? "", name, TreeSource(root, $@"{path}\{name}"));
                        }
                    }
                }
            }
        }

        private void GetDrivers(ConcurrentBag<StartupApp> apps)
        {
            using var services = Registry.LocalMachine.OpenSubKey(ServicesKey, false);
            if (services == null) return;
            foreach (string name in SubKeyNames(services))
            {
                using var service = services.OpenSubKey(name, false);
                if (service?.GetValue("Start") is not int start || start > 2) continue;
                if (service.GetValue("Type") is not int type || (type & 0x0B) == 0) continue;
                if (service.GetValue("Owners") is string[] { Length: > 0 } owners
                    && !owners.Any(o => o.StartsWith("oem", StringComparison.OrdinalIgnoreCase))) continue;
                string image = service.GetValue("ImagePath") as string ?? "";
                if (image.Length == 0) image = $@"System32\drivers\{name}.sys";
                string display = service.GetValue("DisplayName") as string ?? name;
                AddIfThirdParty(apps, display.StartsWith("@", StringComparison.Ordinal) ? name : display, image, "Driver", DwordSource(Registry.LocalMachine, $@"{ServicesKey}\{name}", "Start", 4));
            }
        }

        private void GetWinsockProviders(ConcurrentBag<StartupApp> apps)
        {
            const string winsock = ServicesKey + @"\WinSock2\Parameters\";
            foreach (string entries in new[] { "Catalog_Entries", "Catalog_Entries64" })
            {
                using (var key = Registry.LocalMachine.OpenSubKey(winsock + @"NameSpace_Catalog5\" + entries, false))
                {
                    if (key != null)
                    {
                        foreach (string id in SubKeyNames(key))
                        {
                            using var item = key.OpenSubKey(id, false);
                            if (item?.GetValue("Enabled") is int nameSpaceEnabled && nameSpaceEnabled == 0) continue;
                            string library = item?.GetValue("LibraryPath") as string ?? "";
                            AddIfThirdParty(apps, item?.GetValue("DisplayString") as string ?? Path.GetFileName(library), library, "Winsock Provider", DwordSource(Registry.LocalMachine, $@"{winsock}NameSpace_Catalog5\{entries}\{id}", "Enabled", 0));
                        }
                    }
                }

                using (var key = Registry.LocalMachine.OpenSubKey(winsock + @"Protocol_Catalog9\" + entries, false))
                {
                    if (key == null) continue;
                    foreach (string id in SubKeyNames(key))
                    {
                        using var item = key.OpenSubKey(id, false);
                        if (item?.GetValue("PackedCatalogItem") is not byte[] data) continue;
                        int end = Array.IndexOf(data, (byte)0);
                        string library = Encoding.Default.GetString(data, 0, end < 0 ? Math.Min(data.Length, 260) : end);
                        AddIfThirdParty(apps, Path.GetFileName(library), library, "Winsock Provider");
                    }
                }
            }
        }

        private void GetSystemProviders(ConcurrentBag<StartupApp> apps)
        {
            using (var monitors = Registry.LocalMachine.OpenSubKey(ControlKey + @"\Print\Monitors", false))
            {
                if (monitors != null)
                {
                    foreach (string name in SubKeyNames(monitors))
                    {
                        using var item = monitors.OpenSubKey(name, false);
                        AddIfThirdParty(apps, name, item?.GetValue("Driver") as string ?? "", "Print Monitor", TreeSource(Registry.LocalMachine, $@"{ControlKey}\Print\Monitors\{name}"));
                    }
                }
            }

            void AddLsa(string subKey, string valueName)
            {
                using var key = Registry.LocalMachine.OpenSubKey(subKey, false);
                foreach (string package in ListValue(key?.GetValue(valueName), ',', ' '))
                    if (!package.Equals("\"\"", StringComparison.Ordinal))
                        AddIfThirdParty(apps, package, Path.HasExtension(package) ? package : package + ".dll", "LSA Provider", ListSource(Registry.LocalMachine, subKey, valueName, package));
            }

            AddLsa(ControlKey + @"\Lsa", "Authentication Packages");
            AddLsa(ControlKey + @"\Lsa", "Notification Packages");
            AddLsa(ControlKey + @"\Lsa", "Security Packages");
            AddLsa(ControlKey + @"\Lsa\OSConfig", "Security Packages");
            AddLsa(ControlKey + @"\SecurityProviders", "SecurityProviders");

            using (var order = Registry.LocalMachine.OpenSubKey(ControlKey + @"\NetworkProvider\Order", false))
            {
                foreach (string provider in ListValue(order?.GetValue("ProviderOrder"), ','))
                {
                    using var item = Registry.LocalMachine.OpenSubKey($@"{ServicesKey}\{provider}\NetworkProvider", false);
                    AddIfThirdParty(apps, item?.GetValue("Name") as string ?? provider, item?.GetValue("ProviderPath") as string ?? "", "Network Provider", ListSource(Registry.LocalMachine, ControlKey + @"\NetworkProvider\Order", "ProviderOrder", provider));
                }
            }

            using (var time = Registry.LocalMachine.OpenSubKey(ServicesKey + @"\W32Time\TimeProviders", false))
            {
                if (time != null)
                {
                    foreach (string name in SubKeyNames(time))
                    {
                        using var item = time.OpenSubKey(name, false);
                        if (item?.GetValue("Enabled") is int timeEnabled && timeEnabled == 0) continue;
                        AddIfThirdParty(apps, name, item?.GetValue("DllName") as string ?? "", "Time Provider", DwordSource(Registry.LocalMachine, $@"{ServicesKey}\W32Time\TimeProviders\{name}", "Enabled", 0));
                    }
                }
            }
        }

        private void GetImageHijacks(ConcurrentBag<StartupApp> apps)
        {
            foreach (string baseKey in new[] { @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\", @"SOFTWARE\WOW6432Node\Microsoft\Windows NT\CurrentVersion\" })
            {
                using (var key = Registry.LocalMachine.OpenSubKey(baseKey + "Image File Execution Options", false))
                {
                    if (key != null)
                    {
                        foreach (string image in SubKeyNames(key))
                        {
                            using var item = key.OpenSubKey(image, false);
                            if (item?.GetValue("Debugger") is string debugger && debugger.Length > 0)
                                AddReadOnlyEntry(apps, image, debugger, "Image Hijack", ValueSource(Registry.LocalMachine, $"{baseKey}Image File Execution Options\\{image}", "Debugger"));
                        }
                    }
                }

                using (var key = Registry.LocalMachine.OpenSubKey(baseKey + "SilentProcessExit", false))
                {
                    if (key == null) continue;
                    foreach (string image in SubKeyNames(key))
                    {
                        using var item = key.OpenSubKey(image, false);
                        if (item?.GetValue("MonitorProcess") is string monitor && monitor.Length > 0)
                            AddReadOnlyEntry(apps, image, monitor, "Image Hijack", ValueSource(Registry.LocalMachine, $"{baseKey}SilentProcessExit\\{image}", "MonitorProcess"));
                    }
                }
            }

            foreach (string fileClass in new[] { "exefile", "comfile", "batfile", "cmdfile" })
            {
                foreach (var (root, path) in Hives($@"Software\Classes\{fileClass}\shell\open\command"))
                {
                    using var key = root.OpenSubKey(path, false);
                    if (key?.GetValue(null) is not string command || command.Length == 0) continue;
                    string compact = command.Replace(" ", "", StringComparison.Ordinal);
                    if (compact != "\"%1\"%*" && compact != "%1%*")
                        AddReadOnlyEntry(apps, fileClass, command, "Image Hijack", ValueSource(root, path, "", root == Registry.LocalMachine ? "\"%1\" %*" : null));
                }
            }
        }

        private void GetRemoteDesktopStartup(ConcurrentBag<StartupApp> apps)
        {
            const string terminal = ControlKey + @"\Terminal Server\";
            using (var key = Registry.LocalMachine.OpenSubKey(terminal + @"Wds\rdpwd", false))
            {
                foreach (string program in ListValue(key?.GetValue("StartupPrograms"), ','))
                    AddIfThirdParty(apps, program, Path.HasExtension(program) ? program : program + ".exe", "RDP Startup", ListSource(Registry.LocalMachine, terminal + @"Wds\rdpwd", "StartupPrograms", program));
            }

            using (var key = Registry.LocalMachine.OpenSubKey(terminal + @"WinStations\RDP-Tcp", false))
            {
                if (key?.GetValue("InitialProgram") is string program && program.Length > 0)
                    AddReadOnlyEntry(apps, DisplayName(program, "InitialProgram"), program, "RDP Startup", ValueSource(Registry.LocalMachine, terminal + @"WinStations\RDP-Tcp", "InitialProgram", ""));
            }
        }

        private void GetWmiSubscriptions(ConcurrentBag<StartupApp> apps)
        {
            var scope = new ManagementScope(@"\\.\root\subscription");
            scope.Connect();
            var queries = new[]
            {
                ("SELECT * FROM CommandLineEventConsumer", "CommandLineTemplate"),
                ("SELECT * FROM ActiveScriptEventConsumer", "ScriptFileName"),
            };
            foreach (var (query, property) in queries)
            {
                using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery(query));
                foreach (ManagementBaseObject item in searcher.Get())
                {
                    using (item)
                    {
                        string name = item["Name"] as string ?? property;
                        string command = item[property] as string ?? "";
                        var source = new AdvancedSource { Kind = "Wmi", Path = item["__RELPATH"] as string ?? name };
                        if (!source.IsTurnedOff)
                            AddReadOnlyEntry(apps, name, command.Length > 0 ? command : name, "WMI Event", source);
                    }
                }
            }
        }
    }
}
