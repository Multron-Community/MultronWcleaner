using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Data;

namespace MultronWinCleaner.Processes
{
    public enum RowTone
    {
        Normal,
        Good,
        Problem
    }

    public sealed class FriendlyListRow
    {
        public string Title { get; init; } = string.Empty;
        public string Detail { get; init; } = string.Empty;
        public string Extra { get; init; } = string.Empty;
        public string Badge { get; init; } = string.Empty;
        public string FullText { get; init; } = string.Empty;
        public RowTone Tone { get; init; }

        public override string ToString() => Title;

        public static List<FriendlyListRow> ForFirewall(IEnumerable<FirewallRules.InvalidRule> rules) =>
            GroupByProgram(rules).Select(group =>
            {
                var first = group.First();
                int count = group.Count();
                return new FriendlyListRow
                {
                    Title = ProgramName(first),
                    Detail = ExpandedPath(first.ApplicationPath),
                    Extra = count > 1 ? RuleList(group) : first.Name,
                    Badge = count > 1 ? Loc.F("{0} rules", count) : Loc.T(first.Direction),
                    FullText = first.ApplicationPath
                };
            }).ToList();

        public static List<FriendlyListRow> ForFirewallResult(IEnumerable<FirewallRules.InvalidRule> rules, IEnumerable<FirewallRules.InvalidRule> failed)
        {
            var failedRules = new HashSet<FirewallRules.InvalidRule>(failed);
            return GroupByProgram(rules).Select(group =>
            {
                var first = group.First();
                bool problem = group.Any(failedRules.Contains);
                return new FriendlyListRow
                {
                    Title = ProgramName(first),
                    Detail = ExpandedPath(first.ApplicationPath),
                    Extra = RuleList(group),
                    Badge = Loc.T(problem ? "Could not remove" : "Removed"),
                    FullText = first.ApplicationPath,
                    Tone = problem ? RowTone.Problem : RowTone.Good
                };
            }).ToList();
        }

        public static List<FriendlyListRow> ForShortcuts(IEnumerable<ShortcutFixer.BrokenShortcut> shortcuts) =>
            shortcuts.Select(ForShortcut).ToList();

        public static FriendlyListRow ForShortcut(ShortcutFixer.BrokenShortcut shortcut) => new()
        {
            Title = shortcut.Name,
            Detail = $"{Loc.T(shortcut.Location)} · {shortcut.TargetPath}",
            Extra = shortcut.Action,
            Badge = Loc.T(shortcut.RepairTarget != null ? "Can be repaired" : "Goes to Recycle Bin"),
            FullText = shortcut.Action + Environment.NewLine + shortcut.TargetPath
        };

        public static FriendlyListRow ForShortcutResult(ShortcutFixer.BrokenShortcut shortcut, bool failed) => new()
        {
            Title = shortcut.Name,
            Detail = $"{Loc.T(shortcut.Location)} · {shortcut.TargetPath}",
            Badge = Loc.T(failed ? "Could not fix" : shortcut.RepairTarget != null ? "Repaired" : "Moved to Recycle Bin"),
            FullText = shortcut.TargetPath,
            Tone = failed ? RowTone.Problem : RowTone.Good
        };

        private static IEnumerable<IGrouping<string, FirewallRules.InvalidRule>> GroupByProgram(IEnumerable<FirewallRules.InvalidRule> rules) =>
            rules.GroupBy(rule => rule.ApplicationPath, StringComparer.OrdinalIgnoreCase);

        private static string ProgramName(FirewallRules.InvalidRule rule)
        {
            string name = rule.Name.Trim();
            bool readable = name.Length > 0 && !name.StartsWith("@", StringComparison.Ordinal) && !name.StartsWith("{", StringComparison.Ordinal) && name.IndexOfAny(new[] { '\\', '/', ':' }) < 0;
            if (readable)
                return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name.Substring(0, name.Length - 4) : name;
            string fromPath = Path.GetFileNameWithoutExtension(rule.ApplicationPath);
            return fromPath.Length > 0 ? fromPath : rule.ApplicationPath;
        }

        private static string RuleList(IEnumerable<FirewallRules.InvalidRule> rules) =>
            string.Join(", ", rules.Select(rule => $"{rule.Name} ({Loc.T(rule.Direction)})").Distinct(StringComparer.OrdinalIgnoreCase));

        private static string ExpandedPath(string path) => Environment.ExpandEnvironmentVariables(path);
    }

    public sealed class PathTitleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string text = value?.ToString() ?? string.Empty;
            string name = Path.GetFileName(text.TrimEnd('\\', '/'));
            return name.Length > 0 ? name : text;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    public sealed class PathFolderConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string text = (value?.ToString() ?? string.Empty).TrimEnd('\\', '/');
            return Path.GetDirectoryName(text) ?? string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
