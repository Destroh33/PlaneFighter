using UnityEngine;

public class ShipExploder : MonoBehaviour
{
    public Transform meshContainer;
    public float explosionForce = 500f;
    public float explosionRadius = 10f;
    public float torqueStrength = 200f;
    public float debrisLifetime = 5f;

    public GameObject explosionPrefab;
    public float explosionScale = 2f;
    public float explosionLifetime = 2f;

    public void Explode()
    {
        if (explosionPrefab != null)
        {
            GameObject fx = Instantiate(explosionPrefab, transform.position, Quaternion.identity);

            fx.transform.localScale = Vector3.one * explosionScale;

            ParticleSystem[] systems = fx.GetComponentsInChildren<ParticleSystem>();
            foreach (ParticleSystem ps in systems)
            {
                var main = ps.main;
                main.startSizeMultiplier *= explosionScale;
                main.startSpeedMultiplier *= explosionScale;
            }

            Destroy(fx, explosionLifetime);
        }

        if (meshContainer == null) return;

        Transform[] parts = new Transform[meshContainer.childCount];
        for (int i = 0; i < meshContainer.childCount; i++)
        {
            parts[i] = meshContainer.GetChild(i);
        }

        foreach (Transform child in parts)
        {
            if (child == null) continue;

            Rigidbody rb = child.gameObject.GetComponent<Rigidbody>();
            if (rb == null) rb = child.gameObject.AddComponent<Rigidbody>();

            Collider col = child.gameObject.GetComponent<Collider>();
            if (col == null) col = child.gameObject.AddComponent<BoxCollider>();

            rb.useGravity = false;
            rb.isKinematic = false;

            child.parent = null;

            rb.AddExplosionForce(explosionForce, transform.position, explosionRadius, 0f, ForceMode.Impulse);
            rb.AddTorque(Random.insideUnitSphere * torqueStrength, ForceMode.Impulse);

            Destroy(child.gameObject, debrisLifetime);
        }

        Destroy(gameObject);
    }
}
