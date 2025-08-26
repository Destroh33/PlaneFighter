using System.Collections;
using UnityEngine;
using FishNet.Object;
using FishNet.Managing;
using FishNet.Component.Transforming; // ensure NetworkTransform is on the prefab

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Rigidbody))]
public class ProjectileNet : NetworkBehaviour
{
    [Header("Managers (auto if null)")]
    public NetworkManager networkManager;

    [Header("Gameplay")]
    public float lifetime = 5f;
    public int damage = 15;

    [Header("Trail (auto)")]
    public TrailRenderer[] trails;
    public bool fadeTrailOnImpact = true;
    public float impactDespawnDelay = 0.12f;

    [Header("Optional VFX")]
    public GameObject explosionPrefab;
    public float explosionScale = 1.5f;
    public float explosionLifetime = 2f;

    Rigidbody _rb;
    Collider _col;

    void Awake()
    {
        if (!networkManager) networkManager = FindFirstObjectByType<NetworkManager>();
        _rb = GetComponent<Rigidbody>();
        _col = GetComponent<Collider>();
        if (trails == null || trails.Length == 0)
            trails = GetComponentsInChildren<TrailRenderer>(true);
    }

    public override void OnStartServer()
    {
        base.OnStartServer();

        // Server simulates physics & collisions
        if (_rb)
        {
            _rb.isKinematic = false;
            _rb.useGravity = false;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }
        if (_col) _col.enabled = true;

        // Lifetime cleanup
        Invoke(nameof(ServerTimeout), lifetime);
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        // Trails visible for everyone
        foreach (var t in trails)
        {
            if (!t) continue;
            t.Clear();
            t.emitting = true;
        }

        // Clients do not simulate physics/collide
        if (!IsServerInitialized)
        {
            if (_rb) { _rb.isKinematic = true; _rb.useGravity = false; }
            if (_col) _col.enabled = false;
        }
    }

    void ServerTimeout()
    {
        if (NetworkObject && NetworkObject.IsSpawned)
            networkManager.ServerManager.Despawn(NetworkObject);
    }

    void OnCollisionEnter(Collision other)
    {
        if (!IsServerInitialized) return;

        // --- Ignore if we collided with something owned by the same client as this projectile ---
        var otherNO = other.transform.GetComponentInParent<NetworkObject>();
        if (ShouldIgnoreByOwner(otherNO))
        {
            // Proactively ignore all future contacts between this projectile and that object
            IgnoreCollisionsWith(otherNO);
            return; // keep flying; no damage/VFX/despawn
        }

        // Apply damage to ships
        var health = other.gameObject.GetComponent<ShipHealthNet>();
        if (health != null)
            health.ServerTakeDamage(damage, Owner);

        Vector3 hitPos = (other.contactCount > 0) ? other.GetContact(0).point : transform.position;

        // Stop trails (lets them fade) + optional VFX
        RpcImpact(hitPos, fadeTrailOnImpact);

        // Despawn shortly after so trail can fade on clients
        StartCoroutine(DespawnAfter(impactDespawnDelay));
    }

    IEnumerator DespawnAfter(float delay)
    {
        yield return new WaitForSeconds(delay);
        ServerTimeout();
    }

    [ObserversRpc(BufferLast = false)]
    void RpcImpact(Vector3 pos, bool stopTrail)
    {
        if (stopTrail)
            foreach (var t in trails) if (t) t.emitting = false;

        if (explosionPrefab)
        {
            var fx = Instantiate(explosionPrefab, pos, Quaternion.identity);
            fx.transform.localScale = Vector3.one * explosionScale;
            Destroy(fx, explosionLifetime);
        }
    }

    // ---------------- Owner-based ignore helpers ----------------

    [Server]
    bool ShouldIgnoreByOwner(NetworkObject otherNO)
    {
        if (otherNO == null || Owner == null || otherNO.Owner == null)
            return false;

        // Same client owns both the projectile and the hit object (eg. shooter, shooter's child, or their own projectile)
        return otherNO.Owner.ClientId == Owner.ClientId;
    }

    [Server]
    void IgnoreCollisionsWith(NetworkObject otherNO)
    {
        if (otherNO == null) return;

        var myCols = GetComponentsInChildren<Collider>(true);
        var theirCols = otherNO.GetComponentsInChildren<Collider>(true);
        if (myCols == null || theirCols == null) return;

        foreach (var pc in myCols)
        {
            if (!pc) continue;
            foreach (var sc in theirCols)
            {
                if (!sc) continue;
                Physics.IgnoreCollision(pc, sc, true);
            }
        }
    }
}
