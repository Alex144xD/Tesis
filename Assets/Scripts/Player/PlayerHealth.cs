using UnityEngine;

public class PlayerHealth : MonoBehaviour
{
    [Header("Salud")]
    public float maxHealth = 100f;
    [Tooltip("Puntos de salud regenerados por segundo")]
    public float regenRate = 5f;
    [Tooltip("Retraso en s antes de empezar a regenerar")]
    public float regenDelay = 3f;

    [Header("Penalización de velocidad")]
    [Tooltip("Por debajo de este % de salud, se aplica slowdown")]
    [Range(0f, 1f)] public float slowThreshold = 0.5f;
    [Tooltip("Factor de velocidad cuando estás por debajo del umbral")]
    [Range(0f, 1f)] public float slowFactor = 0.5f;

    private float currentHealth;
    private bool canRegen = true;

    void Awake()
    {
        currentHealth = maxHealth;
    }

    public void TakeDamage(float amount)
    {
        currentHealth = Mathf.Max(0f, currentHealth - amount);
        canRegen = false;
        CancelInvoke(nameof(EnableRegen));
        Invoke(nameof(EnableRegen), regenDelay);
    }

    void EnableRegen()
    {
        canRegen = true;
    }

    void Update()
    {
        if (canRegen && currentHealth < maxHealth)
            currentHealth = Mathf.Min(maxHealth,
                currentHealth + regenRate * Time.deltaTime);
    }


    public float GetHealthNormalized()
    {
        return currentHealth / maxHealth;
    }
}

