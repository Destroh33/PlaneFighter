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

    void Awake()
    {
        if (!networkManager) networkManager = FindFirstObjectByType<NetworkManager>();
    }

    void Start()
    {
        currentHealth = maxHealth;
        if (!exploder) exploder = GetComponent<ShipExploderNet>();
    }

    [Server]
    public void ServerTakeDamage(int amount, NetworkConnection attacker)
    {
        currentHealth -= amount;
        if (currentHealth <= 0)
            ServerDie(attacker);
    }

    [Server]
    void ServerDie(NetworkConnection killer)
    {
        Debug.Log($"ShipHealthNet: {Owner} killed by {killer}");
        if (exploder) exploder.RpcExplode();

        var victim = Owner; // capture before despawn
        if (GameModeManager.Instance != null)
            GameModeManager.Instance.ServerOnKilled(victim, killer);
        else
        {
            Debug.LogWarning("ShipHealthNet: no GameModeManager found in scene, cannot report kill.");
        }

            StartCoroutine(DespawnAfter(0.05f));
    }

    IEnumerator DespawnAfter(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (NetworkObject && NetworkObject.IsSpawned)
            networkManager.ServerManager.Despawn(NetworkObject);
    }
}