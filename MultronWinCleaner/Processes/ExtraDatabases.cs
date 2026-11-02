using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MultronWinCleaner.Processes
{
    public sealed class ExtraDatabase
    {
        public string FileName { get; init; } = "";
        public string Name => Path.GetFileNameWithoutExtension(FileName);
        public string FilePath => Path.Combine(ExtraDatabases.Folder, FileName);
        public string? Url { get; init; }
        public bool Enabled { get; init; }
    }

    public static class ExtraDatabases
    {
        private const long MaxDownloadBytes = 5L * 1024 * 1024;
        private const string ConfigFileName = "databases.json";

        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
        private static readonly object ConfigLock = new();

        public static string Folder => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Databases");
        private static string ConfigPath => Path.Combine(Folder, ConfigFileName);

        private sealed class Config
        {
            public List<string> Disabled { get; set; } = new();
            public Dictionary<string, string> Urls { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        }

        private static Config ReadConfig()
        {
            try
            {
                if (File.Exists(ConfigPath))
                    return JsonSerializer.Deserialize<Config>(File.ReadAllText(ConfigPath)) ?? new Config();
            }
            catch { }
            return new Config();
        }

        private static void WriteConfig(Config config)
        {
            Directory.CreateDirectory(Folder);
            string tmp = ConfigPath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, ConfigPath, overwrite: true);
        }

        public static List<ExtraDatabase> List()
        {
            if (!Directory.Exists(Folder))
                return new List<ExtraDatabase>();
            Config config;
            lock (ConfigLock) config = ReadConfig();
            var disabled = new HashSet<string>(config.Disabled, StringComparer.OrdinalIgnoreCase);
            return Directory.GetFiles(Folder, "*.txt")
                .Select(Path.GetFileName)
                .OfType<string>()
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .Select(f => new ExtraDatabase
                {
                    FileName = f,
                    Url = config.Urls.TryGetValue(f, out string? url) ? url : null,
                    Enabled = !disabled.Contains(f)
                })
                .ToList();
        }

        public static void SetEnabled(string fileName, bool enabled)
        {
            lock (ConfigLock)
            {
                var config = ReadConfig();
                config.Disabled.RemoveAll(f => f.Equals(fileName, StringComparison.OrdinalIgnoreCase));
                if (!enabled)
                    config.Disabled.Add(fileName);
                WriteConfig(config);
            }
        }

        public static void Remove(string fileName)
        {
            lock (ConfigLock)
            {
                var config = ReadConfig();
                config.Disabled.RemoveAll(f => f.Equals(fileName, StringComparison.OrdinalIgnoreCase));
                config.Urls.Remove(fileName);
                WriteConfig(config);
            }
            string path = Path.Combine(Folder, fileName);
            if (File.Exists(path))
                File.Delete(path);
        }

        public static async Task<ExtraDatabase> AddFromUrlAsync(string url, CancellationToken ct)
        {
            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps)
                throw new InvalidDataException(Loc.T("Enter an https:// address of a database .txt file."));

            string text = await DownloadAsync(uri, ct);
            string fileName = UniqueFileName(NameFromUrl(uri));
            Directory.CreateDirectory(Folder);
            await File.WriteAllTextAsync(Path.Combine(Folder, fileName), text, Encoding.UTF8, ct);
            lock (ConfigLock)
            {
                var config = ReadConfig();
                config.Urls[fileName] = uri.ToString();
                WriteConfig(config);
            }
            return new ExtraDatabase { FileName = fileName, Url = uri.ToString(), Enabled = true };
        }

        public static ExtraDatabase AddFromFile(string sourcePath)
        {
            string full = Path.GetFullPath(sourcePath);
            if (string.Equals(full, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "database.txt"), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(Loc.T("This is the official database. It is always loaded."));
            if (string.Equals(Path.GetDirectoryName(full), Path.GetFullPath(Folder), StringComparison.OrdinalIgnoreCase))
            {
                string existing = Path.GetFileName(full);
                SetEnabled(existing, true);
                return new ExtraDatabase { FileName = existing, Enabled = true };
            }
            if (new FileInfo(full).Length > MaxDownloadBytes)
                throw new InvalidDataException(Loc.F("The database is larger than {0} MB.", MaxDownloadBytes / (1024 * 1024)));

            string text = File.ReadAllText(full);
            Validate(text);
            if (Directory.Exists(Folder))
            {
                foreach (string existingPath in Directory.GetFiles(Folder, "*.txt"))
                {
                    string existingText;
                    try { existingText = File.ReadAllText(existingPath); }
                    catch { continue; }
                    if (existingText != text)
                        continue;
                    string existingName = Path.GetFileName(existingPath);
                    SetEnabled(existingName, true);
                    return new ExtraDatabase { FileName = existingName, Enabled = true };
                }
            }
            var invalid = Path.GetInvalidFileNameChars();
            string stem = new string(Path.GetFileNameWithoutExtension(full).Select(c => invalid.Contains(c) || c == '(' || c == ')' || c == '=' ? '_' : c).ToArray()).Trim();
            if (stem.Length == 0 || stem.Equals("database", StringComparison.OrdinalIgnoreCase))
                stem = "custom";
            Directory.CreateDirectory(Folder);
            string fileName = UniqueFileName((stem.Length > 40 ? stem.Substring(0, 40) : stem) + ".txt");
            File.WriteAllText(Path.Combine(Folder, fileName), text, Encoding.UTF8);
            SetEnabled(fileName, true);
            return new ExtraDatabase { FileName = fileName, Enabled = true };
        }

        private static void Validate(string text)
        {
            if (text.IndexOf('\0') >= 0 || !text.Split('\n').Any(l => l.TrimStart().StartsWith("{=")))
                throw new InvalidDataException(Loc.T("This is not a Multron Win Cleaner database: no {=Group lines were found."));
        }

        public static async Task<(int Changed, List<string> Errors)> RefreshFromUrlsAsync(CancellationToken ct)
        {
            int changed = 0;
            var errors = new List<string>();
            foreach (var database in List().Where(d => d.Url != null && d.Enabled))
            {
                try
                {
                    string text = await DownloadAsync(new Uri(database.Url!), ct);
                    if (File.Exists(database.FilePath) && await File.ReadAllTextAsync(database.FilePath, ct) == text)
                        continue;
                    string tmp = database.FilePath + ".tmp";
                    await File.WriteAllTextAsync(tmp, text, Encoding.UTF8, ct);
                    File.Move(tmp, database.FilePath, overwrite: true);
                    changed++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    errors.Add(database.Name + ": " + ex.Message);
                }
            }
            return (changed, errors);
        }

        public static List<string> ReadEnabledLines(Dictionary<string, string> headerSources)
        {
            var lines = new List<string>();
            foreach (var database in List().Where(d => d.Enabled))
            {
                string[] fileLines;
                try { fileLines = File.ReadAllLines(database.FilePath); }
                catch { continue; }

                bool open = false;
                foreach (string raw in fileLines)
                {
                    string line = raw.Trim();
                    if (line.Length == 0)
                        continue;
                    if (line.StartsWith("{"))
                    {
                        if (open)
                            lines.Add("}");
                        string[] parts = line.Split('=');
                        if (parts.Length < 2 || parts[1].Trim().Length == 0)
                        {
                            open = false;
                            continue;
                        }
                        parts[1] = parts[1].Trim() + " (" + database.Name.Replace("=", "-") + ")";
                        string header = string.Join("=", parts);
                        headerSources[header] = database.Name;
                        lines.Add(header);
                        open = true;
                    }
                    else if (line.StartsWith("}"))
                    {
                        if (open)
                            lines.Add("}");
                        open = false;
                    }
                    else if (open && !IsCommandLine(line))
                    {
                        lines.Add(line);
                    }
                }
                if (open)
                    lines.Add("}");
            }
            return lines;
        }

        private static bool IsCommandLine(string line)
        {
            string name = line.Split('=')[0];
            return name.Contains("cleanmgr.exe", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Dism.exe", StringComparison.OrdinalIgnoreCase)
                || name.Contains("sfc.exe", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Deep Log Files Scan", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("USER_", StringComparison.Ordinal);
        }

        private static async Task<string> DownloadAsync(Uri uri, CancellationToken ct)
        {
            using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaxDownloadBytes)
                throw new InvalidDataException(Loc.F("The database is larger than {0} MB.", MaxDownloadBytes / (1024 * 1024)));

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var buffer = new MemoryStream();
            byte[] chunk = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(chunk, ct)) > 0)
            {
                buffer.Write(chunk, 0, read);
                if (buffer.Length > MaxDownloadBytes)
                    throw new InvalidDataException(Loc.F("The database is larger than {0} MB.", MaxDownloadBytes / (1024 * 1024)));
            }

            string text = Encoding.UTF8.GetString(buffer.ToArray());
            Validate(text);
            return text;
        }

        private static string NameFromUrl(Uri uri)
        {
            string name = Path.GetFileNameWithoutExtension(Uri.UnescapeDataString(uri.Segments.LastOrDefault() ?? ""));
            if (name.Length == 0 || name.Equals("database", StringComparison.OrdinalIgnoreCase))
                name = uri.Host;
            var invalid = Path.GetInvalidFileNameChars();
            name = new string(name.Select(c => invalid.Contains(c) || c == '(' || c == ')' || c == '=' ? '_' : c).ToArray()).Trim();
            return (name.Length == 0 ? "database" : name.Length > 40 ? name.Substring(0, 40) : name) + ".txt";
        }

        private static string UniqueFileName(string fileName)
        {
            string stem = Path.GetFileNameWithoutExtension(fileName);
            string candidate = fileName;
            for (int i = 2; File.Exists(Path.Combine(Folder, candidate)); i++)
                candidate = stem + "_" + i + ".txt";
            return candidate;
        }
    }
}
