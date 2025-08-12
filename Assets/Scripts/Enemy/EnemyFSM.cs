using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(CharacterController))]
public class EnemyFSM : MonoBehaviour
{
    public enum EnemyState { Idle, Patrol, Chase, Attack, Flee }
    private EnemyState currentState;

    [Header("Detección")]
    public float detectionRange = 6f;
    public float attackRange = 1.5f;
    public float recalculatePathInterval = 1.0f;

    [Header("Velocidades")]
    public float patrolSpeed = 2f;
    public float chaseSpeed = 3.5f;

    [Header("Daño")]
    public float attackDamage = 10f;
    public float attackCooldown = 1f;

    [Header("Anti-atascos")]
    public float stuckRecalcTime = 1.5f;
    public float minMoveSqr = 0.01f; // ~0.1m

    public int floorIndex = 0;

    private static Transform player;
    private CharacterController controller;
    private Animator animator;
    private List<Vector3> currentPath;
    private int pathIndex;
    private float recalcTimer;
    private float lastAttackTime;

    private float stuckTimer;
    private Vector3 lastPos;

    private MultiFloorDynamicMapManager map;

    // Patrullaje compartido (por piso)
    private static HashSet<Vector3Int> occupiedPatrolCells = new HashSet<Vector3Int>();
    private Vector2Int reservedCell;
    private bool hasReservedCell = false;

    void Start()
    {
        controller = GetComponent<CharacterController>();
        animator = GetComponent<Animator>();
        map = MultiFloorDynamicMapManager.Instance;

        if (player == null)
        {
            var p = GameObject.FindGameObjectWithTag("Player");
            if (p) player = p.transform;
        }

        // Asegurar piso correcto al inicio
        floorIndex = Mathf.Clamp(Mathf.RoundToInt(-transform.position.y / map.floorHeight), 0, map.floors - 1);

        currentState = EnemyState.Idle;
        MultiFloorDynamicMapManager.Instance.OnMapUpdated += OnMapChanged;

        Invoke(nameof(StartPatrolling), 0.75f);
        lastPos = transform.position;
    }

    void LateUpdate()
    {
        // Si cambia de piso, actualízalo
        int fNow = Mathf.Clamp(Mathf.RoundToInt(-transform.position.y / map.floorHeight), 0, map.floors - 1);
        if (fNow != floorIndex) floorIndex = fNow;
    }

    void Update()
    {
        switch (currentState)
        {
            case EnemyState.Idle: Idle(); break;
            case EnemyState.Patrol: Patrol(); break;
            case EnemyState.Chase: Chase(); break;
            case EnemyState.Attack: Attack(); break;
            case EnemyState.Flee: Flee(); break;
        }

        // Anti-atascos genérico
        stuckTimer += Time.deltaTime;
        if ((transform.position - lastPos).sqrMagnitude > minMoveSqr)
        {
            stuckTimer = 0f;
            lastPos = transform.position;
        }
        else if (stuckTimer > stuckRecalcTime)
        {
            ForceRecalcToCurrentTarget();
            // micro nudge lateral
            Vector3 toPlayer = (player ? (player.position - transform.position) : Vector3.forward);
            Vector3 right = Vector3.Cross(Vector3.up, toPlayer).normalized;
            controller.SimpleMove(right * 0.5f);
            stuckTimer = 0f;
        }
    }

    // ---------------- Estados ----------------

    void Idle()
    {
        SetAnimation("Idle");
        if (player && Vector3.Distance(transform.position, player.position) <= detectionRange)
            currentState = EnemyState.Chase;
    }

    void Patrol()
    {
        SetAnimation("Walk");
        MoveAlongPath(patrolSpeed);

        if (player && Vector3.Distance(transform.position, player.position) <= detectionRange)
        {
            ClearReservedCell();
            currentState = EnemyState.Chase;
            recalcTimer = recalculatePathInterval; // fuerza recalc inmediato
        }

        if (ReachedPathEnd())
        {
            ClearReservedCell();
            GoToRandomPatrolPoint();
        }
    }

    void Chase()
    {
        SetAnimation("Run");
        recalcTimer += Time.deltaTime;

        if (recalcTimer >= recalculatePathInterval || ReachedPathEnd())
        {
            RecalcPathTo(player ? player.position : transform.position);
        }

        MoveAlongPath(chaseSpeed);

        if (!player) return;
        float dist = Vector3.Distance(transform.position, player.position);
        if (dist <= attackRange)
            currentState = EnemyState.Attack;
        else if (dist > detectionRange * 1.5f)
        {
            currentState = EnemyState.Patrol;
            GoToRandomPatrolPoint();
        }
    }

    void Attack()
    {
        SetAnimation("Attack");
        if (!player) { currentState = EnemyState.Patrol; return; }

        Vector3 direction = (player.position - transform.position);
        direction.y = 0f;
        direction.Normalize();
        controller.SimpleMove(direction * (chaseSpeed * 0.5f));

        if (Time.time >= lastAttackTime + attackCooldown)
        {
            var playerHealth = player.GetComponent<PlayerHealth>();
            if (playerHealth != null) playerHealth.TakeDamage(attackDamage);
            lastAttackTime = Time.time;
        }

        if (Vector3.Distance(transform.position, player.position) > attackRange)
            currentState = EnemyState.Chase;
    }

    void Flee()
    {
        SetAnimation("Run");
        MoveAlongPath(chaseSpeed * 1.3f);

        if (!player) return;
        if (Vector3.Distance(transform.position, player.position) > detectionRange * 2f)
        {
            currentState = EnemyState.Patrol;
            GoToRandomPatrolPoint();
        }
        else if (ReachedPathEnd())
        {
            // Si terminó la ruta pero aún está cerca, re-elegir otro destino lejano
            ChooseFleeDestination();
        }
    }

