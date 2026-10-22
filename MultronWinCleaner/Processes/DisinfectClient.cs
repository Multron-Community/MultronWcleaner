using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace MultronWinCleaner.Processes
{
    public static class DisinfectClient
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);
        private const long MaxFileBytes = 64L * 1024 * 1024;

        public enum Action { None, RestoreCleanCopy, UseRepaired, Delete, RepairWindows, Unknown }

        public sealed class Report
        {
            public bool Infected;
            public string MalwareName = "";
            public bool Repaired;
            public string RepairedSha256 = "";
            public long RepairedSize;
            public long RemovedBytes;
            public string NotRepairableReason = "";
            public bool SystemFile;
            public Action RecommendedAction = Action.None;
            public string Advice = "";
        }

        public sealed class Outcome
        {
            public Report Report = new();
            public byte[]? RepairedBytes;
            public string Error = "";
            public bool Failed => Error.Length > 0;
        }

        public static string EndpointFor(string serverWsUrl, string path)
        {
            var ws = new Uri(serverWsUrl);
            bool secure = ws.Scheme is "wss" or "https";
            string scheme = secure ? "https" : "http";
            string authority = ws.Host;
            if (ws.Port > 0 && ws.Port != (secure ? 443 : 80))
                authority += ":" + ws.Port;
            return $"{scheme}://{authority}{path}";
        }

        public static async Task<Outcome> DisinfectAsync(string serverWsUrl, string filePath, CancellationToken ct)
        {
            var outcome = new Outcome();

            byte[] data;
            try
            {
                var info = new FileInfo(filePath);
                if (!info.Exists) { outcome.Error = Loc.T("The file no longer exists."); return outcome; }
                if (info.Length > MaxFileBytes) { outcome.Error = Loc.T("The file is larger than the disinfection size limit."); return outcome; }
                data = await File.ReadAllBytesAsync(filePath, ct);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                outcome.Error = Loc.F("Cannot read the file: {0}", ex.Message);
                return outcome;
            }

            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var http = new HttpClient(handler) { Timeout = Timeout };

            JObject report;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, EndpointFor(serverWsUrl, "/api/v1/client-disinfect")) { Content = Body(data, filePath) };
                using HttpResponseMessage response = await http.SendAsync(request, ct);
                string body = await response.Content.ReadAsStringAsync(ct);
                JObject parsed = JObject.Parse(body);
                if ((string?)parsed["status"] != "success")
                {
                    outcome.Error = (string?)parsed["error"] ?? Loc.F("The server returned {0}.", (int)response.StatusCode);
                    return outcome;
                }
                report = parsed["result"] as JObject ?? new JObject();
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                outcome.Error = Loc.F("Could not reach the disinfection service: {0}", ex.Message);
                return outcome;
            }

            outcome.Report = ParseReport(report);

            if (outcome.Report.Repaired &&
                outcome.Report.RecommendedAction is Action.UseRepaired or Action.RestoreCleanCopy)
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, EndpointFor(serverWsUrl, "/api/v1/client-disinfect?download=repaired")) { Content = Body(data, filePath) };
                    using HttpResponseMessage response = await http.SendAsync(request, ct);
                    if (response.IsSuccessStatusCode)
                    {
                        byte[] repaired = await response.Content.ReadAsByteArrayAsync(ct);
                        string actual = Convert.ToHexString(SHA256.HashData(repaired));
                        if (outcome.Report.RepairedSha256.Length == 64 &&
                            !string.Equals(actual, outcome.Report.RepairedSha256, StringComparison.OrdinalIgnoreCase))
                        {
                            outcome.Error = Loc.T("The repaired file did not match the server's checksum, so it was not used.");
                            return outcome;
                        }
                        outcome.RepairedBytes = repaired;
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    outcome.Error = Loc.F("Could not download the repaired file: {0}", ex.Message);
                    return outcome;
                }
            }

            return outcome;
        }

        private static ByteArrayContent Body(byte[] data, string filePath)
        {
            var content = new ByteArrayContent(data);
            content.Headers.TryAddWithoutValidation("X-File-Path", filePath);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            return content;
        }

        private static Report ParseReport(JObject r) => new()
        {
            Infected = (bool?)r["infected"] ?? false,
            MalwareName = (string?)r["malware_name"] ?? "",
            Repaired = (bool?)r["repaired"] ?? false,
            RepairedSha256 = NormalizeHash((string?)r["repaired_sha256"]),
            RepairedSize = (long?)r["repaired_size"] ?? 0,
            RemovedBytes = (long?)r["removed_bytes"] ?? 0,
            NotRepairableReason = (string?)r["not_repairable_reason"] ?? "",
            SystemFile = (bool?)r["system_file"] ?? false,
            RecommendedAction = ParseAction((string?)r["recommended_action"]),
            Advice = (string?)r["advice"] ?? ""
        };

        private static Action ParseAction(string? value) => value switch
        {
            "none" => Action.None,
            "restore_clean_copy" => Action.RestoreCleanCopy,
            "use_repaired" => Action.UseRepaired,
            "delete" => Action.Delete,
            "repair_windows" => Action.RepairWindows,
            _ => Action.Unknown
        };

        private static string NormalizeHash(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            var sb = new System.Text.StringBuilder(value.Length);
            foreach (char c in value)
                if (Uri.IsHexDigit(c)) sb.Append(char.ToUpperInvariant(c));
            return sb.ToString();
        }
    }
}
