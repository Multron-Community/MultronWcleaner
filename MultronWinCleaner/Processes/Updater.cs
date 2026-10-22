using Multron_Win_Cleaner;
using Octokit;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.IO.Compression;
namespace MultronWinCleaner.Processes
{
    public class Updater
    {
        MainWindow main;
        CancellationTokenSource cts = new CancellationTokenSource();
        public Updater(MainWindow main)
        {
            this.main = main;
        }
        public async Task dotsAsync(string text, CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (main.cancelstatus.IsCancellationRequested)
                        break;
                    await main.Dispatcher.InvokeAsync(() => {
                        main.StatusLoad.Text = text + ".";
                        main.StatusLoad.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1e88e5"));
                    });

                    await Task.Delay(1000, cancellationToken);

                    await main.Dispatcher.InvokeAsync(() => {
                        main.StatusLoad.Text = text + "..";
                    });

                    await Task.Delay(1000, cancellationToken);

                    await main.Dispatcher.InvokeAsync(() => {
                        main.StatusLoad.Text = text + "...";
                    });

                    await Task.Delay(1000, cancellationToken);
                }

            }
            catch (TaskCanceledException)
            {

            }
        }
        public const string AutoUpdateSettingKey = "databaseautoupdate";
        public const string AutoUpdateHoursSettingKey = "databaseupdatehours";
        public const string AutoReloadSettingKey = "databaseautoreload";
        public const string StatusSettingKey = "databaseupdatestatus";
        public const string ButtonSettingKey = "databaseupdatebutton";
        public const string RepoOwner = "Multron-Community";
        public const string DatabaseReleasesUrl = "https://github.com/" + RepoOwner + "/MultronWcleaner-Database/releases/latest";
        private const string LastCheckSettingKey = "databaselastcheck";
        public static readonly int[] UpdateIntervals = { 1, 2, 6, 12, 24 };
        public const int DefaultUpdateHours = 2;

        private static readonly SemaphoreSlim databaseLock = new SemaphoreSlim(1, 1);

        private static string SettingsPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Settings.txt");

        private static string? ReadSetting(string key)
        {
            try
            {
                if (!File.Exists(SettingsPath)) return null;
                foreach (string line in File.ReadAllLines(SettingsPath))
                    if (line.StartsWith(key + ":", StringComparison.OrdinalIgnoreCase))
                        return line.Substring(key.Length + 1).Trim();
            }
            catch { }
            return null;
        }

        private static void WriteSetting(string key, string value)
        {
            try
            {
                var lines = File.Exists(SettingsPath) ? File.ReadAllLines(SettingsPath).ToList() : new List<string>();
                int index = lines.FindIndex(l => l.StartsWith(key + ":", StringComparison.OrdinalIgnoreCase));
                string entry = key + ":" + value;
                if (index != -1) lines[index] = entry; else lines.Add(entry);
                File.WriteAllLines(SettingsPath, lines);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Could not save " + key + ": " + ex.Message);
            }
        }

        public static bool IsAutoUpdateEnabled() => ReadSetting(AutoUpdateSettingKey) != "0";
        public static bool IsAutoReloadEnabled() => ReadSetting(AutoReloadSettingKey) != "0";
        public static bool IsStatusEnabled() => ReadSetting(StatusSettingKey) != "0";
        public static bool IsButtonEnabled() => ReadSetting(ButtonSettingKey) != "0";

        public static int GetUpdateHours() =>
            int.TryParse(ReadSetting(AutoUpdateHoursSettingKey), out int hours) && UpdateIntervals.Contains(hours) ? hours : DefaultUpdateHours;

        public static void SaveSetting(string key, string value) => WriteSetting(key, value);

        public static bool IsUpdateCheckDue()
        {
            string? value = ReadSetting(LastCheckSettingKey);
            if (!long.TryParse(value, out long ticks)) return true;
            return DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc) >= TimeSpan.FromHours(GetUpdateHours());
        }

        public static TimeSpan? GetDatabaseAge()
        {
            DateTime? newest = null;
            if (long.TryParse(ReadSetting(LastCheckSettingKey), out long ticks))
                newest = new DateTime(ticks, DateTimeKind.Utc);
            string databaseFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "database.txt");
            if (File.Exists(databaseFile))
            {
                DateTime written = File.GetLastWriteTimeUtc(databaseFile);
                if (newest == null || written > newest) newest = written;
            }
            return newest == null ? null : DateTime.UtcNow - newest.Value;
        }

        public static async Task<string?> UpdateDatabaseAsync(IProgress<int>? progress)
        {
            await databaseLock.WaitAsync();
            try
            {
                string repo = "MultronWcleaner-Database";
                string owner = RepoOwner;
                var client = new GitHubClient(new ProductHeaderValue("MultronWcleaner-Database"));
                var latest = await client.Repository.Release.GetLatest(owner, repo);
                WriteSetting(LastCheckSettingKey, DateTime.UtcNow.Ticks.ToString());
                string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
                string versionFile = Path.Combine(appDirectory, "databaseversion.txt");
                string databaseFile = Path.Combine(appDirectory, "database.txt");
                string getversion = "1.0";
                if (File.Exists(versionFile))
                {
                    getversion = (await File.ReadAllTextAsync(versionFile)).Trim();
                }

                var match = Regex.Match(latest.Name ?? "", @"\d+\.\d+(\.\d+)?");
                if (!match.Success)
                    return null;
                Version latestVersion = new Version(match.Value);
                if (!Version.TryParse(getversion, out Version? currentVersion))
                {
                    currentVersion = new Version(1, 0);
                }

                if (latestVersion <= currentVersion && File.Exists(databaseFile))
                    return null;
                var asset = latest.Assets.FirstOrDefault(a => a.Name.EndsWith(".txt"));
                if (asset == null)
                    return null;
                string? manifestUrl = latest.Assets.FirstOrDefault(a => a.Name == UpdateSignature.ManifestName)?.BrowserDownloadUrl;
                string? signatureUrl = latest.Assets.FirstOrDefault(a => a.Name == UpdateSignature.SignatureName)?.BrowserDownloadUrl;
                if (!UpdateSignature.IsTrustedUrl(asset.BrowserDownloadUrl)
                    || !UpdateSignature.IsTrustedUrl(manifestUrl)
                    || !UpdateSignature.IsTrustedUrl(signatureUrl))
                    throw new InvalidDataException(Loc.T("The update does not come from the official GitHub releases."));
                string expectedHash;
                using (var signedHttp = new HttpClient { Timeout = TimeSpan.FromMinutes(2) })
                {
                    signedHttp.DefaultRequestHeaders.UserAgent.ParseAdd("MultronWcleaner/" + CurrentAppVersion);
                    byte[] manifest = await signedHttp.GetByteArrayAsync(manifestUrl);
                    string signature = await signedHttp.GetStringAsync(signatureUrl);
                    var hashes = UpdateSignature.VerifyManifest(manifest, signature, UpdateKey.DatabasePublicKey,
                        UpdateSignature.DatabaseManifest, latestVersion, currentVersion, !File.Exists(databaseFile));
                    expectedHash = UpdateSignature.ExpectedHash(hashes, asset.Name);
                }

                var downloadFolder = Path.Combine(appDirectory, "Update");
                Directory.CreateDirectory(downloadFolder);
                var filePath = Path.Combine(downloadFolder, asset.Name);
                using (var http = new HttpClient())
                using (var response = await http.GetAsync(asset.BrowserDownloadUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();

                    var totalBytes = response.Content.Headers.ContentLength ?? -1L;

                    using (var stream = await response.Content.ReadAsStreamAsync())
                    using (var file = File.Create(filePath))
                    {
                        var buffer = new byte[81920];
                        long totalRead = 0;
                        int read;
                        int lastProgress = -1;

                        while ((read = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await file.WriteAsync(buffer, 0, read);
                            totalRead += read;

                            if (totalBytes > 0)
                            {
                                int percent = (int)((totalRead * 100L) / totalBytes);
                                if (percent != lastProgress)
                                {
                                    lastProgress = percent;
                                    progress?.Report(percent);
                                }
                            }
                        }
                    }
                }

                UpdateSignature.VerifyFile(filePath, expectedHash);
                string downloaded = await File.ReadAllTextAsync(filePath);
                if (!downloaded.Contains("{=") || !downloaded.Contains("}"))
                {
                    throw new InvalidDataException(Loc.T("The downloaded database is incomplete."));
                }
                string tempDatabase = databaseFile + ".tmp";
                File.Copy(filePath, tempDatabase, overwrite: true);
                File.Move(tempDatabase, databaseFile, overwrite: true);
                await File.WriteAllTextAsync(versionFile, latestVersion.ToString());
                return latestVersion.ToString();
            }
            finally
            {
                databaseLock.Release();
            }
        }

        public async Task downloadlatestdatabase()
        {
            _ = dotsAsync(Loc.T("Checking for latest database"), cts.Token);
            try
            {
                bool started = false;
                var progress = new Progress<int>(percent =>
                {
                    if (!started)
                    {
                        started = true;
                        cts.Cancel();
                    }
                    main.Dispatcher.BeginInvoke(() => main.StatusLoad.Text = Loc.F("Downloading Latest Database {0}%", percent));
                });
                await UpdateDatabaseAsync(progress);
                await cts.CancelAsync();
            }
            catch (Exception ex)
            {
                await main.Dispatcher.InvokeAsync(async () =>
                {
                    await cts.CancelAsync();
                    main.StatusLoad.Text = DescribeError(ex);
                });
            }
        }

        public static string DescribeError(Exception ex)
        {
            if (ex is RateLimitExceededException limit)
                return Loc.F("GitHub's update check limit was reached. Try again after {0:t}.", limit.Reset.LocalDateTime);
            if (ex is NotFoundException)
                return Loc.T("No release was found on GitHub.");
            return ex.Message;
        }

        public const string AppUpdateSettingKey = "appautoupdate";
        private const string AppLastCheckSettingKey = "applastcheck";
        public static readonly Version CurrentAppVersion = new Version("1.27");

        public static bool IsAppAutoUpdateEnabled() => ReadSetting(AppUpdateSettingKey) != "0";

        public static bool IsAppUpdateCheckDue()
        {
            if (!long.TryParse(ReadSetting(AppLastCheckSettingKey), out long ticks)) return true;
            return DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc) >= TimeSpan.FromHours(GetUpdateHours());
        }

        public sealed class AppUpdate
        {
            public Version Version = new Version(0, 0);
            public string DownloadUrl = "";
            public string FileName = "";
            public long Size;
            public string PageUrl = "";
            public string? ManifestUrl;
            public string? SignatureUrl;
        }

        public const string UpdatedArgument = "--updated";
        private const string OldFileSuffix = ".mwcold";
        private const string AppExeName = "MultronWinCleaner.exe";
        private static readonly string[] KeptUserFiles = { "settings.txt", "excluded.txt", "selections.txt" };
        private static readonly string[] LegacyUpdaterFiles = { "Updater.exe", "Updater.dll", "Updater.deps.json", "Updater.runtimeconfig.json", "Updater.pdb" };

        public static async Task<AppUpdate?> FindLatestAppReleaseAsync()
        {
            var client = new GitHubClient(new ProductHeaderValue("MultronWcleaner"));
            Release latest;
            try
            {
                latest = await client.Repository.Release.GetLatest(RepoOwner, "MultronWcleaner");
            }
            catch (NotFoundException)
            {
                return null;
            }
            WriteSetting(AppLastCheckSettingKey, DateTime.UtcNow.Ticks.ToString());

            var match = Regex.Match(latest.Name ?? "", @"\d+\.\d+(\.\d+)?");
            if (!match.Success) return null;

            var asset = latest.Assets.FirstOrDefault(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
            if (asset == null) return null;
            string? AssetUrl(string name) => latest.Assets.FirstOrDefault(a => a.Name == name)?.BrowserDownloadUrl;
            return new AppUpdate
            {
                Version = new Version(match.Value),
                DownloadUrl = asset.BrowserDownloadUrl,
                FileName = asset.Name,
                Size = asset.Size,
                PageUrl = latest.HtmlUrl ?? "",
                ManifestUrl = AssetUrl(UpdateSignature.ManifestName),
                SignatureUrl = AssetUrl(UpdateSignature.SignatureName)
            };
        }

        public static async Task<AppUpdate?> FindAppUpdateAsync()
        {
            var latest = await FindLatestAppReleaseAsync();
            return latest != null && latest.Version > CurrentAppVersion ? latest : null;
        }

        public static async Task InstallAppUpdateAsync(AppUpdate update, IProgress<int>? progress)
        {
            string appDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory));
            string downloadFolder = Path.Combine(appDirectory, "Update");
            string stagingFolder = Path.Combine(downloadFolder, "staging");
            Directory.CreateDirectory(downloadFolder);
            if (Directory.Exists(stagingFolder))
                Directory.Delete(stagingFolder, true);

            string zipPath = Path.Combine(downloadFolder, Path.GetFileName(update.FileName));
            try
            {
                IReadOnlyDictionary<string, string> hashes = await FetchSignedHashesAsync(update);
                string expected = UpdateSignature.ExpectedHash(hashes, update.FileName);
                await DownloadAsync(update, zipPath, progress);
                UpdateSignature.VerifyFile(zipPath, expected);
                ExtractPackage(zipPath, stagingFolder);
                string packageRoot = FindPackageRoot(stagingFolder)
                    ?? throw new InvalidDataException(Loc.T("The update package does not contain Multron Win Cleaner."));
                ReplaceAppFiles(packageRoot, appDirectory);
            }
            finally
            {
                TryDelete(zipPath);
                try { if (Directory.Exists(stagingFolder)) Directory.Delete(stagingFolder, true); } catch { }
            }
        }

        private static async Task<IReadOnlyDictionary<string, string>> FetchSignedHashesAsync(AppUpdate update)
        {
            if (!UpdateSignature.IsTrustedUrl(update.DownloadUrl)
                || !UpdateSignature.IsTrustedUrl(update.ManifestUrl)
                || !UpdateSignature.IsTrustedUrl(update.SignatureUrl))
                throw new InvalidDataException(Loc.T("The update does not come from the official GitHub releases."));
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("MultronWcleaner/" + CurrentAppVersion);
            byte[] manifest = await http.GetByteArrayAsync(update.ManifestUrl);
            string signature = await http.GetStringAsync(update.SignatureUrl);
            return UpdateSignature.VerifyManifest(manifest, signature, UpdateKey.PublicKey, update.Version, CurrentAppVersion);
        }

        private static async Task DownloadAsync(AppUpdate update, string zipPath, IProgress<int>? progress)
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("MultronWcleaner/" + CurrentAppVersion);
            using var response = await http.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            long totalBytes = response.Content.Headers.ContentLength ?? update.Size;
            long totalRead = 0;
            using (var stream = await response.Content.ReadAsStreamAsync())
            using (var file = File.Create(zipPath))
            {
                var buffer = new byte[81920];
                int read;
                int lastProgress = -1;
                while ((read = await stream.ReadAsync(buffer)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read));
                    totalRead += read;
                    if (totalBytes > 0)
                    {
                        int percent = (int)(totalRead * 100L / totalBytes);
                        if (percent != lastProgress)
                        {
                            lastProgress = percent;
                            progress?.Report(percent);
                        }
                    }
                }
            }

            if (update.Size > 0 && totalRead != update.Size)
                throw new InvalidDataException(Loc.F("The update download is incomplete ({0} of {1} bytes).", totalRead, update.Size));
        }

        private static void ExtractPackage(string zipPath, string stagingFolder)
        {
            string extractRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(stagingFolder)) + Path.DirectorySeparatorChar;
            using ZipArchive archive = ZipFile.OpenRead(zipPath);
            string? outside = archive.Entries.Select(e => e.FullName)
                .FirstOrDefault(name => !Path.GetFullPath(Path.Combine(stagingFolder, name)).StartsWith(extractRoot, StringComparison.OrdinalIgnoreCase));
            if (outside != null)
                throw new InvalidDataException(Loc.F("The update contains a file outside the update folder and was not installed: {0}", outside));

            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                string destinationPath = Path.GetFullPath(Path.Combine(stagingFolder, entry.FullName));
                if (string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(destinationPath);
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                entry.ExtractToFile(destinationPath, overwrite: true);
            }
        }

        private static string? FindPackageRoot(string stagingFolder)
        {
            if (File.Exists(Path.Combine(stagingFolder, AppExeName)))
                return stagingFolder;
            return Directory.GetDirectories(stagingFolder)
                .FirstOrDefault(dir => File.Exists(Path.Combine(dir, AppExeName)));
        }

        private static void ReplaceAppFiles(string packageRoot, string appDirectory)
        {
            string stamp = DateTime.UtcNow.Ticks.ToString();
            var replaced = new List<(string Target, string? Backup)>();
            try
            {
                foreach (string source in Directory.GetFiles(packageRoot, "*", SearchOption.AllDirectories))
                {
                    string relative = Path.GetRelativePath(packageRoot, source);
                    if (KeptUserFiles.Contains(relative, StringComparer.OrdinalIgnoreCase))
                        continue;

                    string target = Path.Combine(appDirectory, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);

                    string? backup = null;
                    if (File.Exists(target))
                    {
                        backup = target + "." + stamp + OldFileSuffix;
                        File.Move(target, backup);
                    }
                    replaced.Add((target, backup));
                    File.Move(source, target);
                }
            }
            catch
            {
                for (int i = replaced.Count - 1; i >= 0; i--)
                {
                    var (target, backup) = replaced[i];
                    try
                    {
                        if (backup == null || File.Exists(backup))
                            TryDelete(target);
                        if (backup != null && File.Exists(backup))
                            File.Move(backup, target);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("Could not roll back " + target + ": " + ex.Message);
                    }
                }
                throw;
            }
        }

        public static void RestartAfterUpdate()
        {
            string exe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, AppExeName);
            string arguments = UpdatedArgument + " " + Environment.ProcessId + (App.LaunchedFromStartup ? " -startup" : "");
            Process.Start(new ProcessStartInfo(exe, arguments)
            {
                UseShellExecute = false,
                WorkingDirectory = AppContext.BaseDirectory
            });
        }

        public static void WaitForPreviousInstance(string[] args)
        {
            int index = Array.FindIndex(args, a => a.Equals(UpdatedArgument, StringComparison.OrdinalIgnoreCase));
            if (index < 0 || index + 1 >= args.Length || !int.TryParse(args[index + 1], out int processId))
                return;
            try
            {
                using var previous = Process.GetProcessById(processId);
                previous.WaitForExit(30000);
            }
            catch
            {
            }
        }

        public static void CleanupAfterUpdate()
        {
            string appDirectory = AppContext.BaseDirectory;
            Task.Run(async () =>
            {
                for (int attempt = 0; attempt < 20; attempt++)
                {
                    bool left = false;
                    try
                    {
                        foreach (string file in Directory.EnumerateFiles(appDirectory, "*" + OldFileSuffix, SearchOption.AllDirectories))
                            if (!TryDelete(file))
                                left = true;
                    }
                    catch
                    {
                        left = true;
                    }
                    foreach (string name in LegacyUpdaterFiles)
                    {
                        string file = Path.Combine(appDirectory, name);
                        if (File.Exists(file) && !TryDelete(file))
                            left = true;
                    }
                    foreach (string folder in new[] { "staging", "mwc" })
                    {
                        try
                        {
                            string leftover = Path.Combine(appDirectory, "Update", folder);
                            if (Directory.Exists(leftover))
                                Directory.Delete(leftover, true);
                        }
                        catch
                        {
                        }
                    }
                    if (!left)
                        return;
                    await Task.Delay(1000);
                }
            });
        }

        private static bool TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task checkforapplicationupdates()
        {
            if (!IsAppAutoUpdateEnabled())
                return;
            _ = dotsAsync(Loc.T("Checking For Application Updates"), cts.Token);
            try
            {
                var update = await FindAppUpdateAsync();
                await cts.CancelAsync();
                if (update == null)
                    return;

                var progress = new Progress<int>(percent => main.Dispatcher.BeginInvoke(() => main.StatusLoad.Text = Loc.F("Downloading Update {0}%", percent)));
                await InstallAppUpdateAsync(update, progress);
                await main.Dispatcher.InvokeAsync(() =>
                {
                    main.StatusLoad.Text = Loc.T("Restarting to finish the update...");
                    RestartAfterUpdate();
                    main.ExitApplication();
                });
            }
            catch (Exception ex)
            {
                await main.Dispatcher.InvokeAsync(async () =>
                {
                    await cts.CancelAsync();
                    main.StatusLoad.Text = DescribeError(ex);
                });
            }
        }
        public async Task run()
        {
            await checkforapplicationupdates();
            await downloadlatestdatabase();
        }
    }
}
