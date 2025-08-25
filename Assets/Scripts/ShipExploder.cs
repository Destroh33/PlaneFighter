using UnityEngine;
using Unity.Netcode;
using System.Collections;

public class ShipExploder : NetworkBehaviour
{
    public Transform meshContainer;
    public float explosionForce = 500f;
    public float explosionRadius = 10f;
    public float torqueStrength = 200f;
    public float debrisLifetime = 5f;

    public GameObject explosionPrefab;
    public float explosionScale = 2f;
    public float explosionLifetime = 2f;

    bool explodedOnce;

    public void Explode()
    {
        if (!IsServer) return;
        if (explodedOnce) return;
        explodedOnce = true;

        SpawnExplosionAndScatterClientRpc();

        StartCoroutine(DespawnNextFrame());
    }

    [ClientRpc]
    void SpawnExplosionAndScatterClientRpc()
    {
        if (explosionPrefab != null)
        {
            var fx = Instantiate(explosionPrefab, transform.position, Quaternion.identity);
            fx.transform.localScale = Vector3.one * explosionScale;

            var systems = fx.GetComponentsInChildren<ParticleSystem>(true);
            foreach (var ps in systems)
            {
                var main = ps.main;
                main.startSizeMultiplier *= explosionScale;
                main.startSpeedMultiplier *= explosionScale;
            }
            Destroy(fx, explosionLifetime);
        }

        if (meshContainer == null) return;

        var count = meshContainer.childCount;
        var parts = new Transform[count];
        for (int i = 0; i < count; i++) parts[i] = meshContainer.GetChild(i);

        foreach (var child in parts)
        {
            if (child == null) continue;

            var rb = child.GetComponent<Rigidbody>();
            if (rb == null) rb = child.gameObject.AddComponent<Rigidbody>();

            var col = child.GetComponent<Collider>();
            if (col == null) col = child.gameObject.AddComponent<BoxCollider>();

            rb.useGravity = false;
            rb.isKinematic = false;

            child.SetParent(null, true);

            rb.AddExplosionForce(explosionForce, transform.position, explosionRadius, 0f, ForceMode.Impulse);
            rb.AddTorque(Random.insideUnitSphere * torqueStrength, ForceMode.Impulse);

            Destroy(child.gameObject, debrisLifetime);
        }
    }

    IEnumerator DespawnNextFrame()
    {
        yield return null; // let the ClientRpc run everywhere first
        var no = GetComponent<NetworkObject>();
        if (no != null && no.IsSpawned) no.Despawn(true);
        else Destroy(gameObject);
    }
}