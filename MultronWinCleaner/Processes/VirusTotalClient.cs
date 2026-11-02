using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MultronWinCleaner.Processes
{
    public sealed class VirusTotalReport
    {
        public int Malicious { get; init; }
        public int Suspicious { get; init; }
        public int Undetected { get; init; }
        public int Harmless { get; init; }
        public int Total { get; init; }
        public string Name { get; init; } = "";
        public DateTime? LastAnalysis { get; init; }
        public List<(string Engine, string Result)> Detections { get; init; } = new();
    }

    public sealed class VirusTotalException : Exception
    {
        public VirusTotalException(string message) : base(message) { }
    }

    public static class VirusTotalClient
    {
        public const long MaxUploadBytes = 32L * 1024 * 1024;
        private const string ApiBase = "https://www.virustotal.com/api/v3/";
        private const string ProtectedPrefix = "dpapi:";
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("MultronWinCleaner.VirusTotal");

        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(3) };

        public static string FileUrl(string sha256) => "https://www.virustotal.com/gui/file/" + sha256.ToLowerInvariant();

        public static string ProtectKey(string apiKey)
        {
            if (apiKey.Length == 0) return "";
            byte[] data = ProtectedData.Protect(Encoding.UTF8.GetBytes(apiKey), Entropy, DataProtectionScope.CurrentUser);
            return ProtectedPrefix + Convert.ToBase64String(data);
        }

        public static string UnprotectKey(string? stored)
        {
            if (string.IsNullOrEmpty(stored) || !stored.StartsWith(ProtectedPrefix, StringComparison.Ordinal))
                return "";
            try
            {
                byte[] data = ProtectedData.Unprotect(Convert.FromBase64String(stored.Substring(ProtectedPrefix.Length)), Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(data);
            }
            catch
            {
                return "";
            }
        }

        public static async Task<VirusTotalReport?> GetReportAsync(string apiKey, string sha256, CancellationToken ct)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ApiBase + "files/" + sha256.ToLowerInvariant());
            request.Headers.Add("x-apikey", apiKey);
            using var response = await Http.SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return null;
            await ThrowIfFailedAsync(response, ct);

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var attributes = json.RootElement.GetProperty("data").GetProperty("attributes");
            var stats = attributes.TryGetProperty("last_analysis_stats", out var s) ? s : default;
            int Stat(string name) => stats.ValueKind == JsonValueKind.Object && stats.TryGetProperty(name, out var v) && v.TryGetInt32(out int n) ? n : 0;

            var detections = new List<(string, string)>();
            if (attributes.TryGetProperty("last_analysis_results", out var results) && results.ValueKind == JsonValueKind.Object)
            {
                foreach (var engine in results.EnumerateObject())
                {
                    string category = engine.Value.TryGetProperty("category", out var c) ? c.GetString() ?? "" : "";
                    if (category is "malicious" or "suspicious")
                    {
                        string result = engine.Value.TryGetProperty("result", out var r) ? r.GetString() ?? category : category;
                        detections.Add((engine.Name, result));
                    }
                }
            }

            DateTime? last = attributes.TryGetProperty("last_analysis_date", out var d) && d.TryGetInt64(out long unix)
                ? DateTimeOffset.FromUnixTimeSeconds(unix).LocalDateTime
                : null;
            string name = attributes.TryGetProperty("meaningful_name", out var m) ? m.GetString() ?? "" : "";
            int malicious = Stat("malicious"), suspicious = Stat("suspicious"), undetected = Stat("undetected"), harmless = Stat("harmless");
            return new VirusTotalReport
            {
                Malicious = malicious,
                Suspicious = suspicious,
                Undetected = undetected,
                Harmless = harmless,
                Total = malicious + suspicious + undetected + harmless + Stat("timeout") + Stat("type-unsupported") + Stat("failure"),
                Name = name,
                LastAnalysis = last,
                Detections = detections.OrderBy(x => x.Item1, StringComparer.OrdinalIgnoreCase).ToList()
            };
        }

        public static async Task UploadAsync(string apiKey, string path, CancellationToken ct)
        {
            using var stream = ForceDelete.OpenRead(path, 1 << 16, useAsync: true);
            using var content = new MultipartFormDataContent();
            var file = new StreamContent(stream);
            file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            content.Add(file, "file", Path.GetFileName(path));
            using var request = new HttpRequestMessage(HttpMethod.Post, ApiBase + "files") { Content = content };
            request.Headers.Add("x-apikey", apiKey);
            using var response = await Http.SendAsync(request, ct);
            await ThrowIfFailedAsync(response, ct);
        }

        private static async Task ThrowIfFailedAsync(HttpResponseMessage response, CancellationToken ct)
        {
            if (response.IsSuccessStatusCode)
                return;
            string detail = "";
            try
            {
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                if (json.RootElement.TryGetProperty("error", out var error) && error.TryGetProperty("message", out var message))
                    detail = message.GetString() ?? "";
            }
            catch { }
            throw new VirusTotalException(response.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => Loc.T("VirusTotal rejected the API key. Check the key in the Malware Scan settings."),
                HttpStatusCode.TooManyRequests => Loc.T("The VirusTotal API limit was reached (a free key allows 4 requests a minute and 500 a day). Try again later."),
                _ => Loc.F("VirusTotal answered with an error ({0}). {1}", (int)response.StatusCode, detail).Trim()
            });
        }
    }
}
