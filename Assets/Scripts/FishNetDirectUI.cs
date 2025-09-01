using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FishNet.Managing;
using FishNet.Transporting.Tugboat;
using FishNet.Transporting;
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
    public Image backgroundImage;

    bool _lockStatusToJoinCode = false;
    string _lockedStatusText = null;

    void Awake()
    {
        Application.runInBackground = true;
        if (!networkManager) networkManager = FindFirstObjectByType<NetworkManager>();
        if (!tugboat) tugboat = FindFirstObjectByType<Tugboat>();
    }

    void OnEnable()
    {
        if (hostBtn) hostBtn.onClick.AddListener(StartHost);
        if (serverBtn) serverBtn.onClick.AddListener(StartServerOnly);
        if (joinBtn) joinBtn.onClick.AddListener(StartClient);
    }

    void OnDisable()
    {
        if (hostBtn) hostBtn.onClick.RemoveListener(StartHost);
        if (serverBtn) serverBtn.onClick.RemoveListener(StartServerOnly);
        if (joinBtn) joinBtn.onClick.RemoveListener(StartClient);
    }

    async void StartHost()
    {
        if (edgegap == null)
        {
            SetStatus("[Host] No Edgegap deployer assigned.");
            return;
        }
        SetInteractable(false);
        try
        {
            SetStatus("[Host] Deploying server…");
            var res = await edgegap.CreateAndWaitAsync();
            if (clientAddress) clientAddress.text = res.joinCode;
            SetStatus($"Join Code: {res.joinCode}");
        }
        catch (Exception ex)
        {
            SetStatus($"[Host] Failed: {ex.Message}");
        }
        finally { SetInteractable(true); }
    }

    void StartServerOnly()
    {
        SetStatus("[UI] Server clicked (local dedicated).");
        bool ok = networkManager.ServerManager.StartConnection();
        SetStatus(ok ? "[Server] started." : "[Server] start failed.");
    }

    void StartClient()
    {
        if (!tugboat) { SetStatus("[Client] No Tugboat transport found."); return; }

        ushort defaultPort = tugboat.GetPort();
        string input = clientAddress ? clientAddress.text : null;

        if (!TryParseHostPortOrJoinCode(input, defaultPort, out string host, out ushort port))
        {
            SetStatus("Enter a join code OR host or host:port");
            return;
        }

        string displayCode = DeriveJoinCodeForDisplay(input, host, port);
        if (!string.IsNullOrEmpty(displayCode))
        {
            if (clientAddress) clientAddress.text = displayCode;
            _lockedStatusText = $"Join Code: {displayCode}";
            _lockStatusToJoinCode = true;
            if (statusText) statusText.text = _lockedStatusText;
        }

        if (hostBtn) hostBtn.gameObject.SetActive(false);
        if (joinBtn) joinBtn.gameObject.SetActive(false);
        if (clientAddress) clientAddress.gameObject.SetActive(false);
        if (backgroundImage) backgroundImage.gameObject.SetActive(false);

        networkManager.ServerManager.StopConnection(true);
        networkManager.ClientManager.StopConnection();

        tugboat.SetPort(port);
        tugboat.SetClientAddress(host);

        Debug.Log($"[Client] Connecting to {host}:{port} …");
        networkManager.ClientManager.StartConnection();
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

    static string DeriveJoinCodeForDisplay(string originalInput, string host, ushort port)
    {
        if (!string.IsNullOrEmpty(originalInput) && !originalInput.Contains(".") && !originalInput.Contains(":"))
            return originalInput;
        if (!string.IsNullOrEmpty(host))
        {
            int dot = host.IndexOf('.');
            if (dot > 0)
            {
                string id = host.Substring(0, dot);
                if (IsHex(id))
                    return EGJoinCode.EncodeRequestIdAndPort(id, port);
            }
        }
        return null;
    }

    static bool IsHex(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            bool ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
            if (!ok) return false;
        }
        return true;
    }

    void SetInteractable(bool on)
    {
        if (hostBtn) hostBtn.interactable = on;
        if (serverBtn) serverBtn.interactable = on;
        if (joinBtn) joinBtn.interactable = on;
        if (clientAddress) clientAddress.interactable = on;
    }

    void SetStatus(string s)
    {
        Debug.Log($"[DirectUI] {s}");
        if (_lockStatusToJoinCode)
        {
            if (statusText && !string.IsNullOrEmpty(_lockedStatusText))
                statusText.text = _lockedStatusText;
            return;
        }
        if (statusText) statusText.text = s;
    }
}
