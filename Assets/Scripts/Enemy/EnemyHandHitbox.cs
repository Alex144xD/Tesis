using UnityEngine;
using System.Collections.Generic;

[DisallowMultipleComponent]
public class SimpleDamageBox : MonoBehaviour
{
    [Header("Daño")]
    public int damage = 10;

    [Header("Control")]
    [Tooltip("Si está activo, puede dañar al tocar. Lo enciende/apaga el EnemyFSM.")]
    public bool active = false;

    [Tooltip("Evita multigolpes spameados; tiempo mínimo entre golpes por mismo objetivo.")]
    public float perTargetCooldown = 0.35f;

    [Tooltip("Opcional: referencia al EnemyFSM para solo dañar si el estado es Attack.")]
    public EnemyFSM ownerFSM;
    public bool requireAttackState = true;

    private Collider _col;
    private readonly Dictionary<Transform, float> _lastHitTime = new Dictionary<Transform, float>();

    void Awake()
    {
        _col = GetComponent<Collider>();
        if (!_col)
        {
            Debug.LogWarning($"[SimpleDamageBox] No hay Collider en {name}. Agrega un BoxCollider.");
        }
        // Si lo usas como trigger, asegúrate de tener un Rigidbody (isKinematic) en el root del enemigo.
    }

    public void SetActive(bool on)
    {
        active = on;
        if (_col) _col.enabled = on; // opcional; apaga/enciende físicamente el collider
    }

    void TryHit(Collider other)
    {
        if (!active) return;

        if (requireAttackState && ownerFSM != null && ownerFSM.CurrentState != EnemyFSM.EnemyState.Attack)
            return;

        // Busca PlayerHealth en el objetivo o sus padres
        var hp = other.GetComponent<PlayerHealth>();
        if (!hp) hp = other.GetComponentInParent<PlayerHealth>();
        if (!hp) return;

        Transform key = hp.transform;
        float last;
        if (_lastHitTime.TryGetValue(key, out last))
        {
            if (Time.time - last < perTargetCooldown) return;
        }

        hp.TakeDamage(damage);
        _lastHitTime[key] = Time.time;
    }

    // --- Trigger (recomendado) ---
    void OnTriggerEnter(Collider other) { TryHit(other); }
    void OnTriggerStay(Collider other) { TryHit(other); }

    // --- Colisión sólida (si NO usas trigger) ---
    void OnCollisionEnter(Collision c) { if (_col && !_col.isTrigger && c.collider) TryHit(c.collider); }
    void OnCollisionStay(Collision c) { if (_col && !_col.isTrigger && c.collider) TryHit(c.collider); }
}