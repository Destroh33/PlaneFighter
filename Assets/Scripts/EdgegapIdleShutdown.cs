using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using FishNet.Managing;
using FishNet.Transporting;
using UnityEngine;

public class EdgegapIdleShutdown : MonoBehaviour
{
    public NetworkManager networkManager;
    public float idleSeconds = 30f;

    int _activeClients = 0;
    float _emptySince = -1f;
    bool _everHadAClient = false;
    bool _stopRequested = false;
    bool _stopAttempted = false;

    HttpClient _http;

    void Awake()
    {
        if (!networkManager) networkManager = FindFirstObjectByType<NetworkManager>();
        _http = new HttpClient();
        string url = Environment.GetEnvironmentVariable("ARBITRIUM_DELETE_URL");
        string token = Environment.GetEnvironmentVariable("ARBITRIUM_DELETE_TOKEN");
        string tokenPreview = string.IsNullOrEmpty(token) ? "null" : (token.Length <= 6 ? token : token.Substring(0, 6) + "•••");
        Debug.Log($"[IdleShutdown] Env check → URL: {(string.IsNullOrEmpty(url) ? "missing" : "present")}, TOKEN: {tokenPreview}");
    }

    void OnEnable()
    {
        if (networkManager != null)
            networkManager.ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;
    }

    void OnDisable()
    {
        if (networkManager != null)
            networkManager.ServerManager.OnRemoteConnectionState -= OnRemoteConnectionState;
    }

    void Update()
    {
        if (_stopRequested || _stopAttempted) return;
        if (!_everHadAClient) return;

        if (_activeClients <= 0)
        {
            if (_emptySince < 0f) _emptySince = Time.unscaledTime;
            else if (Time.unscaledTime - _emptySince >= idleSeconds)
            {
                _stopRequested = true;
                _ = StopDeploymentAsync();
            }
        }
        else
        {
            _emptySince = -1f;
        }
    }

    void OnRemoteConnectionState(FishNet.Connection.NetworkConnection conn, RemoteConnectionStateArgs args)
    {
        if (args.ConnectionState == RemoteConnectionState.Started)
        {
            _activeClients++;
            _everHadAClient = true;
        }
        else if (args.ConnectionState == RemoteConnectionState.Stopped)
        {
            _activeClients = Mathf.Max(0, _activeClients - 1);
        }
    }

    async Task StopDeploymentAsync()
    {
        string url = Environment.GetEnvironmentVariable("ARBITRIUM_DELETE_URL");
        string token = Environment.GetEnvironmentVariable("ARBITRIUM_DELETE_TOKEN");

        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(token))
        {
            Debug.LogWarning("[IdleShutdown] Missing ARBITRIUM_DELETE_URL or ARBITRIUM_DELETE_TOKEN; aborting.");
            _stopAttempted = true;
            return;
        }

        try
        {
            var r = await TryDeleteRaw(url, token);                         // authorization: <token>
            if (!r.ok && r.status == HttpStatusCode.Unauthorized)
                r = await TryDeleteBearer(url, token);                      // Authorization: Bearer <token>
            if (!r.ok && r.status == HttpStatusCode.Unauthorized)
                r = await TryDeleteTokenScheme(url, token);                 // Authorization: token <token>

            if (r.ok)
                Debug.Log("[IdleShutdown] Deployment stop requested successfully.");
            else
                Debug.LogWarning($"[IdleShutdown] Stop failed: {(int)r.status} {r.body}");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[IdleShutdown] Exception: {ex.Message}");
        }
        finally
        {
            _stopAttempted = true;  // do not spam attempts
        }
    }

    async Task<(bool ok, HttpStatusCode status, string body)> TryDeleteRaw(string url, string token)
    {
        using var req = new HttpRequestMessage(HttpMethod.Delete, url);
        req.Headers.TryAddWithoutValidation("authorization", token);
        using var resp = await _http.SendAsync(req);
        string body = await resp.Content.ReadAsStringAsync();
        bool ok = ((int)resp.StatusCode >= 200 && (int)resp.StatusCode < 300);
        if (!ok && resp.StatusCode == HttpStatusCode.Unauthorized)
            Debug.LogWarning("[IdleShutdown] 401 with raw token; retrying with Bearer.");
        return (ok, resp.StatusCode, body);
    }

    async Task<(bool ok, HttpStatusCode status, string body)> TryDeleteBearer(string url, string token)
    {
        using var req = new HttpRequestMessage(HttpMethod.Delete, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var resp = await _http.SendAsync(req);
        string body = await resp.Content.ReadAsStringAsync();
        bool ok = ((int)resp.StatusCode >= 200 && (int)resp.StatusCode < 300);
        if (!ok && resp.StatusCode == HttpStatusCode.Unauthorized)
            Debug.LogWarning("[IdleShutdown] 401 with Bearer; retrying with 'token ' scheme.");
        return (ok, resp.StatusCode, body);
    }

    async Task<(bool ok, HttpStatusCode status, string body)> TryDeleteTokenScheme(string url, string token)
    {
        using var req = new HttpRequestMessage(HttpMethod.Delete, url);
        req.Headers.TryAddWithoutValidation("Authorization", $"token {token}");
        using var resp = await _http.SendAsync(req);
        string body = await resp.Content.ReadAsStringAsync();
        bool ok = ((int)resp.StatusCode >= 200 && (int)resp.StatusCode < 300);
        return (ok, resp.StatusCode, body);
    }
}
