using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(Animator))]
public class EnemyFSM : MonoBehaviour
{
    public enum State { Patrol, Chase, Attack }
    private State currentState;

    [Header("General")]
    public float chaseRange = 10f;
    public float attackRange = 2f;
    public float damagePerSecond = 10f;

    [Header("Waypoints (solo si deseas patrullar)")]
    public Transform[] patrolPoints;
    private int currentPatrolIndex = 0;

    private NavMeshAgent agent;
    private Animator animator;
    private Transform player;
    private PlayerHealth playerHealth;

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

        currentState = State.Patrol;
        GoToNextPatrolPoint();
    }

    void Update()
    {
        if (player == null) return;

        float distanceToPlayer = Vector3.Distance(transform.position, player.position);

        switch (currentState)
        {
            case State.Patrol:
                if (distanceToPlayer <= chaseRange)
                {
                    currentState = State.Chase;
                    animator.SetTrigger("Chase");
                }
                else if (!agent.pathPending && agent.remainingDistance < 0.5f)
                {
                    GoToNextPatrolPoint();
                }
                break;

            case State.Chase:
                if (distanceToPlayer <= attackRange)
                {
                    currentState = State.Attack;
                    animator.SetTrigger("Attack");
                    agent.isStopped = true;
                }
                else if (distanceToPlayer > chaseRange)
                {
                    currentState = State.Patrol;
                    animator.SetTrigger("Patrol");
                    GoToNextPatrolPoint();
                }
                else
                {
                    agent.SetDestination(player.position);
                }
                break;

            case State.Attack:
                transform.LookAt(player.position);

                if (distanceToPlayer > attackRange)
                {
                    currentState = State.Chase;
                    animator.SetTrigger("Chase");
                    agent.isStopped = false;
                }
                else if (playerHealth != null)
                {
                    playerHealth.TakeDamage(damagePerSecond * Time.deltaTime);
                }
                break;
        }
    }

    void GoToNextPatrolPoint()
    {
        if (patrolPoints.Length == 0) return;

        agent.destination = patrolPoints[currentPatrolIndex].position;
        agent.isStopped = false;

        currentPatrolIndex = (currentPatrolIndex + 1) % patrolPoints.Length;
        animator.SetTrigger("Patrol");
    }
}