    // ---------------- Movimiento ----------------

    void MoveAlongPath(float speed)
    {
        if (currentPath == null || pathIndex >= currentPath.Count) return;

        Vector3 targetPos = currentPath[pathIndex];
        // Mantener altura para no pelear con SimpleMove/gravedad
        targetPos.y = transform.position.y;

        Vector3 direction = (targetPos - transform.position);
        direction.y = 0f;

        if (direction.sqrMagnitude > 0.0001f)
        {
            controller.SimpleMove(direction.normalized * speed);
        }

        if (Vector3.Distance(transform.position, targetPos) < 0.25f)
        {
            pathIndex++;
            // Si en CHASE se acaba la ruta, forzar recalc pronto
            if (pathIndex >= currentPath.Count && currentState == EnemyState.Chase)
                recalcTimer = recalculatePathInterval;
        }
        else if (currentState == EnemyState.Chase && (currentPath.Count - pathIndex) <= 1)
        {
            // Si ya vamos al último nodo y el jugador se movió, recalc
            recalcTimer = Mathf.Max(recalcTimer, recalculatePathInterval * 0.75f);
        }
    }

    bool ReachedPathEnd()
    {
        return currentPath == null || pathIndex >= currentPath.Count;
    }

    void RecalcPathTo(Vector3 worldTarget)
    {
        // Intento normal (evita santuarios)
        var path = PathfindingBFS.Instance.FindPath(floorIndex, transform.position, worldTarget);
        if (path == null || path.Count == 0)
        {
            // Fallback: camina directo un poco para “salir” y reintentar luego
            Vector3 dir = (worldTarget - transform.position);
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.01f)
                controller.SimpleMove(dir.normalized * patrolSpeed);
            // dejar currentPath tal cual; next frame volveremos a intentar
            return;
        }

        currentPath = path;
        pathIndex = 0;
        recalcTimer = 0f;
        // Debug.Log($"[{name}] Recalc path -> {currentPath.Count} nodes");
    }

    void GoToRandomPatrolPoint()
    {
        var free = MultiFloorDynamicMapManager.Instance.GetFreeCells(floorIndex);
        if (free.Count == 0) return;

        // Evita celdas reservadas por otros enemigos en este piso
        free.RemoveAll(cell => occupiedPatrolCells.Contains(new Vector3Int(cell.x, cell.y, floorIndex)));
        if (free.Count == 0) return;

        reservedCell = free[Random.Range(0, free.Count)];
        hasReservedCell = true;
        occupiedPatrolCells.Add(new Vector3Int(reservedCell.x, reservedCell.y, floorIndex));

        Vector3 patrolPos = MultiFloorDynamicMapManager.Instance.CellToWorld(reservedCell, floorIndex);
        currentPath = PathfindingBFS.Instance.FindPath(floorIndex, transform.position, patrolPos);
        pathIndex = 0;
        currentState = EnemyState.Patrol;
    }

    void StartPatrolling()
    {
        GoToRandomPatrolPoint();
    }

    void ClearReservedCell()
    {
        if (hasReservedCell)
        {
            occupiedPatrolCells.Remove(new Vector3Int(reservedCell.x, reservedCell.y, floorIndex));
            hasReservedCell = false;
        }
    }

    private void OnMapChanged()
    {
        Vector3 target;
        if (currentState == EnemyState.Chase && player != null)
            target = player.position;
        else if ((currentState == EnemyState.Patrol || currentState == EnemyState.Flee) && currentPath != null && pathIndex < currentPath.Count)
            target = currentPath[pathIndex];
        else
            return;

        RecalcPathTo(target);
    }

    private void OnDestroy()
    {
        ClearReservedCell();
        if (MultiFloorDynamicMapManager.Instance != null)
            MultiFloorDynamicMapManager.Instance.OnMapUpdated -= OnMapChanged;
    }

    // ---------------- Linterna ----------------

    public void OnFlashlightHit()
    {
        if (currentState != EnemyState.Flee)
        {
            ClearReservedCell();
            currentState = EnemyState.Flee;
            ChooseFleeDestination();
        }
    }

    private void ChooseFleeDestination()
    {
        var free = MultiFloorDynamicMapManager.Instance.GetFreeCells(floorIndex);
        if (free.Count == 0 || player == null) return;

        Vector2Int farthest = free[0];
        float maxDist = -1f;

        foreach (var cell in free)
        {
            Vector3 cellPos = MultiFloorDynamicMapManager.Instance.CellToWorld(cell, floorIndex);
            float dist = Vector3.Distance(cellPos, player.position);
            if (dist > maxDist)
            {
                maxDist = dist;
                farthest = cell;
            }
        }

        Vector3 fleePos = MultiFloorDynamicMapManager.Instance.CellToWorld(farthest, floorIndex);
        RecalcPathTo(fleePos);
    }

    private void ForceRecalcToCurrentTarget()
    {
        if (currentState == EnemyState.Chase)
        {
            currentPath = PathfindingBFS.Instance.FindPath(floorIndex, transform.position, player.position);
        }
        else if ((currentState == EnemyState.Patrol || currentState == EnemyState.Flee)
                 && currentPath != null && pathIndex < currentPath.Count)
        {
            currentPath = PathfindingBFS.Instance.FindPath(floorIndex, transform.position, currentPath[pathIndex]);
        }
        pathIndex = 0;
    }

    void SetAnimation(string state)
    {
        if (animator == null) return;
        animator.SetBool("Idle", state == "Idle");
        animator.SetBool("Walk", state == "Walk");
        animator.SetBool("Run", state == "Run");
        animator.SetBool("Attack", state == "Attack");
    }
}