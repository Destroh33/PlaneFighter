using UnityEngine;
using Unity.Netcode;
using System.Collections;

public class PlayerSpawner : MonoBehaviour
{
    public NetworkObject xwingPrefab;
    public NetworkObject tiePrefab;

    bool hooked;

    void OnEnable()
    {
        StartCoroutine(HookWhenReady());
    }

    void OnDisable()
    {
        Unhook();
    }

    IEnumerator HookWhenReady()
    {
        // Wait until NetworkManager is created/initialized
        while (NetworkManager.Singleton == null)
            yield return null;

        var nm = NetworkManager.Singleton;
        nm.OnServerStarted += OnServerStarted;
        nm.OnClientConnectedCallback += OnClientConnected;
        hooked = true;
    }

    void Unhook()
    {
        if (!hooked) return;
        var nm = NetworkManager.Singleton;
        if (nm != null)
        {
            nm.OnServerStarted -= OnServerStarted;
            nm.OnClientConnectedCallback -= OnClientConnected;
        }
        hooked = false;
    }

    void OnServerStarted()
    {
        var nm = NetworkManager.Singleton;
        if (nm.IsHost) SpawnFor(nm.LocalClientId, xwingPrefab);
    }

    void OnClientConnected(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        if (!nm.IsServer) return;
        if (clientId != nm.LocalClientId) SpawnFor(clientId, tiePrefab);
    }

    void SpawnFor(ulong clientId, NetworkObject prefab)
    {
        if (prefab == null)
        {
            Debug.LogError("PlayerSpawner: prefab not assigned.");
            return;
        }
        var no = Instantiate(prefab, GetSpawnPos(clientId), Quaternion.identity);
        no.SpawnAsPlayerObject(clientId);
    }

    Vector3 GetSpawnPos(ulong clientId) => new Vector3((int)clientId * 10f, 0f, 0f);
}