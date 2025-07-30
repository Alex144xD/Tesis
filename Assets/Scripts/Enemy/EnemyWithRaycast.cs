using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class EnemyWithRaycast : MonoBehaviour
{
    public Transform player;
    public float wallDetectionRange = 1f;
    public LayerMask wallLayer;

    private NavMeshAgent agent;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
    }

    void Update()
    {
        if (player == null) return;

        agent.SetDestination(player.position);

        // Verificar si hay muro enfrente
        if (Physics.Raycast(transform.position + Vector3.up * 0.5f, transform.forward, out RaycastHit hit, wallDetectionRange, wallLayer))
        {
            // Desviarse un poco para no chocar
            Vector3 avoidDir = Vector3.Cross(Vector3.up, hit.normal).normalized;
            Vector3 newPos = transform.position + avoidDir * 2f;

            if (NavMesh.SamplePosition(newPos, out NavMeshHit navHit, 1f, agent.areaMask))
            {
                agent.SetDestination(navHit.position);
            }
        }
    }
}
