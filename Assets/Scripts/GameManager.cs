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

    [Header("Round Settings")]
    public float roundRestartDelay = 5f;

    // kill tally: clientId -> kills
    private readonly Dictionary<int, int> _kills = new();
    // role mapping: clientId -> isHost
    private readonly Dictionary<int, bool> _isHostByConn = new();
    // currently spawned ship per client
    private readonly Dictionary<int, NetworkObject> _activeShips = new();

    private int _clientSpawnIndex = 0;
    private bool _roundRestarting = false;

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

        // Despawn any lingering ship for this player
        if (_activeShips.TryGetValue(conn.ClientId, out var old) && old && old.IsSpawned)
            networkManager.ServerManager.Despawn(old);

        NetworkObject no = Instantiate(prefab, pos, rot);
        networkManager.ServerManager.Spawn(no);
        no.GiveOwnership(conn);
        _activeShips[conn.ClientId] = no;

        // Tell ONLY the owner to bind their camera + HUD to this ship.
        TargetBindLocal(conn, no);
    }

    /// Called by ShipHealthNet when a ship dies.
    [Server]
    public void ServerOnKilled(NetworkConnection victim, NetworkConnection killer)
    {
        if (killer != null)
        {
            _kills.TryGetValue(killer.ClientId, out int v);
            _kills[killer.ClientId] = v + 1;
            PushScoresToClients();
        }

        if (!_roundRestarting)
            StartCoroutine(RestartRoundAfter(roundRestartDelay));
    }

    [Server]
    private IEnumerator RestartRoundAfter(float delay)
    {
        _roundRestarting = true;
        RpcRoundMessage($"Round over! Restarting in {delay:0}s…");
        yield return new WaitForSeconds(delay);

        // Despawn all current ships.
        foreach (var kv in _activeShips.ToArray())
        {
            var ship = kv.Value;
            if (ship && ship.IsSpawned)
                networkManager.ServerManager.Despawn(ship);
        }
        _activeShips.Clear();
        _clientSpawnIndex = 0;

        // Respawn everyone currently connected.
        foreach (var kv in networkManager.ServerManager.Clients)
        {
            var conn = kv.Value;
            if (conn == null || !conn.IsActive) continue;

            bool isHost = _isHostByConn.TryGetValue(conn.ClientId, out bool val) && val;
            SpawnFor(conn, isHost);
        }

        RpcRoundMessage("Fight!");
        // Also refresh the 0:0 style scoreboard at round start.
        PushScoresToClients();

        _roundRestarting = false;
    }

    [ObserversRpc(BufferLast = false)]
    private void RpcRoundMessage(string s)
    {
        Debug.Log($"[Round] {s}");
    }

    [ObserversRpc(BufferLast = false)]
    private void RpcSetScoreboardText(string s)
    {
        if (ScoreboardUI.Instance) ScoreboardUI.Instance.SetText(s);
    }

    // --- SCOREBOARD: compact host:clients format ---

    // Sum kills into two buckets: host vs everyone else.
    (int host, int clients) GetTeamScores()
    {
        // Find the (single) host clientId if known
        int hostId = -1;
        foreach (var kv in _isHostByConn)
        {
            if (kv.Value) { hostId = kv.Key; break; }
        }

        int hostKills = 0;
        int clientKills = 0;

        foreach (var kv in _kills)
        {
            if (kv.Key == hostId) hostKills += kv.Value;
            else clientKills += kv.Value;
        }

        return (hostKills, clientKills);
    }

    [Server]
    private void PushScoresToClients()
    {
        var (host, clients) = GetTeamScores();
        RpcSetScoreboardText($"{host}:{clients}");
    }

    // Runs ONLY on the owner client. Binds camera + UI to their newly spawned ship.
    [TargetRpc]
    private void TargetBindLocal(NetworkConnection conn, NetworkObject playerNo)
    {
        var pc = playerNo ? playerNo.GetComponent<PlaneController>() : null;
        if (pc != null)
            pc.OwnerLocalSetup();
    }
}
