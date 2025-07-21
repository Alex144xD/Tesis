using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(PlayerHealth))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Movimiento")]
    public float walkSpeed = 4f;
    public float runSpeed = 7f;

    [Header("Stamina")]
    public float staminaMax = 5f;
    public float staminaRegenRate = 1f;
    public float staminaDrainRate = 1f;

    [Header("Inventario")]
    public int llaves = 0;
    public int pilas = 0;

    [Header("Gravedad")]
    public float gravity = 9.8f;

    private CharacterController controller;
    private PlayerHealth health;
    private Vector3 moveDirection;
    private float stamina;
    private bool isRunning;

    void Start()
    {
        controller = GetComponent<CharacterController>();
        health = GetComponent<PlayerHealth>();
        stamina = staminaMax;
    }

    void Update()
    {
        MoverJugador();
        ControlarStamina();
    }

    void MoverJugador()
    {
        float moveX = Input.GetAxis("Horizontal");
        float moveZ = Input.GetAxis("Vertical");

        moveDirection = new Vector3(moveX, 0, moveZ);
        moveDirection = transform.TransformDirection(moveDirection).normalized;

        isRunning = Input.GetKey(KeyCode.LeftShift)
                    && stamina > 0f
                    && moveDirection.magnitude > 0f;

        float currentSpeed = isRunning ? runSpeed : walkSpeed;

        // Aplica slowdown si vida < umbral
        if (health.GetHealthNormalized() < health.slowThreshold)
            currentSpeed *= health.slowFactor;

        // Mover
        controller.Move(moveDirection * currentSpeed * Time.deltaTime);

        // Gravedad
        if (!controller.isGrounded)
            controller.Move(Vector3.down * gravity * Time.deltaTime);
    }

    void ControlarStamina()
    {
        if (isRunning)
            stamina -= staminaDrainRate * Time.deltaTime;
        else
            stamina += staminaRegenRate * Time.deltaTime;

        stamina = Mathf.Clamp(stamina, 0f, staminaMax);
    }

    public float GetStaminaNormalized()
    {
        return stamina / staminaMax;
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Llave"))
        {
            llaves++;
            Destroy(other.gameObject);
            Debug.Log($"¡Recogiste una llave! Total de llaves: {llaves}");
        }
        else if (other.CompareTag("Pila"))
        {
            pilas++;
            Destroy(other.gameObject);
            Debug.Log($"¡Recogiste una pila! Total de pilas: {pilas}");
        }
    }
}
