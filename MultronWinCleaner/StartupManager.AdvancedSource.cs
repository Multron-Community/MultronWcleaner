using Microsoft.Win32;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Security.Cryptography;
using System.Text;

namespace MultronWinCleaner
{
    public partial class StartupManager
    {
        public sealed class AdvancedSource
        {
            private const string RecordsKey = @"Software\MultronWinCleaner\DisabledStartup";
            private const string WmiNamespace = @"\\.\root\subscription";

            public string Kind { get; init; } = "";
            public string Root { get; init; } = "HKLM";
            public string Path { get; init; } = "";
            public string ValueName { get; init; } = "";
            public string Element { get; init; } = "";
            public string Separator { get; init; } = ",";
            public string? Fallback { get; init; }
            public int OffValue { get; init; }

            private RegistryKey RootKey => Root == "HKCU" ? Registry.CurrentUser : Registry.LocalMachine;

            public string Id => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes($"{Kind}|{Root}|{Path}|{ValueName}|{Element}".ToLowerInvariant())));

            private string RecordPath => $@"{RecordsKey}\{Id}";

            public bool IsTurnedOff
            {
                get
                {
                    using var record = RootKey.OpenSubKey(RecordPath, false);
                    return record != null;
                }
            }

            public void TurnOff(string name, string command, string type)
            {
                using (var record = RootKey.CreateSubKey(RecordPath, true))
                {
                    record.SetValue("Kind", Kind);
                    record.SetValue("Path", Path);
                    record.SetValue("ValueName", ValueName);
                    record.SetValue("Element", Element);
                    record.SetValue("Separator", Separator);
                    record.SetValue("OffValue", OffValue, RegistryValueKind.DWord);
                    if (Fallback != null) record.SetValue("Fallback", Fallback);
                    record.SetValue("Name", name);
                    record.SetValue("Command", command);
                    record.SetValue("Type", type);
                    try
                    {
                        Disable(record);
                    }
                    catch
                    {
                        RootKey.DeleteSubKeyTree(RecordPath, false);
                        throw;
                    }
                }
            }

            public void TurnOn()
            {
                using (var record = RootKey.OpenSubKey(RecordPath, false) ?? throw new InvalidOperationException(Loc.T("The saved settings of this item were not found.")))
                    Restore(record);
                RootKey.DeleteSubKeyTree(RecordPath, false);
            }

            private RegistryKey OpenForChange() => RootKey.OpenSubKey(Path, true) ?? throw new InvalidOperationException(Path);

            private List<string> Tokens(object? value)
            {
                if (value is string[] many) return many.Where(v => v.Length > 0).ToList();
                char[] separators = Separator == " " ? new[] { ',', ' ' } : new[] { Separator[0] };
                return (value as string ?? "").Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            }

            private void WriteList(RegistryKey key, List<string> parts, RegistryValueKind kind)
            {
                if (kind == RegistryValueKind.MultiString)
                    key.SetValue(ValueName, parts.ToArray(), kind);
                else
                    key.SetValue(ValueName, string.Join(Separator, parts), kind);
            }

