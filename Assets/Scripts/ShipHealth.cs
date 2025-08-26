using UnityEngine;

public class ShipHealth : MonoBehaviour
{
    public int maxHealth = 100;
    private int currentHealth;

    public ShipExploder exploder;

    void Start()
    {
        currentHealth = maxHealth;
        if (exploder == null) exploder = GetComponent<ShipExploder>();
    }

    public void TakeDamage(int amount)
    {
        currentHealth -= amount;
        if (currentHealth <= 0) Die();
    }

    void Die()
    {
        if (exploder != null) exploder.Explode();
        Destroy(gameObject);
    }
}