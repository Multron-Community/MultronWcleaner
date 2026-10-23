using System;
using System.IO;
using System.Linq;

namespace MultronWinCleaner.Processes
{
    public static class DeepLogOptions
    {
        public const string ExtensionsKey = "logextensions";
        public const string MinAgeKey = "logminage";
        public const string MinSizeKey = "logminkb";
        public const string DefaultExtensions = ".log.etl.dmp.trace.tmp.temp.bak.swp.old.chk.mdmp.hdmp.wer.err.gid";

        public static string Extensions { get; private set; } = DefaultExtensions;
        public static int MinAgeDays { get; private set; }
        public static int MinSizeKb { get; private set; }

        public static void Load()
        {
            Extensions = DefaultExtensions;
            MinAgeDays = 0;
            MinSizeKb = 0;
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Settings.txt");
                if (!File.Exists(path)) return;
                foreach (string line in File.ReadAllLines(path))
                {
                    int i = line.IndexOf(':');
                    if (i <= 0) continue;
                    string key = line.Substring(0, i).Trim();
                    string value = line.Substring(i + 1).Trim();
                    if (key.Equals(ExtensionsKey, StringComparison.OrdinalIgnoreCase) && IsValidExtensionList(value))
                        Extensions = value;
                    else if (key.Equals(MinAgeKey, StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out int days))
                        MinAgeDays = Math.Clamp(days, 0, 3650);
                    else if (key.Equals(MinSizeKey, StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out int kb))
                        MinSizeKb = Math.Clamp(kb, 0, 10_000_000);
                }
            }
            catch { }
        }

        public static bool IsValidExtensionList(string value) =>
            System.Text.RegularExpressions.Regex.IsMatch(value ?? "", @"^(\.[a-zA-Z0-9]+)+$");

        public static bool Matches(string file, string extensions) =>
            extensions.Split('.', StringSplitOptions.RemoveEmptyEntries)
                .Any(ext => file.EndsWith("." + ext, StringComparison.OrdinalIgnoreCase));

        public static bool PassesFilters(long size, double modifiedDays) =>
            (MinAgeDays <= 0 || modifiedDays >= MinAgeDays) && (MinSizeKb <= 0 || size >= MinSizeKb * 1024L);
    }
}
