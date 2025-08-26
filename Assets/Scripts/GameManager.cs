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
    public NetworkObject xwingPrefab;   // X-Wing prefab
    public NetworkObject tiePrefab;     // TIE prefab

    [Header("Spawn Points")]
    public Transform hostSpawn;         // Preferred X-Wing spawn (fallbacks to array if null)
    public Transform[] clientSpawns;    // General spawn cycle (used for TIE or when hostSpawn is null)

    [Header("Round Settings")]
    public float roundRestartDelay = 5f;

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

    /// <summary>
    /// Called by each client on join. Server decides which ship to give.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void RequestSpawnServerRpc(NetworkConnection conn = null)
    {
        if (conn == null || !conn.IsActive) return;

        // Alternate deterministically: even ClientId => X-Wing, odd => TIE
        bool asXWing = (conn.ClientId % 2) == 0;
        SpawnFor(conn, asXWing);

        // After any spawn, refresh targeting on all clients so they lock onto each other.
        RefreshAllTargetingForClients();
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

        // Choose a spawn transform. Prefer hostSpawn for X-Wing if present; else cycle through clientSpawns.
        Vector3 pos; Quaternion rot;
        Transform useT = null;

        if (asXWing && hostSpawn != null)
            useT = hostSpawn;
        else if (clientSpawns != null && clientSpawns.Length > 0)
        {
            useT = clientSpawns[_clientSpawnIndex % clientSpawns.Length];
            _clientSpawnIndex++;
        }

        if (useT != null) { pos = useT.position; rot = useT.rotation; }
        else { pos = Vector3.zero; rot = Quaternion.identity; }

        // Despawn any lingering ship for this player
        if (_activeShips.TryGetValue(conn.ClientId, out var old) && old && old.IsSpawned)
            networkManager.ServerManager.Despawn(old);

        // Spawn and give ownership
        NetworkObject no = Instantiate(prefab, pos, rot);
        networkManager.ServerManager.Spawn(no);
        no.GiveOwnership(conn);

        _activeShips[conn.ClientId] = no;

        // Bind camera/UI for this owner on their machine.
        TargetBindLocal(conn, no);
    }

    /// <summary>
    /// ShipHealthNet should call this when someone dies.
    /// </summary>
    [Server]
    public void ServerOnKilled(NetworkConnection victim, NetworkConnection killer)
    {
        if (_roundRestarting) return;
        StartCoroutine(RestartRoundAfter(roundRestartDelay));
    }

    [Server]
    private IEnumerator RestartRoundAfter(float delay)
    {
        _roundRestarting = true;

        // Small message to everyone (optional)
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

        // Respawn everyone currently connected with alternating roles by ClientId parity.
        foreach (var kv in networkManager.ServerManager.Clients)
        {
            var conn = kv.Value;
            if (conn == null || !conn.IsActive) continue;

            bool asXWing = (conn.ClientId % 2) == 0;
            SpawnFor(conn, asXWing);
        }

        // Ensure UIs lock onto the other ship(s) after respawn.
        RefreshAllTargetingForClients();

        RpcRoundMessage("Fight!");
        _roundRestarting = false;
    }

    [ObserversRpc(BufferLast = false)]
    private void RpcRoundMessage(string s) => Debug.Log($"[Round] {s}");

    /// <summary>
    /// Rebind every client's TargetingUI so the 'enemy' pointer is valid after spawns/respawns.
    /// </summary>
    [Server]
    private void RefreshAllTargetingForClients()
    {
        foreach (var kv in networkManager.ServerManager.Clients)
        {
            var conn = kv.Value;
            if (conn == null || !conn.IsActive) continue;

            if (_activeShips.TryGetValue(conn.ClientId, out var no) && no)
                TargetBindLocal(conn, no);
        }
    }

    /// <summary>
    /// Runs on the owning client; wires their camera + TargetingUI.
    /// </summary>
    [TargetRpc]
    private void TargetBindLocal(NetworkConnection conn, NetworkObject playerNo)
    {
        var pc = playerNo ? playerNo.GetComponent<PlaneController>() : null;
        if (pc != null) pc.OwnerLocalSetup();

        // Also nudge the TargetingUI so it immediately picks an enemy.
        var tui = Object.FindFirstObjectByType<TargetingUI>();
        if (tui != null && playerNo != null)
        {
            tui.plane = playerNo.transform;
            tui.mainCamera = Camera.main;

            // Set nearest opponent right now (TargetingUI can still re-acquire later if needed)
            tui.enemy = FindNearestEnemyOf(playerNo.transform);
        }
    }

    // Helper used only client-side in TargetBindLocal
    private Transform FindNearestEnemyOf(Transform me)
    {
        var all = Object.FindObjectsByType<PlaneController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        Transform best = null; float bestD2 = float.MaxValue;
        foreach (var pc in all)
        {
            if (!pc) continue;
            var t = pc.transform;
            if (t == me) continue;

            float d2 = (t.position - me.position).sqrMagnitude;
            if (d2 < bestD2) { bestD2 = d2; best = t; }
        }
        return best;
    }
}
