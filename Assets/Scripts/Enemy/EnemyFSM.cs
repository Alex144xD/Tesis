using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(CharacterController))]
public class EnemyFSM : MonoBehaviour
{
    public enum EnemyState { Idle, Patrol, Chase, Attack, Flee }
    public enum EnemyKind { Basic, Heavy, Runner }

    [Header("Tipo de enemigo")]
    public EnemyKind kind = EnemyKind.Basic;

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
    public float stuckRecalcTime = 1.2f;
    public float minMoveSqr = 0.01f;

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

    private static HashSet<Vector3Int> occupiedPatrolCells = new HashSet<Vector3Int>();
    private Vector2Int reservedCell;
    private bool hasReservedCell = false;

    [Header("Opcional: LOS")]
    public bool requireLineOfSightToChase = false;
    public LayerMask losObstacles;
    private float onMapChangedCooldown = 0f;
    public float onMapChangedMinInterval = 0.15f;

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

        floorIndex = Mathf.Clamp(Mathf.RoundToInt(-transform.position.y / map.floorHeight), 0, map.floors - 1);

        currentState = EnemyState.Idle;
        MultiFloorDynamicMapManager.Instance.OnMapUpdated += OnMapChanged;

        Invoke(nameof(StartPatrolling), 0.75f);
        lastPos = transform.position;
    }

    void LateUpdate()
    {
        int fNow = Mathf.Clamp(Mathf.RoundToInt(-transform.position.y / map.floorHeight), 0, map.floors - 1);
        if (fNow != floorIndex) floorIndex = fNow;
        if (onMapChangedCooldown > 0f) onMapChangedCooldown -= Time.deltaTime;
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

        stuckTimer += Time.deltaTime;
        if ((transform.position - lastPos).sqrMagnitude > minMoveSqr)
        {
            stuckTimer = 0f;
            lastPos = transform.position;
        }
        else if (stuckTimer > stuckRecalcTime)
        {
            RecoverFromStuck();
            stuckTimer = 0f;
        }
    }

    // ---------------- Estados ----------------

    void Idle()
    {
        SetAnimation("Idle");
        if (PlayerInDetection()) currentState = EnemyState.Chase;
    }

    void Patrol()
    {
        SetAnimation("Walk");
        MoveAlongPath(patrolSpeed);

        if (PlayerInDetection())
        {
            ClearReservedCell();
            currentState = EnemyState.Chase;
            recalcTimer = recalculatePathInterval;
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

        if (ReachedPathEnd())
            recalcTimer = recalculatePathInterval;

        if (recalcTimer >= recalculatePathInterval)
        {
            RecalcPathTo(player ? player.position : transform.position);
        }

        MoveAlongPath(chaseSpeed);

        if (!player) return;
        float dist = Vector3.Distance(transform.position, player.position);
        if (dist <= attackRange)
            currentState = EnemyState.Attack;
        else if (!PlayerInDetection())
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
            ChooseFleeDestination();
        }
    }

    // ---------------- Movimiento ----------------

    void MoveAlongPath(float speed)
    {
        if (currentPath == null || pathIndex >= currentPath.Count)
        {
            if (currentState == EnemyState.Chase)
                recalcTimer = recalculatePathInterval;
            return;
        }

        Vector3 targetPos = currentPath[pathIndex];
        targetPos.y = transform.position.y;

        Vector3 to = targetPos - transform.position;
        to.y = 0f;

        float reachDist = MultiFloorDynamicMapManager.Instance.cellSize * 0.45f;
        if (to.sqrMagnitude <= reachDist * reachDist)
        {
            pathIndex++;
            if (pathIndex >= currentPath.Count && currentState == EnemyState.Chase)
                recalcTimer = recalculatePathInterval;
            return;
        }

        Vector3 dir = to.normalized;
        var flags = controller.Move(dir * speed * Time.deltaTime);

        if ((flags & CollisionFlags.Sides) != 0)
        {
            Vector3 right = Vector3.Cross(Vector3.up, dir).normalized;
            controller.Move(right * 0.2f);
        }
    }

    bool ReachedPathEnd() => currentPath == null || pathIndex >= currentPath.Count;

    void RecalcPathTo(Vector3 worldTarget)
    {
        var path = PathfindingBFS.Instance.FindPathStrict(floorIndex, transform.position, worldTarget);
        if (path != null && path.Count > 0)
        {
            currentPath = path;
            pathIndex = 0;
            recalcTimer = 0f;
        }
    }

    void GoToRandomPatrolPoint()
    {
        var free = MultiFloorDynamicMapManager.Instance.GetFreeCells(floorIndex);
        if (free.Count == 0) return;

        free.RemoveAll(cell => occupiedPatrolCells.Contains(new Vector3Int(cell.x, cell.y, floorIndex)));
        if (free.Count == 0) return;

        reservedCell = free[Random.Range(0, free.Count)];
        hasReservedCell = true;
        occupiedPatrolCells.Add(new Vector3Int(reservedCell.x, reservedCell.y, floorIndex));

        Vector3 patrolPos = MultiFloorDynamicMapManager.Instance.CellCenterToWorld(reservedCell, floorIndex);
        currentPath = PathfindingBFS.Instance.FindPathStrict(floorIndex, transform.position, patrolPos);
        pathIndex = 0;
        currentState = EnemyState.Patrol;
    }

    void StartPatrolling() => GoToRandomPatrolPoint();

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
        if (onMapChangedCooldown > 0f) return;
        onMapChangedCooldown = onMapChangedMinInterval;

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

    // ---------------- Linterna (reacciones) ----------------

    public void OnFlashlightHit()
    {
        if (currentState != EnemyState.Flee)
        {
            ClearReservedCell();
            currentState = EnemyState.Flee;
            ChooseFleeDestination();
        }
    }

    public void OnFlashlightHitByBattery(BatteryType type)
    {
        bool shouldFlee = false;
        switch (kind)
        {
            case EnemyKind.Basic: shouldFlee = (type == BatteryType.Green || type == BatteryType.Red); break;
            case EnemyKind.Heavy: shouldFlee = (type == BatteryType.Red); break;
            case EnemyKind.Runner: shouldFlee = (type == BatteryType.Blue); break;
        }

        if (shouldFlee)
        {
            if (currentState != EnemyState.Flee)
            {
                ClearReservedCell();
                currentState = EnemyState.Flee;
                ChooseFleeDestination();
            }
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
            Vector3 cellPos = MultiFloorDynamicMapManager.Instance.CellCenterToWorld(cell, floorIndex);
            float dist = Vector3.Distance(cellPos, player.position);
            if (dist > maxDist) { maxDist = dist; farthest = cell; }
        }

        Vector3 fleePos = MultiFloorDynamicMapManager.Instance.CellCenterToWorld(farthest, floorIndex);
        RecalcPathTo(fleePos);
    }

    void RecoverFromStuck()
    {
        if (currentState == EnemyState.Chase && player != null)
        {
            if (PathfindingBFS.Instance.TryFindPathStrict(floorIndex, transform.position, player.position, out var p1))
            {
                currentPath = p1; pathIndex = 0; recalcTimer = 0f;
                return;
            }
        }
        else if ((currentState == EnemyState.Patrol || currentState == EnemyState.Flee) && currentPath != null && pathIndex < currentPath.Count)
        {
            var target = currentPath[pathIndex];
            if (PathfindingBFS.Instance.TryFindPathStrict(floorIndex, transform.position, target, out var p2))
            {
                currentPath = p2; pathIndex = 0; recalcTimer = 0f;
                return;
            }
        }

        var wander = PathfindingBFS.Instance.FindWanderTarget(floorIndex, transform.position, player, minDistCells: 4f);
        if (PathfindingBFS.Instance.TryFindPathStrict(floorIndex, transform.position, wander, out var p3))
        {
            currentPath = p3; pathIndex = 0; recalcTimer = 0f;
            return;
        }

        Vector3 toPlayer = (player ? (player.position - transform.position) : Vector3.forward);
        Vector3 right = Vector3.Cross(Vector3.up, toPlayer).normalized;
        controller.SimpleMove(right * 0.6f);
        recalcTimer = recalculatePathInterval;
    }

    // ---------------- Utilidades ----------------

    bool PlayerInDetection()
    {
        if (!player) return false;
        if (Vector3.Distance(transform.position, player.position) > detectionRange) return false;

        if (!requireLineOfSightToChase) return true;

        Vector3 origin = transform.position + Vector3.up * 0.6f;
        Vector3 target = player.position + Vector3.up * 0.6f;
        Vector3 dir = (target - origin).normalized;
        float dist = Vector3.Distance(origin, target);

        if (Physics.Raycast(origin, dir, out RaycastHit hit, dist, losObstacles))
            return false;

        return true;
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