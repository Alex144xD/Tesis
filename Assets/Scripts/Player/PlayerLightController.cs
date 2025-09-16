using UnityEngine;
using System.Collections;

[RequireComponent(typeof(Light))]
public class PlayerLightController : MonoBehaviour
{
    public enum FlashlightUIMode { Off, Low, High }

    [Header("Referencia a Cámara")]
    public Camera playerCamera;

    [Header("Offset local (hacia adelante)")]
    public Vector3 localOffset = new Vector3(0, 0, 0.2f);

    [Header("Luz (base)")]
    public float minRange = 5f;
    public float maxRange = 15f;
    public float flickerSpeed = 0.1f;
    public float baseIntensity = 1.2f;

    [Header("Batería (legacy si no hay BatterySystem)")]
    public float maxBattery = 30f;

    [Header("Umbrales batería (HUD/parpadeo)")]
    [Range(0f, 1f)] public float lowBatteryThreshold = 0.50f;
    [Range(0f, 1f)] public float criticalBatteryThreshold = 0.20f;

    [Header("Parpadeo crítico")]
    public float blinkMin = 0.06f;
    public float blinkMax = 0.14f;

    [Header("Color por tipo de batería")]
    public Color greenColor = new Color32(80, 255, 140, 255);
    public Color redColor = new Color32(255, 80, 80, 255);
    public Color blueColor = new Color32(80, 160, 255, 255);
    public Color legacyColor = Color.white;
    [Range(1f, 20f)] public float colorLerpSpeed = 8f;

    [Header("Audio Linterna")]
    public AudioClip soundOn;
    public AudioClip soundOff;
    public AudioClip soundModeSwitch; // [MEJORA] sonido LOW/HIGH
    public float volume = 1f;

    [Header("Integración con sistema de baterías")]
    public bool batterySystemControlsDrain = false;

    // ===== Haz por modo =====
    [Header("Beam por modo")]
    [Range(0.3f, 0.95f)] public float lowAngleScale = 0.75f;
    [Range(0.3f, 0.95f)] public float lowRangeScale = 0.85f;
    [Range(0.5f, 1.0f)] public float lowIntensityScale = 0.9f;
    public bool overrideHighAngle = true;
    [Range(10f, 120f)] public float highAngleOverride = 60f;

    // ===== Consumo por porcentaje =====
    [Header("Consumo por porcentaje")]
    [Range(0.001f, 0.2f)] public float baseDrainPercentPerSecond = 0.03f;
    [Range(0.2f, 1.0f)] public float lowPercentMult = 0.55f;
    [Range(0.6f, 2.0f)] public float highPercentMult = 1.25f;

    [Header("Multiplicadores por batería")]
    public float greenDrainMult = 1.0f;
    public float redDrainMult = 1.3f;
    public float blueDrainMult = 0.85f;

    // ===== Detección por Rayo =====
    [Header("Raycast rápido (sustituye barrido por cono)")]
    public bool useRaycastDetection = true;      // [MEJORA] activa raycast simple
    public bool useSphereCast = true;            // [MEJORA] spherecast para perdonar puntería
    [Range(0f, 0.75f)] public float sphereRadius = 0.18f;
    public LayerMask losMask = ~0;               // [MEJORA] capas que golpea el rayo (enemigos + paredes)
    public bool stopAtFirstHit = true;           // [MEJORA] solo el primer impacto válido

    // ===== (Compatibilidad) Filtros adicionales =====
    [Header("Compatibilidad filtros (si reutilizas)")]
    public bool requireEnemyTag = false;
    public string enemyTag = "Enemy";

    // ===== Gizmos / Debug =====
    [Header("Debug")]
    public bool debugEnemyHit = false;          // log por enemigo impactado
    public bool debugTrace = true;              // logs de flujo (por qué se aborta)
    public bool debugVerboseRejects = false;    // detalle de descartes
    public bool debugDrawCone = true;           // gizmo de cono (visual)
    public Color gizmoColorHigh = new Color(1f, 1f, 0f, 0.25f);
    public Color gizmoColorLow = new Color(0f, 1f, 1f, 0.25f);

