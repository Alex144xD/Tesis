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

    [Header("Waypoints (solo si deseas patrullar)")]
    public Transform[] patrolPoints;
    private int currentPatrolIndex = 0;

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

        Debug.Log("FSM iniciado. Estado inicial: " + currentState);
    }

    void Update()
    {
        if (isDead || player == null) return;

        float distanceToPlayer = Vector3.Distance(transform.position, player.position);
        Debug.Log("Estado actual: " + currentState + " | Distancia al jugador: " + distanceToPlayer);

        switch (currentState)
        {
            case State.Idle:
                animator.SetBool("IsWalking", false);
                animator.SetBool("IsAttacking", false);

                if (distanceToPlayer <= chaseRange)
                    ChangeState(State.Chase);
                break;

            case State.Patrol:
                animator.SetBool("IsWalking", true);
                animator.SetBool("IsAttacking", false);

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
                animator.SetBool("IsWalking", true);
                animator.SetBool("IsAttacking", false);
                agent.isStopped = false;
                agent.SetDestination(player.position);

                Debug.Log("Persiguiendo al jugador. Destino: " + agent.destination);

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
                animator.SetBool("IsWalking", false);
                animator.SetBool("IsAttacking", true);
                agent.isStopped = true;

                transform.LookAt(player.position);

                Debug.Log("Atacando al jugador");

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
                animator.SetBool("IsWalking", true);
                animator.SetBool("IsAttacking", false);

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

                Debug.Log("Huyendo del jugador. Destino: " + fleePosition);
                break;
        }
    }

    void GoToNextPatrolPoint()
    {
        if (patrolPoints.Length == 0) return;

        agent.destination = patrolPoints[currentPatrolIndex].position;
        agent.isStopped = false;

        Debug.Log("Patrullando al siguiente punto: " + patrolPoints[currentPatrolIndex].name);

        currentPatrolIndex = (currentPatrolIndex + 1) % patrolPoints.Length;
    }

    public void TakeFlashlightDamage(float amount)
    {
        if (isDead) return;

        health -= amount;
        Debug.Log("Daño recibido de linterna. Salud actual: " + health);

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

        Debug.Log("Enemigo muerto");
    }

    private void ChangeState(State newState)
    {
        Debug.Log("Cambiando estado: " + currentState + " → " + newState);
        currentState = newState;
    }
}
