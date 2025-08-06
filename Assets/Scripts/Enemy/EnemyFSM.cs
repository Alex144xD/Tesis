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
    public float recalculatePathInterval = 1.5f;

    [Header("Velocidades")]
    public float patrolSpeed = 2f;
    public float chaseSpeed = 3.5f;

    [Header("Daño")]
    public float attackDamage = 10f;
    public float attackCooldown = 1f;

    public int floorIndex = 0;

    private static Transform player;
    private CharacterController controller;
    private Animator animator;
    private List<Vector3> currentPath;
    private int pathIndex;
    private float recalcTimer;
    private float lastAttackTime;

    // Patrullaje compartido
    private static HashSet<Vector2Int> occupiedPatrolCells = new HashSet<Vector2Int>();
    private Vector2Int reservedCell;

    void Start()
    {
        controller = GetComponent<CharacterController>();
        animator = GetComponent<Animator>();

        if (player == null)
            player = GameObject.FindGameObjectWithTag("Player").transform;

        currentState = EnemyState.Idle;
        MultiFloorDynamicMapManager.Instance.OnMapUpdated += OnMapChanged;

        Invoke(nameof(StartPatrolling), 1f);
    }

    void Update()
    {
        switch (currentState)
        {
            case EnemyState.Idle:
                Idle();
                break;
            case EnemyState.Patrol:
                Patrol();
                break;
            case EnemyState.Chase:
                Chase();
                break;
            case EnemyState.Attack:
                Attack();
                break;
            case EnemyState.Flee:
                Flee();
                break;
        }
    }

    // ---------------- Estados ----------------

    void Idle()
    {
        SetAnimation("Idle");
        if (Vector3.Distance(transform.position, player.position) <= detectionRange)
            currentState = EnemyState.Chase;
    }

    void Patrol()
    {
        SetAnimation("Walk");
        MoveAlongPath(patrolSpeed);

        if (Vector3.Distance(transform.position, player.position) <= detectionRange)
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

        if (recalcTimer >= recalculatePathInterval)
        {
            currentPath = PathfindingBFS.Instance.FindPath(floorIndex, transform.position, player.position);
            pathIndex = 0;
            recalcTimer = 0f;
        }

        MoveAlongPath(chaseSpeed);

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

        Vector3 direction = (player.position - transform.position).normalized;
        controller.SimpleMove(direction * chaseSpeed * 0.5f);

        if (Time.time >= lastAttackTime + attackCooldown)
        {
            var playerHealth = player.GetComponent<PlayerHealth>();
            if (playerHealth != null)
                playerHealth.TakeDamage(attackDamage);

            lastAttackTime = Time.time;
        }

        if (Vector3.Distance(transform.position, player.position) > attackRange)
            currentState = EnemyState.Chase;
    }

    void Flee()
    {
        SetAnimation("Run");
        MoveAlongPath(chaseSpeed * 1.3f);

        if (Vector3.Distance(transform.position, player.position) > detectionRange * 2f)
        {
            currentState = EnemyState.Patrol;
            GoToRandomPatrolPoint();
        }
    }

    // ---------------- Movimiento ----------------

    void MoveAlongPath(float speed)
    {
        if (currentPath == null || pathIndex >= currentPath.Count) return;

        Vector3 targetPos = currentPath[pathIndex];
        Vector3 direction = (targetPos - transform.position).normalized;

        controller.SimpleMove(direction * speed);

        if (Vector3.Distance(transform.position, targetPos) < 0.2f)
            pathIndex++;
    }

    bool ReachedPathEnd()
    {
        return currentPath == null || pathIndex >= currentPath.Count;
    }

    void GoToRandomPatrolPoint()
    {
        var freeCells = MultiFloorDynamicMapManager.Instance.GetFreeCells(floorIndex);
        if (freeCells.Count == 0) return;

        freeCells.RemoveAll(cell => occupiedPatrolCells.Contains(cell));
        if (freeCells.Count == 0) return;

        reservedCell = freeCells[Random.Range(0, freeCells.Count)];
        occupiedPatrolCells.Add(reservedCell);

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
        if (reservedCell != Vector2Int.zero)
        {
            occupiedPatrolCells.Remove(reservedCell);
            reservedCell = Vector2Int.zero;
        }
    }

    private void OnMapChanged()
    {
        if (currentState == EnemyState.Chase)
        {
            currentPath = PathfindingBFS.Instance.FindPath(floorIndex, transform.position, player.position);
        }
        else if (currentState == EnemyState.Patrol && currentPath != null && pathIndex < currentPath.Count)
        {
            currentPath = PathfindingBFS.Instance.FindPath(floorIndex, transform.position, currentPath[pathIndex]);
        }
        else if (currentState == EnemyState.Flee && currentPath != null && pathIndex < currentPath.Count)
        {
            currentPath = PathfindingBFS.Instance.FindPath(floorIndex, transform.position, currentPath[pathIndex]);
        }
        pathIndex = 0;
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
        var freeCells = MultiFloorDynamicMapManager.Instance.GetFreeCells(floorIndex);
        if (freeCells.Count == 0) return;

        Vector2Int farthestCell = freeCells[0];
        float maxDist = 0f;

        foreach (var cell in freeCells)
        {
            Vector3 cellPos = MultiFloorDynamicMapManager.Instance.CellToWorld(cell, floorIndex);
            float dist = Vector3.Distance(cellPos, player.position);
            if (dist > maxDist)
            {
                maxDist = dist;
                farthestCell = cell;
            }
        }

        Vector3 fleePos = MultiFloorDynamicMapManager.Instance.CellToWorld(farthestCell, floorIndex);
        currentPath = PathfindingBFS.Instance.FindPath(floorIndex, transform.position, fleePos);
        pathIndex = 0;
    }

    // ---------------- Animaciones ----------------

    void SetAnimation(string state)
    {
        if (animator == null) return;

        animator.SetBool("Idle", state == "Idle");
        animator.SetBool("Walk", state == "Walk");
        animator.SetBool("Run", state == "Run");
        animator.SetBool("Attack", state == "Attack");
    }
}