    // Runtime
    private AudioSource audioSrc;
    private Light lamp;
    private bool isOn = false;
    private Coroutine blinkRoutine;
    private float currentBattery; // legacy
    private PlayerBatterySystem batteries;
    private Color currentLightColor;
    private FlashlightUIMode currentMode = FlashlightUIMode.Off;
    private FlashlightUIMode lastModePlayed = FlashlightUIMode.Off;
    private float originalSpotAngle = 60f;
    private bool warnedNoCamera = false;

    void Awake()
    {
        lamp = GetComponent<Light>();
        lamp.type = LightType.Spot;

        originalSpotAngle = lamp.spotAngle > 0f ? lamp.spotAngle : originalSpotAngle;
        lamp.spotAngle = originalSpotAngle;
        lamp.intensity = baseIntensity;

        currentBattery = maxBattery; // legacy

        batteries = GetComponent<PlayerBatterySystem>();
        if (!batteries) batteries = GetComponentInParent<PlayerBatterySystem>();
        if (!batteries) batteries = FindObjectOfType<PlayerBatterySystem>();

        currentLightColor = legacyColor;
        lamp.color = currentLightColor;

        if (!playerCamera)
        {
            var main = Camera.main;
            if (main) playerCamera = main;
            if (!playerCamera)
            {
                var anyCam = FindObjectOfType<Camera>();
                if (anyCam) playerCamera = anyCam;
            }
        }
        if (!playerCamera && debugTrace && !warnedNoCamera)
        {
            Debug.LogWarning("[Light] No hay Camera asignada ni MainCamera en escena.");
            warnedNoCamera = true;
        }

        audioSrc = GetComponent<AudioSource>();
        if (!audioSrc) audioSrc = gameObject.AddComponent<AudioSource>();
        audioSrc.playOnAwake = false;
        audioSrc.spatialBlend = 0f;
    }

    void Start()
    {
        if (batteries != null)
            OnBatterySwitched(batteries.activeType);

        SetLampEnabled(false);
    }

