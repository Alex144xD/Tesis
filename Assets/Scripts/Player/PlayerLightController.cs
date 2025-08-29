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

    [Header("Luz")]
    public float minRange = 5f;
    public float maxRange = 15f;
    public float flickerSpeed = 0.1f;
    public float baseIntensity = 1.2f;

    [Header("Consumo / Energía")]
    [Tooltip("Consumo base por segundo (se multiplica por LOW/HIGH).")]
    public float drainRate = 1f;
    [Tooltip("Multiplicador de consumo cuando estás en LOW (clic izquierdo).")]
    [Range(0.1f, 1.0f)] public float lowDrainMultiplier = 0.6f;
    [Tooltip("Multiplicador de consumo cuando estás en HIGH (clic derecho).")]
    [Range(0.6f, 2.0f)] public float highDrainMultiplier = 1.0f;

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

    private AudioSource audioSrc;

    private Light lamp;
    private bool isOn = false;
    private Coroutine blinkRoutine;

    // Legacy
    private float currentBattery;

    // Integración
    private PlayerBatterySystem batteries;

    // Color runtime
    private Color currentLightColor;

    // Modo actual (Low/High/Off)
    private FlashlightUIMode currentMode = FlashlightUIMode.Off;
    private FlashlightUIMode lastModePlayed = FlashlightUIMode.Off; // para sonido

    void Awake()
    {
        lamp = GetComponent<Light>();
        lamp.type = LightType.Spot;
        lamp.spotAngle = 60f;
        lamp.intensity = baseIntensity;
        RenderSettings.ambientIntensity = 0.1f;

        currentBattery = maxBattery; // legacy

        batteries = GetComponent<PlayerBatterySystem>();
        if (batteries == null) batteries = GetComponentInParent<PlayerBatterySystem>();
        if (batteries == null) batteries = FindObjectOfType<PlayerBatterySystem>();

        currentLightColor = legacyColor;
        lamp.color = currentLightColor;

        if (playerCamera == null)
            Debug.LogError("Asigna la Main Camera al PlayerLightController.");

        audioSrc = GetComponent<AudioSource>();
        if (!audioSrc) audioSrc = gameObject.AddComponent<AudioSource>();
        audioSrc.playOnAwake = false;
        audioSrc.spatialBlend = 0f; // 2D
    }

    void Start()
    {
        if (batteries != null)
        {
            OnBatterySwitched(batteries.activeType);
        }
    }

    void Update()
    {
        if (playerCamera == null) return;

        // --- Entrada: mantener pulsado ---
        bool leftPressed = Input.GetMouseButton(0);   // LOW
        bool rightPressed = Input.GetMouseButton(1);  // HIGH

        // Prioridad: HIGH si el derecho está pulsado; si no, LOW
        if (rightPressed) currentMode = FlashlightUIMode.High;
        else if (leftPressed) currentMode = FlashlightUIMode.Low;
        else currentMode = FlashlightUIMode.Off;

        // Si no hay batería, forzar Off
        if (GetBatteryNormalized() <= 0f) currentMode = FlashlightUIMode.Off;

        bool wantOn = (currentMode != FlashlightUIMode.Off);
        if (wantOn != isOn)
        {
            isOn = wantOn;
            PlayToggleSound(isOn);
        }

        if (isOn)
        {
            // Consumo según modo
            float mult = (currentMode == FlashlightUIMode.High) ? highDrainMultiplier : lowDrainMultiplier;
            float consume = drainRate * Mathf.Max(0.05f, mult) * Time.deltaTime;
            bool stillHas = true;

            if (batteries != null)
            {
                stillHas = batteries.ConsumeActiveBattery(consume);
            }
            else
            {
                currentBattery = Mathf.Max(0f, currentBattery - consume);
                stillHas = currentBattery > 0f;
            }

            if (!stillHas)
            {
                isOn = false;
                currentMode = FlashlightUIMode.Off;
                StopBlinkIfRunning();
                lamp.enabled = false;
            }
            else
            {
                // Flicker + rango
                float t = 0.5f + 0.5f * Mathf.PerlinNoise(Time.time * flickerSpeed, 0f);
                lamp.range = Mathf.Lerp(minRange, maxRange, t);

                // Intensidad escala con batería
                float tBattery = GetBatteryNormalized();
                lamp.intensity = Mathf.Lerp(baseIntensity * 0.6f, baseIntensity, tBattery);

                // Parpadeo crítico
                if (tBattery <= criticalBatteryThreshold)
                {
                    if (blinkRoutine == null) blinkRoutine = StartCoroutine(BlinkLoop());
                }
                else
                {
                    StopBlinkIfRunning();
                    lamp.enabled = true;
                }

                // Color by battery
                UpdateTintByBattery();

                // Notificar a enemigos (modo detallado)
                AffectEnemiesInLight();
            }
        }
        else
        {
            StopBlinkIfRunning();
            lamp.enabled = false;
        }

        // Posicionar la luz con la cámara
        transform.position = playerCamera.transform.position +
                             playerCamera.transform.TransformVector(localOffset);
        transform.rotation = playerCamera.transform.rotation;

        // Sonido de cambio de modo (opcional: solo cuando cambia entre Low/High)
        if (isOn && currentMode != lastModePlayed)
        {
            PlayToggleSound(true);
            lastModePlayed = currentMode;
        }
        else if (!isOn)
        {
            lastModePlayed = FlashlightUIMode.Off;
        }
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
            if (!isOn || GetBatteryNormalized() <= 0f) { lamp.enabled = false; yield break; }
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

    private void AffectEnemiesInLight()
    {
        if (!lamp.enabled) return;

        Collider[] hits = Physics.OverlapSphere(transform.position, lamp.range);
        for (int i = 0; i < hits.Length; i++)
        {
            var hit = hits[i];
            if (!hit.CompareTag("Enemy")) continue;

            Vector3 dirToEnemy = (hit.transform.position - transform.position).normalized;
            float angle = Vector3.Angle(transform.forward, dirToEnemy);
            if (angle >= lamp.spotAngle / 2f) continue;

            EnemyFSM enemy = hit.GetComponent<EnemyFSM>();
            if (enemy == null) continue;

            // Intensidad (1 centro, 0 borde)
            float intensity01 = 1f - Mathf.Clamp01(angle / (lamp.spotAngle * 0.5f));

            // Determinar modo actual
            FlashlightUIMode mode = currentMode;
            if (mode == FlashlightUIMode.Off) continue;

            // Batería activa
            BatteryType bType = (batteries != null) ? batteries.activeType : BatteryType.Green;

            // Notificar de forma detallada
            enemy.OnFlashlightHitDetailed(bType, mode, Time.deltaTime, intensity01);
        }
    }

    // ===================== API pública =====================

    public float GetBatteryNormalized()
    {
        if (batteries != null)
            return batteries.GetActiveBatteryNormalized();
        return maxBattery > 0 ? currentBattery / maxBattery : 0f;
    }

    public bool IsOn() => isOn;
    public float GetLowThreshold() => lowBatteryThreshold;
    public float GetCriticalThreshold() => criticalBatteryThreshold;

    public void ForceOnIfHasBattery()
    {
        if (GetBatteryNormalized() > 0f) isOn = true;
    }

    public void RechargeBattery(float amount)
    {
        if (batteries != null) return;
        currentBattery = Mathf.Min(maxBattery, currentBattery + amount);
        if (currentBattery > 0f) isOn = true;
    }

    public void SetLightColor(Color newColor)
    {
        if (lamp != null) lamp.color = newColor;
        currentLightColor = newColor;
    }

    public void OnBatterySwitched(BatteryType newType)
    {
        Color target = legacyColor;
        switch (newType)
        {
            case BatteryType.Green: target = greenColor; break;
            case BatteryType.Red: target = redColor; break;
            case BatteryType.Blue: target = blueColor; break;
        }
        currentLightColor = target;
        if (lamp != null) lamp.color = currentLightColor;
    }

    // === API para HUD / otros sistemas ===
    public FlashlightUIMode GetCurrentMode()
    {
        return currentMode;
    }

    public float GetCurrentDrainPerSecondForHUD()
    {
        if (currentMode == FlashlightUIMode.Off) return 0f;

        float mult = (currentMode == FlashlightUIMode.High)
            ? Mathf.Max(0.01f, highDrainMultiplier)
            : Mathf.Max(0.01f, lowDrainMultiplier);

        return drainRate * mult;
    }
}