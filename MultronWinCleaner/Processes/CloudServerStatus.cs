using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace MultronWinCleaner.Processes
{
    public enum CloudServerState { Online, BackgroundUpdate, Updating, Switching, Maintenance, Unreachable, NoInternet }

    public sealed record CloudServerHealth(CloudServerState State, string ServerMessage, int? LatencyMs = null);

    public static class CloudServerStatus
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

        public static Func<string, CancellationToken, Task<CloudServerHealth>>? Override { get; set; }

        public static event Action<bool>? ConnectionObserved;

        public static void ReportConnection(bool connected) => ConnectionObserved?.Invoke(connected);

        public static bool BlocksScans(CloudServerState state) =>
            state is CloudServerState.Updating or CloudServerState.Switching or CloudServerState.Maintenance
                or CloudServerState.Unreachable or CloudServerState.NoInternet;

        public static Uri? HealthUri(string serverUrl)
        {
            if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out Uri? uri))
                return null;
            var builder = new UriBuilder(uri)
            {
                Scheme = uri.Scheme == "ws" ? "http" : "https",
                Path = "/health",
                Query = "",
                Port = uri.IsDefaultPort ? -1 : uri.Port
            };
            return builder.Uri;
        }

        public static async Task<CloudServerHealth> CheckAsync(string serverUrl, CancellationToken ct, TimeSpan? timeout = null)
        {
            if (Override != null)
                return await Override(serverUrl, ct);
            if (!System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable())
                return new CloudServerHealth(CloudServerState.NoInternet, "");
            Uri? uri = HealthUri(serverUrl);
            if (uri == null)
                return new CloudServerHealth(CloudServerState.Unreachable, "");
            try
            {
                using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
                limit.CancelAfter(timeout ?? TimeSpan.FromSeconds(6));
                var timer = System.Diagnostics.Stopwatch.StartNew();
                using var response = await Http.GetAsync(uri, limit.Token);
                string body = await response.Content.ReadAsStringAsync(limit.Token);
                int latency = (int)Math.Round(timer.Elapsed.TotalMilliseconds);
                if (!response.IsSuccessStatusCode)
                    return new CloudServerHealth(CloudServerState.Unreachable, "HTTP " + (int)response.StatusCode);
                return Parse(body) with { LatencyMs = latency };
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                return new CloudServerHealth(CloudServerState.Unreachable, "");
            }
        }

        public static CloudServerHealth Parse(string body)
        {
            JObject json;
            try
            {
                json = JObject.Parse(body);
            }
            catch (Exception)
            {
                return new CloudServerHealth(CloudServerState.Unreachable, "");
            }
            string message = (string?)json["message"] ?? "";
            string mode = (string?)json["maintenanceMode"] ?? ((bool?)json["maintenance"] == true ? "maintenance" : "off");
            var state = mode switch
            {
                "off" => CloudServerState.Online,
                "getting_updated" => CloudServerState.BackgroundUpdate,
                "updating" => CloudServerState.Updating,
                "switching" => CloudServerState.Switching,
                _ => CloudServerState.Maintenance
            };
            return new CloudServerHealth(state, message);
        }

        public static string PillText(CloudServerState state) => state switch
        {
            CloudServerState.Online => Loc.T("Server online"),
            CloudServerState.BackgroundUpdate => Loc.T("Updating in the background"),
            CloudServerState.Updating or CloudServerState.Switching => Loc.T("Server updating"),
            CloudServerState.Maintenance => Loc.T("Under maintenance"),
            CloudServerState.NoInternet => Loc.T("No internet"),
            _ => Loc.T("Server offline")
        };

        public static string Title(CloudServerState state) => state switch
        {
            CloudServerState.BackgroundUpdate => Loc.T("The cloud scan server is being updated in the background"),
            CloudServerState.Updating => Loc.T("The cloud scan server is being updated"),
            CloudServerState.Switching => Loc.T("The cloud scan server is switching to its new version"),
            CloudServerState.Maintenance => Loc.T("The cloud scan server is under maintenance"),
            CloudServerState.NoInternet => Loc.T("You are not connected to the internet"),
            CloudServerState.Unreachable => Loc.T("The cloud scan server cannot be reached"),
            _ => Loc.T("The cloud scan server is online")
        };

        public static string Detail(CloudServerState state) => state switch
        {
            CloudServerState.BackgroundUpdate => Loc.T("Scans continue as usual. You do not need to do anything."),
            CloudServerState.Updating => Loc.T("Scans cannot start for a few minutes. Try again shortly."),
            CloudServerState.Switching => Loc.T("Try again in a few seconds."),
            CloudServerState.Maintenance => Loc.T("Scans cannot start right now. Try again later."),
            CloudServerState.NoInternet => Loc.T("Connect to the internet to use the cloud scan."),
            CloudServerState.Unreachable => Loc.T("The server may be down, or your connection or firewall may be blocking it. Scans cannot start until it is back."),
            _ => ""
        };
    }
}
