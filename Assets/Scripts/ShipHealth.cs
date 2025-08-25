using UnityEngine;
using Unity.Netcode;

public class ShipHealth : NetworkBehaviour
{
    public int maxHealth = 100;
    public ShipExploder exploder;

    NetworkVariable<int> currentHealth = new NetworkVariable<int>();

    void Start()
    {
        if (IsServer) currentHealth.Value = maxHealth;
        if (exploder == null) exploder = GetComponent<ShipExploder>();
    }

    public void TakeDamage(int amount)
    {
        if (!IsServer) return;
        currentHealth.Value -= amount;
        if (currentHealth.Value <= 0) Die();
    }

    void Die()
    {
        if (exploder != null) exploder.Explode();
        if (NetworkObject != null && NetworkObject.IsSpawned)
            NetworkObject.Despawn();
    }
}
