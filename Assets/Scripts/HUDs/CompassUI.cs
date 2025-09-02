using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class CompassUI : MonoBehaviour
{
    [Header("Refs")]
    [Tooltip("Jugador (si se deja vacío intentará encontrarlo por tag 'Player').")]
    public Transform player;
    [Tooltip("Aguja que apunta al fragmento (roja).")]
    public RectTransform fragmentNeedle;
    [Tooltip("Aguja que apunta a enemigos cercanos (blanca).")]
    public RectTransform enemyNeedle;

    [Header("Fragment Targeting")]
    [Tooltip("Si hay varios CompassTarget, prioriza el que tenga isPrimary.")]
    public bool preferPrimaryTargets = true;
    [Tooltip("Velocidad de giro de la aguja de fragmento.")]
    public float fragmentNeedleTurnSpeed = 720f; // deg/s

    [Header("Enemy Targeting")]
    [Tooltip("Radio de detección de enemigos en unidades de mundo.")]
    public float enemyDetectRadius = 25f;
    [Tooltip("Cada cuánto actualizar la búsqueda de enemigo más cercano.")]
    public float detectInterval = 0.35f;
    [Tooltip("Cap de enemigos a considerar por escaneo (performance).")]
    public int maxCandidates = 64;
    [Tooltip("Velocidad de giro de la aguja de enemigo.")]
    public float enemyNeedleTurnSpeed = 720f; // deg/s
    [Tooltip("Ocultar/atenuar la aguja si no hay enemigo cercano.")]
    public bool hideEnemyNeedleWhenNone = true;

    [Header("Línea de vista (opcional)")]
    [Tooltip("Comprobar si hay LOS jugador->enemigo con Raycast.")]
    public bool useLineOfSightCheck = true;
    [Tooltip("Layers considerados como bloqueo de visión (paredes).")]
    public LayerMask losBlockMask;
    [Tooltip("Si no hay LOS, reducir alpha de la aguja a este valor.")]
    [Range(0f, 1f)] public float occludedAlpha = 0.35f;

    [Header("UI Fading")]
    [Range(0f, 1f)] public float enemyNeedleVisibleAlpha = 1f;
    [Range(0f, 1f)] public float enemyNeedleHiddenAlpha = 0f;

    // cache
    readonly List<CompassTarget> _targets = new List<CompassTarget>(16);
    float _scanTimer;
    Transform _nearestEnemy;
    bool _nearestEnemyHasLOS = false;

    void Awake()
    {
        // Player autowire
        if (!player)
        {
            var go = GameObject.FindGameObjectWithTag("Player");
            if (go) player = go.transform;
        }
    }

    void OnEnable()
    {
        // Fuerza primer escaneo inmediato
        _scanTimer = 999f;
    }

    void Update()
    {
        if (!player) return;

        // 1) Fragment needle (roja)
        var frag = PickFragmentTarget();
        if (fragmentNeedle)
            AimNeedle(fragmentNeedle, frag ? frag.transform.position : (Vector3?)null, fragmentNeedleTurnSpeed);

        // 2) Enemy needle (blanca)
        _scanTimer += Time.deltaTime;
        if (_scanTimer >= detectInterval)
        {
            _scanTimer = 0f;
            (_nearestEnemy, _nearestEnemyHasLOS) = FindNearestEnemy(player.position, enemyDetectRadius);
        }

        bool hasEnemy = _nearestEnemy != null;
        if (enemyNeedle)
        {
            if (hasEnemy)
            {
                // Rotamos hacia el enemigo
                AimNeedle(enemyNeedle, _nearestEnemy.position, enemyNeedleTurnSpeed);

                // Mostrar/atenuar según LOS
                float targetAlpha = enemyNeedleVisibleAlpha;
                if (useLineOfSightCheck && !_nearestEnemyHasLOS)
                    targetAlpha = Mathf.Min(targetAlpha, occludedAlpha);
                SetNeedleAlpha(enemyNeedle, targetAlpha);
            }
            else
            {
                // Sin enemigo cercano
                float a = hideEnemyNeedleWhenNone ? enemyNeedleHiddenAlpha : occludedAlpha;
                SetNeedleAlpha(enemyNeedle, a);
            }
        }
    }

    // ======== Fragment target picking ========
    CompassTarget PickFragmentTarget()
    {
        _targets.Clear();
        CompassTarget[] all = GameObject.FindObjectsOfType<CompassTarget>(true);
        for (int i = 0; i < all.Length; i++)
        {
            var t = all[i];
            if (!t || !t.gameObject.activeInHierarchy) continue;
            _targets.Add(t);
        }
        if (_targets.Count == 0) return null;

        CompassTarget best = null;
        float bestScore = float.NegativeInfinity;

        for (int i = 0; i < _targets.Count; i++)
        {
            var t = _targets[i];
            if (!t) continue;

            // Prioriza primario si está activo
            float pri = (preferPrimaryTargets && t.isPrimary) ? 10000f : 0f;
            // Más cercano mejor
            float dist = Vector3.SqrMagnitude(t.transform.position - player.position);
            float score = pri - dist;

            if (score > bestScore)
            {
                bestScore = score;
                best = t;
            }
        }
        return best;
    }

    // ======== Enemy search ========
    (Transform tr, bool hasLOS) FindNearestEnemy(Vector3 from, float radius)
    {
        Transform best = null;
        float bestDist2 = float.PositiveInfinity;
        bool bestLOS = false;

        // 1) Primero intenta por EnemyFSM
        var enemiesFSM = GameObject.FindObjectsOfType<MonoBehaviour>(false);
        int seen = 0;
        for (int i = 0; i < enemiesFSM.Length && seen < maxCandidates; i++)
        {
            var mb = enemiesFSM[i];
            if (!mb || !mb.gameObject.activeInHierarchy) continue;
            // Usa el nombre de clase para evitar hard-dependencia si EnemyFSM no siempre está en el proyecto
            if (mb.GetType().Name != "EnemyFSM") continue;

            seen++;
            var tr = mb.transform;
            float d2 = (tr.position - from).sqrMagnitude;
            if (d2 > radius * radius) continue;

            bool hasLOS = !useLineOfSightCheck || HasLOS(from, tr.position);
            if (d2 < bestDist2)
            {
                best = tr; bestDist2 = d2; bestLOS = hasLOS;
            }
        }

        // 2) Si no encontró con EnemyFSM, intenta por Tag "Enemy"
        if (!best)
        {
            var tagged = GameObject.FindGameObjectsWithTag("Enemy");
            for (int i = 0; i < tagged.Length && i < maxCandidates; i++)
            {
                var go = tagged[i];
                if (!go || !go.activeInHierarchy) continue;

                float d2 = (go.transform.position - from).sqrMagnitude;
                if (d2 > radius * radius) continue;

                bool hasLOS = !useLineOfSightCheck || HasLOS(from, go.transform.position);
                if (d2 < bestDist2)
                {
                    best = go.transform; bestDist2 = d2; bestLOS = hasLOS;
                }
            }
        }

        return (best, bestLOS);
    }

    bool HasLOS(Vector3 from, Vector3 to)
    {
        Vector3 dir = (to - from);
        float dist = dir.magnitude;
        if (dist <= 0.001f) return true;
        dir /= dist;

        // Levanta un poco el origen y el destino para raycast “de ojos”
        from.y += 1.5f;
        to.y += 1.5f;
        dir = (to - from).normalized;
        float maxDist = Vector3.Distance(from, to);

        return !Physics.Raycast(from, dir, maxDist, losBlockMask, QueryTriggerInteraction.Ignore);
    }

    // ======== Needles control ========
    void AimNeedle(RectTransform needle, Vector3? worldTarget, float turnSpeed)
    {
        if (!needle || !player) return;

        // Si no hay target, no rotamos (pero podría estar oculta).
        if (!worldTarget.HasValue) return;

        Vector3 to = worldTarget.Value - player.position;
        to.y = 0f;
        if (to.sqrMagnitude < 0.0001f) return;

        float angle = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg; // plano XZ, 0° = +Z
        // Suavizado: Lerp angular hacia target
        float current = needle.eulerAngles.z;
        float target = -angle; // UI suele estar con eje Z invertido para agujas 2D
        float maxStep = turnSpeed * Time.deltaTime;
        float newZ = Mathf.MoveTowardsAngle(current, target, maxStep);

        var e = needle.eulerAngles;
        e.z = newZ;
        needle.eulerAngles = e;
    }

    void SetNeedleAlpha(RectTransform needle, float a)
    {
        if (!needle) return;
        var g = needle.GetComponent<Graphic>();
        if (g)
        {
            var c = g.color;
            c.a = a;
            g.color = c;
        }
        // Si la aguja tiene hijos (sprite + sombra), ajusta todos
        var graphics = needle.GetComponentsInChildren<Graphic>(true);
        for (int i = 0; i < graphics.Length; i++)
        {
            var cg = graphics[i].color;
            cg.a = a;
            graphics[i].color = cg;
        }
    }
}