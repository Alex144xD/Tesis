using UnityEngine;
using UnityEngine.AI;
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(Animator))]
public class EnemyFSM : MonoBehaviour
{
    public enum State { Idle, Patrol, Chase, Attack, Flee, Dead }
    private State currentState;

    [Header("General")]
    public float chaseRange = 10f;
    public float attackRange = 2f;
    public float damagePerSecond = 10f;
    public float fleeDistance = 5f;
    public float fleeDuration = 3f;
    public float health = 100f;

    [Header("Patrullaje Dinámico")]
    public Transform[] patrolPoints;
    private int currentPatrolIndex = 0;

    [Header("Raycast Anti-Muros")]
    public float wallDetectionRange = 1f;
    public LayerMask wallLayer;

    private NavMeshAgent agent;
    private Animator animator;
    private Transform player;
    private PlayerHealth playerHealth;

    private bool isDead = false;
    private float fleeTimer = 0f;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponent<Animator>();
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");

        if (playerObj != null)
        {
            player = playerObj.transform;
            playerHealth = player.GetComponent<PlayerHealth>();
        }

        currentState = patrolPoints.Length > 0 ? State.Patrol : State.Idle;
        if (currentState == State.Patrol) GoToNextPatrolPoint();

        Debug.Log("FSM iniciada. Estado inicial: " + currentState);
    }

    void Update()
    {
        if (isDead || player == null) return;

        float distanceToPlayer = Vector3.Distance(transform.position, player.position);

        // Verificar muros con Raycast
        DetectWalls();

        switch (currentState)
        {
            case State.Idle:
                SetAnimation(false, false);
                if (distanceToPlayer <= chaseRange)
                    ChangeState(State.Chase);
                break;

            case State.Patrol:
                SetAnimation(true, false);
                if (distanceToPlayer <= chaseRange)
                {
                    ChangeState(State.Chase);
                }
                else if (!agent.pathPending && agent.remainingDistance < 0.5f)
                {
                    GoToNextPatrolPoint();
                }
                break;

            case State.Chase:
                SetAnimation(true, false);
                agent.isStopped = false;
                agent.SetDestination(player.position);

                if (distanceToPlayer <= attackRange)
                {
                    ChangeState(State.Attack);
                }
                else if (distanceToPlayer > chaseRange)
                {
                    ChangeState(patrolPoints.Length > 0 ? State.Patrol : State.Idle);
                }
                break;

            case State.Attack:
                SetAnimation(false, true);
                agent.isStopped = true;
                transform.LookAt(player.position);

                if (distanceToPlayer > attackRange)
                {
                    ChangeState(State.Chase);
                }
                else if (playerHealth != null)
                {
                    playerHealth.TakeDamage(damagePerSecond * Time.deltaTime);
                }
                break;

            case State.Flee:
                SetAnimation(true, false);
                fleeTimer -= Time.deltaTime;
                if (fleeTimer <= 0)
                {
                    ChangeState(distanceToPlayer <= chaseRange ? State.Chase : State.Patrol);
                    break;
                }

                Vector3 fleeDirection = (transform.position - player.position).normalized;
                Vector3 fleePosition = transform.position + fleeDirection * fleeDistance;
                agent.isStopped = false;
                agent.SetDestination(fleePosition);
                break;
        }
    }

    void DetectWalls()
    {
        if (Physics.Raycast(transform.position + Vector3.up * 0.5f, transform.forward, out RaycastHit hit, wallDetectionRange, wallLayer))
        {
            Vector3 avoidDir = Vector3.Cross(Vector3.up, hit.normal).normalized;
            Vector3 newPos = transform.position + avoidDir * 2f;

            if (NavMesh.SamplePosition(newPos, out NavMeshHit navHit, 1f, agent.areaMask))
            {
                agent.SetDestination(navHit.position);
            }
        }
    }

    void GoToNextPatrolPoint()
    {
        if (patrolPoints.Length == 0) return;

        agent.destination = patrolPoints[currentPatrolIndex].position;
        agent.isStopped = false;

        currentPatrolIndex = (currentPatrolIndex + 1) % patrolPoints.Length;
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

    public void Die()
    {
        if (isDead) return;

        isDead = true;
        currentState = State.Dead;
        agent.isStopped = true;
        animator.SetBool("IsDead", true);
    }

    private void ChangeState(State newState)
    {
        currentState = newState;
        Debug.Log("Estado cambiado a: " + newState);
    }

    private void SetAnimation(bool walking, bool attacking)
    {
        animator.SetBool("IsWalking", walking);
        animator.SetBool("IsAttacking", attacking);
    }
}

