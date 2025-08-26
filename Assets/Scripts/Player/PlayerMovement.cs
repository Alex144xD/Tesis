using System.Collections;
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

    [Header("Audio movimiento (loop único)")]
    public AudioClip footLoopClip;
    [Range(0f, 1f)] public float loopVolumeWalk = 0.8f;
    [Range(0f, 1f)] public float loopVolumeRun = 1.0f;
    public float loopPitchWalk = 1.00f;
    public float loopPitchRun = 1.08f;
    public float loopFadeOut = 0.08f;
    public float minFootstepSpeed = 0.12f;

    [Header("Grounding extra (recomendado)")]
    public LayerMask groundLayers = ~0;         
    public float groundCheckRadius = 0.3f;      
    public float groundCheckDistance = 0.6f; 
    public Vector3 groundCheckOffset = new Vector3(0f, 0.1f, 0f);
    [Tooltip("Fuerza de 'pegado' al suelo (empujón hacia abajo)")]
    public float stickToGroundForce = 5f;

    [Header("CharacterController tuning")]
    [Tooltip("Usar un paso alto en suelo y 0 en aire evita 'subir escalones' al caer")]
    public float stepOffsetWhileGrounded = 0.35f;
    public float stepOffsetInAir = 0f;
    [Range(0f, 80f)] public float slopeLimit = 55f;

    [Header("Recuperación anti-caídas")]
    public bool enableRecovery = true;
    public float killY = -50f;
    public float minFallSpeedForRescue = -25f;
    public float respawnUpOffset = 0.3f;
    public Transform fallbackSpawnPoint;

    [Header("Depuración")]
    public bool drawGizmos = true;
    public bool debugLogs = false;

    AudioSource footSrc;
    Coroutine fadeCo;

    CharacterController controller;
    PlayerHealth health;

    Vector3 moveDirection; 
    float stamina;
    bool isRunning;


    bool sprintBlocked = false;


    float yVelocity = 0f;              
    bool onGround = false;             
    Vector3 lastSafePosition;           
    bool hasSafePosition = false;

    void Start()
    {
        controller = GetComponent<CharacterController>();
        health = GetComponent<PlayerHealth>();
        stamina = staminaMax;

        // AudioSource para los pasos (2D)
        footSrc = GetComponent<AudioSource>();
        if (!footSrc) footSrc = gameObject.AddComponent<AudioSource>();
        footSrc.playOnAwake = false;
        footSrc.loop = true;
        footSrc.spatialBlend = 0f;
        footSrc.dopplerLevel = 0f;
        footSrc.volume = 0f;
        footSrc.pitch = 1f;
        if (footLoopClip) footSrc.clip = footLoopClip;

        // Ajustes aconsejados del CC
        controller.slopeLimit = slopeLimit;
        controller.stepOffset = stepOffsetWhileGrounded;

        // Posición inicial = segura
        lastSafePosition = transform.position;
        hasSafePosition = true;
    }

    void Update()
    {
        MoverJugador();
        ControlarStamina();
        ManejarPasosLoop();
        ChecarCaidaYRespawn();
    }

    void MoverJugador()
    {
        float moveX = Input.GetAxis("Horizontal");
        float moveZ = Input.GetAxis("Vertical");

        moveDirection = new Vector3(moveX, 0f, moveZ);
        moveDirection = transform.TransformDirection(moveDirection).normalized;

        bool wantsRun = Input.GetKey(KeyCode.LeftShift);
        isRunning = wantsRun
                    && !sprintBlocked
                    && stamina > 0f
                    && moveDirection.sqrMagnitude > 0.0001f;

        float currentSpeed = isRunning ? runSpeed : walkSpeed;
        currentSpeed *= health.GetSpeedFactor();

        // --- Gravedad acumulada ---
        // Si estamos sólidamente en suelo, mantenemos un pequeño empuje hacia abajo (-2) para "pegar"
        if (onGround && yVelocity < -2f) yVelocity = -2f;

        // Mientras no haya suelo después del Move, acumulamos gravedad
        yVelocity += -gravity * Time.deltaTime;

        // Movimiento total (XZ + Y)
        Vector3 horizontal = moveDirection * currentSpeed;
        Vector3 motion = new Vector3(horizontal.x, yVelocity, horizontal.z) * Time.deltaTime;

        // StepOffset dinámico (evita escalones en aire)
        controller.stepOffset = onGround ? stepOffsetWhileGrounded : stepOffsetInAir;

        // Aplicar movimiento
        CollisionFlags flags = controller.Move(motion);

        // --- Actualizar estado de suelo de forma robusta ---
        onGround = ((flags & CollisionFlags.Below) != 0) || controller.isGrounded || IsGroundedSphere(out Vector3 hitPt);

        // Snap extra al suelo cuando estamos casi tocando (suaviza rampas/escalones)
        if (!onGround)
        {
            if (IsGroundedSphere(out hitPt))
            {
                // “pega” hacia abajo un poquito si estamos muy cerca
                controller.Move(Vector3.down * stickToGroundForce * Time.deltaTime);
            }
        }

        // Si tocamos suelo, guarda posición segura (ligeramente elevada)
        if (onGround)
        {
            lastSafePosition = transform.position + Vector3.up * 0.01f;
            hasSafePosition = true;
            // al tocar suelo, clamp vertical para no seguir acumulando
            if (yVelocity < -2f) yVelocity = -2f;
        }
    }

    bool IsGroundedSphere(out Vector3 hitPoint)
    {
        Vector3 origin = transform.position + groundCheckOffset;
        Ray ray = new Ray(origin, Vector3.down);
        if (Physics.SphereCast(ray, groundCheckRadius, out RaycastHit hit, groundCheckDistance, groundLayers, QueryTriggerInteraction.Ignore))
        {
            hitPoint = hit.point;
            return true;
        }
        hitPoint = Vector3.zero;
        return false;
    }

    void ChecarCaidaYRespawn()
    {
        if (!enableRecovery) return;

        // Si estamos MUY abajo del mundo -> respawn
        if (transform.position.y < killY)
        {
            if (debugLogs) Debug.Log("[PlayerMovement] killY activado. Respawn a última posición segura.");
            HacerRespawn();
            return;
        }

        // Si caemos muy rápido y vamos por debajo de la última posición segura -> respawn
        if (!onGround && yVelocity < minFallSpeedForRescue && transform.position.y < lastSafePosition.y - 2f)
        {
            if (debugLogs) Debug.Log("[PlayerMovement] Caída rápida detectada. Respawn a última posición segura.");
            HacerRespawn();
        }
    }

    void HacerRespawn()
    {
        Vector3 target = hasSafePosition ? lastSafePosition : transform.position;
        if (!hasSafePosition && fallbackSpawnPoint != null)
            target = fallbackSpawnPoint.position + Vector3.up * respawnUpOffset;

        // mover con CC deshabilitado para evitar choques
        controller.enabled = false;
        transform.position = target + Vector3.up * respawnUpOffset;
        controller.enabled = true;

        // reset vertical
        yVelocity = -2f;

        // actualizar onGround
        onGround = controller.isGrounded || IsGroundedSphere(out _);
    }

    void ControlarStamina()
    {
        if (isRunning) stamina -= staminaDrainRate * Time.deltaTime;
        else stamina += staminaRegenRate * Time.deltaTime;

        stamina = Mathf.Clamp(stamina, 0f, staminaMax);
    }

    void ManejarPasosLoop()
    {
        if (!footLoopClip || !footSrc) return;

        // Condiciones de movimiento real
        bool groundedNow = onGround; // usa grounding robusto
        Vector3 hv = controller.velocity; hv.y = 0f;
        float speed = hv.magnitude;

        bool hasInput = Mathf.Abs(Input.GetAxisRaw("Horizontal")) > 0.01f ||
                        Mathf.Abs(Input.GetAxisRaw("Vertical")) > 0.01f;

        bool moving = groundedNow && hasInput && speed > minFootstepSpeed;

        float targetVol = isRunning ? loopVolumeRun : loopVolumeWalk;
        float targetPitch = isRunning ? loopPitchRun : loopPitchWalk;

        if (moving)
        {
            if (footSrc.clip != footLoopClip) footSrc.clip = footLoopClip;
            if (!footSrc.isPlaying) footSrc.Play();

            footSrc.volume = Mathf.MoveTowards(footSrc.volume, targetVol, Time.deltaTime * 8f);
            footSrc.pitch = Mathf.MoveTowards(footSrc.pitch, targetPitch, Time.deltaTime * 8f);

            if (fadeCo != null) { StopCoroutine(fadeCo); fadeCo = null; }
        }
        else
        {
            if (footSrc.isPlaying && footSrc.volume > 0f)
            {
                if (fadeCo != null) StopCoroutine(fadeCo);
                fadeCo = StartCoroutine(FadeOutAndStop(footSrc, loopFadeOut));
            }
        }
    }

    IEnumerator FadeOutAndStop(AudioSource src, float dur)
    {
        float start = src.volume;
        float t = 0f;
        while (t < dur)
        {
            t += Time.deltaTime;
            src.volume = Mathf.Lerp(start, 0f, t / dur);
            yield return null;
        }
        src.Stop();
        src.volume = 0f;
        fadeCo = null;
    }

    public float GetStaminaNormalized() => stamina / staminaMax;
    public bool IsRunning() => isRunning;

    public void SetSprintBlocked(bool blocked)
    {
        sprintBlocked = blocked;
        if (sprintBlocked && isRunning) isRunning = false;
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

    void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;
        Gizmos.color = Color.green;
        Vector3 origin = transform.position + groundCheckOffset;
        Gizmos.DrawWireSphere(origin + Vector3.down * groundCheckDistance, groundCheckRadius);

        Gizmos.color = Color.red;
        Gizmos.DrawLine(new Vector3(-50, killY, -50), new Vector3(50, killY, 50));
    }

}