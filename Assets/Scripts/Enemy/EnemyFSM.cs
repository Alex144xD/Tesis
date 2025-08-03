using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

public class EnemyFSM : MonoBehaviour
{
    public enum EnemyState { Patrol, Chase, Attack }
    private EnemyState currentState;

    private NavMeshAgent agent;
    private Animator animator;
    private Transform player;

    [Header("Detección")]
    public float detectionRange = 6f;
    public float viewAngle = 120f;
    public float attackRange = 1.5f;
    public LayerMask playerLayer;

    [Header("Patrulla")]
    public int patrolAreaIndex = 0;
    private List<Transform> patrolPoints;
    private int currentPoint = -1;
    private float repeatTimer = 0f;

    [Header("Velocidades")]
    public float patrolSpeed = 2f;
    public float chaseSpeed = 4f;

    [Header("Daño")]
    public float attackDamage = 10f;
    public float attackCooldown = 1f;
    private float lastAttackTime = 0f;

    [Header("Chase Timeout")]
    public float maxChaseTime = 10f;
    private float chaseTimer = 0f;

    [Header("Anti-atascos")]
    public float stuckThreshold = 0.1f;
    public float stuckCheckInterval = 2f;
    private float stuckTimer = 0f;
    private Vector3 lastPosition;

    void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponent<Animator>();
        player = GameObject.FindGameObjectWithTag("Player")?.transform;
    }

    void Start()
    {
        currentState = EnemyState.Patrol;
        patrolAreaIndex = GetEnemyAssignedArea();
        AssignPatrolPoints();
        GoToNextPatrolPoint();
        lastPosition = transform.position;
    }

    void Update()
    {
        switch (currentState)
        {
            case EnemyState.Patrol:
                Patrol();
                break;
            case EnemyState.Chase:
                Chase();
                break;
            case EnemyState.Attack:
                Attack();
                break;
        }
        CheckStuck();
    }

    int GetEnemyAssignedArea()
    {
        int totalAreas = MultiFloorDynamicMapManager.Instance.GetTotalPatrolAreas();
        int enemyIndex = transform.GetSiblingIndex();
        return totalAreas > 0 ? enemyIndex % totalAreas : 0;
    }

    void Patrol()
    {
        agent.speed = patrolSpeed;
        animator.SetBool("isWalking", true);
        animator.SetBool("isRunning", false);

        if (!agent.pathPending && agent.remainingDistance < 0.5f)
        {
            repeatTimer += Time.deltaTime;
            if (repeatTimer >= 2f)
            {
                GoToNextPatrolPoint();
                repeatTimer = 0f;
            }
        }

        if (PlayerInSight())
        {
            currentState = EnemyState.Chase;
            chaseTimer = 0f;
        }
    }

    void Chase()
    {
        agent.speed = chaseSpeed;
        animator.SetBool("isWalking", false);
        animator.SetBool("isRunning", true);

        if (player != null)
            agent.SetDestination(player.position);

        chaseTimer += Time.deltaTime;

        if (Vector3.Distance(transform.position, player.position) <= attackRange)
        {
            currentState = EnemyState.Attack;
        }
        else if (!PlayerInSight() || chaseTimer >= maxChaseTime)
        {
            currentState = EnemyState.Patrol;
            GoToNextPatrolPoint();
        }
    }

    void Attack()
    {
        animator.SetTrigger("attack");
        agent.ResetPath();

        if (player != null && Vector3.Distance(transform.position, player.position) <= attackRange)
        {
            if (Time.time >= lastAttackTime + attackCooldown)
            {
                var playerHealth = player.GetComponent<PlayerHealth>();
                if (playerHealth != null)
                {
                    playerHealth.TakeDamage(attackDamage);
                }
                lastAttackTime = Time.time;
            }
        }

        if (Vector3.Distance(transform.position, player.position) > attackRange)
        {
            currentState = EnemyState.Chase;
        }
    }

    bool PlayerInSight()
    {
        if (player == null) return false;

        Vector3 dirToPlayer = (player.position - transform.position).normalized;
        float distance = Vector3.Distance(transform.position, player.position);

        if (distance <= detectionRange)
        {
            float angle = Vector3.Angle(transform.forward, dirToPlayer);
            if (angle < viewAngle / 2f)
            {
                return true;
            }
        }
        return false;
    }

    void AssignPatrolPoints()
    {
        var patrolArea = MultiFloorDynamicMapManager.Instance.GetPatrolArea(0, patrolAreaIndex);
        if (patrolArea != null)
            patrolPoints = patrolArea.patrolPoints;
        else
            patrolPoints = new List<Transform>();
    }

    void GoToNextPatrolPoint()
    {
        if (patrolPoints == null || patrolPoints.Count == 0) return;

        currentPoint = (currentPoint + 1) % patrolPoints.Count;
        agent.SetDestination(patrolPoints[currentPoint].position);
    }

    void CheckStuck()
    {
        stuckTimer += Time.deltaTime;
        if (stuckTimer >= stuckCheckInterval)
        {
            float moved = Vector3.Distance(transform.position, lastPosition);
            if (moved < stuckThreshold)
            {
                GoToNextPatrolPoint();
            }
            lastPosition = transform.position;
            stuckTimer = 0f;
        }
    }

    public void TakeFlashlightDamage(float damage = 10f)
    {
        Debug.Log($"{gameObject.name} recibió {damage} de daño de la linterna.");
    }
}