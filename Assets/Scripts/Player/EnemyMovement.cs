using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class EnemyMovement : MonoBehaviour
{
    [Tooltip("Transform del jugador al que perseguir")]
    public Transform player;

    [Tooltip("Velocidad de movimiento del enemigo")]
    public float speed = 3f;

    [Tooltip("Rango a partir del cual el enemigo comienza a perseguir al jugador")]
    public float chaseRange = 10f;

    [Tooltip("Daño por segundo al jugador si está muy cerca")]
    public float damagePerSecond = 10f;

    private CharacterController controller;
    private PlayerHealth playerHealth;

    void Start()
    {
        controller = GetComponent<CharacterController>();
        if (player != null)
            playerHealth = player.GetComponent<PlayerHealth>();
    }

    void Update()
    {
        if (player == null) return;

        float dist = Vector3.Distance(transform.position, player.position);

        if (dist <= 1.5f && playerHealth != null)
        {
            // Inflige daño continuo
            playerHealth.TakeDamage(damagePerSecond * Time.deltaTime);
            ChasePlayer();
        }
        else if (dist <= chaseRange)
        {
            ChasePlayer();
        }
        else
        {
            PatrolInPlace();
        }
    }

    void ChasePlayer()
    {
        Vector3 lookPos = player.position;
        lookPos.y = transform.position.y;
        transform.LookAt(lookPos);

        Vector3 dir = (lookPos - transform.position).normalized;
        controller.Move(dir * speed * Time.deltaTime);
    }

    void PatrolInPlace()
    {
        // Gira lentamente en su lugar
        transform.Rotate(0, speed * Time.deltaTime * 10f, 0);
    }
}

