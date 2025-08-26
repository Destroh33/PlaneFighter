using UnityEngine;

public class Projectile : MonoBehaviour
{
    public float lifetime = 5f;
    public GameObject explosionPrefab;
    public float explosionScale = 2f;
    public float explosionLifetime = 2f;

    void Start()
    {
        Destroy(gameObject, lifetime);
    }

    void OnCollisionEnter(Collision other)
    {
        if (other.gameObject.CompareTag("Player"))
        {
            other.gameObject.GetComponent<ShipHealth>()?.TakeDamage(15);

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
        }

        Destroy(gameObject);
    }
}
