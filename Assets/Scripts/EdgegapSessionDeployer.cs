using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

public class EdgegapSessionDeployer : MonoBehaviour
{
    public string apiToken;
    public string appName;
    public string versionName;
    public string preferredPortName = "gameport";
    [Min(0.1f)] public float pollEverySeconds = 0.75f;
    [Min(5)] public int maxPollSeconds = 90;
    public bool useHostPublicIpForPlacement = true;

    private HttpClient _http;

    void Awake()
    {
        _http = new HttpClient();
        if (!string.IsNullOrWhiteSpace(apiToken))
            _http.DefaultRequestHeaders.Add("Authorization", $"token {apiToken}");
    }

    public struct DeployResult
    {
        public string requestId;
        public string fqdn;
        public ushort externalPort;
        public string joinCode;
    }

    public async Task<DeployResult> CreateAndWaitAsync()
    {
        if (string.IsNullOrWhiteSpace(appName))
            throw new Exception("Edgegap appName is empty.");
        if (string.IsNullOrWhiteSpace(versionName))
            throw new Exception("Edgegap versionName is empty.");

        var createUrl = "https://api.edgegap.com/v2/deployments";
        var users = new JArray();
        if (useHostPublicIpForPlacement)
        {
            string ip = await GetPublicIPAsync();
            if (string.IsNullOrWhiteSpace(ip))
                throw new Exception("Could not determine public IP.");
            users.Add(new JObject
            {
                ["user_type"] = "ip_address",
                ["user_data"] = new JObject { ["ip_address"] = ip }
            });
        }
        else
        {
            throw new Exception("No placement strategy configured.");
        }

        var body = new JObject
        {
            ["application"] = appName,
            ["version"] = versionName,
            ["users"] = users
        };

        var content = new StringContent(body.ToString(), Encoding.UTF8, "application/json");
        string requestId;
        using (var resp = await _http.PostAsync(createUrl, content))
        {
            string json = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
                throw new Exception($"Deployment create failed ({(int)resp.StatusCode}): {json}");
            var jo = JObject.Parse(json);
            requestId = jo.Value<string>("request_id");
            if (string.IsNullOrWhiteSpace(requestId))
                throw new Exception("Edgegap: missing request_id in deploy response.");
        }

        var startedAt = Time.realtimeSinceStartup;
        while (true)
        {
            await Task.Delay(TimeSpan.FromSeconds(pollEverySeconds));
            var statusUrl = $"https://api.edgegap.com/v1/status/{requestId}";
            using (var sresp = await _http.GetAsync(statusUrl))
            {
                string sjson = await sresp.Content.ReadAsStringAsync();
                if (!sresp.IsSuccessStatusCode)
                    throw new Exception($"Status failed ({(int)sresp.StatusCode}): {sjson}");

                var sj = JObject.Parse(sjson);
                bool running = sj.Value<bool?>("running") == true;
                var ports = sj["ports"] as JObject;
                string fqdn = sj.Value<string>("fqdn");
                if (running && ports != null && !string.IsNullOrWhiteSpace(fqdn))
                {
                    if (!TrySelectExternalPort(ports, out ushort extPort))
                        throw new Exception("Edgegap: no external port found.");
                    string joinCode = EGJoinCode.EncodeRequestIdAndPort(requestId, extPort);
                    return new DeployResult
                    {
                        requestId = requestId,
                        fqdn = fqdn,
                        externalPort = extPort,
                        joinCode = joinCode
                    };
                }
                bool error = sj.Value<bool?>("error") == true;
                if (error)
                    throw new Exception("Edgegap: deployment entered error state.");
            }
            if (Time.realtimeSinceStartup - startedAt > maxPollSeconds)
                throw new TimeoutException("Edgegap: deployment did not become ready in time.");
        }
    }

    private bool TrySelectExternalPort(JObject ports, out ushort ext)
    {
        ext = 0;
        if (!string.IsNullOrWhiteSpace(preferredPortName) && ports.TryGetValue(preferredPortName, out var tok))
        {
            var p = tok as JObject;
            if (p != null && p["external"] != null)
            {
                ext = (ushort)p.Value<int>("external");
                return true;
            }
        }
        foreach (var kv in ports)
        {
            var p = kv.Value as JObject;
            if (p == null) continue;
            var proto = p.Value<string>("protocol");
            if (!string.IsNullOrEmpty(proto) && proto.ToUpperInvariant().Contains("UDP") && p["external"] != null)
            {
                ext = (ushort)p.Value<int>("external");
                return true;
            }
        }
        foreach (var kv in ports)
        {
            var p = kv.Value as JObject;
            if (p == null) continue;
            if (p["external"] != null)
            {
                ext = (ushort)p.Value<int>("external");
                return true;
            }
        }
        return false;
    }

    private async Task<string> GetPublicIPAsync()
    {
        try
        {
            var r = await _http.GetAsync("https://api.ipify.org");
            if (r.IsSuccessStatusCode)
            {
                string s = (await r.Content.ReadAsStringAsync()).Trim();
                if (!string.IsNullOrWhiteSpace(s)) return s;
            }
            var r2 = await _http.GetAsync("https://checkip.amazonaws.com");
            if (r2.IsSuccessStatusCode)
            {
                string s2 = (await r2.Content.ReadAsStringAsync()).Trim();
                return s2;
            }
        }
        catch { }
        return null;
    }
}
