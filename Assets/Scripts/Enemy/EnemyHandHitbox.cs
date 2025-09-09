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

    }

    public void SetActive(bool on)
    {
        active = on;
        if (_col) _col.enabled = on; 
    }

    void TryHit(Collider other)
    {
        if (!active) return;

        if (requireAttackState && ownerFSM != null && ownerFSM.CurrentState != EnemyFSM.EnemyState.Attack)
            return;

 
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


    void OnTriggerEnter(Collider other) { TryHit(other); }
    void OnTriggerStay(Collider other) { TryHit(other); }


    void OnCollisionEnter(Collision c) { if (_col && !_col.isTrigger && c.collider) TryHit(c.collider); }
    void OnCollisionStay(Collision c) { if (_col && !_col.isTrigger && c.collider) TryHit(c.collider); }
}