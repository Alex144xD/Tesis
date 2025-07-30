using UnityEngine;

public class PlayerHealth : MonoBehaviour
{
    [Header("Salud")]
    public float maxHealth = 100f;
    public float regenRate = 5f;
    public float regenDelay = 3f;

    [Header("Penalizaci�n de velocidad")]
    [Range(0f, 1f)] public float slowThreshold = 0.5f;
    [Range(0f, 1f)] public float slowFactor = 0.5f;

    private float currentHealth;
    private bool canRegen = true;

    private void Awake()
    {
        currentHealth = maxHealth;
    }

    public void TakeDamage(float amount)
    {
        currentHealth = Mathf.Max(0f, currentHealth - amount);
        canRegen = false;
        CancelInvoke(nameof(EnableRegen));
        Invoke(nameof(EnableRegen), regenDelay);

        CheckPlayerDeath();
    }

    private void EnableRegen()
    {
        canRegen = true;
    }

    private void Update()
    {
        if (canRegen && currentHealth < maxHealth)
        {
            float regenCap = (currentHealth <= maxHealth * slowThreshold)
                ? maxHealth * slowThreshold
                : maxHealth;

            currentHealth = Mathf.Min(
                regenCap,
                currentHealth + regenRate * Time.deltaTime
            );
        }
    }

    private void CheckPlayerDeath()
    {
        if (currentHealth <= 0f)
        {
            Debug.Log("Jugador ha muerto");
            if (GameManager.Instance != null)
                GameManager.Instance.PlayerLose();
        }
    }

    public float GetHealthNormalized()
    {
        return currentHealth / maxHealth;
    }

    public float GetSpeedFactor()
    {
        return (currentHealth <= maxHealth * slowThreshold) ? slowFactor : 1f;
    }

    public void Heal(float amount)
    {
        currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
    }
}