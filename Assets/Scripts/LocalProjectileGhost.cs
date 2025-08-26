using UnityEngine;

public class LocalProjectileGhost : MonoBehaviour
{
    public Vector3 velocity;
    public float lifetime = 2f;
    public float gravity = 0f;

    void Update()
    {
        velocity += Vector3.down * gravity * Time.deltaTime;
        transform.position += velocity * Time.deltaTime;

        lifetime -= Time.deltaTime;
        if (lifetime <= 0f) Destroy(gameObject);
    }
}
