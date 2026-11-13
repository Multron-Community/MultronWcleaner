using Microsoft.Win32;
using NetFwTypeLib;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.DirectoryServices.AccountManagement;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MultronWinCleaner.Processes
{
    public static class SecurityCheck
    {
        public enum Level { High, Medium, Low }

        public sealed class Issue : INotifyPropertyChanged
        {
            public string Id { get; init; } = "";
            public string Category { get; init; } = "";
            public string Title { get; init; } = "";
            public string Description { get; init; } = "";
            public string Details { get; init; } = "";
            public string FixText { get; init; } = "";
            public Level Severity { get; init; }
            public bool NeedsRestart { get; init; }
            internal Action<Backup>? Apply { get; init; }

            public bool CanFix => Apply != null;
            public string SeverityText => Loc.T(Severity switch { Level.High => "HIGH", Level.Medium => "MEDIUM", _ => "LOW" });
            public string SeverityColor => Severity switch { Level.High => "#DC3545", Level.Medium => "#C2570A", _ => "#1E88E5" };
            public string DetailsText => Details.Length == 0 ? Category : Category + " · " + Details;
            public string ActionText => Loc.T(CanFix ? "Fix: " : "How to fix: ") + FixText + (NeedsRestart ? Loc.T(" (restart needed)") : "");
            public bool HasDetails => Details.Length > 0;

            private bool isSelected;
            public bool IsSelected
            {
                get => isSelected;
                set
                {
                    if (isSelected == value) return;
                    isSelected = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                }
            }

            public event PropertyChangedEventHandler? PropertyChanged;
        }

        public sealed class RegValue
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
            public List<RegValue> Registry { get; set; } = new List<RegValue>();
            public Dictionary<string, string> Services { get; set; } = new Dictionary<string, string>();
            public Dictionary<string, bool> Firewall { get; set; } = new Dictionary<string, bool>();
            public Dictionary<string, bool> Accounts { get; set; } = new Dictionary<string, bool>();
            public string? Nx { get; set; }
            public List<string> Features { get; set; } = new List<string>();
        }

        public sealed class FixResult
        {
            public int Fixed { get; set; }
            public List<string> Failed { get; } = new List<string>();
            public List<string> Log { get; } = new List<string>();
            public bool NeedsRestart { get; set; }
        }

        private const string HKLM = "HKLM";
        private const string HKCU = "HKCU";
        private const string PolicySystem = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";
        private const string PolicyExplorer = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer";
        private const string Lsa = @"SYSTEM\CurrentControlSet\Control\Lsa";
        private const string Winlogon = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon";
        private const string DefenderPolicy = @"SOFTWARE\Policies\Microsoft\Windows Defender";
        private const string TerminalServer = @"SYSTEM\CurrentControlSet\Control\Terminal Server";
        private const string WindowsUpdatePolicy = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate";

        private static string BackupPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "security_backup.json");

        public static bool HasBackup => File.Exists(BackupPath);

        public const string IgnoredSettingKey = "securityignored";
        private static readonly object ignoredLock = new object();
        private static string SettingsPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Settings.txt");

        public static HashSet<string> IgnoredIds()
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                lock (ignoredLock)
                {
                    if (!File.Exists(SettingsPath)) return ids;
                    string? line = File.ReadLines(SettingsPath).LastOrDefault(l => l.StartsWith(IgnoredSettingKey + ":", StringComparison.OrdinalIgnoreCase));
                    if (line != null)
                        foreach (string id in line.Substring(IgnoredSettingKey.Length + 1).Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                            ids.Add(id);
                }
            }
            catch (Exception)
            {
            }
            return ids;
        }

        public static int IgnoredCount => IgnoredIds().Count;

        public static void Ignore(string id)
        {
            var ids = IgnoredIds();
            if (ids.Add(id))
                SaveIgnored(ids);
        }

        public static void ClearIgnored() => SaveIgnored(new HashSet<string>());

        private static void SaveIgnored(HashSet<string> ids)
        {
            lock (ignoredLock)
            {
                var lines = File.Exists(SettingsPath) ? File.ReadAllLines(SettingsPath).ToList() : new List<string>();
                lines.RemoveAll(l => l.StartsWith(IgnoredSettingKey + ":", StringComparison.OrdinalIgnoreCase));
                if (ids.Count > 0)
                    lines.Add(IgnoredSettingKey + ":" + string.Join("|", ids.OrderBy(i => i, StringComparer.OrdinalIgnoreCase)));
                File.WriteAllLines(SettingsPath, lines);
            }
        }

        #region Registry

        private static RegistryKey Root(string hive) =>
            RegistryKey.OpenBaseKey(hive == HKLM ? RegistryHive.LocalMachine : RegistryHive.CurrentUser, RegistryView.Registry64);

        private static object? Get(string hive, string key, string name)
        {
            try
            {
                using var root = Root(hive);
                using var k = root.OpenSubKey(key);
                return k?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static int? GetInt(string hive, string key, string name)
        {
            object? value = Get(hive, key, name);
            return value switch
            {
                int i => i,
                long l => (int)l,
                string s when int.TryParse(s.Trim(), out int parsed) => parsed,
                _ => null
            };
        }

        private static string? GetString(string hive, string key, string name) => Get(hive, key, name)?.ToString();

        private static bool KeyExists(string hive, string key)
        {
            try
            {
                using var root = Root(hive);
                using var k = root.OpenSubKey(key);
                return k != null;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static List<string> ValueNames(string hive, string key)
        {
            try
            {
                using var root = Root(hive);
                using var k = root.OpenSubKey(key);
                return k?.GetValueNames().ToList() ?? new List<string>();
            }
            catch (Exception)
            {
                return new List<string>();
            }
        }

        private static List<string> SubKeyNames(string hive, string key)
        {
            try
            {
                using var root = Root(hive);
                using var k = root.OpenSubKey(key);
                return k?.GetSubKeyNames().ToList() ?? new List<string>();
            }
            catch (Exception)
            {
                return new List<string>();
            }
        }

        private static void Remember(Backup backup, string hive, string key, string name)
        {
            if (backup.Registry.Any(r => r.Hive == hive && r.Key.Equals(key, StringComparison.OrdinalIgnoreCase) && r.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                return;

            var item = new RegValue { Hive = hive, Key = key, Name = name };
            try
            {
                using var root = Root(hive);
                using var k = root.OpenSubKey(key);
                object? value = k?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                if (k != null && value != null)
                {
                    var kind = k.GetValueKind(name);
                    item.Existed = true;
                    item.Kind = kind.ToString();
                    item.Value = kind switch
                    {
                        RegistryValueKind.MultiString => string.Join("\n", (string[])value),
                        RegistryValueKind.Binary => Convert.ToBase64String((byte[])value),
                        _ => Convert.ToString(value) ?? ""
                    };
                }
            }
            catch (Exception)
            {
            }
            backup.Registry.Add(item);
        }

        private static void SetValue(Backup backup, string hive, string key, string name, object value, RegistryValueKind kind)
        {
            Remember(backup, hive, key, name);
            using var root = Root(hive);
            using var k = root.CreateSubKey(key, true);
            k.SetValue(name, value, kind);
        }

        private static void DeleteValue(Backup backup, string hive, string key, string name)
        {
            if (Get(hive, key, name) == null)
                return;
            Remember(backup, hive, key, name);
            using var root = Root(hive);
            using var k = root.OpenSubKey(key, true);
            k?.DeleteValue(name, false);
        }

        private static void SetDword(Backup backup, string hive, string key, string name, int value) =>
            SetValue(backup, hive, key, name, value, RegistryValueKind.DWord);

        private static void Restore(RegValue item)
        {
            using var root = Root(item.Hive);
            if (!item.Existed)
            {
                using var existing = root.OpenSubKey(item.Key, true);
                existing?.DeleteValue(item.Name, false);
                return;
            }

            var kind = Enum.TryParse(item.Kind, out RegistryValueKind parsed) ? parsed : RegistryValueKind.String;
            object value = kind switch
            {
                RegistryValueKind.DWord => int.Parse(item.Value),
                RegistryValueKind.QWord => long.Parse(item.Value),
                RegistryValueKind.MultiString => item.Value.Split('\n'),
                RegistryValueKind.Binary => Convert.FromBase64String(item.Value),
                _ => item.Value
            };
            using var k = root.CreateSubKey(item.Key, true);
            k.SetValue(item.Name, value, kind);
        }

        #endregion

        #region Helpers

        private static (int ExitCode, string Output) RunTool(string fileName, string arguments, int timeoutMs = 120000)
        {
            try
            {
                var info = new ProcessStartInfo(fileName, arguments)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var process = Process.Start(info);
                if (process == null) return (-1, "");
                var output = process.StandardOutput.ReadToEndAsync();
                var error = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(timeoutMs))
                {
                    try { process.Kill(true); } catch (Exception) { }
                    return (-1, Loc.T("timed out"));
                }
                return (process.ExitCode, output.Result + error.Result);
            }
            catch (Exception ex)
            {
                return (-1, ex.Message);
            }
        }

        private static string SystemTool(string name) => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), name);

        private static string ServiceKey(string name) => $@"SYSTEM\CurrentControlSet\Services\{name}";

        private static int? ServiceStart(string name) => GetInt(HKLM, ServiceKey(name), "Start");

        private static string StartMode(string name)
        {
            int? start = ServiceStart(name);
            return start switch
            {
                2 when GetInt(HKLM, ServiceKey(name), "DelayedAutostart") == 1 => "delayed-auto",
                2 => "auto",
                3 => "demand",
                4 => "disabled",
                0 => "boot",
                1 => "system",
                _ => "demand"
            };
        }

        private static void SetServiceMode(Backup backup, string name, string mode)
        {
            if (!backup.Services.ContainsKey(name))
                backup.Services[name] = StartMode(name);

            var (exit, output) = RunTool(SystemTool("sc.exe"), $"config \"{name}\" start= {mode}");
            if (exit != 0)
                throw new InvalidOperationException(LastLine(output, Loc.F("sc.exe failed with code {0}", exit)));

            try
            {
                using var service = new ServiceController(name);
                if (mode == "disabled")
                {
                    if (service.Status != ServiceControllerStatus.Stopped)
                    {
                        service.Stop();
                        service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(15));
                    }
                }
                else if (mode == "auto" || mode == "delayed-auto")
                {
                    if (service.Status == ServiceControllerStatus.Stopped)
                        service.Start();
                }
            }
            catch (Exception)
            {
            }
        }

        private static string LastLine(string text, string fallback)
        {
            string? line = text.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0);
            return string.IsNullOrEmpty(line) ? fallback : line;
        }

        private static void DisableFeature(Backup backup, string feature)
        {
            var (exit, output) = RunTool(SystemTool("dism.exe"), $"/online /disable-feature /featurename:{feature} /norestart /English", 600000);
            if (exit != 0 && exit != 3010)
                throw new InvalidOperationException(LastLine(output, Loc.F("DISM failed with code {0}", exit)));
            if (!backup.Features.Contains(feature))
                backup.Features.Add(feature);
        }

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int index);

        private static bool IsRemoteSession()
        {
            try { return GetSystemMetrics(0x1000) != 0; }
            catch (Exception) { return false; }
        }

        private sealed class Product
        {
            public string Name = "";
            public int State;
            public bool Enabled => ((State >> 12) & 0xF) == 1;
            public bool UpToDate => ((State >> 4) & 0xF) == 0;
            public bool IsMicrosoft => IsMicrosoftName(Name);

            public static bool IsMicrosoftName(string name) => MicrosoftProducts.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase));

            private static readonly string[] MicrosoftProducts =
            {
                "Windows Defender", "Microsoft Defender", "Windows Firewall", "Microsoft Windows Firewall"
            };
        }

        private const string SecurityCenterAvProviders = @"SOFTWARE\Microsoft\Security Center\Provider\Av";

        private static readonly string[] KnownAntivirusServices =
        {
            "ekrn", "klnagent", "AvastSvc", "avast! Antivirus", "AVGSvc", "avgsvc", "VSSERV", "bdredline", "bdservicehost",
            "EPSecurityService", "NortonSecurity", "N360", "mfemms", "McAfeeFramework", "MBAMService", "WRSVC",
            "Sophos MCS Agent", "SAVService", "CSFalconService", "SentinelAgent", "Amsp", "TMBMServer",
            "AntiVirService", "Avira.ServiceHost", "PSANHost", "FSMA", "AVKWCtl", "GDScan", "K7RTScan", "cmdAgent",
            "ZillyaAVCoreSvc", "DrWebAVService", "QHActiveDefense", "ZAPrivacyService"
        };

        private static bool IsKnownAntivirusService(string service) =>
            KnownAntivirusServices.Contains(service, StringComparer.OrdinalIgnoreCase)
            || (service.StartsWith("AVP", StringComparison.OrdinalIgnoreCase) && service.Length > 3 && char.IsDigit(service[3]));

        private static List<string> FindThirdPartyAntivirus(List<Product>? registered)
        {
            var names = new List<string>();
            if (registered != null)
                names.AddRange(registered.Where(p => !p.IsMicrosoft).Select(p => p.Name));

            foreach (string provider in SubKeyNames(HKLM, SecurityCenterAvProviders))
            {
                string? name = GetString(HKLM, SecurityCenterAvProviders + "\\" + provider, "DISPLAYNAME");
                if (!string.IsNullOrWhiteSpace(name) && !Product.IsMicrosoftName(name))
                    names.Add(name);
            }

            foreach (string service in SubKeyNames(HKLM, @"SYSTEM\CurrentControlSet\Services"))
            {
                if (IsKnownAntivirusService(service) && ServiceStart(service) != 4)
                    names.Add(service);
            }

            return names.Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static bool IsServiceRunning(string name)
        {
            try
            {
                using var service = new ServiceController(name);
                return service.Status == ServiceControllerStatus.Running;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static List<Product>? SecurityProducts(string className)
        {
            try
            {
                Type? type = Type.GetTypeFromProgID("WbemScripting.SWbemLocator");
                if (type == null) return null;
                dynamic locator = Activator.CreateInstance(type)!;
                dynamic service = locator.ConnectServer(".", @"root\SecurityCenter2");
                dynamic items = service.ExecQuery("SELECT displayName, productState FROM " + className);
                var list = new List<Product>();
                foreach (dynamic item in items)
                {
                    list.Add(new Product
                    {
                        Name = Convert.ToString(item.displayName) ?? "",
                        State = Convert.ToInt32(item.productState)
                    });
                }
                return list;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static INetFwPolicy2 FirewallPolicy()
        {
            Type type = Type.GetTypeFromProgID("HNetCfg.FwPolicy2")!;
            return (INetFwPolicy2)Activator.CreateInstance(type)!;
        }

        private static readonly (NET_FW_PROFILE_TYPE2_ Profile, string Name, string PolicyKey)[] FirewallProfiles =
        {
            (NET_FW_PROFILE_TYPE2_.NET_FW_PROFILE2_DOMAIN, "Domain", "DomainProfile"),
            (NET_FW_PROFILE_TYPE2_.NET_FW_PROFILE2_PRIVATE, "Private", "PrivateProfile"),
            (NET_FW_PROFILE_TYPE2_.NET_FW_PROFILE2_PUBLIC, "Public", "PublicProfile")
        };

        private static UserPrincipal? FindBuiltInAccount(PrincipalContext context, string rid)
        {
            using var searcher = new PrincipalSearcher(new UserPrincipal(context));
            foreach (Principal principal in searcher.FindAll())
            {
                if (principal is UserPrincipal user && user.Sid != null && user.Sid.Value.EndsWith("-" + rid, StringComparison.Ordinal))
                    return user;
                principal.Dispose();
            }
            return null;
        }

        private static void SetAccountEnabled(Backup backup, string rid, bool enabled)
        {
            using var context = new PrincipalContext(ContextType.Machine);
            using var user = FindBuiltInAccount(context, rid) ?? throw new InvalidOperationException(Loc.T("The account was not found."));
            string sid = user.Sid.Value;
            if (!backup.Accounts.ContainsKey(sid))
                backup.Accounts[sid] = user.Enabled == true;
            user.Enabled = enabled;
            user.Save();
        }

        private static string? NxPolicy()
        {
            var (exit, output) = RunTool(SystemTool("bcdedit.exe"), "/enum {current}", 15000);
            if (exit != 0) return null;
            var match = Regex.Match(output, @"^\s*nx\s+(\S+)", RegexOptions.Multiline | RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : null;
        }

        private static void SetNx(string value)
        {
            var (exit, output) = RunTool(SystemTool("bcdedit.exe"), "/set {current} nx " + value, 15000);
            if (exit != 0)
                throw new InvalidOperationException(LastLine(output, Loc.F("bcdedit failed with code {0}", exit)));
        }

        #endregion

        #region Scan

        private sealed class Context
        {
            public List<Issue> Issues { get; } = new List<Issue>();
            public bool ThirdPartyAntivirus;
            public List<string> ThirdPartyAntivirusNames = new List<string>();
            public bool ThirdPartyFirewall;
            public List<Product>? Antivirus;

            public void Add(string id, string category, Level level, string title, string description, string fixText,
                Action<Backup>? fix, bool selected = true, string details = "", bool restart = false)
            {
                Issues.Add(new Issue
                {
                    Id = id,
                    Category = Loc.T(category),
                    Severity = level,
                    Title = Loc.T(title),
                    Description = Loc.T(description),
                    FixText = Loc.T(fixText),
                    Apply = fix,
                    Details = details,
                    NeedsRestart = restart,
                    IsSelected = selected && fix != null
                });
            }
        }

        public static List<Issue> Scan()
        {
            var context = new Context();
            context.Antivirus = SecurityProducts("AntiVirusProduct");
            context.ThirdPartyAntivirusNames = FindThirdPartyAntivirus(context.Antivirus);
            context.ThirdPartyAntivirus = context.ThirdPartyAntivirusNames.Count > 0;
            context.ThirdPartyFirewall = SecurityProducts("FirewallProduct")?.Any(p => p.Enabled && !p.IsMicrosoft) == true;

            Action<Context>[] checks =
            {
                CheckUac, CheckAccounts, CheckCredentials,
                CheckFirewall, CheckRemoteAccess, CheckSmb, CheckNetwork,
                CheckAntivirus, CheckDefenderPolicies, CheckDefenderExclusions, CheckSmartScreen, CheckSecurityServices,
                CheckAutoPlay,
                CheckWindowsUpdate,
                CheckBackdoors, CheckBlockedTools, CheckSystemProtection, CheckServicePaths, CheckOffice, CheckExplorer
            };

            foreach (var check in checks)
            {
                try
                {
                    check(context);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Security check {check.Method.Name} failed: {ex.Message}");
                }
            }

            var ignored = IgnoredIds();
            return context.Issues.Where(i => !ignored.Contains(i.Id)).OrderBy(i => i.Severity).ThenBy(i => i.Category).ToList();
        }

        private static void CheckUac(Context c)
        {
            const string cat = "Account protection";
            int? lua = GetInt(HKLM, PolicySystem, "EnableLUA");
            if (lua == 0)
            {
                c.Add("uac_off", cat, Level.High, "User Account Control (UAC) is turned off",
                    "Every program runs with administrator rights, so malware can change the system without asking.",
                    "Turn UAC on", b => SetDword(b, HKLM, PolicySystem, "EnableLUA", 1), restart: true);
                return;
            }

            if (GetInt(HKLM, PolicySystem, "ConsentPromptBehaviorAdmin") == 0)
                c.Add("uac_noprompt", cat, Level.High, "UAC gives administrator rights without asking",
                    "Programs can get administrator rights silently, so you are never warned about system changes.",
                    "Ask for consent for non-Windows programs", b => SetDword(b, HKLM, PolicySystem, "ConsentPromptBehaviorAdmin", 5));

            if (GetInt(HKLM, PolicySystem, "PromptOnSecureDesktop") == 0)
                c.Add("uac_desktop", cat, Level.Medium, "UAC prompts are not shown on the secure desktop",
                    "Other programs can see or click the UAC prompt.",
                    "Show UAC prompts on the secure desktop", b => SetDword(b, HKLM, PolicySystem, "PromptOnSecureDesktop", 1));

            if (GetInt(HKLM, PolicySystem, "LocalAccountTokenFilterPolicy") == 1)
                c.Add("uac_remote", cat, Level.Medium, "Remote UAC restrictions are turned off",
                    "Local administrator accounts get full rights over the network, which is used by worms to spread.",
                    "Turn remote UAC restrictions back on", b => SetDword(b, HKLM, PolicySystem, "LocalAccountTokenFilterPolicy", 0));
        }

        private static void CheckAccounts(Context c)
        {
            const string cat = "Account protection";
            using var context = new PrincipalContext(ContextType.Machine);

            using (var guest = FindBuiltInAccount(context, "501"))
            {
                if (guest?.Enabled == true)
                    c.Add("guest", cat, Level.High, "The Guest account is turned on",
                        "Anyone can sign in to this PC and reach shared files without a password.",
                        "Turn the Guest account off", b => SetAccountEnabled(b, "501", false), details: guest.SamAccountName);
            }

            string? current = WindowsIdentity.GetCurrent().User?.Value;
            using (var admin = FindBuiltInAccount(context, "500"))
            {
                if (admin?.Enabled == true && admin.Sid.Value != current)
                    c.Add("admin", cat, Level.Medium, "The built-in Administrator account is turned on",
                        "This account has a well-known name, is not protected by UAC and is a common target for password guessing.",
                        "Turn the built-in Administrator account off", b => SetAccountEnabled(b, "500", false), details: admin.SamAccountName);
            }

            if (GetString(HKLM, Winlogon, "AutoAdminLogon") == "1" && Get(HKLM, Winlogon, "DefaultPassword") != null)
                c.Add("autologon", cat, Level.High, "Automatic sign-in stores your password in plain text",
                    "The password is saved unencrypted in the registry, where any program can read it.",
                    "Remove the saved password and turn automatic sign-in off", b =>
                    {
                        DeleteValue(b, HKLM, Winlogon, "DefaultPassword");
                        SetValue(b, HKLM, Winlogon, "AutoAdminLogon", "0", RegistryValueKind.String);
                    }, details: GetString(HKLM, Winlogon, "DefaultUserName") ?? "");
        }

        private static void CheckCredentials(Context c)
        {
            const string cat = "Account protection";
            const string wdigest = @"SYSTEM\CurrentControlSet\Control\SecurityProviders\WDigest";
            if (GetInt(HKLM, wdigest, "UseLogonCredential") == 1)
                c.Add("wdigest", cat, Level.High, "Passwords are kept in memory in plain text (WDigest)",
                    "Password stealing tools can read your Windows password from memory.",
                    "Turn WDigest password caching off", b => SetDword(b, HKLM, wdigest, "UseLogonCredential", 0));

            if (GetInt(HKLM, Lsa, "NoLMHash") == 0)
                c.Add("lmhash", cat, Level.Medium, "Weak LM password hashes are stored",
                    "LM hashes can be cracked in seconds.",
                    "Stop storing LM hashes", b => SetDword(b, HKLM, Lsa, "NoLMHash", 1));

            int? lmLevel = GetInt(HKLM, Lsa, "LmCompatibilityLevel");
            if (lmLevel.HasValue && lmLevel.Value < 3)
                c.Add("ntlm", cat, Level.Medium, "Old LM and NTLMv1 sign-in is allowed",
                    "These old protocols can be broken or relayed by attackers on the network.",
                    "Send NTLMv2 only", b => SetDword(b, HKLM, Lsa, "LmCompatibilityLevel", 3), details: Loc.F("Level {0}", lmLevel.Value));

            if (GetInt(HKLM, Lsa, "RestrictAnonymousSAM") == 0)
                c.Add("anon_sam", cat, Level.Medium, "Anonymous users can list the accounts on this PC",
                    "Attackers on the network can get the user names they need for password guessing.",
                    "Block anonymous account listing", b => SetDword(b, HKLM, Lsa, "RestrictAnonymousSAM", 1));

            if (GetInt(HKLM, Lsa, "EveryoneIncludesAnonymous") == 1)
                c.Add("anon_everyone", cat, Level.High, "Anonymous users get the rights of Everyone",
                    "Files and settings shared with Everyone can be reached without signing in.",
                    "Remove anonymous users from Everyone", b => SetDword(b, HKLM, Lsa, "EveryoneIncludesAnonymous", 0));

            if ((GetInt(HKLM, Lsa, "RunAsPPL") ?? 0) == 0 && (GetInt(HKLM, Lsa, "RunAsPPLBoot") ?? 0) == 0)
                c.Add("lsa_ppl", cat, Level.Low, "LSA protection is off",
                    "Protects the process that holds your sign-in secrets from being read by other programs. Some old drivers and tools do not work with it.",
                    "Turn LSA protection on", b => SetDword(b, HKLM, Lsa, "RunAsPPL", 1), selected: false, restart: true);
        }

        private static void CheckFirewall(Context c)
        {
            const string cat = "Firewall and network";
            if (c.ThirdPartyFirewall)
                return;

            var policy = FirewallPolicy();
            var off = FirewallProfiles.Where(p => !policy.FirewallEnabled[p.Profile]).ToList();
            var policyOff = FirewallProfiles.Where(p => GetInt(HKLM, $@"SOFTWARE\Policies\Microsoft\WindowsFirewall\{p.PolicyKey}", "EnableFirewall") == 0).ToList();
            var all = off.Select(p => p.Name).Union(policyOff.Select(p => p.Name)).ToList();
            if (all.Count == 0)
                return;

            c.Add("firewall", cat, Level.High, "Windows Firewall is turned off",
                "Programs and attackers on the network can connect to this PC without being blocked.",
                "Turn Windows Firewall on", b =>
                {
                    foreach (var p in policyOff)
                        DeleteValue(b, HKLM, $@"SOFTWARE\Policies\Microsoft\WindowsFirewall\{p.PolicyKey}", "EnableFirewall");
                    var fw = FirewallPolicy();
                    foreach (var p in FirewallProfiles.Where(p => all.Contains(p.Name)))
                    {
                        if (!b.Firewall.ContainsKey(p.Name))
                            b.Firewall[p.Name] = fw.FirewallEnabled[p.Profile];
                        fw.FirewallEnabled[p.Profile] = true;
                    }
                }, details: Loc.F("{0} network", string.Join(", ", all.Select(Loc.T))));
        }

        private static void CheckRemoteAccess(Context c)
        {
            const string cat = "Firewall and network";
            bool remote = IsRemoteSession();
            bool rdpOn = GetInt(HKLM, TerminalServer, "fDenyTSConnections") == 0;
            if (rdpOn)
            {
                c.Add("rdp", cat, Level.Medium, "Remote Desktop is turned on",
                    "Remote Desktop is a common target for password guessing from the internet. Turn it off if you do not use it.",
                    remote ? "Turn Remote Desktop off in Settings > System > Remote Desktop (you are connected remotely now)" : "Turn Remote Desktop off",
                    remote ? null : b => SetDword(b, HKLM, TerminalServer, "fDenyTSConnections", 1), selected: false);

                const string rdpTcp = TerminalServer + @"\WinStations\RDP-Tcp";
                if (GetInt(HKLM, rdpTcp, "UserAuthentication") == 0)
                    c.Add("rdp_nla", cat, Level.High, "Remote Desktop does not require Network Level Authentication",
                        "Anyone can reach the sign-in screen and attack it before giving a password.",
                        "Require Network Level Authentication", b => SetDword(b, HKLM, rdpTcp, "UserAuthentication", 1));
            }

            if (GetInt(HKLM, @"SYSTEM\CurrentControlSet\Control\Remote Assistance", "fAllowToGetHelp") == 1)
                c.Add("remote_assist", cat, Level.Low, "Remote Assistance is allowed",
                    "Someone you invite can take control of this PC. Scammers often ask for it.",
                    "Turn Remote Assistance off", b => SetDword(b, HKLM, @"SYSTEM\CurrentControlSet\Control\Remote Assistance", "fAllowToGetHelp", 0), selected: false);

            if (ServiceStart("RemoteRegistry") == 2)
                c.Add("remote_registry", cat, Level.Medium, "Remote Registry starts automatically",
                    "Other computers on the network can read and change the registry of this PC.",
                    "Disable the Remote Registry service", b => SetServiceMode(b, "RemoteRegistry", "disabled"));

            int? telnet = ServiceStart("TlntSvr");
            if (telnet.HasValue && telnet.Value != 4)
                c.Add("telnet", cat, Level.High, "Telnet Server is installed",
                    "Telnet sends passwords over the network without encryption.",
                    "Disable the Telnet service", b => SetServiceMode(b, "TlntSvr", "disabled"));

            if (ServiceStart("WinRM") == 2)
                c.Add("winrm", cat, Level.Low, "Windows Remote Management starts automatically",
                    "PowerShell remoting accepts commands from the network. Leave it on only if you manage this PC remotely.",
                    "Set the WinRM service to manual", b => SetServiceMode(b, "WinRM", "demand"), selected: false);

            if (ServiceStart("sshd") == 2)
                c.Add("sshd", cat, Level.Low, "OpenSSH Server starts automatically",
                    "Other computers can sign in to this PC over SSH. Leave it on only if you use it.",
                    "Set the OpenSSH Server service to manual", b => SetServiceMode(b, "sshd", "demand"), selected: false);
        }

        private static void CheckSmb(Context c)
        {
            const string cat = "Firewall and network";
            const string server = @"SYSTEM\CurrentControlSet\Services\LanmanServer\Parameters";
            if (GetInt(HKLM, server, "SMB1") == 1)
                c.Add("smb1_server", cat, Level.High, "The SMBv1 file sharing server is turned on",
                    "SMBv1 is an outdated protocol with known security vulnerabilities that can be exploited to spread threats between computers.",
                    "Turn the SMBv1 server off", b => SetDword(b, HKLM, server, "SMB1", 0), restart: true);

            int? client = ServiceStart("mrxsmb10");
            if (client.HasValue && client.Value != 4)
                c.Add("smb1", cat, Level.High, "SMB 1.0 is installed",
                    "SMB 1.0 is outdated and has known security vulnerabilities. Only very old network drives still need it.",
                    "Remove SMB 1.0 with DISM", b => DisableFeature(b, "SMB1Protocol"), restart: true);
        }

        private static void CheckNetwork(Context c)
        {
            const string cat = "Firewall and network";
            const string dns = @"SOFTWARE\Policies\Microsoft\Windows NT\DNSClient";
            if (GetInt(HKLM, dns, "EnableMulticast") != 0)
                c.Add("llmnr", cat, Level.Low, "LLMNR name resolution is on",
                    "Attackers on the same network can answer these requests and capture password hashes.",
                    "Turn LLMNR off", b => SetDword(b, HKLM, dns, "EnableMulticast", 0), selected: false);

            const string internet = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
            string proxy = GetInt(HKCU, internet, "ProxyEnable") == 1 ? GetString(HKCU, internet, "ProxyServer") ?? "" : "";
            string script = GetString(HKCU, internet, "AutoConfigURL") ?? "";
            if (proxy.Length > 0 || script.Length > 0)
                c.Add("proxy", cat, Level.Low, "A proxy server is set",
                    "All web traffic goes through this proxy. Malware sets a proxy to read or change your traffic. Ignore this if your company set it.",
                    "Turn the proxy off", b =>
                    {
                        SetDword(b, HKCU, internet, "ProxyEnable", 0);
                        DeleteValue(b, HKCU, internet, "AutoConfigURL");
                    }, selected: false, details: string.Join(", ", new[] { proxy, script }.Where(s => s.Length > 0)));

            const string wifi = @"SOFTWARE\Microsoft\WcmSvc\wifinetworkmanager\config";
            if (GetInt(HKLM, wifi, "AutoConnectAllowedOEM") == 1)
                c.Add("wifi_sense", cat, Level.Low, "Automatic connection to open hotspots is on",
                    "The PC can join open Wi-Fi networks where others can watch the traffic.",
                    "Turn automatic hotspot connection off", b => SetDword(b, HKLM, wifi, "AutoConnectAllowedOEM", 0));
        }

        private static void CheckAntivirus(Context c)
        {
            const string cat = "Virus and threat protection";
            if (c.Antivirus == null || c.Antivirus.Count == 0)
            {
                if (c.ThirdPartyAntivirus || IsServiceRunning("WinDefend"))
                    return;
                c.Add("no_av", cat, Level.High, "No antivirus is turned on",
                    "Nothing checks downloaded files and programs for malware.",
                    "Install an antivirus or open Windows Security > Virus & threat protection and turn real-time protection on", null,
                    details: Loc.T("Windows Security Center could not list any antivirus program."));
                return;
            }

            var enabled = c.Antivirus.Where(p => p.Enabled).ToList();
            bool unlistedThirdParty = c.ThirdPartyAntivirus && !c.Antivirus.Any(p => !p.IsMicrosoft);
            if (enabled.Count == 0 && !unlistedThirdParty)
            {
                c.Add("no_av", cat, Level.High, "No antivirus is turned on",
                    "Nothing checks downloaded files and programs for malware.",
                    "Open Windows Security > Virus & threat protection and turn real-time protection on", null,
                    details: c.Antivirus.Count > 0 ? Loc.F("Installed: {0}", string.Join(", ", c.Antivirus.Select(p => p.Name).Distinct())) : "");
                return;
            }

            var outdated = enabled.Where(p => !p.UpToDate).ToList();
            if (outdated.Count > 0)
                c.Add("av_outdated", cat, Level.Medium, "Antivirus definitions are out of date",
                    "New malware is not recognised until the definitions are updated.",
                    "Update the antivirus definitions", null, details: string.Join(", ", outdated.Select(p => p.Name)));
        }

        private static void CheckDefenderPolicies(Context c)
        {
            const string cat = "Virus and threat protection";
            if (c.ThirdPartyAntivirus)
                return;

            const string realTime = DefenderPolicy + @"\Real-Time Protection";
            var found = new List<(string Key, string Name)>();
            foreach (string name in new[] { "DisableAntiSpyware", "DisableAntiVirus" })
                if (GetInt(HKLM, DefenderPolicy, name) == 1) found.Add((DefenderPolicy, name));
            foreach (string name in new[] { "DisableRealtimeMonitoring", "DisableBehaviorMonitoring", "DisableOnAccessProtection", "DisableIOAVProtection", "DisableScanOnRealtimeEnable" })
                if (GetInt(HKLM, realTime, name) == 1) found.Add((realTime, name));

            if (found.Count > 0)
                c.Add("defender_policy", cat, Level.High, "Microsoft Defender is turned off by a policy",
                    "A group policy switches parts of Defender off. Malware and \"tweaking\" tools often set these values.",
                    "Remove the policies that turn Defender off", b =>
                    {
                        foreach (var (key, name) in found)
                            DeleteValue(b, HKLM, key, name);
                    }, details: string.Join(", ", found.Select(f => f.Name)), restart: true);

            const string spynet = DefenderPolicy + @"\Spynet";
            if (GetInt(HKLM, spynet, "SpynetReporting") == 0 || GetInt(HKLM, spynet, "SubmitSamplesConsent") == 2)
                c.Add("defender_cloud", cat, Level.Low, "Defender cloud protection is turned off by a policy",
                    "New threats are found much faster with cloud protection.",
                    "Remove the policy", b =>
                    {
                        DeleteValue(b, HKLM, spynet, "SpynetReporting");
                        DeleteValue(b, HKLM, spynet, "SubmitSamplesConsent");
                    });

            if (GetInt(HKLM, DefenderPolicy, "PUAProtection") == 0)
                c.Add("defender_pua", cat, Level.Low, "Defender does not block unwanted apps",
                    "Adware, bundled toolbars and crypto miners are not blocked.",
                    "Remove the policy", b => DeleteValue(b, HKLM, DefenderPolicy, "PUAProtection"));

            const string notifications = @"SOFTWARE\Policies\Microsoft\Windows Defender Security Center\Notifications";
            if (GetInt(HKLM, notifications, "DisableNotifications") == 1 || GetInt(HKLM, notifications, "DisableEnhancedNotifications") == 1)
                c.Add("defender_notify", cat, Level.Low, "Windows Security notifications are turned off",
                    "You are not told when a threat is found or protection is turned off.",
                    "Turn the notifications back on", b =>
                    {
                        DeleteValue(b, HKLM, notifications, "DisableNotifications");
                        DeleteValue(b, HKLM, notifications, "DisableEnhancedNotifications");
                    });
        }

        private static readonly string[] RiskyExtensions = { "exe", "dll", "scr", "bat", "cmd", "ps1", "vbs", "js", "msi", "com", "jar", "hta" };

        private static bool IsBroadPath(string path)
        {
            string p = Environment.ExpandEnvironmentVariables(path.Trim()).TrimEnd('\\', '*');
            if (p.Length <= 3) return true;
            string[] broad =
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
                Path.GetTempPath().TrimEnd('\\'),
                Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)) ?? ""
            };
            return broad.Any(b => b.Length > 0 && p.Equals(b.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
        }

        private static void CheckDefenderExclusions(Context c)
        {
            const string cat = "Virus and threat protection";
            if (c.ThirdPartyAntivirus)
                return;

            var found = new List<(string Key, string Name)>();
            foreach (string root in new[] { @"SOFTWARE\Microsoft\Windows Defender\Exclusions", DefenderPolicy + @"\Exclusions" })
            {
                foreach (string name in ValueNames(HKLM, root + @"\Paths").Where(IsBroadPath))
                    found.Add((root + @"\Paths", name));
                foreach (string name in ValueNames(HKLM, root + @"\Extensions").Where(n => RiskyExtensions.Contains(n.Trim().TrimStart('*', '.'), StringComparer.OrdinalIgnoreCase)))
                    found.Add((root + @"\Extensions", name));
            }

            if (found.Count > 0)
                c.Add("defender_exclusions", cat, Level.High, "Defender skips whole folders or program files",
                    "Malware adds exclusions like these so that it is never scanned.",
                    "Remove these exclusions (if Tamper Protection blocks it, remove them in Windows Security > Virus & threat protection > Exclusions)", b =>
                    {
                        foreach (var (key, name) in found)
                            DeleteValue(b, HKLM, key, name);
                    }, details: string.Join(", ", found.Select(f => f.Name).Distinct()));
        }

        private static void CheckSmartScreen(Context c)
        {
            const string cat = "Virus and threat protection";
            const string systemPolicy = @"SOFTWARE\Policies\Microsoft\Windows\System";
            const string explorer = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer";
            bool policyOff = GetInt(HKLM, systemPolicy, "EnableSmartScreen") == 0;
            bool settingOff = string.Equals(GetString(HKLM, explorer, "SmartScreenEnabled"), "Off", StringComparison.OrdinalIgnoreCase);
            if (policyOff || settingOff)
                c.Add("smartscreen", cat, Level.Medium, "SmartScreen for apps and files is turned off",
                    "Downloaded programs that are known to be dangerous or are unknown are started without a warning.",
                    "Turn SmartScreen on (warn)", b =>
                    {
                        DeleteValue(b, HKLM, systemPolicy, "EnableSmartScreen");
                        DeleteValue(b, HKLM, systemPolicy, "ShellSmartScreenLevel");
                        if (settingOff)
                            SetValue(b, HKLM, explorer, "SmartScreenEnabled", "Warn", RegistryValueKind.String);
                    });

            const string edge = @"SOFTWARE\Policies\Microsoft\Edge";
            if (GetInt(HKLM, edge, "SmartScreenEnabled") == 0 || GetInt(HKCU, edge, "SmartScreenEnabled") == 0)
                c.Add("smartscreen_edge", cat, Level.Medium, "SmartScreen is turned off in Microsoft Edge",
                    "Phishing and malware sites are opened without a warning.",
                    "Remove the policy", b =>
                    {
                        DeleteValue(b, HKLM, edge, "SmartScreenEnabled");
                        DeleteValue(b, HKCU, edge, "SmartScreenEnabled");
                    });

            const string appHost = @"Software\Microsoft\Windows\CurrentVersion\AppHost";
            if (GetInt(HKCU, appHost, "EnableWebContentEvaluation") == 0)
                c.Add("smartscreen_store", cat, Level.Low, "SmartScreen for Microsoft Store apps is turned off",
                    "Web content used by Store apps is not checked.",
                    "Turn it on", b => SetDword(b, HKCU, appHost, "EnableWebContentEvaluation", 1));
        }

        private static void CheckSecurityServices(Context c)
        {
            const string cat = "Virus and threat protection";
            var services = new List<(string Name, string Title, string Mode)>
            {
                ("mpssvc", "Windows Defender Firewall", "auto"),
                ("BFE", "Base Filtering Engine", "auto"),
                ("wscsvc", "Security Center", "delayed-auto"),
                ("EventLog", "Windows Event Log", "auto"),
                ("CryptSvc", "Cryptographic Services", "auto"),
                ("SecurityHealthService", "Windows Security Service", "demand")
            };
            if (c.ThirdPartyAntivirus)
                services.RemoveAll(s => s.Name is "wscsvc" or "SecurityHealthService");
            else
                services.Add(("WinDefend", "Microsoft Defender Antivirus", "auto"));
            if (c.ThirdPartyFirewall)
                services.RemoveAll(s => s.Name == "mpssvc");

            foreach (var (name, title, mode) in services)
            {
                if (ServiceStart(name) != 4)
                    continue;
                c.Add("service_" + name, cat, Level.High, Loc.F("The {0} service is disabled", Loc.T(title)),
                    "Windows protection that depends on this service does not work.",
                    Loc.T(mode == "demand" ? "Set the service to manual and start it" : "Set the service to automatic and start it"), b => SetServiceMode(b, name, mode), details: name);
            }
        }

        private static void CheckAutoPlay(Context c)
        {
            const string cat = "USB and removable drives";
            const string handlers = @"Software\Microsoft\Windows\CurrentVersion\Explorer\AutoplayHandlers";
            int? noDrive = GetInt(HKLM, PolicyExplorer, "NoDriveTypeAutoRun") ?? GetInt(HKCU, PolicyExplorer, "NoDriveTypeAutoRun");
            bool allBlocked = noDrive == 0xFF;

            if (GetInt(HKCU, handlers, "DisableAutoplay") != 1 && !allBlocked)
                c.Add("autoplay", cat, Level.Medium, "AutoPlay is on for USB drives and other media",
                    "When a USB stick, memory card or phone is plugged in, Windows offers to open or run its content right away. Infected USB drives use this to spread.",
                    "Turn AutoPlay off for all media and devices", b => SetDword(b, HKCU, handlers, "DisableAutoplay", 1));

            if (!allBlocked && GetInt(HKLM, PolicyExplorer, "NoAutorun") != 1)
                c.Add("autorun", cat, Level.Medium, "AutoRun is not turned off for all drive types",
                    "autorun.inf files on CDs, network drives and some USB devices can start programs automatically.",
                    "Turn AutoRun off for all drives", b =>
                    {
                        SetDword(b, HKLM, PolicyExplorer, "NoDriveTypeAutoRun", 0xFF);
                        SetDword(b, HKLM, PolicyExplorer, "NoAutorun", 1);
                    }, details: noDrive.HasValue ? $"NoDriveTypeAutoRun = 0x{noDrive.Value:X2}" : Loc.T("Not configured"));
        }

        private static void CheckWindowsUpdate(Context c)
        {
            const string cat = "Windows Update";
            foreach (var (name, title, mode) in new[] { ("wuauserv", "Windows Update", "demand"), ("UsoSvc", "Update Orchestrator", "delayed-auto"), ("BITS", "Background Intelligent Transfer", "demand") })
            {
                if (ServiceStart(name) == 4)
                    c.Add("wu_service_" + name, cat, Level.High, Loc.F("The {0} service is disabled", Loc.T(title)),
                        "Windows does not download security updates, so known security holes stay open.",
                        Loc.T(mode == "demand" ? "Set the service back to manual" : "Set the service back to automatic"), b => SetServiceMode(b, name, mode), details: name);
            }

            const string au = WindowsUpdatePolicy + @"\AU";
            var policies = new List<(string Key, string Name)>();
            if (GetInt(HKLM, au, "NoAutoUpdate") == 1) policies.Add((au, "NoAutoUpdate"));
            if (GetInt(HKLM, WindowsUpdatePolicy, "DisableWindowsUpdateAccess") == 1) policies.Add((WindowsUpdatePolicy, "DisableWindowsUpdateAccess"));
            if (GetInt(HKLM, WindowsUpdatePolicy, "SetDisableUXWUAccess") == 1) policies.Add((WindowsUpdatePolicy, "SetDisableUXWUAccess"));
            if (GetInt(HKLM, WindowsUpdatePolicy, "DoNotConnectToWindowsUpdateInternetLocations") == 1) policies.Add((WindowsUpdatePolicy, "DoNotConnectToWindowsUpdateInternetLocations"));
            if (policies.Count > 0)
                c.Add("wu_policy", cat, Level.Medium, "Automatic updates are blocked by a policy",
                    "Security updates are not installed automatically.",
                    "Remove the policies that block Windows Update", b =>
                    {
                        foreach (var (key, name) in policies)
                            DeleteValue(b, HKLM, key, name);
                    }, details: string.Join(", ", policies.Select(p => p.Name)));

            const string ux = @"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings";
            string? pause = GetString(HKLM, ux, "PauseUpdatesExpiryTime");
            if (pause != null && DateTime.TryParse(pause, null, System.Globalization.DateTimeStyles.AdjustToUniversal, out DateTime until) && until > DateTime.UtcNow)
                c.Add("wu_paused", cat, Level.Low, "Windows updates are paused",
                    "Security updates are not installed until the pause ends.",
                    "Resume updates", b =>
                    {
                        foreach (string name in new[] { "PauseUpdatesExpiryTime", "PauseUpdatesStartTime", "PauseFeatureUpdatesStartTime", "PauseFeatureUpdatesEndTime", "PauseQualityUpdatesStartTime", "PauseQualityUpdatesEndTime" })
                            DeleteValue(b, HKLM, ux, name);
                    }, selected: false, details: Loc.F("Until {0}", until.ToLocalTime().ToString("d")));
        }

        private static void CheckBackdoors(Context c)
        {
            const string cat = "System integrity";
            const string ifeo = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options";
            var hijacked = new[] { "sethc.exe", "utilman.exe", "osk.exe", "magnify.exe", "narrator.exe", "displayswitch.exe", "atbroker.exe" }
                .Where(exe => !string.IsNullOrWhiteSpace(GetString(HKLM, ifeo + "\\" + exe, "Debugger"))).ToList();
            if (hijacked.Count > 0)
                c.Add("ifeo", cat, Level.High, "Accessibility tools on the sign-in screen are redirected",
                    "This is a known backdoor: pressing Shift five times or the Ease of Access button on the sign-in screen opens another program, often a command prompt with full rights.",
                    "Remove the redirection", b =>
                    {
                        foreach (string exe in hijacked)
                            DeleteValue(b, HKLM, ifeo + "\\" + exe, "Debugger");
                    }, details: string.Join(", ", hijacked));

            string shell = (GetString(HKLM, Winlogon, "Shell") ?? "explorer.exe").Trim();
            if (!shell.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase) && !shell.Equals(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), StringComparison.OrdinalIgnoreCase))
                c.Add("winlogon_shell", cat, Level.High, "The Windows shell was changed",
                    "Another program starts instead of or together with Explorer when you sign in. Malware uses this to start every time.",
                    "Restore explorer.exe as the shell", b => SetValue(b, HKLM, Winlogon, "Shell", "explorer.exe", RegistryValueKind.String), details: shell);

            string? userShell = GetString(HKCU, Winlogon, "Shell");
            if (!string.IsNullOrWhiteSpace(userShell))
                c.Add("winlogon_usershell", cat, Level.High, "A different shell is set for your account",
                    "Another program starts instead of Explorer when you sign in.",
                    "Remove the shell set for your account", b => DeleteValue(b, HKCU, Winlogon, "Shell"), details: userShell);

            string defaultUserinit = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "userinit.exe");
            string userinit = GetString(HKLM, Winlogon, "Userinit") ?? "";
            var parts = userinit.Split(',').Select(p => Environment.ExpandEnvironmentVariables(p.Trim())).Where(p => p.Length > 0).ToList();
            if (parts.Count != 1 || !(parts[0].Equals(defaultUserinit, StringComparison.OrdinalIgnoreCase) || parts[0].Equals("userinit.exe", StringComparison.OrdinalIgnoreCase)))
                c.Add("winlogon_userinit", cat, Level.High, "Extra programs start with Userinit at sign-in",
                    "Programs added here start before your desktop appears. Malware uses this to start every time.",
                    "Restore the default Userinit value", b => SetValue(b, HKLM, Winlogon, "Userinit", defaultUserinit + ",", RegistryValueKind.String), details: userinit);

            var appInit = new List<string>();
            foreach (string key in new[] { @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Windows", @"SOFTWARE\WOW6432Node\Microsoft\Windows NT\CurrentVersion\Windows" })
                if (GetInt(HKLM, key, "LoadAppInit_DLLs") == 1 && !string.IsNullOrWhiteSpace(GetString(HKLM, key, "AppInit_DLLs")))
                    appInit.Add(key);
            if (appInit.Count > 0)
                c.Add("appinit", cat, Level.Medium, "AppInit DLLs are loaded into every program",
                    "A DLL is injected into every program that starts. Malware and some old tools use this.",
                    "Stop loading AppInit DLLs", b =>
                    {
                        foreach (string key in appInit)
                            SetDword(b, HKLM, key, "LoadAppInit_DLLs", 0);
                    }, details: string.Join(", ", appInit.Select(k => GetString(HKLM, k, "AppInit_DLLs"))));
        }

        private static void CheckBlockedTools(Context c)
        {
            const string cat = "System integrity";
            var tools = new List<(string Hive, string Key, string Name, string Title)>();
            foreach (string hive in new[] { HKCU, HKLM })
            {
                if (GetInt(hive, PolicySystem, "DisableTaskMgr") == 1) tools.Add((hive, PolicySystem, "DisableTaskMgr", "Task Manager"));
                if (GetInt(hive, PolicySystem, "DisableRegistryTools") >= 1) tools.Add((hive, PolicySystem, "DisableRegistryTools", "Registry Editor"));
                if (GetInt(hive, @"Software\Policies\Microsoft\Windows\System", "DisableCMD") >= 1) tools.Add((hive, @"Software\Policies\Microsoft\Windows\System", "DisableCMD", "Command Prompt"));
                if (GetInt(hive, PolicyExplorer, "NoControlPanel") == 1) tools.Add((hive, PolicyExplorer, "NoControlPanel", "Control Panel and Settings"));
            }
            if (tools.Count > 0)
                c.Add("blocked_tools", cat, Level.High, "Windows tools are blocked",
                    "Malware blocks these tools so that it cannot be found and stopped. Ignore this if your company blocked them.",
                    "Unblock the tools", b =>
                    {
                        foreach (var t in tools)
                            DeleteValue(b, t.Hive, t.Key, t.Name);
                    }, details: string.Join(", ", tools.Select(t => Loc.T(t.Title)).Distinct()));

            const string installer = @"SOFTWARE\Policies\Microsoft\Windows\Installer";
            if (GetInt(HKLM, installer, "AlwaysInstallElevated") == 1 && GetInt(HKCU, installer, "AlwaysInstallElevated") == 1)
                c.Add("install_elevated", cat, Level.High, "Every installer runs with full system rights",
                    "Any user or program can get full control of the PC by starting a .msi file.",
                    "Turn AlwaysInstallElevated off", b =>
                    {
                        DeleteValue(b, HKLM, installer, "AlwaysInstallElevated");
                        DeleteValue(b, HKCU, installer, "AlwaysInstallElevated");
                    });
        }

        private static void CheckSystemProtection(Context c)
        {
            const string cat = "System integrity";
            string? nx = NxPolicy();
            if (string.Equals(nx, "AlwaysOff", StringComparison.OrdinalIgnoreCase))
                c.Add("dep", cat, Level.High, "Data Execution Prevention (DEP) is turned off",
                    "Exploits can run code from memory areas that should only hold data.",
                    "Turn DEP on for Windows programs", b =>
                    {
                        b.Nx ??= nx;
                        SetNx("OptIn");
                    }, restart: true);

            const string memory = @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management";
            if (GetInt(HKLM, memory, "FeatureSettingsOverride") == 3 && GetInt(HKLM, memory, "FeatureSettingsOverrideMask") == 3)
                c.Add("spectre", cat, Level.Medium, "Spectre and Meltdown protection is turned off",
                    "Websites and programs can read data from other programs through the processor.",
                    "Turn the protection back on", b =>
                    {
                        DeleteValue(b, HKLM, memory, "FeatureSettingsOverride");
                        DeleteValue(b, HKLM, memory, "FeatureSettingsOverrideMask");
                    }, restart: true);

            const string restore = @"SOFTWARE\Policies\Microsoft\Windows NT\SystemRestore";
            if (GetInt(HKLM, restore, "DisableSR") == 1 || GetInt(HKLM, restore, "DisableConfig") == 1)
                c.Add("system_restore", cat, Level.Medium, "System Restore is turned off by a policy",
                    "No restore points are made, so you cannot go back after a bad driver, update or infection.",
                    "Remove the policy", b =>
                    {
                        DeleteValue(b, HKLM, restore, "DisableSR");
                        DeleteValue(b, HKLM, restore, "DisableConfig");
                    });

            const string hvci = @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity";
            if (GetInt(HKLM, hvci, "Enabled") == 0)
                c.Add("hvci", cat, Level.Low, "Memory integrity is off",
                    "Memory integrity stops malicious drivers from loading. Some old drivers are not compatible with it.",
                    "Turn it on in Windows Security > Device security > Core isolation", null);

            if (GetInt(HKLM, @"SYSTEM\CurrentControlSet\Control\SecureBoot\State", "UEFISecureBootEnabled") == 0)
                c.Add("secureboot", cat, Level.Low, "Secure Boot is off",
                    "Boot-level malware (bootkits) can start before Windows.",
                    "Turn Secure Boot on in the UEFI/BIOS settings of your PC", null);

            if (GetString(HKLM, @"SOFTWARE\Microsoft\PowerShell\1\PowerShellEngine", "PowerShellVersion") == "2.0")
                c.Add("powershell2", cat, Level.Low, "Windows PowerShell 2.0 is installed",
                    "The old PowerShell 2.0 has no script logging and AMSI, so attackers use it to hide.",
                    "Remove PowerShell 2.0 with DISM", b => DisableFeature(b, "MicrosoftWindowsPowerShellV2Root"), selected: false);
        }

        private static void CheckServicePaths(Context c)
        {
            const string cat = "System integrity";
            var found = new List<(string Name, string Raw)>();
            try
            {
                using var root = Root(HKLM);
                using var services = root.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
                if (services == null) return;
                foreach (string name in services.GetSubKeyNames())
                {
                    try
                    {
                        using var key = services.OpenSubKey(name);
                        if (key == null) continue;
                        int type = key.GetValue("Type") is int t ? t : 0;
                        if ((type & 0x30) == 0) continue;
                        string raw = key.GetValue("ImagePath", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string ?? "";
                        if (UnquotedExe(raw) != null)
                            found.Add((name, raw));
                    }
                    catch (Exception)
                    {
                    }
                }
            }
            catch (Exception)
            {
                return;
            }

            if (found.Count > 0)
                c.Add("service_paths", cat, Level.Medium, "Services with unquoted paths",
                    "The program path has spaces and no quotes, so Windows may start a program planted in a parent folder with full system rights.",
                    "Put quotes around the paths", b =>
                    {
                        foreach (var (name, raw) in found)
                        {
                            string? exe = UnquotedExe(raw);
                            if (exe == null) continue;
                            string key = ServiceKey(name);
                            using var root = Root(HKLM);
                            using var k = root.OpenSubKey(key);
                            var kind = k?.GetValueKind("ImagePath") ?? RegistryValueKind.ExpandString;
                            SetValue(b, HKLM, key, "ImagePath", "\"" + exe + "\"" + raw.Substring(exe.Length), kind);
                        }
                    }, details: string.Join(", ", found.Select(f => f.Name)), restart: true);
        }

        private static string? UnquotedExe(string raw)
        {
            string trimmed = raw.TrimStart();
            if (trimmed.Length == 0 || trimmed.StartsWith("\"") || trimmed != raw)
                return null;
            int index = raw.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (index < 0) return null;
            string exe = raw.Substring(0, index + 4);
            if (!exe.Contains(' ')) return null;
            string expanded = Environment.ExpandEnvironmentVariables(exe);
            return File.Exists(expanded) ? exe : null;
        }

        private static void CheckOffice(Context c)
        {
            const string cat = "Apps";
            var found = new List<(string Hive, string Key, string App, bool Policy)>();
            foreach (string version in new[] { "16.0", "15.0", "14.0" })
            {
                foreach (string app in new[] { "Word", "Excel", "PowerPoint", "Access", "Publisher", "Visio" })
                {
                    string key = $@"Software\Microsoft\Office\{version}\{app}\Security";
                    if (GetInt(HKCU, key, "VBAWarnings") == 1) found.Add((HKCU, key, app, false));
                    string policy = $@"Software\Policies\Microsoft\Office\{version}\{app}\Security";
                    if (GetInt(HKCU, policy, "VBAWarnings") == 1) found.Add((HKCU, policy, app, true));
                }
            }

            if (found.Count > 0)
                c.Add("office_macros", cat, Level.High, "Office runs all macros without asking",
                    "Opening a document from an e-mail or download can run malware right away.",
                    "Disable macros with a notification", b =>
                    {
                        foreach (var f in found)
                        {
                            if (f.Policy)
                                DeleteValue(b, f.Hive, f.Key, "VBAWarnings");
                            else
                                SetDword(b, f.Hive, f.Key, "VBAWarnings", 2);
                        }
                    }, details: string.Join(", ", found.Select(f => f.App).Distinct()));
        }

        private static void CheckExplorer(Context c)
        {
            const string cat = "Apps";
            const string advanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
            if (GetInt(HKCU, advanced, "HideFileExt") != 0)
                c.Add("file_ext", cat, Level.Low, "File name extensions are hidden",
                    "A file called invoice.pdf.exe is shown as invoice.pdf, so programs can pretend to be documents.",
                    "Show file name extensions", b => SetDword(b, HKCU, advanced, "HideFileExt", 0));
        }

        #endregion

        #region Fix and undo

        private static Backup LoadBackup()
        {
            try
            {
                if (File.Exists(BackupPath))
                    return JsonSerializer.Deserialize<Backup>(File.ReadAllText(BackupPath)) ?? new Backup();
            }
            catch (Exception)
            {
            }
            return new Backup();
        }

        private static void SaveBackup(Backup backup) =>
            File.WriteAllText(BackupPath, JsonSerializer.Serialize(backup, new JsonSerializerOptions { WriteIndented = true }));

        public static FixResult Fix(IEnumerable<Issue> issues)
        {
            var result = new FixResult();
            var backup = LoadBackup();
            foreach (var issue in issues.Where(i => i.CanFix))
            {
                try
                {
                    issue.Apply!(backup);
                    result.Fixed++;
                    result.Log.Add(Loc.F("Security fix: {0}", issue.Title));
                    if (issue.NeedsRestart)
                        result.NeedsRestart = true;
                }
                catch (Exception ex)
                {
                    result.Failed.Add($"{issue.Title}: {ex.Message}");
                }
                finally
                {
                    try { SaveBackup(backup); } catch (Exception) { }
                }
            }
            return result;
        }

        public static List<string> Undo()
        {
            var log = new List<string>();
            if (!File.Exists(BackupPath))
                return log;

            var backup = LoadBackup();
            bool complete = true;

            for (int i = backup.Registry.Count - 1; i >= 0; i--)
            {
                var item = backup.Registry[i];
                try
                {
                    Restore(item);
                }
                catch (Exception ex)
                {
                    complete = false;
                    log.Add(Loc.F("Could not restore {0}\\{1}: {2}", item.Key, item.Name, ex.Message));
                }
            }

            foreach (var (name, mode) in backup.Services)
            {
                var (exit, output) = RunTool(SystemTool("sc.exe"), $"config \"{name}\" start= {mode}");
                if (exit != 0)
                {
                    complete = false;
                    log.Add(Loc.F("Could not restore the {0} service: {1}", name, LastLine(output, Loc.T("sc.exe failed"))));
                }
                else if (mode == "disabled")
                {
                    try
                    {
                        using var service = new ServiceController(name);
                        if (service.Status != ServiceControllerStatus.Stopped)
                            service.Stop();
                    }
                    catch (Exception) { }
                }
            }

            if (backup.Firewall.Count > 0)
            {
                try
                {
                    var fw = FirewallPolicy();
                    foreach (var p in FirewallProfiles)
                        if (backup.Firewall.TryGetValue(p.Name, out bool on))
                            fw.FirewallEnabled[p.Profile] = on;
                }
                catch (Exception ex)
                {
                    complete = false;
                    log.Add(Loc.F("Could not restore Windows Firewall: {0}", ex.Message));
                }
            }

            if (backup.Accounts.Count > 0)
            {
                try
                {
                    using var context = new PrincipalContext(ContextType.Machine);
                    foreach (var (sid, enabled) in backup.Accounts)
                    {
                        using var user = UserPrincipal.FindByIdentity(context, IdentityType.Sid, sid);
                        if (user == null) continue;
                        user.Enabled = enabled;
                        user.Save();
                    }
                }
                catch (Exception ex)
                {
                    complete = false;
                    log.Add(Loc.F("Could not restore the accounts: {0}", ex.Message));
                }
            }

            if (backup.Nx != null)
            {
                try { SetNx(backup.Nx); }
                catch (Exception ex)
                {
                    complete = false;
                    log.Add(Loc.F("Could not restore DEP: {0}", ex.Message));
                }
            }

            foreach (string feature in backup.Features)
            {
                var (exit, output) = RunTool(SystemTool("dism.exe"), $"/online /enable-feature /featurename:{feature} /all /norestart /English", 600000);
                if (exit != 0 && exit != 3010)
                {
                    complete = false;
                    log.Add(Loc.F("Could not turn {0} back on: {1}. Turn it on in Windows Features.", feature, LastLine(output, Loc.T("DISM failed"))));
                }
            }

            log.Insert(0, Loc.T(complete ? "Security fixes were undone." : "Security fixes were undone, with errors:"));
            try { File.Delete(BackupPath); } catch (Exception) { }
            return log;
        }

        #endregion
    }
}
