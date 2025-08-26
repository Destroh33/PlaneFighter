using UnityEngine;
using FishNet.Managing;
using FishNet.Transporting.Tugboat;
using System;

public class DedicatedServerBootstrap : MonoBehaviour
{
    public NetworkManager networkManager;
    public ushort defaultServerPort = 7770;

    void Awake()
    {
        if (!networkManager) networkManager = FindFirstObjectByType<NetworkManager>();
        DontDestroyOnLoad(gameObject);

        // Ensure Tugboat is using the right port on the server.
        var tugboat = FindFirstObjectByType<Tugboat>();
        if (tugboat != null)
        {
            ushort p = defaultServerPort;
            // If a PORT env var is present, use it; otherwise keep the default (7770).
            if (ushort.TryParse(Environment.GetEnvironmentVariable("PORT"), out var envPort))
                p = envPort;

            tugboat.SetPort(p);
            Debug.Log($"[DS] Tugboat port set to {p}.");
        }
        else
        {
            Debug.Log("[DS] No Tugboat transport found.");
        }

#if UNITY_SERVER
        StartServer();
#else
        // Allow -server on desktop builds for local testing
        string[] args = Environment.GetCommandLineArgs();
        foreach (string a in args)
        {
            if (a.Equals("-server", StringComparison.OrdinalIgnoreCase))
            {
                StartServer();
                break;
            }
        }
#endif
    }

    void StartServer()
    {
        if (networkManager.ServerManager.StartConnection())
            Debug.Log("[DS] Dedicated server started.");
        else
            Debug.LogError("[DS] Server failed to start.");
    }
}
