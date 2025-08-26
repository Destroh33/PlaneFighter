// ShipHealthNet.cs
using UnityEngine;
using FishNet.Object;
using FishNet.Connection;

public class ShipHealthNet : NetworkBehaviour
{
    public int maxHealth = 100;
    int currentHealth;

    public ShipExploderNet exploder;

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
        if (exploder) exploder.RpcExplode();

        // Notify the game mode for scoring + respawn
        var victim = Owner; // owner of this ship
        if (GameModeManager.Instance != null)
            GameModeManager.Instance.ServerOnKilled(victim, killer);

        // Despawn destroyed ship
        if (NetworkObject && NetworkObject.IsSpawned)
            NetworkObject.Despawn();
    }
}
