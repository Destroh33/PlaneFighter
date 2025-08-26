using UnityEngine;
using UnityEngine.UI;
using TMPro;
using FishNet.Managing;
using FishNet.Transporting.Tugboat;

public class FishNetDirectUI : MonoBehaviour
{
    public NetworkManager networkManager;
    public Tugboat tugboat;
    public Button hostBtn, serverBtn, joinBtn;
    public TMP_InputField clientAddress; // 127.0.0.1, LAN IP, public IP
    public TMP_Text statusText;

    void Awake()
    {
        if (!networkManager) networkManager = FindFirstObjectByType<NetworkManager>();
        if (!tugboat) tugboat = FindFirstObjectByType<Tugboat>();
        Application.runInBackground = true;
    }

    void Start()
    {
        if (hostBtn) hostBtn.onClick.AddListener(StartHost);
        if (serverBtn) serverBtn.onClick.AddListener(StartServer);
        if (joinBtn) joinBtn.onClick.AddListener(StartClient);
        Log("Idle");
    }

    void StartHost()
    {
        if (networkManager.ServerManager.StartConnection())
        {
            networkManager.ClientManager.StartConnection();
            Log("Host started (server + local client).");
        }
        else Log("Host start failed.");
    }

    void StartServer()
    {
        if (networkManager.ServerManager.StartConnection())
            Log("Server started.");
        else
            Log("Server start failed.");
    }

    void StartClient()
    {
        if (clientAddress && !string.IsNullOrWhiteSpace(clientAddress.text))
            tugboat.SetClientAddress(clientAddress.text.Trim());

        if (networkManager.ClientManager.StartConnection())
            Log($"Client started → {tugboat.GetClientAddress()}:{tugboat.GetPort()}");
        else
            Log("Client start failed.");
    }

    void Log(string s) { Debug.Log($"[DirectUI] {s}"); if (statusText) statusText.text = s; }
}
