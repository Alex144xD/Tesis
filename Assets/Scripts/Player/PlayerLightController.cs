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

    // ===== Detección de enemigos =====
    [Header("Detección de enemigos")]
    public bool queryAllLayers = true;
    public LayerMask enemyLayer = ~0;
    public bool requireEnemyTag = false;
    public string enemyTag = "Enemy";

    // ===== Debug =====
    [Header("Debug")]
    public bool debugEnemyHit = false;          // log por enemigo impactado
    public bool debugTrace = true;              // logs de flujo (por qué se aborta)
    public bool debugVerboseRejects = false;    // por qué se descarta cada collider
    public bool debugDrawCone = true;           // dibuja cono y rango en escena
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
    private Collider[] hitsBuffer;
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

        hitsBuffer = new Collider[Mathf.Max(64, 256)];
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

        // Entrada
        bool leftPressed = Input.GetMouseButton(0);   // LOW
        bool rightPressed = Input.GetMouseButton(1);   // HIGH

        FlashlightUIMode desiredMode = FlashlightUIMode.Off;
        if (rightPressed) desiredMode = FlashlightUIMode.High;
        else if (leftPressed) desiredMode = FlashlightUIMode.Low;

        if (GetBatteryNormalized() <= 0f) desiredMode = FlashlightUIMode.Off;

        bool wantOn = (desiredMode != FlashlightUIMode.Off);

        if (wantOn != isOn)
        {
            isOn = wantOn;
            PlayToggleSound(isOn);
            SetLampEnabled(isOn);
            StopBlinkIfRunning();
            if (isOn && debugTrace) Debug.Log("[Light] ON");
            if (!isOn && debugTrace) Debug.Log("[Light] OFF");
        }

        if (isOn && desiredMode != currentMode)
        {
            currentMode = desiredMode;
            PlayToggleSound(true);
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
                stillHas = GetBatteryNormalized() > 0f;

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
                    lamp.spotAngle = highAngle; // HAZ GRANDE (como original)
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
                AffectEnemiesInLight(); // <<<<<< clave
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

    // ====== DETECCIÓN + NOTIFICACIÓN A ENEMIGOS ======
    private void AffectEnemiesInLight()
    {
        if (!lamp)
        {
            if (debugTrace) Debug.LogWarning("[Light] No hay componente Light.");
            return;
        }
        if (!lamp.enabled)
        {
            if (debugTrace) Debug.Log("[Light] Lámpara deshabilitada: no se detecta.");
            return;
        }

        int layerMask = queryAllLayers ? Physics.AllLayers : (int)enemyLayer;

        if (hitsBuffer == null || hitsBuffer.Length < 64) hitsBuffer = new Collider[64];

        int count = Physics.OverlapSphereNonAlloc(
            transform.position, lamp.range, hitsBuffer, layerMask, QueryTriggerInteraction.Ignore
        );

        if (debugTrace)
            Debug.Log($"[Light] Sweep r={lamp.range:0.0} angle={lamp.spotAngle:0} mode={currentMode} hits={count}");

        float halfAngle = lamp.spotAngle * 0.5f;

        for (int i = 0; i < count; i++)
        {
            var col = hitsBuffer[i];
            if (!col)
            {
                if (debugVerboseRejects) Debug.Log("[Light] hit=null (descartado)");
                continue;
            }

            EnemyFSM enemy =
                col.GetComponent<EnemyFSM>() ??
                col.GetComponentInParent<EnemyFSM>() ??
                col.GetComponentInChildren<EnemyFSM>();

            if (!enemy)
            {
                if (debugVerboseRejects) Debug.Log($"[Light] {col.name} sin EnemyFSM (descartado)");
                continue;
            }

            if (requireEnemyTag)
            {
                bool hasTag = col.CompareTag(enemyTag) || enemy.CompareTag(enemyTag);
                if (!hasTag)
                {
                    if (debugVerboseRejects) Debug.Log($"[Light] {enemy.name} sin tag '{enemyTag}' (descartado)");
                    continue;
                }
            }

            Vector3 center = col.bounds.center;
            Vector3 toEnemy = (center - transform.position);
            float dist = toEnemy.magnitude;
            if (dist > lamp.range)
            {
                if (debugVerboseRejects) Debug.Log($"[Light] {enemy.name} fuera de rango ({dist:0.00})");
                continue;
            }

            Vector3 dir = toEnemy / Mathf.Max(0.0001f, dist);
            float angle = Vector3.Angle(transform.forward, dir);
            if (angle > halfAngle)
            {
                if (debugVerboseRejects) Debug.Log($"[Light] {enemy.name} fuera de cono (ang={angle:0})");
                continue;
            }

            var bType = (batteries != null) ? batteries.activeType : BatteryType.Green;
            if (currentMode == FlashlightUIMode.Off)
            {
                if (debugVerboseRejects) Debug.Log($"[Light] {enemy.name} -> modo Off (descartado)");
                continue;
            }

            // IMPACTO: detener ataque + huida opuesta
            enemy.OnLightImpact(
                lightOrigin: transform.position,
                lightForward: transform.forward,
                battery: bType,
                mode: currentMode
            );

            if (debugEnemyHit)
                Debug.Log($"[Light] Impact {enemy.name} dist={dist:0.0} angle={angle:0} mode={currentMode} battery={bType}");
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
            lamp.enabled = true;
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

    // ===== Gizmos para ver el cono =====
    void OnDrawGizmosSelected()
    {
        if (!debugDrawCone) return;
        if (!lamp) lamp = GetComponent<Light>();
        if (!lamp) return;

        float r = lamp.range;
        float half = lamp.spotAngle * 0.5f;

        // color según modo
        Gizmos.color = (currentMode == FlashlightUIMode.High) ? gizmoColorHigh : gizmoColorLow;

        // Dibuja “cono” aproximado (círculo al final + líneas)
        Vector3 origin = transform.position;
        Vector3 forward = transform.forward;

        // plano del círculo final
        Vector3 right = Quaternion.AngleAxis(half, transform.up) * forward;
        Vector3 left = Quaternion.AngleAxis(-half, transform.up) * forward;

        Gizmos.DrawLine(origin, origin + forward * r);
        Gizmos.DrawLine(origin, origin + right * r);
        Gizmos.DrawLine(origin, origin + left * r);

        // dibujar círculo aproximado
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
}