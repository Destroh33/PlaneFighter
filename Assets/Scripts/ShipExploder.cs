// ShipExploderNet.cs
using UnityEngine;
using FishNet.Object;

public class ShipExploderNet : NetworkBehaviour
{
    public Transform meshContainer;
    public float explosionForce = 500f, explosionRadius = 10f, torqueStrength = 200f, debrisLifetime = 5f;

    public GameObject explosionPrefab;
    public float explosionScale = 2f, explosionLifetime = 2f;

    [ObserversRpc(BufferLast = false)]
    public void RpcExplode()
    {
        if (explosionPrefab)
        {
            GameObject fx = Instantiate(explosionPrefab, transform.position, Quaternion.identity);
            fx.transform.localScale = Vector3.one * explosionScale;

            var systems = fx.GetComponentsInChildren<ParticleSystem>();
            foreach (var ps in systems)
            {
                var main = ps.main;
                main.startSizeMultiplier *= explosionScale;
                main.startSpeedMultiplier *= explosionScale;
            }
            Destroy(fx, explosionLifetime);
        }

        if (!meshContainer) return;

        var parts = new Transform[meshContainer.childCount];
        for (int i = 0; i < meshContainer.childCount; i++) parts[i] = meshContainer.GetChild(i);

        foreach (Transform child in parts)
        {
            if (!child) continue;
            var rb = child.GetComponent<Rigidbody>() ?? child.gameObject.AddComponent<Rigidbody>();
            var col = child.GetComponent<Collider>() ?? child.gameObject.AddComponent<BoxCollider>();
            rb.useGravity = false; rb.isKinematic = false;
            child.parent = null;
            rb.AddExplosionForce(explosionForce, transform.position, explosionRadius, 0f, ForceMode.Impulse);
            rb.AddTorque(Random.insideUnitSphere * torqueStrength, ForceMode.Impulse);
            Destroy(child.gameObject, debrisLifetime);
        }
    }
}
