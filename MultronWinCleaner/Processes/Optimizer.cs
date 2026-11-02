using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MultronWinCleaner.Processes
{
    public static class Optimizer
    {
        private static string BackupPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "optimization_backup.json");
        private static string StoppedServicesPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "optimization_stopped.txt");
        private static string LogPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "optimization_log.txt");
        private static readonly object LogLock = new object();

        public static void AppendLog(IEnumerable<string> lines)
        {
            lock (LogLock)
            {
                try
                {
                    string stamp = DateTime.Now.ToString("g");
                    var all = (File.Exists(LogPath) ? File.ReadAllLines(LogPath).ToList() : new List<string>());
                    all.AddRange(lines.Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => stamp + "  " + l));
                    File.WriteAllLines(LogPath, all.Skip(Math.Max(0, all.Count - 200)));
                }
                catch (Exception) { }
            }
        }

        public static List<string> ReadLog()
        {
            lock (LogLock)
            {
                try
                {
                    if (File.Exists(LogPath))
                        return File.ReadAllLines(LogPath).Reverse().ToList();
                }
                catch (Exception) { }
                return new List<string>();
            }
        }

        public static void ClearLog()
        {
            lock (LogLock)
            {
                try
                {
                    if (File.Exists(LogPath))
                        File.Delete(LogPath);
                }
                catch (Exception) { }
            }
        }

        public static void FlushDns() => RunTool("ipconfig.exe", "/flushdns");

        private const string UltimatePerformance = "e9a42b02-d5df-448d-aa00-03f14749eb61";
        private const string HighPerformance = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

        public sealed class RegistryBackup
        {
            public string Hive { get; set; } = "";
            public string Key { get; set; } = "";
            public string Name { get; set; } = "";
            public bool Existed { get; set; }
            public string Kind { get; set; } = "";
            public string Value { get; set; } = "";
        }

        public sealed class Backup
        {
            public List<RegistryBackup> Registry { get; set; } = new List<RegistryBackup>();
            public string? PreviousPowerScheme { get; set; }
            public string? CreatedPowerScheme { get; set; }
            public string? PreviousAutoTuning { get; set; }
            public Dictionary<string, int>? PreviousVisualEffects { get; set; }
        }

        private static string? TcpAutoTuningLevel()
        {
            string output = RunTool(@"WindowsPowerShell\v1.0\powershell.exe",
                "-NoProfile -NonInteractive -Command \"(Get-NetTCPSetting -SettingName Internet).AutoTuningLevelLocal\"").Trim();
            return Regex.IsMatch(output, "^[A-Za-z]+$") ? output : null;
        }

        private static void SetTcpAutoTuning(string level) =>
            RunTool("netsh.exe", "interface tcp set global autotuninglevel=" + level.ToLowerInvariant());

        private static bool FixTcpAutoTuning(Backup backup)
        {
            string? level = TcpAutoTuningLevel();
            if (level == null || level.Equals("Normal", StringComparison.OrdinalIgnoreCase))
                return false;
            backup.PreviousAutoTuning ??= level;
            SetTcpAutoTuning("normal");
            return true;
        }

        private static ServiceItem Item(string id, string title, string description, bool selected) =>
            new ServiceItem { ServiceName = id, DisplayName = Loc.T(title), Description = Loc.T(description), IsSelected = selected };

        public static ObservableCollection<ServiceItem> CreateNewSystemTweaks() => new ObservableCollection<ServiceItem>
        {
            Item("powerplan", "Ultimate Performance power plan", "Switches to Ultimate Performance (High performance when it is not available). Your current plan comes back on Undo.", true),
            Item("gamemode", "Game Mode", "Turns on Windows Game Mode so games get priority over background work.", true),
            Item("gamedvr", "Turn off background game recording", "Turns off Game DVR and background capture, which record your games all the time.", true),
            Item("multimedia", "Prioritize games and multimedia", "Gives games a higher CPU and GPU priority, lowers the time reserved for background tasks and turns off network throttling for media.", true),
            Item("startupdelay", "Remove the startup app delay", "Starts startup apps right after sign-in instead of waiting about 10 seconds.", true),
            Item("menudelay", "Faster menus", "Opens menus after 100 ms instead of 400 ms.", true),
            Item("gpuscheduling", "Hardware-accelerated GPU scheduling", "Lets a supported graphics card manage its own memory. Takes effect after a restart and is ignored by graphics cards that do not support it.", false),
            Item("powerthrottling", "Turn off power throttling", "Stops Windows from slowing down background apps to save power. Uses more battery on laptops.", false),
            Item("mouseaccel", "Turn off mouse acceleration", "Turns off Enhance pointer precision for 1:1 mouse movement in games. Takes effect after signing out.", false),
            Item("transparency", "Turn off transparency effects", "Saves some GPU time on the taskbar, Start menu and windows.", false),
            Item("visualeffects", "Visual effects for best performance", "Like Windows' Adjust for best performance: turns off animations, fades and shadows. Window contents while dragging, smooth screen fonts, thumbnails, Peek, sliding combo boxes and smooth scrolling stay on. Your previous settings come back on Undo.", true),
            Item("flushdns", "Flush the DNS cache", "Clears old cached DNS entries when you apply, and again every time Multron Win Cleaner starts while this optimization is on. Windows fills the cache again by itself, so nothing has to be restored on Undo.", true),
            Item("memory", "Automatic memory cleaning", "Turns on automatic cleaning in Memory Cleaner with its current settings and cleans memory once now. It stays on after restarting the app. Undo turns it off again if it was off before.", true),
            Item("tcpautotuning", "Restore TCP auto-tuning", "When TCP receive window auto-tuning is turned off, downloads get slower. This sets it back to normal only when it was changed; your previous setting comes back on Undo.", true),
            Item("networkreset", "Repair the network connection", "Resets Winsock and TCP/IP to the Windows defaults. Use it only when the internet does not work at all. Needs a restart and cannot be undone.", false),
        };

        private static RegistryKey Root(string hive) => hive == "HKLM" ? Registry.LocalMachine : Registry.CurrentUser;

        private static void SetValue(Backup backup, string hive, string key, string name, object value, RegistryValueKind kind)
        {
            using RegistryKey regKey = Root(hive).CreateSubKey(key, true);
            if (!backup.Registry.Any(b => b.Hive == hive && b.Key.Equals(key, StringComparison.OrdinalIgnoreCase) && b.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                object? old = regKey.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                backup.Registry.Add(new RegistryBackup
                {
                    Hive = hive,
                    Key = key,
                    Name = name,
                    Existed = old != null,
                    Kind = old != null ? regKey.GetValueKind(name).ToString() : "",
                    Value = old switch
                    {
                        null => "",
                        byte[] bytes => Convert.ToBase64String(bytes),
                        string[] lines => string.Join("\n", lines),
                        _ => Convert.ToString(old, System.Globalization.CultureInfo.InvariantCulture) ?? ""
                    }
                });
            }
            regKey.SetValue(name, value, kind);
        }

        private static void Restore(RegistryBackup item)
        {
            using RegistryKey? regKey = Root(item.Hive).OpenSubKey(item.Key, true);
            if (regKey == null)
                return;
            if (!item.Existed)
            {
                regKey.DeleteValue(item.Name, false);
                return;
            }

            var kind = Enum.Parse<RegistryValueKind>(item.Kind);
            object value = kind switch
            {
                RegistryValueKind.DWord => int.Parse(item.Value, System.Globalization.CultureInfo.InvariantCulture),
                RegistryValueKind.QWord => long.Parse(item.Value, System.Globalization.CultureInfo.InvariantCulture),
                RegistryValueKind.Binary => Convert.FromBase64String(item.Value),
                RegistryValueKind.MultiString => item.Value.Split('\n'),
                _ => item.Value
            };
            regKey.SetValue(item.Name, value, kind);
        }

        private static string RunTool(string fileName, string arguments)
        {
            var info = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, fileName),
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using Process process = Process.Start(info) ?? throw new InvalidOperationException(fileName + " could not be started.");
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(30000);
            return output;
        }

        private static string? FindGuid(string text)
        {
            var match = Regex.Match(text ?? "", @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
            return match.Success ? match.Value : null;
        }

        private static Backup LoadBackup()
        {
            try
            {
                if (File.Exists(BackupPath))
                    return JsonSerializer.Deserialize<Backup>(File.ReadAllText(BackupPath)) ?? new Backup();
            }
            catch (Exception) { }
            return new Backup();
        }

        private static void SaveBackup(Backup backup) =>
            File.WriteAllText(BackupPath, JsonSerializer.Serialize(backup, new JsonSerializerOptions { WriteIndented = true }));

        private const string MultimediaProfile = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";

        private static readonly Dictionary<string, (string Hive, string Key, string Name, object Value, RegistryValueKind Kind)[]> RegistryTweaks = new()
        {
            ["gamemode"] = new (string, string, string, object, RegistryValueKind)[]
            {
                ("HKCU", @"Software\Microsoft\GameBar", "AutoGameModeEnabled", 1, RegistryValueKind.DWord),
                ("HKCU", @"Software\Microsoft\GameBar", "AllowAutoGameMode", 1, RegistryValueKind.DWord)
            },
            ["gamedvr"] = new (string, string, string, object, RegistryValueKind)[]
            {
                ("HKCU", @"System\GameConfigStore", "GameDVR_Enabled", 0, RegistryValueKind.DWord),
                ("HKCU", @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0, RegistryValueKind.DWord)
            },
            ["multimedia"] = new (string, string, string, object, RegistryValueKind)[]
            {
                ("HKLM", MultimediaProfile, "SystemResponsiveness", 10, RegistryValueKind.DWord),
                ("HKLM", MultimediaProfile, "NetworkThrottlingIndex", unchecked((int)0xFFFFFFFF), RegistryValueKind.DWord),
                ("HKLM", MultimediaProfile + @"\Tasks\Games", "GPU Priority", 8, RegistryValueKind.DWord),
                ("HKLM", MultimediaProfile + @"\Tasks\Games", "Priority", 6, RegistryValueKind.DWord),
                ("HKLM", MultimediaProfile + @"\Tasks\Games", "Scheduling Category", "High", RegistryValueKind.String),
                ("HKLM", MultimediaProfile + @"\Tasks\Games", "SFIO Priority", "High", RegistryValueKind.String)
            },
            ["startupdelay"] = new (string, string, string, object, RegistryValueKind)[]
            {
                ("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize", "StartupDelayInMSec", 0, RegistryValueKind.DWord)
            },
            ["menudelay"] = new (string, string, string, object, RegistryValueKind)[]
            {
                ("HKCU", @"Control Panel\Desktop", "MenuShowDelay", "100", RegistryValueKind.String)
            },
            ["gpuscheduling"] = new (string, string, string, object, RegistryValueKind)[]
            {
                ("HKLM", @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 2, RegistryValueKind.DWord)
            },
            ["powerthrottling"] = new (string, string, string, object, RegistryValueKind)[]
            {
                ("HKLM", @"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", "PowerThrottlingOff", 1, RegistryValueKind.DWord)
            },
            ["mouseaccel"] = new (string, string, string, object, RegistryValueKind)[]
            {
                ("HKCU", @"Control Panel\Mouse", "MouseSpeed", "0", RegistryValueKind.String),
                ("HKCU", @"Control Panel\Mouse", "MouseThreshold1", "0", RegistryValueKind.String),
                ("HKCU", @"Control Panel\Mouse", "MouseThreshold2", "0", RegistryValueKind.String)
            },
            ["transparency"] = new (string, string, string, object, RegistryValueKind)[]
            {
                ("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 0, RegistryValueKind.DWord)
            },
            ["visualeffects"] = new (string, string, string, object, RegistryValueKind)[]
            {
                ("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", 3, RegistryValueKind.DWord),
                ("HKCU", ExplorerAdvanced, "TaskbarAnimations", 0, RegistryValueKind.DWord),
                ("HKCU", ExplorerAdvanced, "ListviewAlphaSelect", 0, RegistryValueKind.DWord),
                ("HKCU", ExplorerAdvanced, "ListviewShadow", 0, RegistryValueKind.DWord),
                ("HKCU", ExplorerAdvanced, "IconsOnly", 0, RegistryValueKind.DWord),
                ("HKCU", @"Software\Microsoft\Windows\DWM", "EnableAeroPeek", 1, RegistryValueKind.DWord),
                ("HKCU", @"Software\Microsoft\Windows\DWM", "AlwaysHibernateThumbnails", 0, RegistryValueKind.DWord)
            }
        };

        private const string ExplorerAdvanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

        private static void ApplyVisualEffects(Backup backup)
        {
            backup.PreviousVisualEffects ??= VisualEffects.Read();
            VisualEffects.Write(VisualEffects.Wanted());
        }

        private static bool HasValue(string hive, string key, string name, object value)
        {
            using RegistryKey? regKey = Root(hive).OpenSubKey(key);
            object? current = regKey?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            return current != null && Convert.ToString(current, System.Globalization.CultureInfo.InvariantCulture) == Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string? ActivePowerScheme() => FindGuid(RunTool("powercfg.exe", "/getactivescheme"));

        public static List<string> ApplyNewSystemTweaks(IEnumerable<ServiceItem> tweaks)
        {
            var messages = new List<string>();
            Backup backup = LoadBackup();

            foreach (ServiceItem tweak in tweaks.Where(t => t.IsSelected))
            {
                try
                {
                    if (RegistryTweaks.TryGetValue(tweak.ServiceName, out var values))
                    {
                        foreach (var value in values)
                            SetValue(backup, value.Hive, value.Key, value.Name, value.Value, value.Kind);
                        if (tweak.ServiceName == "visualeffects")
                            ApplyVisualEffects(backup);
                    }
                    else if (tweak.ServiceName == "powerplan")
                    {
                        if (backup.PreviousPowerScheme == null)
                            backup.PreviousPowerScheme = ActivePowerScheme();
                        string? scheme = backup.CreatedPowerScheme ?? FindGuid(RunTool("powercfg.exe", "-duplicatescheme " + UltimatePerformance));
                        if (scheme != null)
                        {
                            backup.CreatedPowerScheme = scheme;
                            RunTool("powercfg.exe", "/setactive " + scheme);
                        }
                        else
                        {
                            RunTool("powercfg.exe", "/setactive " + HighPerformance);
                        }
                    }
                    else if (tweak.ServiceName == "flushdns")
                    {
                        FlushDns();
                    }
                    else if (tweak.ServiceName == "tcpautotuning")
                    {
                        if (!FixTcpAutoTuning(backup))
                        {
                            messages.Add(Loc.F("Nothing to change: {0} (it is already normal)", tweak.DisplayName));
                            continue;
                        }
                    }
                    else if (tweak.ServiceName == "networkreset")
                    {
                        RunTool("netsh.exe", "winsock reset");
                        RunTool("netsh.exe", "int ip reset");
                        messages.Add(Loc.F("Applied: {0}. Restart the PC to finish the repair.", tweak.DisplayName));
                        continue;
                    }
                    else
                    {
                        continue;
                    }
                    messages.Add(Loc.F("Applied: {0}", tweak.DisplayName));
                }
                catch (Exception ex)
                {
                    messages.Add(Loc.F("Could not apply {0}: {1}", tweak.DisplayName, ex.Message));
                }
            }

            SaveBackup(backup);
            return messages;
        }

        public static List<string> ReapplyNewSystemTweaks(IEnumerable<ServiceItem> tweaks)
        {
            var messages = new List<string>();
            Backup backup = LoadBackup();

            foreach (ServiceItem tweak in tweaks.Where(t => t.IsSelected))
            {
                try
                {
                    if (RegistryTweaks.TryGetValue(tweak.ServiceName, out var values))
                    {
                        bool changed = false;
                        foreach (var value in values.Where(v => !HasValue(v.Hive, v.Key, v.Name, v.Value)))
                        {
                            SetValue(backup, value.Hive, value.Key, value.Name, value.Value, value.Kind);
                            changed = true;
                        }
                        if (tweak.ServiceName == "visualeffects" && !VisualEffects.Matches(VisualEffects.Wanted()))
                        {
                            ApplyVisualEffects(backup);
                            changed = true;
                        }
                        if (changed)
                            messages.Add(Loc.F("Applied again at startup: {0} (it had been changed)", tweak.DisplayName));
                    }
                    else if (tweak.ServiceName == "powerplan")
                    {
                        string wanted = backup.CreatedPowerScheme ?? HighPerformance;
                        if (!wanted.Equals(ActivePowerScheme(), StringComparison.OrdinalIgnoreCase))
                        {
                            RunTool("powercfg.exe", "/setactive " + wanted);
                            messages.Add(Loc.F("Applied again at startup: {0} (another power plan had been selected)", tweak.DisplayName));
                        }
                    }
                    else if (tweak.ServiceName == "flushdns")
                    {
                        FlushDns();
                        messages.Add(Loc.T("DNS cache flushed automatically at startup"));
                    }
                    else if (tweak.ServiceName == "tcpautotuning" && FixTcpAutoTuning(backup))
                    {
                        messages.Add(Loc.F("Applied again at startup: {0} (it had been changed)", tweak.DisplayName));
                    }
                }
                catch (Exception ex)
                {
                    messages.Add(Loc.F("Could not apply {0} at startup: {1}", tweak.DisplayName, ex.Message));
                }
            }

            SaveBackup(backup);
            return messages;
        }

        public static List<string> UndoNewSystemTweaks()
        {
            var messages = new List<string>();
            Backup backup = LoadBackup();

            foreach (RegistryBackup item in backup.Registry)
            {
                try
                {
                    Restore(item);
                }
                catch (Exception ex)
                {
                    messages.Add(Loc.F("Could not restore {0}\\{1}: {2}", item.Key, item.Name, ex.Message));
                }
            }

            try
            {
                if (backup.PreviousPowerScheme != null)
                    RunTool("powercfg.exe", "/setactive " + backup.PreviousPowerScheme);
                if (backup.CreatedPowerScheme != null && !backup.CreatedPowerScheme.Equals(backup.PreviousPowerScheme, StringComparison.OrdinalIgnoreCase))
                    RunTool("powercfg.exe", "-delete " + backup.CreatedPowerScheme);
            }
            catch (Exception ex)
            {
                messages.Add(Loc.F("Could not restore the power plan: {0}", ex.Message));
            }

            try
            {
                if (backup.PreviousVisualEffects != null)
                    VisualEffects.Write(backup.PreviousVisualEffects);
            }
            catch (Exception ex)
            {
                messages.Add(Loc.F("Could not restore {0}: {1}", Loc.T("Visual effects for best performance"), ex.Message));
            }

            try
            {
                if (backup.PreviousAutoTuning != null)
                    SetTcpAutoTuning(backup.PreviousAutoTuning);
            }
            catch (Exception ex)
            {
                messages.Add(Loc.F("Could not restore {0}: {1}", Loc.T("Restore TCP auto-tuning"), ex.Message));
            }

            if (messages.Count == 0)
            {
                try { File.Delete(BackupPath); } catch (Exception) { }
            }
            messages.Insert(0, Loc.F(backup.PreviousPowerScheme != null ? "Restored {0} settings and the previous power plan." : "Restored {0} settings.", backup.Registry.Count));
            return messages;
        }

        public static List<string> StopOldSystemServices(IEnumerable<ServiceItem> services)
        {
            var stopped = new List<string>();
            foreach (ServiceItem service in services.Where(s => s.IsSelected))
            {
                try
                {
                    using var sc = new ServiceController(service.ServiceName);
                    if (sc.Status != ServiceControllerStatus.Running || !sc.CanStop)
                        continue;
                    if (sc.DependentServices.Any(d => d.Status != ServiceControllerStatus.Stopped))
                        continue;
                    sc.Stop();
                    sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
                    stopped.Add(service.ServiceName);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Service {service.ServiceName} error: {ex.Message}");
                }
            }

            var previous = File.Exists(StoppedServicesPath) ? File.ReadAllLines(StoppedServicesPath) : Array.Empty<string>();
            File.WriteAllLines(StoppedServicesPath, previous.Concat(stopped).Distinct(StringComparer.OrdinalIgnoreCase));
            return stopped;
        }

        public static List<string> StartStoppedServices()
        {
            var started = new List<string>();
            if (!File.Exists(StoppedServicesPath))
                return started;

            foreach (string name in File.ReadAllLines(StoppedServicesPath).Where(l => l.Trim().Length > 0))
            {
                try
                {
                    using var sc = new ServiceController(name.Trim());
                    if (sc.Status == ServiceControllerStatus.Stopped && sc.StartType != ServiceStartMode.Disabled)
                    {
                        sc.Start();
                        sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(10));
                    }
                    started.Add(name.Trim());
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Service {name} error: {ex.Message}");
                }
            }
            try { File.Delete(StoppedServicesPath); } catch (Exception) { }
            return started;
        }
    }
}
