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
    public NetworkObject xwingPrefab;   // first client
    public NetworkObject tiePrefab;     // other clients

    [Header("Spawn Points")]
    public Transform hostSpawn;         // X-Wing spawn
    public Transform[] clientSpawns;    // TIE spawns

    [Header("Round Settings")]
    public float roundRestartDelay = 5f;

    // clientId -> kills
    private readonly Dictionary<int, int> _kills = new();
    // clientId -> isXWing
    private readonly Dictionary<int, bool> _isXWingByConn = new();
    // clientId -> spawned ship
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
        // Dedicated server has no local client; this runs only for real clients.
        RequestSpawnServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestSpawnServerRpc(NetworkConnection conn = null)
    {
        if (conn == null || !conn.IsActive) return;

        // Assign roles: first client gets X-Wing, others TIE
        bool giveXWing = !_isXWingByConn.ContainsValue(true);
        _isXWingByConn[conn.ClientId] = giveXWing;

        SpawnFor(conn, giveXWing);
    }

    [Server]
    private void SpawnFor(NetworkConnection conn, bool asXWing)
    {
        var prefab = asXWing ? xwingPrefab : tiePrefab;
        if (prefab == null)
        {
            Debug.LogError("GameModeManager: assign xwingPrefab/tiePrefab.");
            return;
        }

        Vector3 pos; Quaternion rot;
        if (asXWing && hostSpawn) { pos = hostSpawn.position; rot = hostSpawn.rotation; }
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

        // Bind camera/UI for this owner on their machine.
        TargetBindLocal(conn, no);
    }

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

        // Respawn everyone currently connected with their assigned role.
        foreach (var kv in networkManager.ServerManager.Clients)
        {
            var conn = kv.Value;
            if (conn == null || !conn.IsActive) continue;

            bool asXWing = _isXWingByConn.TryGetValue(conn.ClientId, out bool val) && val;
            SpawnFor(conn, asXWing);
        }

        RpcRoundMessage("Fight!");
        PushScoresToClients();
        _roundRestarting = false;
    }

    [ObserversRpc(BufferLast = false)]
    private void RpcRoundMessage(string s) => Debug.Log($"[Round] {s}");

    [ObserversRpc(BufferLast = false)]
    private void RpcSetScoreboardText(string s)
    {
        if (ScoreboardUI.Instance) ScoreboardUI.Instance.SetText(s);
    }

    // Compact scoreboard: X-Wing team : TIE team
    (int xwing, int tie) GetTeamScores()
    {
        int xwingKills = 0, tieKills = 0;
        foreach (var kv in _kills)
        {
            bool isX = _isXWingByConn.TryGetValue(kv.Key, out bool v) && v;
            if (isX) xwingKills += kv.Value; else tieKills += kv.Value;
        }
        return (xwingKills, tieKills);
    }

    [Server]
    private void PushScoresToClients()
    {
        var (x, t) = GetTeamScores();
        RpcSetScoreboardText($"{x}:{t}");
    }

    [TargetRpc]
    private void TargetBindLocal(NetworkConnection conn, NetworkObject playerNo)
    {
        var pc = playerNo ? playerNo.GetComponent<PlaneController>() : null;
        if (pc != null) pc.OwnerLocalSetup();
    }
}