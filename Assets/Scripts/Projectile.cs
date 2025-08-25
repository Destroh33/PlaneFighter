using UnityEngine;
using Unity.Netcode;

public class Projectile : NetworkBehaviour
{
    public float lifetime = 5f;
    public GameObject explosionPrefab;
    public float explosionScale = 2f;
    public float explosionLifetime = 2f;

    ulong ownerId;

    public void SetOwner(ulong id) => ownerId = id;

    public override void OnNetworkSpawn()
    {
        if (IsServer) Invoke(nameof(DespawnMe), lifetime);
    }

    void DespawnMe()
    {
        if (this != null && NetworkObject != null && NetworkObject.IsSpawned)
            NetworkObject.Despawn();
    }

    void OnCollisionEnter(Collision other)
    {
        if (!IsServer) return;

        var otherNO = other.gameObject.GetComponent<NetworkObject>();
        if (otherNO != null && otherNO.NetworkObjectId == ownerId) return;

        var hp = other.gameObject.GetComponent<ShipHealth>();
        if (hp != null) hp.TakeDamage(15);

        Vector3 pos = transform.position;
        SpawnExplosionClientRpc(pos, explosionScale, explosionLifetime);

        if (NetworkObject != null && NetworkObject.IsSpawned)
            NetworkObject.Despawn();
    }

    [ClientRpc]
    void SpawnExplosionClientRpc(Vector3 position, float scale, float life)
    {
        if (explosionPrefab == null) return;
        var fx = Object.Instantiate(explosionPrefab, position, Quaternion.identity);
        fx.transform.localScale = Vector3.one * scale;

        var systems = fx.GetComponentsInChildren<ParticleSystem>(true);
        foreach (var ps in systems)
        {
            var main = ps.main;
            main.startSizeMultiplier *= scale;
            main.startSpeedMultiplier *= scale;
        }
        Object.Destroy(fx, life);
    }
}
