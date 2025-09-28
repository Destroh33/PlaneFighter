using UnityEngine;
using FishNet.Object;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Object.Synchronizing;
using System.Collections;

public class ShipHealthNet : NetworkBehaviour
{
    [Header("Managers (auto if null)")]
    public NetworkManager networkManager;

    public int maxHealth = 100;

    // New-style SyncVar (auto-syncs to clients)
    public readonly SyncVar<int> currentHealth = new SyncVar<int>();

    public ShipExploderNet exploder;
    private bool _isDead;

    void Awake()
    {
        if (!networkManager) networkManager = FindFirstObjectByType<NetworkManager>();
    }

    public override void OnStartNetwork()
    {
        if (!exploder) exploder = GetComponent<ShipExploderNet>();

        if (IsServerInitialized)  // ✅ replaces IsServer
        {
            currentHealth.Value = maxHealth;
            _isDead = false;
        }

        // Subscribe to SyncVar changes
        currentHealth.OnChange += OnHealthChanged;
    }

    public override void OnStopNetwork()
    {
        currentHealth.OnChange -= OnHealthChanged;
    }

    private void OnHealthChanged(int prev, int next, bool asServer)
    {
        // Optional hook: UI/FX could listen here.
        // TargetingUI polls CurrentHealth(), so no extra work needed.
    }

    public float CurrentHealth() => currentHealth.Value;
    public float MaxHealth() => maxHealth;

    [Server]
    public void ServerTakeDamage(int amount, NetworkConnection attacker)
    {
        if (_isDead) return;
        if (attacker == Owner) return;

        currentHealth.Value = Mathf.Max(0, currentHealth.Value - amount);

        if (currentHealth.Value <= 0)
            ServerDie(attacker);
    }

    [Server]
    private void ServerDie(NetworkConnection killer)
    {
        if (_isDead) return;
        _isDead = true;

        if (exploder) exploder.RpcExplode();

        var victim = Owner;
        if (GameModeManager.Instance != null)
            GameModeManager.Instance.ServerOnKilled(victim, killer);

        StartCoroutine(DespawnAfter(0.05f));
    }

    private IEnumerator DespawnAfter(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (NetworkObject && NetworkObject.IsSpawned)
            networkManager.ServerManager.Despawn(NetworkObject);
    }
}
