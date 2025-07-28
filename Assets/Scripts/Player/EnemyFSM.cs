using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(CapsuleCollider))]
[RequireComponent(typeof(Animator))]
public class EnemyFSM : MonoBehaviour
{
    public enum State { Patrol, Chase, Attack, Flee, Dead }
    private State currentState;

    [Header("General")]
    public float chaseRange = 10f;
    public float attackRange = 2f;
    public float damagePerSecond = 10f;
    public float fleeDistance = 5f;
    public float fleeDuration = 3f;
    public float health = 100f;

    [Header("Referencias")]
    public MultiFloorDynamicMapManager mapManager;

    private NavMeshAgent agent;
    private Animator animator;
    private Transform player;
    private PlayerHealth playerHealth;

    private bool isDead = false;
    private float fleeTimer = 0f;

    private List<Transform> patrolPoints;
    private Transform currentPatrolPoint;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponent<Animator>();
        CapsuleCollider collider = GetComponent<CapsuleCollider>();

        // Configurar collider para evitar traspasar muros
        collider.isTrigger = false;
        collider.height = 2f;
        collider.radius = 0.5f;

        // Referencias
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            player = playerObj.transform;
            playerHealth = player.GetComponent<PlayerHealth>();
        }

        if (mapManager == null)
            mapManager = FindObjectOfType<MultiFloorDynamicMapManager>();

        patrolPoints = GetPatrolPointsForCurrentFloor();
        PickNextPatrolPoint();

        currentState = State.Patrol;
    }

    void Update()
    {
        if (isDead || player == null) return;

        float distanceToPlayer = Vector3.Distance(transform.position, player.position);

        switch (currentState)
        {
            case State.Patrol:
                Patrol();
                if (distanceToPlayer <= chaseRange)
                    ChangeState(State.Chase);
                break;

            case State.Chase:
                Chase();
                if (distanceToPlayer <= attackRange)
                    ChangeState(State.Attack);
                else if (distanceToPlayer > chaseRange)
                    ChangeState(State.Patrol);
                break;

            case State.Attack:
                Attack(distanceToPlayer);
                break;

            case State.Flee:
                Flee();
                break;
        }
    }

    private void Patrol()
    {
        animator.SetBool("IsWalking", true);
        animator.SetBool("IsAttacking", false);

        if (currentPatrolPoint == null)
        {
            PickNextPatrolPoint();
            return;
        }

        agent.isStopped = false;
        agent.SetDestination(currentPatrolPoint.position);

        if (!agent.pathPending && agent.remainingDistance < 0.5f)
            PickNextPatrolPoint();
    }

    private void Chase()
    {
        animator.SetBool("IsWalking", true);
        animator.SetBool("IsAttacking", false);

        agent.isStopped = false;
        agent.SetDestination(player.position);
    }

    private void Attack(float distance)
    {
        animator.SetBool("IsWalking", false);
        animator.SetBool("IsAttacking", true);

        agent.isStopped = true;
        transform.LookAt(player.position);

        if (distance > attackRange)
        {
            ChangeState(State.Chase);
        }
        else if (playerHealth != null)
        {
            playerHealth.TakeDamage(damagePerSecond * Time.deltaTime);
        }
    }

    private void Flee()
    {
        animator.SetBool("IsWalking", true);
        animator.SetBool("IsAttacking", false);

        fleeTimer -= Time.deltaTime;
        if (fleeTimer <= 0)
        {
            ChangeState(State.Patrol);
            return;
        }

        Vector3 fleeDirection = (transform.position - player.position).normalized;
        Vector3 fleePosition = transform.position + fleeDirection * fleeDistance;

        NavMeshHit hit;
        if (NavMesh.SamplePosition(fleePosition, out hit, 2f, NavMesh.AllAreas))
        {
            agent.isStopped = false;
            agent.SetDestination(hit.position);
        }
    }

    public void TakeFlashlightDamage(float amount)
    {
        if (isDead) return;

        health -= amount;
        if (health <= 0)
        {
            Die();
        }
        else
        {
            ChangeState(State.Flee);
            fleeTimer = fleeDuration;
        }
    }

    private void Die()
    {
        if (isDead) return;

        isDead = true;
        currentState = State.Dead;
        agent.isStopped = true;
        animator.SetBool("IsDead", true);
        Destroy(gameObject, 3f); // se elimina después de la animación
    }

    private void PickNextPatrolPoint()
    {
        if (patrolPoints == null || patrolPoints.Count == 0) return;

        currentPatrolPoint = patrolPoints[Random.Range(0, patrolPoints.Count)];
    }

    private List<Transform> GetPatrolPointsForCurrentFloor()
    {
        if (mapManager == null) return new List<Transform>();

        int floorIndex = Mathf.Clamp(
            Mathf.RoundToInt(-transform.position.y / mapManager.floorHeight),
            0, mapManager.floors - 1
        );

        return mapManager.patrolPointsPerFloor[floorIndex];
    }

    private void ChangeState(State newState)
    {
        currentState = newState;
    }
}
