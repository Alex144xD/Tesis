using UnityEngine;

public class EnemyMovement : MonoBehaviour
{
    [Tooltip("Transform del jugador al que perseguir")]
    public Transform player;

    [Tooltip("Velocidad de movimiento del enemigo")]
    public float speed = 3f;

    [Tooltip("Rango a partir del cual el enemigo comienza a perseguir al jugador")]
    public float chaseRange = 10f;

    private CharacterController controller;
    private Vector3 patrolDirection;
    private float patrolRotateSpeed = 30f;

    void Start()
    {
        controller = GetComponent<CharacterController>();
        // Elegimos una dirección aleatoria inicial para patrullar
        patrolDirection = Random.insideUnitSphere;
        patrolDirection.y = 0;
        patrolDirection.Normalize();
    }

    void Update()
    {
        float dist = Vector3.Distance(transform.position, player.position);

        if (dist <= chaseRange)
            ChasePlayer();
        else
            PatrolInPlace();
    }

 
    void ChasePlayer()
    {
        // Mirar al jugador en el plano XZ
        Vector3 lookPos = player.position;
        lookPos.y = transform.position.y;
        transform.LookAt(lookPos);

        // Mover hacia la posición del jugador
        Vector3 dir = (lookPos - transform.position).normalized;
        controller.Move(dir * speed * Time.deltaTime);
    }

    void PatrolInPlace()
    {
        // Gira lentamente
        transform.Rotate(0, patrolRotateSpeed * Time.deltaTime, 0);
    }
}