    void Update()
    {
        if (!playerCamera)
        {
            if (debugTrace) Debug.LogWarning("[Light] Sin cámara: no puedo actualizar linterna.");
            return;
        }

        // Entrada (rápida): mouse 0 = LOW, mouse 1 = HIGH
        bool leftPressed = Input.GetMouseButton(0); // LOW
        bool rightPressed = Input.GetMouseButton(1); // HIGH

        FlashlightUIMode desiredMode = FlashlightUIMode.Off;
        if (rightPressed) desiredMode = FlashlightUIMode.High;
        else if (leftPressed) desiredMode = FlashlightUIMode.Low;

        if (GetBatteryNormalized() <= 0f) desiredMode = FlashlightUIMode.Off;
        bool wantOn = (desiredMode != FlashlightUIMode.Off);

        if (wantOn != isOn)
        {
            isOn = wantOn;
            PlayToggleSound(isOn);           // ON/OFF
            SetLampEnabled(isOn);
            StopBlinkIfRunning();
            if (isOn && debugTrace) Debug.Log("[Light] ON");
            if (!isOn && debugTrace) Debug.Log("[Light] OFF");
        }

        if (isOn && desiredMode != currentMode)
        {
            currentMode = desiredMode;
            PlayModeSwitchSound();           // [MEJORA] sonido de cambio LOW/HIGH
            lastModePlayed = currentMode;
            if (debugTrace) Debug.Log($"[Light] Modo -> {currentMode}");
        }
        else if (!isOn)
        {
            currentMode = FlashlightUIMode.Off;
            lastModePlayed = FlashlightUIMode.Off;
        }

        if (isOn)
        {
            bool stillHas = true;

            if (!batterySystemControlsDrain)
            {
                float modeMult = (currentMode == FlashlightUIMode.High) ? highPercentMult : lowPercentMult;
                float batteryMult = 1f;
                var bType = (batteries != null) ? batteries.activeType : BatteryType.Green;
                switch (bType)
                {
                    case BatteryType.Green: batteryMult = greenDrainMult; break;
                    case BatteryType.Red: batteryMult = redDrainMult; break;
                    case BatteryType.Blue: batteryMult = blueDrainMult; break;
                }

                float pctPerSec = baseDrainPercentPerSecond * Mathf.Max(0.01f, modeMult) * batteryMult;
                float maxCap = (batteries != null)
                    ? Mathf.Max(0.0001f, batteries.GetMax(bType))
                    : Mathf.Max(0.0001f, maxBattery);

                float amount = maxCap * pctPerSec * Time.deltaTime;

                if (batteries != null)
                    stillHas = batteries.ConsumeActiveBattery(amount);
                else
                {
                    currentBattery = Mathf.Max(0f, currentBattery - amount);
                    stillHas = currentBattery > 0f;
                }
            }
            else
            {
                stillHas = GetBatteryNormalized() > 0f;
            }

            if (!stillHas)
            {
                isOn = false;
                currentMode = FlashlightUIMode.Off;
                StopBlinkIfRunning();
                SetLampEnabled(false);
                if (debugTrace) Debug.Log("[Light] Batería agotada, apagando.");
            }
            else
            {
                // VISUALES por modo
                float tNoise = 0.5f + 0.5f * Mathf.PerlinNoise(Time.time * flickerSpeed, 0f);
                float p = GetBatteryNormalized();

                if (currentMode == FlashlightUIMode.High)
                {
                    float highAngle = overrideHighAngle ? highAngleOverride : originalSpotAngle;
                    lamp.spotAngle = highAngle; // HAZ GRANDE
                    lamp.range = Mathf.Lerp(minRange, maxRange, tNoise);
                    lamp.intensity = Mathf.Lerp(baseIntensity * 0.7f, baseIntensity, p);
                }
                else // LOW
                {
                    lamp.spotAngle = originalSpotAngle * lowAngleScale; // HAZ MÁS PEQUEÑO
                    float lowMin = minRange * lowRangeScale;
                    float lowMax = maxRange * lowRangeScale;
                    lamp.range = Mathf.Lerp(lowMin, lowMax, tNoise);
                    lamp.intensity = Mathf.Lerp(baseIntensity * 0.6f, baseIntensity, p) * lowIntensityScale;
                }

                if (p <= criticalBatteryThreshold)
                {
                    if (blinkRoutine == null) blinkRoutine = StartCoroutine(BlinkLoop());
                }
                else { StopBlinkIfRunning(); lamp.enabled = true; }

                UpdateTintByBattery();

                // --------- DETECCIÓN POR RAYO (rápido) ---------
                if (useRaycastDetection)
                    AffectEnemiesInLight_Raycast(); // [MEJORA]
            }
        }
        else
        {
            StopBlinkIfRunning();
            SetLampEnabled(false);
        }

        // Posición/rotación de la luz
        transform.position = playerCamera.transform.position + playerCamera.transform.TransformVector(localOffset);
        transform.rotation = playerCamera.transform.rotation;
    }

    private void SetLampEnabled(bool on)
    {
        if (lamp && lamp.enabled != on) lamp.enabled = on;
    }

    private void PlayToggleSound(bool turningOn)
    {
        if (!audioSrc) return;
        AudioClip clip = turningOn ? soundOn : soundOff;
        if (clip) audioSrc.PlayOneShot(clip, volume);
    }

    private void PlayModeSwitchSound() // [MEJORA]
    {
        if (!audioSrc || !soundModeSwitch) return;
        audioSrc.PlayOneShot(soundModeSwitch, volume);
    }

