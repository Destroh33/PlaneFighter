using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FishNet.Managing;
using FishNet.Transporting.Tugboat;
using FishNet.Transporting;
using FishNet.Connection;
using System;

public class FishNetDirectUI : MonoBehaviour
{
    public NetworkManager networkManager;
    public Tugboat tugboat;
    public EdgegapSessionDeployer edgegap;

    public Button hostBtn;
    public Button serverBtn;
    public Button joinBtn;
    public TMP_InputField clientAddress;
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

    async void StartHost()
    {
        if (edgegap == null)
        {
            Log("[Host] No Edgegap deployer assigned.");
            return;
        }
        SetInteractable(false);
        try
        {
            Log("[Host] Deploying server…");
            var res = await edgegap.CreateAndWaitAsync();
            if (clientAddress) clientAddress.text = res.joinCode;
            Log($"Server Ready → {res.fqdn}:{res.externalPort}\nJoin Code: {res.joinCode}");
        }
        catch (Exception ex)
        {
            Log($"[Host] Failed: {ex.Message}");
        }
        finally { SetInteractable(true); }
    }

    void StartServerOnly()
    {
        Log("[UI] Server clicked (local dedicated).");
        bool ok = networkManager.ServerManager.StartConnection();
        Log(ok ? "[Server] started." : "[Server] start failed.");
    }

    void StartClient()
    {
        if (!tugboat) { Log("[Client] No Tugboat transport found."); return; }
        ushort defaultPort = tugboat.GetPort();
        string input = clientAddress ? clientAddress.text : null;

        if (!TryParseHostPortOrJoinCode(input, defaultPort, out string host, out ushort port))
        {
            Log("Enter a join code OR host or host:port");
            return;
        }

        //if (networkManager.ServerManager.IsOnline)
            networkManager.ServerManager.StopConnection(true);
        //if (networkManager.ClientManager.ConnectionState != LocalConnectionState.Stopped)
            networkManager.ClientManager.StopConnection();

        tugboat.SetPort(port);
        tugboat.SetClientAddress(host);

        Log($"[Client] Connecting to {host}:{port} …");
        if (!networkManager.ClientManager.StartConnection())
            Log("[Client] StartConnection() returned false.");
    }

    void OnClientConnectionState(ClientConnectionStateArgs args)
    {
        Log($"[ClientState] {args.ConnectionState}");
    }

    void OnClientAuthenticated()
    {
        Log("[Client] Authenticated with server.");
    }

    void OnClientLoadedStartScenes(NetworkConnection conn, bool asServer)
    {
        if (!asServer) Log($"[Client] Loaded start scenes. ClientId={conn.ClientId}");
    }

    static bool TryParseHostPortOrJoinCode(string input, ushort defaultPort, out string host, out ushort port)
    {
        host = "127.0.0.1"; port = defaultPort;
        if (string.IsNullOrWhiteSpace(input)) return false;
        input = input.Trim();
        if (!input.Contains('.') && !input.Contains(':') && EGJoinCode.TryDecodeToHostPort(input, out host, out port))
            return true;
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
        int idx = input.LastIndexOf(':');
        if (idx > 0 && idx == input.IndexOf(':'))
        {
            host = input.Substring(0, idx);
            string ps = input[(idx + 1)..];
            if (!ushort.TryParse(ps, out port)) return false;
            return true;
        }
        host = input;
        return true;
    }

    void SetInteractable(bool on)
    {
        if (hostBtn) hostBtn.interactable = on;
        if (serverBtn) serverBtn.interactable = on;
        if (joinBtn) joinBtn.interactable = on;
        if (clientAddress) clientAddress.interactable = on;
    }

    void Log(string s)
    {
        Debug.Log($"[DirectUI] {s}");
        if (statusText) statusText.text = s;
    }
}
