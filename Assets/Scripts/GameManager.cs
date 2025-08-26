using FishNet;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Object;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class GameModeManager : NetworkBehaviour
{
    public static GameModeManager Instance;

    [Header("Prefabs")]
    public NetworkObject xwingPrefab;   // host gets this
    public NetworkObject tiePrefab;     // clients get this

    [Header("Spawn Points")]
    public Transform hostSpawn;
    public Transform[] clientSpawns;

    // killer -> kills
    private readonly Dictionary<int, int> _kills = new();
    private int _clientSpawnIndex = 0;

    void Awake() => Instance = this;

    /* Every client (including the host's local client) will call this on connect. */
    public override void OnStartClient()
    {
        base.OnStartClient();
        // Host will have the server running locally, remote clients will not.
        bool iAmHost = InstanceFinder.IsServerStarted;
        RequestSpawnServerRpc(iAmHost);
    }

    /* Clients ask the server to spawn their ship. */
    [ServerRpc(RequireOwnership = false)]
    private void RequestSpawnServerRpc(bool isHost, NetworkConnection conn = null)
    {
        SpawnFor(conn, isHost);
    }

    private void SpawnFor(NetworkConnection conn, bool isHost)
    {
        var prefab = isHost ? xwingPrefab : tiePrefab;
        if (prefab == null)
        {
            Debug.LogError("GameModeManager: Assign xwingPrefab/tiePrefab in the inspector.");
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
        // Version-tolerant spawn with ownership for this connection.
        InstanceFinder.ServerManager.Spawn(no, conn);
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
            // The host calls RequestSpawnServerRpc with isHost=true,
            // remote clients with false. Reuse the same rule here:
            bool isHost = conn == InstanceFinder.ClientManager.Connection && InstanceFinder.IsServerStarted;
            SpawnFor(conn, isHost);
        }
    }

    [ObserversRpc(BufferLast = false)]
    private void RpcSetScoreboardText(string s)
    {
        if (ScoreboardUI.Instance) ScoreboardUI.Instance.SetText(s);
    }

    private void PushScoresToClients()
    {
        if (_kills.Count == 0) { RpcSetScoreboardText("No kills yet."); return; }
        var lines = _kills.OrderByDescending(kv => kv.Value)
                          .Select(kv => $"Player {kv.Key}: {kv.Value}");
        RpcSetScoreboardText(string.Join("\n", lines));
    }
}
