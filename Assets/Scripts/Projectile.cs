using System.Collections;
using UnityEngine;
using FishNet.Object;
using FishNet.Managing;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Rigidbody))]
public class ProjectileNet : NetworkBehaviour
{
    [Header("Managers (auto if null)")]
    public NetworkManager networkManager;

    [Header("Gameplay")]
    public float lifetime = 5f;
    public int damage = 15;

    [Header("Visuals")]
    public TrailRenderer[] trails;
    public GameObject explosionPrefab;
    public float explosionScale = 1.5f;
    public float explosionLifetime = 2f;

    Rigidbody _rb;
    Collider _col;
    bool _impacted;

    void Awake()
    {
        if (!networkManager) networkManager = FindFirstObjectByType<NetworkManager>();
        _rb = GetComponent<Rigidbody>();
        _col = GetComponent<Collider>();
        if (trails == null || trails.Length == 0)
            trails = GetComponentsInChildren<TrailRenderer>(true);
    }

    public override void OnStartClient()
    {
        base.OnStartClient();

        foreach (var t in trails)
        {
            if (!t) continue;
            t.Clear();
            t.emitting = true;
        }

        // keep your existing server/client setup unchanged
        if (!IsServerInitialized && _rb != null)
        {
            _rb.isKinematic = true;
            _rb.detectCollisions = false;
        }
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        StartCoroutine(DespawnAfter(lifetime));
        _impacted = false;
        if (_rb != null)
        {
            _rb.useGravity = false;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }
    }

    IEnumerator DespawnAfter(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (!_impacted) RpcImpact(transform.position, true);

        if (NetworkObject && NetworkObject.IsSpawned)
            networkManager.ServerManager.Despawn(NetworkObject, DespawnType.Destroy);
        else
            Destroy(gameObject);
    }

    void OnCollisionEnter(Collision c)
    {
        if (!IsServerInitialized || _impacted) return;
        _impacted = true;
        if (_col) _col.enabled = false;
        if (_rb) _rb.isKinematic = true;

        var sh = c.collider.GetComponentInParent<ShipHealthNet>();
        if (sh != null) sh.ServerTakeDamage(damage, Owner);
        Vector3 hitPos = c.contacts.Length > 0 ? c.contacts[0].point : transform.position;
        RpcImpact(hitPos, true);

        if (NetworkObject && NetworkObject.IsSpawned)
            networkManager.ServerManager.Despawn(NetworkObject, DespawnType.Destroy);
        else
            Destroy(gameObject);
    }

    void OnTriggerEnter(Collider other)
    {
        if (!IsServerInitialized || _impacted) return;
        _impacted = true;
        if (_col) _col.enabled = false;
        if (_rb) _rb.isKinematic = true;

        var sh = other.GetComponentInParent<ShipHealthNet>();
        if (sh != null) sh.ServerTakeDamage(damage, Owner);
        RpcImpact(transform.position, true);

        if (NetworkObject && NetworkObject.IsSpawned)
            networkManager.ServerManager.Despawn(NetworkObject, DespawnType.Destroy);
        else
            Destroy(gameObject);
    }

    [ObserversRpc(BufferLast = false)]
    void RpcImpact(Vector3 pos, bool stopTrails)
    {
        if (stopTrails) DetachTrails();

        if (explosionPrefab)
        {
            var fx = Instantiate(explosionPrefab, pos, Quaternion.identity);
            fx.transform.localScale = Vector3.one * explosionScale;
            Destroy(fx, explosionLifetime);
        }
    }

    /// <summary>
    /// Detach each trail so it can fade out after projectile is destroyed.
    /// </summary>
    void DetachTrails()
    {
        foreach (var t in trails)
        {
            if (!t) continue;

            t.emitting = false;

            // make a ghost GameObject to hold the trail
            var ghost = new GameObject("TrailGhost");
            ghost.transform.SetPositionAndRotation(t.transform.position, t.transform.rotation);

            var newTrail = ghost.AddComponent<TrailRenderer>();
            CopyTrailSettings(t, newTrail);

            // destroy the ghost after the trail lifetime so it fades naturally
            Destroy(ghost, newTrail.time + 0.1f);
        }
    }

    static void CopyTrailSettings(TrailRenderer src, TrailRenderer dst)
    {
        dst.time = src.time;
        dst.minVertexDistance = src.minVertexDistance;
        dst.widthCurve = src.widthCurve;
        dst.widthMultiplier = src.widthMultiplier;
        dst.numCornerVertices = src.numCornerVertices;
        dst.numCapVertices = src.numCapVertices;
        dst.alignment = src.alignment;
        dst.textureMode = src.textureMode;
        dst.shadowCastingMode = src.shadowCastingMode;
        dst.receiveShadows = src.receiveShadows;
        dst.generateLightingData = src.generateLightingData;
        dst.sharedMaterial = src.sharedMaterial;
        dst.colorGradient = src.colorGradient;
        dst.allowOcclusionWhenDynamic = src.allowOcclusionWhenDynamic;

        // keep world-space
        dst.transform.parent = null;
    }
}
