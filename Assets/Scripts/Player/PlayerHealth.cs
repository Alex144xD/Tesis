using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(PlayerMovement))]
public class PlayerHealth : MonoBehaviour, IDamageable
{
    [Header("Salud")]
    public float maxHealth = 100f;
    public float regenRate = 5f;
    public float regenDelay = 3f;

    [Header("Penalización de velocidad")]
    [Range(0f, 1f)] public float slowThreshold = 0.5f;
    [Range(0f, 1f)] public float slowFactor = 0.5f;

    [Header("Eventos (observer)")]
    public UnityEvent<float, float> onHealthChanged;   // (current,max)
    public UnityEvent<float, Transform> onDamaged;     // (amount, source)
    public UnityEvent onDeath;

    float currentHealth;
    bool canRegen = true;
    bool isDead = false;

    PlayerMovement movement;

    public bool IsDead => isDead;

    void Awake()
    {
        currentHealth = maxHealth;
        movement = GetComponent<PlayerMovement>();
        onHealthChanged?.Invoke(currentHealth, maxHealth);
    }

    // === IDamageable ===
    public void TakeDamage(DamageInfo info)
    {
        if (info.amount <= 0f) return;
        TakeDamage(info.amount);
        onDamaged?.Invoke(info.amount, info.source);
        
    }

  
    public void TakeDamage(float amount)
    {
        if (isDead) return;

        currentHealth = Mathf.Max(0f, currentHealth - amount);
        canRegen = false;
        CancelInvoke(nameof(EnableRegen));
        Invoke(nameof(EnableRegen), regenDelay);

        onHealthChanged?.Invoke(currentHealth, maxHealth);
        CheckPlayerDeath();
        CameraShake.instance.Shake(0.3f, 0.3f);
    }

    void EnableRegen() => canRegen = true;

    void Update()
    {
        if (isDead) return;

        if (canRegen && currentHealth < maxHealth)
        {
            // Tu diseño: regenera hasta el umbral si estás por debajo
            float regenCap = (currentHealth <= maxHealth * slowThreshold)
                ? maxHealth * slowThreshold
                : maxHealth;

            float prev = currentHealth;
            currentHealth = Mathf.Min(regenCap, currentHealth + regenRate * Time.deltaTime);

            if (!Mathf.Approximately(prev, currentHealth))
                onHealthChanged?.Invoke(currentHealth, maxHealth);
        }
    }

    void CheckPlayerDeath()
    {
        if (currentHealth <= 0f && !isDead)
        {
            isDead = true;
            Debug.Log("Jugador ha muerto");

            if (movement != null) movement.enabled = false;

            foreach (var enemy in FindObjectsOfType<EnemyFSM>())
                enemy.enabled = false;

            onDeath?.Invoke();
            if (GameManager.Instance != null)
                GameManager.Instance.PlayerLose();
        }
    }

    public float GetHealthNormalized() => currentHealth / maxHealth;
    public float GetSpeedFactor() => (currentHealth <= maxHealth * slowThreshold) ? slowFactor : 1f;

    public void Heal(float amount)
    {
        if (isDead || amount <= 0f) return;

        float prev = currentHealth;
        currentHealth = Mathf.Min(maxHealth, currentHealth + amount);

        if (currentHealth > prev)
            onHealthChanged?.Invoke(currentHealth, maxHealth);
    }
}