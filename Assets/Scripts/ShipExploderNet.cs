using UnityEngine;
using FishNet.Object;

public class ShipExploderNet : NetworkBehaviour
{
    public Transform meshContainer;
    public float explosionForce = 500f;
    public float explosionRadius = 10f;
    public float torqueStrength = 200f;
    public float debrisLifetime = 5f;

    public GameObject explosionPrefab;
    public float explosionScale = 2f;
    public float explosionLifetime = 2f;

    [ObserversRpc(BufferLast = false)]
    public void RpcExplode()
    {
        // VFX
        if (explosionPrefab)
        {
            GameObject fx = Instantiate(explosionPrefab, transform.position, Quaternion.identity);
            fx.transform.localScale = Vector3.one * explosionScale;

            foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>())
            {
                var main = ps.main;
                main.startSizeMultiplier *= explosionScale;
                main.startSpeedMultiplier *= explosionScale;
            }
            Destroy(fx, explosionLifetime);
        }

        // If no mesh container assigned, at least hide renderers so no ghost remains
        if (!meshContainer)
        {
            foreach (var r in GetComponentsInChildren<Renderer>())
                r.enabled = false;
            return;
        }

        // Detach pieces as debris (client-side visuals only)
        int count = meshContainer.childCount;
        var parts = new Transform[count];
        for (int i = 0; i < count; i++)
            parts[i] = meshContainer.GetChild(i);

        foreach (Transform child in parts)
        {
            if (!child) continue;

            // Ensure Rigidbody
            var rb = child.GetComponent<Rigidbody>();
            if (rb == null) rb = child.gameObject.AddComponent<Rigidbody>();

            // Ensure Collider (FIX: no '??' as a statement)
            var col = child.GetComponent<Collider>();
            if (col == null) col = child.gameObject.AddComponent<BoxCollider>();

            rb.useGravity = false;
            rb.isKinematic = false;

            child.parent = null;

            rb.AddExplosionForce(explosionForce, transform.position, explosionRadius, 0f, ForceMode.Impulse);
            rb.AddTorque(Random.insideUnitSphere * torqueStrength, ForceMode.Impulse);

            Destroy(child.gameObject, debrisLifetime);
        }
    }
}
