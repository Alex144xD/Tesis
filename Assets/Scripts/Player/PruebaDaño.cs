using UnityEngine;

public class PruebaDaño : MonoBehaviour
{
    [Tooltip("Referencia al script PlayerHealth")]
    public PlayerHealth playerHealth;

    void Start()
    {
        if (playerHealth == null)
            playerHealth = FindObjectOfType<PlayerHealth>();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.K) && playerHealth != null)
        {
            playerHealth.TakeDamage(20f);
            Debug.Log("Daño de prueba: -20 HP");
        }
    }
}
