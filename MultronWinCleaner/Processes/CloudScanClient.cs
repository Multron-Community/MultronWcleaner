using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Security;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MultronWinCleaner.Processes
{
    public sealed class CloudScanFatalException : Exception
    {
        public CloudScanFatalException(string message, Exception? inner = null) : base(message, inner)
        {
        }
    }

    public sealed class CloudScanClient : IDisposable
    {
        public const string ServerUrl = "wss://api.viruskov.com/ws";

        public const string ServerCertSha256 = "";

        public static string ServerToken = "";

        public static readonly bool DeveloperServerUrlBox = false;

        public static bool IsServerConfigured => ServerUrl.Length > 0;

        public const int ProtocolVersion = 3;
        private const int CheckBatchDelayMs = 15;
        private const int MaxJsonMessageBytes = 1024 * 1024;
        private const int UploadChunkSize = 256 * 1024;

        private readonly Uri serverUri;
        private readonly string pinnedCertSha256;
        private readonly SemaphoreSlim connectLock = new(1, 1);
        private Connection? current;
        private long nextId;

        public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(15);
        public TimeSpan ResultTimeout { get; set; } = TimeSpan.FromMinutes(10);

        public bool IsConnected => current?.IsUsable == true;

        public bool SupportsReports(bool falseNegative) =>
            current is { } c && (falseNegative ? c.SupportsFalseNegativeReports : c.SupportsFalsePositiveReports);

        public CloudScanClient() : this(ServerUrl, ServerCertSha256)
        {
        }

        public CloudScanClient(string serverUrl, string pinnedCertSha256)
        {
            serverUri = new Uri(serverUrl);
            if (serverUri.Scheme == "ws" && !serverUri.IsLoopback)
                throw new InvalidOperationException(Loc.T("Unencrypted ws:// addresses are only allowed for a server on this PC (localhost). Use wss:// for other servers."));
            this.pinnedCertSha256 = NormalizeThumbprint(pinnedCertSha256);
        }

        public static string NormalizeThumbprint(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            var sb = new StringBuilder();
            foreach (char c in value)
                if (Uri.IsHexDigit(c)) sb.Append(char.ToUpperInvariant(c));
            return sb.ToString();
        }

        public async Task ConnectAsync(CancellationToken ct)
        {
            if (!MalwareScanEula.IsAccepted)
                throw new InvalidOperationException(Loc.T("The license agreement was not accepted. The scan did not start."));
            if (MultronWinCleaner.MalwareScan.IsOfflineMode)
                throw new InvalidOperationException(Loc.T("Malware scan is disabled in offline mode."));

            current?.Fault(new IOException(Loc.T("Reconnecting.")));

            var connection = new Connection();
            try
            {
                await connection.OpenAsync(serverUri, pinnedCertSha256.Length > 0 ? ValidateServerCertificate : null, ConnectTimeout, ct);
            }
            catch
            {
                connection.Fault(new IOException(Loc.T("Connect failed.")));
                throw;
            }
            current = connection;
        }

        private static readonly TimeSpan[] ReconnectDelays =
        {
            TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8),
            TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30)
        };
        private int reconnectFailures;
        private DateTime nextConnectAttemptUtc = DateTime.MinValue;
        private DateTime? unreachableSinceUtc;
        private CloudScanFatalException? giveUpError;

        public TimeSpan ReconnectGiveUp { get; set; } = TimeSpan.FromMinutes(4);

        public async Task EnsureConnectedAsync(CancellationToken ct)
        {
            if (IsConnected) return;
            await connectLock.WaitAsync(ct);
            try
            {
                if (IsConnected) return;
                if (giveUpError != null) throw giveUpError;

                TimeSpan wait = nextConnectAttemptUtc - DateTime.UtcNow;
                if (wait > TimeSpan.Zero)
                    await Task.Delay(wait, ct);

                try
                {
                    await ConnectAsync(ct);
                    reconnectFailures = 0;
                    unreachableSinceUtc = null;
                    nextConnectAttemptUtc = DateTime.MinValue;
                }
                catch (Exception ex) when (!ct.IsCancellationRequested && IsTransientError(ex))
                {
                    DateTime now = DateTime.UtcNow;
                    unreachableSinceUtc ??= now;
                    if (now - unreachableSinceUtc.Value >= ReconnectGiveUp)
                    {
                        giveUpError = new CloudScanFatalException(Loc.F("Could not reach the cloud scan server: {0}", ex.Message), ex);
                        throw giveUpError;
                    }
                    nextConnectAttemptUtc = now + ReconnectDelays[Math.Min(reconnectFailures, ReconnectDelays.Length - 1)];
                    reconnectFailures++;
                    throw;
                }
            }
            finally
            {
                connectLock.Release();
            }
        }

        public static bool IsTransientError(Exception ex) =>
            ex is IOException or WebSocketException or TimeoutException or ObjectDisposedException
                or OperationCanceledException or System.Net.Http.HttpRequestException;

        private bool ValidateServerCertificate(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors errors)
        {
            if (errors == SslPolicyErrors.None)
                return true;

            if (certificate != null && pinnedCertSha256.Length > 0)
            {
                string actual = certificate.GetCertHashString(HashAlgorithmName.SHA256);
                return string.Equals(actual, pinnedCertSha256, StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        public async Task<CloudScanResult> ScanFileAsync(string path, IProgress<double>? uploadProgress, CancellationToken ct, bool rescan = false)
        {
            Connection connection = current is { IsUsable: true } c ? c : throw new IOException(Loc.T("Not connected."));

            var result = new CloudScanResult { FilePath = path };

            try
            {
                (result.Sha256, result.Size) = await HashFileAsync(path, ct);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                result.Verdict = "error";
                result.Detail = Loc.F("Cannot read file: {0}", ex.Message);
                return result;
            }

            long id = Interlocked.Increment(ref nextId);
            var pending = new PendingScan();
            connection.Pending[id] = pending;
            try
            {
                if (!connection.IsUsable)
                    throw new IOException(Loc.T("Connection lost."));

                if (connection.SupportsCheck)
                {
                    var check = new JObject
                    {
                        ["id"] = id,
                        ["name"] = Path.GetFileName(path),
                        ["size"] = result.Size,
                        ["sha256"] = result.Sha256,
                        ["folder"] = NormalizeFolder(path),
                        ["path"] = FullFolder(path)
                    };
                    if (rescan)
                        check["rescan"] = true;
                    connection.QueueCheck(check);

                    JObject answer;
                    try
                    {
                        answer = await pending.Check.Task.WaitAsync(ResultTimeout, ct);
                    }
                    catch (TimeoutException)
                    {
                        result.Verdict = "error";
                        result.Detail = Loc.T("The cloud server did not answer in time");
                        return result;
                    }

                    if ((string?)answer["type"] != "need_upload")
                        return Fill(result, answer);
                }

                return await UploadAndScanAsync(connection, pending, id, path, result, uploadProgress, ct, rescan);
            }
            finally
            {
                connection.Pending.TryRemove(id, out _);
            }
        }

        public async Task<VerdictReport> ReportVerdictAsync(bool falseNegative, string sha256, string fileName, string note, CancellationToken ct)
        {
            Connection connection = current is { IsUsable: true } c ? c : throw new IOException(Loc.T("Not connected."));
            if (!SupportsReports(falseNegative))
                throw new CloudScanFatalException(falseNegative
                    ? Loc.T("The cloud server does not accept false negative reports yet.")
                    : Loc.T("The cloud server does not accept false positive reports yet."));

            long id = Interlocked.Increment(ref nextId);
            var pending = new PendingScan();
            connection.Pending[id] = pending;
            try
            {
                await connection.SendLock.WaitAsync(ct);
                try
                {
                    await connection.SendJsonAsync(new JObject
                    {
                        ["type"] = falseNegative ? "report_fn" : "report_fp",
                        ["id"] = id,
                        ["sha256"] = sha256,
                        ["name"] = fileName,
                        ["note"] = note
                    }, ct);
                }
                catch (Exception ex)
                {
                    connection.Fault(ex);
                    throw;
                }
                finally
                {
                    connection.SendLock.Release();
                }

                JObject reply;
                try
                {
                    reply = await pending.Final.Task.WaitAsync(ResultTimeout, ct);
                }
                catch (TimeoutException)
                {
                    throw new IOException(Loc.T("The cloud server did not answer in time"));
                }

                if ((string?)reply["type"] == "error")
                    throw new CloudScanFatalException((string?)reply["message"] ?? Loc.T("Server error"));

                return new VerdictReport
                {
                    Status = (string?)reply["status"] ?? "pending",
                    Verdict = (string?)reply["verdict"] ?? "",
                    ThreatName = (string?)reply["threat_name"] ?? "",
                    Note = (string?)reply["note"] ?? "",
                    FileOnServer = (bool?)reply["file_on_server"] ?? true
                };
            }
            finally
            {
                connection.Pending.TryRemove(id, out _);
            }
        }

        private async Task<CloudScanResult> UploadAndScanAsync(Connection connection, PendingScan pending, long id, string path,
            CloudScanResult result, IProgress<double>? uploadProgress, CancellationToken ct, bool rescan = false)
        {
            await connection.UploadSlots.WaitAsync(ct);
            try
            {
                byte[]? data;
                try
                {
                    data = await ReadFileAsync(path, ct);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    result.Verdict = "error";
                    result.Detail = Loc.F("Cannot read file: {0}", ex.Message);
                    return result;
                }

                result.Size = data.Length;
                result.Sha256 = Convert.ToHexString(SHA256.HashData(data));

                byte[] payload = data;
                if (connection.SupportsBrotli && ShouldTryCompress(path, data.Length))
                {
                    byte[] source = data;
                    byte[]? packed = await Task.Run(() => TryBrotli(source), ct);
                    if (packed != null)
                        payload = packed;
                }
                bool compressed = !ReferenceEquals(payload, data);

                await connection.SendLock.WaitAsync(ct);
                try
                {
                    var scanMsg = new JObject
                    {
                        ["type"] = "scan",
                        ["id"] = id,
                        ["name"] = Path.GetFileName(path),
                        ["size"] = result.Size,
                        ["sha256"] = result.Sha256,
                        ["folder"] = NormalizeFolder(path),
                        ["path"] = FullFolder(path)
                    };
                    if (rescan)
                        scanMsg["rescan"] = true;
                    if (compressed)
                    {
                        scanMsg["encoding"] = "br";
                        scanMsg["csize"] = payload.Length;
                    }
                    await connection.SendJsonAsync(scanMsg, ct);

                    JObject ack = await pending.Ack.Task.WaitAsync(ResultTimeout, ct);
                    if ((string?)ack["type"] == "send_file")
                        await connection.SendBytesAsync(payload, uploadProgress, ct);
                }
                catch (Exception ex)
                {
                    connection.Fault(ex);
                    throw;
                }
                finally
                {
                    connection.SendLock.Release();
                }
                data = null;
            }
            finally
            {
                connection.UploadSlots.Release();
            }

            JObject reply;
            try
            {
                reply = await pending.Final.Task.WaitAsync(ResultTimeout, ct);
            }
            catch (TimeoutException)
            {
                result.Verdict = "error";
                result.Detail = Loc.T("The viruskov.com OPEN-EDR cloud engine did not answer in time");
                return result;
            }
            return Fill(result, reply);
        }

        private static CloudScanResult Fill(CloudScanResult result, JObject reply)
        {
            string type = (string?)reply["type"] ?? "";
            if (type == "result")
            {
                var ecs = reply.ToObject<EcsScanResult>() ?? new EcsScanResult();
                result.Ecs = ecs;

                result.Verdict = ecs.EffectiveVerdict.ToLowerInvariant();
                result.ThreatName = ecs.Threat?.Indicator?.Name ?? ecs.Rule?.Name ?? "";

                if (ecs.Antivirus.Detections is { Count: > 0 } detections)
                {
                    var details = new List<string>();
                    foreach (var d in detections)
                    {
                        details.Add(string.IsNullOrEmpty(d.Details) ? $"{d.Name} ({d.Layer})" : $"{d.Name} ({d.Layer}): {d.Details}");
                    }
                    result.Detail = string.Join(" — ", details);
                }
                else if (!string.IsNullOrEmpty(ecs.Antivirus.Detail))
                {
                    result.Detail = ecs.Antivirus.Detail;
                }
                else if (result.Verdict == "clean")
                {
                    result.Detail = Loc.T("Clean (viruskov.com Verified)");
                }
                else if (result.Verdict == "possible_clean")
                {
                    result.Detail = Loc.T("Possibly clean (very similar to a verified clean file)");
                }

                if (!string.IsNullOrEmpty(ecs.File.Hash?.Sha256))
                    result.Sha256 = ecs.File.Hash.Sha256;
                if (ecs.File.Size > 0)
                    result.Size = ecs.File.Size;
            }
            else
            {
                result.Verdict = "error";
                result.Detail = (string?)reply["message"] ?? Loc.T("Server error");
            }
            return result;
        }

        private static async Task<(string Sha256, long Size)> HashFileAsync(string path, CancellationToken ct)
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, UploadChunkSize, useAsync: true);
            byte[] hash = await SHA256.HashDataAsync(file, ct);
            return (Convert.ToHexString(hash), file.Length);
        }

        private static readonly Lazy<(string Name, string Path)[]> FolderPlaceholders = new(() =>
        {
            var list = new List<(string Name, string Path)>();
            void Add(string name, string? value)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    list.Add((name, value.TrimEnd('\\', '/')));
            }
            Add("%TEMP%", System.IO.Path.GetTempPath());
            Add("%LOCALAPPDATA%", Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            Add("%APPDATA%", Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
            Add("%USERPROFILE%", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            Add("%PROGRAMFILES(X86)%", Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
            Add("%PROGRAMFILES%", Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
            Add("%PROGRAMDATA%", Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));
            Add("%WINDIR%", Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            return list.OrderByDescending(x => x.Path.Length).ToArray();
        });

        internal static string FullFolder(string path)
        {
            try
            {
                string? dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
                if (string.IsNullOrEmpty(dir))
                    return "";
                dir = dir.TrimEnd('\\', '/');
                return dir.Length > 1024 ? dir.Substring(0, 1024) : dir;
            }
            catch
            {
                return "";
            }
        }

        internal static string NormalizeFolder(string path)
        {
            try
            {
                string? dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
                if (string.IsNullOrEmpty(dir))
                    return "";
                dir = dir.TrimEnd('\\', '/');
                foreach (var (name, value) in FolderPlaceholders.Value)
                {
                    if (dir.Equals(value, StringComparison.OrdinalIgnoreCase))
                        return name;
                    if (dir.StartsWith(value + "\\", StringComparison.OrdinalIgnoreCase))
                        return Trim260(name + dir.Substring(value.Length));
                }
                if (dir.Length > 9 && dir[1] == ':' && dir.Substring(2).StartsWith("\\Users\\", StringComparison.OrdinalIgnoreCase))
                {
                    int end = dir.IndexOf('\\', 9);
                    string user = end < 0 ? dir.Substring(9) : dir.Substring(9, end - 9);
                    if (!user.Equals("Public", StringComparison.OrdinalIgnoreCase) && !user.Equals("Default", StringComparison.OrdinalIgnoreCase))
                        return Trim260("%USERPROFILE%" + (end < 0 ? "" : dir.Substring(end)));
                }
                return Trim260(dir);
            }
            catch
            {
                return "";
            }

            static string Trim260(string s) => s.Length > 260 ? s.Substring(0, 260) : s;
        }

        private static readonly HashSet<string> IncompressibleExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".zip", ".7z", ".rar", ".gz", ".tgz", ".bz2", ".xz", ".zst", ".lz4", ".cab", ".msi", ".jar", ".apk",
            ".docx", ".xlsx", ".pptx", ".odt", ".epub", ".pdf", ".jpg", ".jpeg", ".png", ".gif", ".webp",
            ".mp3", ".mp4", ".mkv", ".avi", ".mov", ".webm", ".ogg", ".flac"
        };

        private static bool ShouldTryCompress(string path, int length) =>
            length >= 4096 && !IncompressibleExtensions.Contains(Path.GetExtension(path));

        private static byte[]? TryBrotli(byte[] data)
        {
            var dest = new byte[BrotliEncoder.GetMaxCompressedLength(data.Length)];
            if (!BrotliEncoder.TryCompress(data, dest, out int written, quality: 5, window: 22))
                return null;
            if (written >= data.Length - data.Length / 10)
                return null;
            return dest.AsSpan(0, written).ToArray();
        }

        private static async Task<byte[]> ReadFileAsync(string path, CancellationToken ct)
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, UploadChunkSize, useAsync: true);
            using var buffer = new MemoryStream(file.Length > 0 && file.Length < int.MaxValue ? (int)file.Length : 0);
            await file.CopyToAsync(buffer, ct);
            return buffer.Length == buffer.Capacity ? buffer.GetBuffer() : buffer.ToArray();
        }

        public async Task CloseAsync()
        {
            Connection? connection = current;
            if (connection is not { IsUsable: true }) return;
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await connection.SendLock.WaitAsync(cts.Token);
                try
                {
                    await connection.Socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", cts.Token);
                }
                finally
                {
                    connection.SendLock.Release();
                }
            }
            catch { }
        }

        public void Dispose()
        {
            current?.Fault(new IOException(Loc.T("Client closed.")));
            current = null;
        }

        private sealed class PendingScan
        {
            public readonly TaskCompletionSource<JObject> Check = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public readonly TaskCompletionSource<JObject> Ack = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public readonly TaskCompletionSource<JObject> Final = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private sealed class Connection
        {
            public readonly ClientWebSocket Socket = new();
            public readonly ConcurrentDictionary<long, PendingScan> Pending = new();
            public readonly SemaphoreSlim SendLock = new(1, 1);
            private readonly Channel<JObject> checkQueue = Channel.CreateUnbounded<JObject>(new UnboundedChannelOptions { SingleReader = true });
            private int faulted;

            public bool SupportsCheck { get; private set; }
            public bool SupportsBrotli { get; private set; }
            public bool SupportsFalsePositiveReports { get; private set; }
            public bool SupportsFalseNegativeReports { get; private set; }
            public int CheckBatchSize { get; private set; } = 256;
            public SemaphoreSlim UploadSlots { get; private set; } = new(4, 4);

            public bool IsUsable => Volatile.Read(ref faulted) == 0 && Socket.State == WebSocketState.Open;

            public void QueueCheck(JObject item)
            {
                if (!checkQueue.Writer.TryWrite(item))
                    throw new IOException(Loc.T("Connection lost."));
            }

            private async Task CheckSendLoopAsync()
            {
                var reader = checkQueue.Reader;
                try
                {
                    while (await reader.WaitToReadAsync())
                    {
                        await Task.Delay(CheckBatchDelayMs);
                        var items = new JArray();
                        while (items.Count < CheckBatchSize && reader.TryRead(out JObject? item))
                            items.Add(item);
                        if (items.Count == 0)
                            continue;

                        await SendLock.WaitAsync();
                        try
                        {
                            await SendJsonAsync(new JObject { ["type"] = "check", ["items"] = items }, CancellationToken.None);
                        }
                        finally
                        {
                            SendLock.Release();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Fault(ex);
                }
            }

            public async Task OpenAsync(Uri uri, RemoteCertificateValidationCallback? certificateValidation, TimeSpan timeout, CancellationToken ct)
            {
                using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                connectCts.CancelAfter(timeout);

                Socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                if (certificateValidation != null)
                    Socket.Options.RemoteCertificateValidationCallback = certificateValidation;

                JObject reply;
                try
                {
                    await Socket.ConnectAsync(uri, connectCts.Token);

                    await SendJsonAsync(new JObject
                    {
                        ["type"] = "hello",
                        ["version"] = ProtocolVersion,
                        ["client"] = "MultronWinCleaner",
                        ["token"] = ServerToken
                    }, connectCts.Token);

                    reply = await ReceiveJsonAsync(connectCts.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    throw new TimeoutException(Loc.T("The cloud server did not answer in time"));
                }

                if ((string?)reply["type"] != "hello_ok")
                {
                    string message = (string?)reply["message"] ?? (string?)reply["type"] ?? Loc.T("no reason");
                    throw new CloudScanFatalException(Loc.F("Server rejected the connection: {0}", message));
                }

                SupportsCheck = ((int?)reply["version"] ?? 2) >= 3;
                CheckBatchSize = Math.Clamp((int?)reply["checkBatch"] ?? 256, 1, 1024);
                SupportsBrotli = false;
                if (reply["encodings"] is JArray encodings)
                    foreach (JToken e in encodings)
                        if ((string?)e == "br")
                            SupportsBrotli = true;
                SupportsFalsePositiveReports = false;
                SupportsFalseNegativeReports = false;
                if (reply["features"] is JArray features)
                    foreach (JToken f in features)
                    {
                        if ((string?)f == "report_fp")
                            SupportsFalsePositiveReports = true;
                        else if ((string?)f == "report_fn")
                            SupportsFalseNegativeReports = true;
                    }
                int pipeline = Math.Clamp((int?)reply["pipeline"] ?? 4, 1, 32);
                UploadSlots = new SemaphoreSlim(pipeline, pipeline);

                _ = Task.Run(ReceiveLoopAsync);
                if (SupportsCheck)
                    _ = Task.Run(CheckSendLoopAsync);
            }

            private async Task ReceiveLoopAsync()
            {
                try
                {
                    while (true)
                    {
                        JObject message = await ReceiveJsonAsync(CancellationToken.None);
                        string type = (string?)message["type"] ?? "";
                        long? id = (long?)message["id"];

                        if (id is null or 0)
                        {
                            if (type == "error")
                                throw new IOException(Loc.F("Server error: {0}", (string?)message["message"] ?? Loc.T("unknown")));
                            continue;
                        }
                        if (!Pending.TryGetValue(id.Value, out PendingScan? pending))
                            continue;

                        switch (type)
                        {
                            case "need_upload":
                                pending.Check.TrySetResult(message);
                                break;
                            case "send_file":
                                pending.Ack.TrySetResult(message);
                                break;
                            case "report_result":
                                pending.Final.TrySetResult(message);
                                break;
                            case "result":
                            case "error":
                                pending.Check.TrySetResult(message);
                                pending.Ack.TrySetResult(message);
                                pending.Final.TrySetResult(message);
                                break;
                            default:
                                throw new IOException(Loc.F("Unexpected server message: {0}", type));
                        }
                    }
                }
                catch (Exception ex)
                {
                    Fault(ex);
                }
            }

            public void Fault(Exception reason)
            {
                if (Interlocked.Exchange(ref faulted, 1) == 1) return;
                try { Socket.Abort(); } catch { }
                try { Socket.Dispose(); } catch { }

                checkQueue.Writer.TryComplete();
                var error = reason as IOException ?? new IOException(reason.Message, reason);
                foreach (PendingScan pending in Pending.Values)
                {
                    pending.Check.TrySetException(error);
                    pending.Ack.TrySetException(error);
                    pending.Final.TrySetException(error);
                }
            }

            public async Task SendJsonAsync(JObject message, CancellationToken ct)
            {
                byte[] payload = Encoding.UTF8.GetBytes(message.ToString(Formatting.None));
                await Socket.SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, ct);
            }

            public async Task SendBytesAsync(byte[] data, IProgress<double>? progress, CancellationToken ct)
            {
                int sent = 0;
                while (sent < data.Length)
                {
                    int count = Math.Min(UploadChunkSize, data.Length - sent);
                    await Socket.SendAsync(data.AsMemory(sent, count), WebSocketMessageType.Binary, endOfMessage: true, ct);
                    sent += count;
                    progress?.Report((double)sent / data.Length);
                }
            }

            private async Task<JObject> ReceiveJsonAsync(CancellationToken ct)
            {
                using var message = new MemoryStream();
                byte[] buffer = new byte[16 * 1024];

                while (true)
                {
                    ValueWebSocketReceiveResult received = await Socket.ReceiveAsync(buffer.AsMemory(), ct);

                    if (received.MessageType == WebSocketMessageType.Close)
                        throw new IOException(Loc.F("Server closed the connection: {0}", Socket.CloseStatusDescription ?? Socket.CloseStatus?.ToString() ?? Loc.T("no reason")));
                    if (received.MessageType != WebSocketMessageType.Text)
                        throw new IOException(Loc.T("Unexpected binary message from server."));

                    message.Write(buffer, 0, received.Count);
                    if (message.Length > MaxJsonMessageBytes)
                        throw new IOException(Loc.T("Server message too large."));

                    if (received.EndOfMessage)
                        break;
                }

                return JObject.Parse(Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length));
            }
        }
    }

    public sealed class VerdictReport
    {
        public string Status { get; set; } = "pending";
        public string Verdict { get; set; } = "";
        public string ThreatName { get; set; } = "";
        public string Note { get; set; } = "";
        public bool FileOnServer { get; set; } = true;
        public bool IsCompleted => Status == "completed";
    }

    public class CloudScanResult : System.ComponentModel.INotifyPropertyChanged
    {
        private bool isSelected;

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        public bool IsSelected
        {
            get => isSelected;
            set
            {
                if (isSelected == value) return;
                isSelected = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }
        public string FilePath { get; set; } = "";
        public string FileName => Path.GetFileName(FilePath);
        public long Size { get; set; }
        public string Sha256 { get; set; } = "";
        public EcsScanResult? Ecs { get; set; }

        public string Verdict { get; set; } = "unknown";
        public string ThreatName { get; set; } = "";
        public string Detail { get; set; } = "";
        public string Threat => string.IsNullOrEmpty(ThreatName) ? Detail : ThreatName;
        public string ThreatOrDetail => RemovalDetail.Length == 0 ? Threat : Threat.Length == 0 ? RemovalDetail : Threat + " · " + RemovalDetail;

        public string OriginalVerdict { get; set; } = "";
        public string RemovalDetail { get; set; } = "";
        public DateTime? RemovedAt { get; set; }
        public List<string> RemovalSteps { get; set; } = new();
        public bool IsRemoved => Verdict is "deleted" or "quarantined" or "reboot";

        public CloudScanResult MarkRemoved(string verdict, string detail, IEnumerable<string>? steps = null)
        {
            var copy = (CloudScanResult)MemberwiseClone();
            copy.PropertyChanged = null;
            copy.isSelected = false;
            if (!IsRemoved)
                copy.OriginalVerdict = Verdict;
            copy.Verdict = verdict;
            copy.RemovalDetail = detail;
            copy.RemovedAt = DateTime.Now;
            copy.RemovalSteps = steps?.ToList() ?? new List<string>();
            return copy;
        }

        public string VerdictText => Loc.T(Verdict switch
        {
            "clean" => "Clean",
            "possible_clean" => "Possibly clean",
            "malicious" => "Malicious",
            "suspicious" => "Suspicious",
            "error" => "Error",
            "skipped" => "Skipped",
            "deleted" => "Deleted by user",
            "quarantined" => "Quarantined by user",
            "reboot" => "Deleted at restart",
            _ => "Unknown"
        }, "verdict");

        public string FormattedSize
        {
            get
            {
                double size = Size;
                string[] units = { "B", "KB", "MB", "GB" };
                int unit = 0;
                while (size >= 1024 && unit < units.Length - 1)
                {
                    size /= 1024;
                    unit++;
                }
                return $"{size:0.##} {units[unit]}";
            }
        }
    }
}
