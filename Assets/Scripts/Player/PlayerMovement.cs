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

    AudioSource footSrc;
    Coroutine fadeCo;
   

    CharacterController controller;
    PlayerHealth health;
    Vector3 moveDirection;
    float stamina;
    bool isRunning;

    // Bloqueo de sprint (si lo usas en otras partes)
    bool sprintBlocked = false;

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
    }

    void Update()
    {
        MoverJugador();
        ControlarStamina();
        ManejarPasosLoop();
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

        controller.Move(moveDirection * currentSpeed * Time.deltaTime);

        // Gravedad simple
        if (!controller.isGrounded)
            controller.Move(Vector3.down * gravity * Time.deltaTime);
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
        bool grounded = controller.isGrounded;
        Vector3 hv = controller.velocity; hv.y = 0f;
        float speed = hv.magnitude;

        
        bool hasInput = Mathf.Abs(Input.GetAxisRaw("Horizontal")) > 0.01f ||
                        Mathf.Abs(Input.GetAxisRaw("Vertical")) > 0.01f;

        bool moving = grounded && hasInput && speed > minFootstepSpeed;

        
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
}