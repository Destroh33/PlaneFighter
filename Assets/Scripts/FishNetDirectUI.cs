using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FishNet.Managing;
using FishNet.Transporting.Tugboat;
using FishNet.Transporting;          // ClientConnectionStateArgs / LocalConnectionState
using FishNet.Connection;            // NetworkConnection

public class FishNetDirectUI : MonoBehaviour
{
    [Header("Refs")]
    public NetworkManager networkManager;
    public Tugboat tugboat;

    [Header("UI")]
    public Button hostBtn;
    public Button serverBtn;
    public Button joinBtn;
    public TMP_InputField clientAddress; // host or host:port (e.g., abcd.pr.edgegap.net:30987)
    public TMP_Text statusText;

    void Awake()
    {
        Application.runInBackground = true;
        if (!networkManager) networkManager = FindFirstObjectByType<NetworkManager>();
        if (!tugboat) tugboat = FindFirstObjectByType<Tugboat>();
    }

    void OnEnable()
    {
        if (networkManager != null)
        {
            networkManager.ClientManager.OnClientConnectionState += OnClientConnectionState;
            networkManager.ClientManager.OnAuthenticated += OnClientAuthenticated;
            networkManager.SceneManager.OnClientLoadedStartScenes += OnClientLoadedStartScenes;
        }

        if (hostBtn) hostBtn.onClick.AddListener(StartHost);
        if (serverBtn) serverBtn.onClick.AddListener(StartServerOnly);
        if (joinBtn) joinBtn.onClick.AddListener(StartClient);
    }

    void OnDisable()
    {
        if (networkManager != null)
        {
            networkManager.ClientManager.OnClientConnectionState -= OnClientConnectionState;
            networkManager.ClientManager.OnAuthenticated -= OnClientAuthenticated;
            networkManager.SceneManager.OnClientLoadedStartScenes -= OnClientLoadedStartScenes;
        }

        if (hostBtn) hostBtn.onClick.RemoveListener(StartHost);
        if (serverBtn) serverBtn.onClick.RemoveListener(StartServerOnly);
        if (joinBtn) joinBtn.onClick.RemoveListener(StartClient);
    }

    void StartHost()
    {
        Log("[UI] Host clicked.");
        bool s = networkManager.ServerManager.StartConnection();
        bool c = networkManager.ClientManager.StartConnection();
        Log(s && c ? "[Host] started." : "[Host] start failed.");
    }

    void StartServerOnly()
    {
        Log("[UI] Server clicked.");
        bool ok = networkManager.ServerManager.StartConnection();
        Log(ok ? "[Server] started." : "[Server] start failed.");
    }

    void StartClient()
    {
        if (!tugboat) { Log("[Client] No Tugboat transport found."); return; }

        ushort defaultPort = tugboat.GetPort();
        if (!TryParseHostPort(clientAddress ? clientAddress.text : null, defaultPort,
                              out string host, out ushort port))
        {
            Log("Enter host or host:port (e.g., abcd.pr.edgegap.net:30987).");
            return;
        }

        tugboat.SetPort(port);
        tugboat.SetClientAddress(host);

        Log($"[Client] Connecting to {host}:{port} ...");
        if (!networkManager.ClientManager.StartConnection())
            Log("[Client] StartConnection() returned false.");
    }

    // === State logging ===
    void OnClientConnectionState(ClientConnectionStateArgs args)
    {
        // LocalConnectionState: Starting, Started, Stopping, Stopped
        Log($"[ClientState] {args.ConnectionState}");
    }

    void OnClientAuthenticated()
    {
        Log("[Client] Authenticated with server (fully connected).");
    }

    void OnClientLoadedStartScenes(NetworkConnection conn, bool asServer)
    {
        if (!asServer) // only log for the actual client, not host's server side
            Log($"[Client] Loaded start scenes. ClientId={conn.ClientId}");
    }

    // host / host:port / [ipv6]:port
    static bool TryParseHostPort(string input, ushort defaultPort, out string host, out ushort port)
    {
        host = "127.0.0.1";
        port = defaultPort;

        if (string.IsNullOrWhiteSpace(input))
            return false;

        input = input.Trim();

        // IPv6 bracket form: [addr]:port
        if (input.StartsWith("["))
        {
            int close = input.IndexOf(']');
            if (close <= 0) return false;
            host = input.Substring(1, close - 1);

            if (close + 1 < input.Length && input[close + 1] == ':')
            {
                string ps = input.Substring(close + 2);
                if (!ushort.TryParse(ps, out port)) return false;
            }
            return true;
        }

        // IPv4 / hostname with optional single colon
        int idx = input.LastIndexOf(':');
        if (idx > 0 && idx == input.IndexOf(':'))
        {
            host = input.Substring(0, idx);
            string ps = input[(idx + 1)..];
            if (!ushort.TryParse(ps, out port)) return false;
            return true;
        }

        // No colon: just a host
        host = input;
        return true;
    }

    void Log(string s)
    {
        Debug.Log($"[DirectUI] {s}");
        if (statusText) statusText.text = s;
    }
}
