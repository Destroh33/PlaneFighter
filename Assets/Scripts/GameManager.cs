using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using FishNet.Object;
using FishNet.Managing;
using FishNet.Connection;

public class GameModeManager : NetworkBehaviour
{
    public static GameModeManager Instance;

    [Header("Managers (auto if null)")]
    public NetworkManager networkManager;

    [Header("Prefabs (NetworkObject roots)")]
    public NetworkObject xwingPrefab;   // host/local client
    public NetworkObject tiePrefab;     // remote clients

    [Header("Spawn Points")]
    public Transform hostSpawn;
    public Transform[] clientSpawns;

    // scoring: clientId -> kills
    private readonly Dictionary<int, int> _kills = new();
    // connection role: clientId -> isHost
    private readonly Dictionary<int, bool> _isHostByConn = new();

    private int _clientSpawnIndex = 0;

    void Awake()
    {
        Instance = this;
        if (!networkManager) networkManager = FindFirstObjectByType<NetworkManager>();
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        // Host local client will have server running locally; remote clients won't.
        bool iAmHost = networkManager && networkManager.IsServerStarted;
        RequestSpawnServerRpc(iAmHost);
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestSpawnServerRpc(bool isHost, NetworkConnection conn = null)
    {
        if (conn == null || !conn.IsActive) return;
        _isHostByConn[conn.ClientId] = isHost;
        SpawnFor(conn, isHost);
    }

    [Server]
    private void SpawnFor(NetworkConnection conn, bool isHost)
    {
        var prefab = isHost ? xwingPrefab : tiePrefab;
        if (prefab == null)
        {
            Debug.LogError("GameModeManager: assign xwingPrefab/tiePrefab.");
            return;
        }

        Vector3 pos; Quaternion rot;
        if (isHost && hostSpawn) { pos = hostSpawn.position; rot = hostSpawn.rotation; }
        else if (clientSpawns != null && clientSpawns.Length > 0)
        {
            var t = clientSpawns[_clientSpawnIndex % clientSpawns.Length];
            pos = t ? t.position : Vector3.zero;
            rot = t ? t.rotation : Quaternion.identity;
            _clientSpawnIndex++;
        }
        else { pos = Vector3.zero; rot = Quaternion.identity; }

        NetworkObject no = Instantiate(prefab, pos, rot);

        // Spawn then give ownership to that connection.
        networkManager.ServerManager.Spawn(no);
        no.GiveOwnership(conn);
    }

    /// Called by ShipHealthNet when a ship dies.
    [Server]
    public void ServerOnKilled(NetworkConnection victim, NetworkConnection killer)
    {
        if (killer != null)
        {
            int kid = killer.ClientId;
            _kills.TryGetValue(kid, out int v);
            _kills[kid] = v + 1;
            PushScoresToClients();
        }

        StartCoroutine(RespawnAfterDelay(victim, 5f));
    }

    private IEnumerator RespawnAfterDelay(NetworkConnection conn, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (conn != null && conn.IsActive)
        {
            bool isHost = _isHostByConn.TryGetValue(conn.ClientId, out bool val) && val;
            SpawnFor(conn, isHost);
        }
    }

    [ObserversRpc(BufferLast = false)]
    private void RpcSetScoreboardText(string s)
    {
        var ui = ScoreboardUI.Instance;
        if (ui != null) ui.SetText(s);
    }

    [Server]
    private void PushScoresToClients()
    {
        if (_kills.Count == 0) { RpcSetScoreboardText("No kills yet."); return; }
        string text = string.Join("\n", _kills.OrderByDescending(kv => kv.Value).Select(kv => $"Player {kv.Key}: {kv.Value}"));
        RpcSetScoreboardText(text);
    }
}