    private void UpdateTintByBattery()
    {
        Color target = legacyColor;
        if (batteries != null)
        {
            switch (batteries.activeType)
            {
                case BatteryType.Green: target = greenColor; break;
                case BatteryType.Red: target = redColor; break;
                case BatteryType.Blue: target = blueColor; break;
            }
        }
        currentLightColor = Color.Lerp(currentLightColor, target, Time.deltaTime * colorLerpSpeed);
        lamp.color = currentLightColor;
    }

    private IEnumerator BlinkLoop()
    {
        while (true)
        {
            if (!isOn || GetBatteryNormalized() <= 0f)
            {
                lamp.enabled = false;
                yield break;
            }
            lamp.enabled = !lamp.enabled;
            yield return new WaitForSeconds(Random.Range(blinkMin, blinkMax));
        }
    }

    private void StopBlinkIfRunning()
    {
        if (blinkRoutine != null)
        {
            StopCoroutine(blinkRoutine);
            blinkRoutine = null;
        }
    }

    // ====== REGLA de espanto por tipo + color + modo ======
    private bool MatchesScareRule(EnemyFSM enemy, BatteryType bType, FlashlightUIMode mode)
    {
        switch (enemy.kind)
        {
            case EnemyFSM.EnemyKind.Basic:
                // Basic: Verde con Low o High
                return (bType == BatteryType.Green) &&
                       (mode == FlashlightUIMode.Low || mode == FlashlightUIMode.High);

            case EnemyFSM.EnemyKind.Heavy:
                // Heavy: Rojo con High
                return (bType == BatteryType.Red) &&
                       (mode == FlashlightUIMode.High);

            case EnemyFSM.EnemyKind.Runner:
                // Runner: Azul con Low
                return (bType == BatteryType.Blue) &&
                       (mode == FlashlightUIMode.Low);
        }
        return false;
    }

    // ====== DETECCIÓN + NOTIFICACIÓN: RAYCAST/SPHERECAST ======
    private void AffectEnemiesInLight_Raycast()
    {
        if (!lamp || !lamp.enabled) return;
        if (currentMode == FlashlightUIMode.Off) return;

        Vector3 origin = playerCamera ? playerCamera.transform.position : transform.position;
        Vector3 dir = playerCamera ? playerCamera.transform.forward : transform.forward;
        float dist = lamp.range;

        bool hitSomething;
        RaycastHit hit;

        if (useSphereCast && sphereRadius > 0f)
            hitSomething = Physics.SphereCast(origin, sphereRadius, dir, out hit, dist, losMask, QueryTriggerInteraction.Ignore);
        else
            hitSomething = Physics.Raycast(origin, dir, out hit, dist, losMask, QueryTriggerInteraction.Ignore);

        if (!hitSomething) return;

        var enemy = hit.collider.GetComponent<EnemyFSM>() ??
                    hit.collider.GetComponentInParent<EnemyFSM>() ??
                    hit.collider.GetComponentInChildren<EnemyFSM>();

        if (enemy == null) return; // golpeó pared u otro objeto

        if (requireEnemyTag)
        {
            bool hasTag = hit.collider.CompareTag(enemyTag) || enemy.CompareTag(enemyTag);
            if (!hasTag) return;
        }

        var bType = (batteries != null) ? batteries.activeType : BatteryType.Green;
        if (!MatchesScareRule(enemy, bType, currentMode)) return;

        enemy.OnLightImpact(
            lightOrigin: origin,
            lightForward: dir,
            battery: bType,
            mode: currentMode
        );

        if (debugEnemyHit)
            Debug.Log($"[Light/Raycast] Impact {enemy.name} ({enemy.kind}) mode={currentMode} color={bType} dist={hit.distance:0.0}");

        if (!stopAtFirstHit)
        {
            // Si quieres espantar varios en línea, puedes usar RaycastAll/SphereCastAll aquí.
            // var hits = Physics.SphereCastAll(origin, sphereRadius, dir, dist, losMask, QueryTriggerInteraction.Ignore);
            // foreach (var h in hits) { ... misma lógica ... }
        }
    }

