using UnityEngine;
using FishNet.Object;
using FishNet.Connection;
using FishNet.Managing;
using System.Collections;

public class ShipHealthNet : NetworkBehaviour
{
    [Header("Managers (auto if null)")]
    public NetworkManager networkManager;

    public int maxHealth = 100;
    int currentHealth;

    public ShipExploderNet exploder;

    // ✅ prevents double-counting a kill
    bool _isDead;

    void Awake()
    {
        if (!networkManager) networkManager = FindFirstObjectByType<NetworkManager>();
    }

    void Start()
    {
        currentHealth = maxHealth;
        _isDead = false;
        if (!exploder) exploder = GetComponent<ShipExploderNet>();
    }

    [Server]
    public void ServerTakeDamage(int amount, NetworkConnection attacker)
    {
        if (_isDead) return;  // already dead; ignore stray hits
        var victim  = Owner;
        if (attacker == Owner)
            return; // ignore self-hits
        currentHealth -= amount;
        if (currentHealth <= 0)
            ServerDie(attacker);
    }

    [Server]
    void ServerDie(NetworkConnection killer)
    {
        if (_isDead) return;  // guard reentry
        _isDead = true;

        if (exploder) exploder.RpcExplode();

        var victim = Owner; // capture before despawn
        if (GameModeManager.Instance != null)
            GameModeManager.Instance.ServerOnKilled(victim, killer);

        StartCoroutine(DespawnAfter(0.05f));
    }

    IEnumerator DespawnAfter(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (NetworkObject && NetworkObject.IsSpawned)
            networkManager.ServerManager.Despawn(NetworkObject);
    }
}
