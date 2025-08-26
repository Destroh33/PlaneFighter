using FishNet;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Object;
using UnityEngine;

public class ProjectileNet : NetworkBehaviour
{
    public float lifetime = 5f;
    public GameObject explosionPrefab;
    public float explosionScale = 2f;
    public float explosionLifetime = 2f;
    public int damage = 15;

    public override void OnStartServer()
    {
        base.OnStartServer();
        Invoke(nameof(ServerDespawn), lifetime);
    }

    void ServerDespawn()
    {
        if (NetworkObject && NetworkObject.IsSpawned)
            InstanceFinder.ServerManager.Despawn(NetworkObject);
    }

    void OnCollisionEnter(Collision other)
    {
        if (!IsServerInitialized) return;

        var health = other.gameObject.GetComponent<ShipHealthNet>();
        if (health != null)
            health.ServerTakeDamage(damage, Owner); // credit to shooter

        RpcExplosionFx(transform.position);
        ServerDespawn();
    }

    [ObserversRpc(BufferLast = false)]
    void RpcExplosionFx(Vector3 pos)
    {
        if (!explosionPrefab) return;
        GameObject fx = Instantiate(explosionPrefab, pos, Quaternion.identity);
        fx.transform.localScale = Vector3.one * explosionScale;

        foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>())
        {
            var main = ps.main;
            main.startSizeMultiplier *= explosionScale;
            main.startSpeedMultiplier *= explosionScale;
        }
        Destroy(fx, explosionLifetime);
    }
}