    // ===== Gizmos para ver el cono (útil para debug visual) =====
    void OnDrawGizmosSelected()
    {
        if (!debugDrawCone) return;
        if (!lamp) lamp = GetComponent<Light>();
        if (!lamp) return;

        float r = lamp.range;
        float half = lamp.spotAngle * 0.5f;

        Gizmos.color = (currentMode == FlashlightUIMode.High) ? gizmoColorHigh : gizmoColorLow;

        Vector3 origin = transform.position;
        Vector3 forward = transform.forward;

        Vector3 right = Quaternion.AngleAxis(half, transform.up) * forward;
        Vector3 left = Quaternion.AngleAxis(-half, transform.up) * forward;

        Gizmos.DrawLine(origin, origin + forward * r);
        Gizmos.DrawLine(origin, origin + right * r);
        Gizmos.DrawLine(origin, origin + left * r);

        int seg = 24;
        Vector3 prev = origin + (Quaternion.AngleAxis(-half, transform.up) * forward) * r;
        for (int i = 1; i <= seg; i++)
        {
            float t = -half + (lamp.spotAngle) * (i / (float)seg);
            Vector3 dir = Quaternion.AngleAxis(t, transform.up) * forward;
            Vector3 p = origin + dir * r;
            Gizmos.DrawLine(prev, p);
            prev = p;
        }
    }

    // ===== API pública =====
    public float GetBatteryNormalized()
    {
        if (batteries != null) return batteries.GetActiveBatteryNormalized();
        return maxBattery > 0 ? currentBattery / maxBattery : 0f;
    }

    public bool IsOn() => isOn;

    public void ForceOnIfHasBattery()
    {
        if (GetBatteryNormalized() > 0f)
        {
            isOn = true;
            SetLampEnabled(true);
            StopBlinkIfRunning();
            if (lamp) lamp.enabled = true;
        }
    }

    public void ForceOff()
    {
        isOn = false;
        currentMode = FlashlightUIMode.Off;
        StopBlinkIfRunning();
        SetLampEnabled(false);
    }

    public void RechargeBattery(float amount)
    {
        if (batteries != null) return;
        currentBattery = Mathf.Min(maxBattery, currentBattery + amount);
        if (currentBattery > 0f) ForceOnIfHasBattery();
    }

    public void SetLightColor(Color newColor)
    {
        if (lamp != null) lamp.color = newColor;
        currentLightColor = newColor;
    }

    public void OnBatterySwitched(BatteryType newType)
    {
        switch (newType)
        {
            case BatteryType.Green: currentLightColor = greenColor; break;
            case BatteryType.Red: currentLightColor = redColor; break;
            case BatteryType.Blue: currentLightColor = blueColor; break;
            default: currentLightColor = legacyColor; break;
        }
        if (lamp != null) lamp.color = currentLightColor;
    }

    public FlashlightUIMode GetCurrentMode() => currentMode;

    public float GetCurrentDrainPerSecondForHUD()
    {
        if (currentMode == FlashlightUIMode.Off) return 0f;

        float modeMult = (currentMode == FlashlightUIMode.High) ? highPercentMult : lowPercentMult;
        float bMult = 1f;
        var bType = (batteries != null) ? batteries.activeType : BatteryType.Green;
        switch (bType)
        {
            case BatteryType.Green: bMult = greenDrainMult; break;
            case BatteryType.Red: bMult = redDrainMult; break;
            case BatteryType.Blue: bMult = blueDrainMult; break;
        }

        float pctPerSec = baseDrainPercentPerSecond * Mathf.Max(0.01f, modeMult) * bMult;
        float maxCap = (batteries != null)
            ? Mathf.Max(0.0001f, batteries.GetMax(bType))
            : Mathf.Max(0.0001f, maxBattery);

        return maxCap * pctPerSec;
    }
}