            private static object? Raw(RegistryKey key, string name) => key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);

            private static void CopyTree(RegistryKey source, RegistryKey target)
            {
                foreach (string name in source.GetValueNames())
                    target.SetValue(name, Raw(source, name)!, source.GetValueKind(name));
                foreach (string name in source.GetSubKeyNames())
                {
                    using var from = source.OpenSubKey(name, false)!;
                    using var to = target.CreateSubKey(name, true);
                    CopyTree(from, to);
                }
            }

            private void Disable(RegistryKey record)
            {
                switch (Kind)
                {
                    case "Value":
                    {
                        using var key = OpenForChange();
                        object data = Raw(key, ValueName) ?? throw new InvalidOperationException(ValueName);
                        var kind = key.GetValueKind(ValueName);
                        record.SetValue("Saved", data, kind);
                        if (Fallback != null) key.SetValue(ValueName, Fallback, kind);
                        else key.DeleteValue(ValueName);
                        break;
                    }
                    case "List":
                    {
                        using var key = OpenForChange();
                        var kind = key.GetValueKind(ValueName);
                        var parts = Tokens(Raw(key, ValueName));
                        int index = parts.FindIndex(p => p.Equals(Element, StringComparison.OrdinalIgnoreCase));
                        if (index < 0) throw new InvalidOperationException(Element);
                        record.SetValue("SavedKind", (int)kind, RegistryValueKind.DWord);
                        record.SetValue("Index", index, RegistryValueKind.DWord);
                        parts.RemoveAt(index);
                        if (parts.Count == 0 && Fallback != null) parts.Add(Fallback);
                        WriteList(key, parts, kind);
                        break;
                    }
                    case "Dword":
                    {
                        using var key = OpenForChange();
                        if (key.GetValue(ValueName) is int current) record.SetValue("Saved", current, RegistryValueKind.DWord);
                        key.SetValue(ValueName, OffValue, RegistryValueKind.DWord);
                        break;
                    }
                    case "Tree":
                    {
                        using (var source = RootKey.OpenSubKey(Path, false) ?? throw new InvalidOperationException(Path))
                        using (var saved = record.CreateSubKey("Tree", true))
                            CopyTree(source, saved);
                        RootKey.DeleteSubKeyTree(Path);
                        break;
                    }
                    case "Wmi":
                    {
                        var bindings = new List<string>();
                        var scope = new ManagementScope(WmiNamespace);
                        scope.Connect();
                        using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM __FilterToConsumerBinding"));
                        var matches = searcher.Get().Cast<ManagementObject>()
                            .Where(b => ((string)b["Consumer"]).EndsWith(Path, StringComparison.OrdinalIgnoreCase)).ToList();
                        foreach (var binding in matches)
                            bindings.Add((string)binding["Filter"] + "\n" + (string)binding["Consumer"]);
                        record.SetValue("Saved", bindings.ToArray(), RegistryValueKind.MultiString);
                        foreach (var binding in matches)
                            binding.Delete();
                        break;
                    }
                    default:
                        throw new InvalidOperationException(Kind);
                }
            }

            private void Restore(RegistryKey record)
            {
                switch (Kind)
                {
                    case "Value":
                    {
                        using var key = RootKey.CreateSubKey(Path, true);
                        key.SetValue(ValueName, Raw(record, "Saved")!, record.GetValueKind("Saved"));
                        break;
                    }
                    case "List":
                    {
                        using var key = RootKey.CreateSubKey(Path, true);
                        var kind = (RegistryValueKind)(int)record.GetValue("SavedKind")!;
                        var parts = Tokens(Raw(key, ValueName));
                        if (Fallback != null && parts.Count == 1 && parts[0].Equals(Fallback, StringComparison.OrdinalIgnoreCase))
                            parts.Clear();
                        if (!parts.Contains(Element, StringComparer.OrdinalIgnoreCase))
                            parts.Insert(Math.Min((int)record.GetValue("Index")!, parts.Count), Element);
                        WriteList(key, parts, kind);
                        break;
                    }
                    case "Dword":
                    {
                        using var key = OpenForChange();
                        if (record.GetValue("Saved") is int saved) key.SetValue(ValueName, saved, RegistryValueKind.DWord);
                        else key.DeleteValue(ValueName, false);
                        break;
                    }
                    case "Tree":
                    {
                        using var saved = record.OpenSubKey("Tree", false) ?? throw new InvalidOperationException(Path);
                        using var target = RootKey.CreateSubKey(Path, true);
                        CopyTree(saved, target);
                        break;
                    }
                    case "Wmi":
                    {
                        var scope = new ManagementScope(WmiNamespace);
                        scope.Connect();
                        using var bindingClass = new ManagementClass(scope, new ManagementPath("__FilterToConsumerBinding"), null);
                        foreach (string pair in record.GetValue("Saved") as string[] ?? Array.Empty<string>())
                        {
                            string[] refs = pair.Split('\n');
                            using var binding = bindingClass.CreateInstance();
                            binding["Filter"] = refs[0];
                            binding["Consumer"] = refs[1];
                            binding.Put();
                        }
                        break;
                    }
                    default:
                        throw new InvalidOperationException(Kind);
                }
            }

            public static AdvancedSource FromRecord(string root, RegistryKey record) => new()
            {
                Kind = record.GetValue("Kind") as string ?? "",
                Root = root,
                Path = record.GetValue("Path") as string ?? "",
                ValueName = record.GetValue("ValueName") as string ?? "",
                Element = record.GetValue("Element") as string ?? "",
                Separator = record.GetValue("Separator") as string ?? ",",
                Fallback = record.GetValue("Fallback") as string,
                OffValue = record.GetValue("OffValue") is int off ? off : 0
            };

            public static IEnumerable<(string Root, string Id)> RecordIds()
            {
                foreach (var (name, hive) in new[] { ("HKLM", Registry.LocalMachine), ("HKCU", Registry.CurrentUser) })
                {
                    using var records = hive.OpenSubKey(RecordsKey, false);
                    if (records == null) continue;
                    foreach (string id in records.GetSubKeyNames())
                        yield return (name, id);
                }
            }

            public static RegistryKey? OpenRecord(string root, string id) =>
                (root == "HKCU" ? Registry.CurrentUser : Registry.LocalMachine).OpenSubKey($@"{RecordsKey}\{id}", false);
        }

        private static AdvancedSource ValueSource(RegistryKey root, string path, string valueName, string? fallback = null) =>
            new() { Kind = "Value", Root = RootName(root), Path = path, ValueName = valueName, Fallback = fallback };

        private static AdvancedSource ListSource(RegistryKey root, string path, string valueName, string element, string separator = ",", string? fallback = null) =>
            new() { Kind = "List", Root = RootName(root), Path = path, ValueName = valueName, Element = element, Separator = separator, Fallback = fallback };

        private static AdvancedSource TreeSource(RegistryKey root, string path) =>
            new() { Kind = "Tree", Root = RootName(root), Path = path };

        private static AdvancedSource DwordSource(RegistryKey root, string path, string valueName, int offValue) =>
            new() { Kind = "Dword", Root = RootName(root), Path = path, ValueName = valueName, OffValue = offValue };

        private static string RootName(RegistryKey root) => root == Registry.CurrentUser ? "HKCU" : "HKLM";

        private void AddTurnedOffRecords(ConcurrentBag<StartupApp> apps)
        {
            foreach (var (root, id) in AdvancedSource.RecordIds())
            {
                Safely(() =>
                {
                    using var record = AdvancedSource.OpenRecord(root, id);
                    if (record == null) return;
                    string name = record.GetValue("Name") as string ?? id;
                    string command = record.GetValue("Command") as string ?? "";
                    apps.Add(new StartupApp
                    {
                        Name = name,
                        Path = command,
                        ValueName = name,
                        StartupType = record.GetValue("Type") as string ?? "",
                        Advanced = AdvancedSource.FromRecord(root, record),
                        IsAdvancedRecord = true,
                        IsEnabled = false,
                        IsPresent = true,
                        Icon = GetIcon(command),
                        Impact = CalculateImpactAutomatically(command, name)
                    });
                });
            }
        }
    }
}
