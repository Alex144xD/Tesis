using UnityEngine;
using System.Collections;

[RequireComponent(typeof(Light))]
public class PlayerLightController : MonoBehaviour
{
    private enum LightMode { Off, Low, High }

    // === API pública para HUD ===
    public enum FlashlightUIMode { Off, Low, High }

    [Header("Referencia a Cámara")]
    public Camera playerCamera;

    [Header("Offset local (hacia adelante)")]
    public Vector3 localOffset = new Vector3(0, 0, 0.2f);

    [Header("Luz (modo HIGH)")]
    public float minRange = 5f;
    public float maxRange = 15f;
    public float baseIntensity = 1.2f;

    [Header("Luz (modo LOW)")]
    public float minRangeLow = 3.0f;
    public float maxRangeLow = 9.0f;
    [Range(0.2f, 1.0f)] public float lowIntensityMultiplier = 0.65f;

    [Header("Flicker / ruido")]
    public float flickerSpeed = 0.1f;

    [Header("Energía por consumo")]
    public float drainHigh = 1.0f;   // consumo/seg en HIGH (RMB)
    public float drainLow = 0.45f;  // consumo/seg en LOW  (LMB)
    [Tooltip("Evita micro-encendidos con energía casi nula.")]
    [Range(0f, 0.2f)] public float minEnergyToTurnOn = 0.02f;

    [Header("Batería (legacy si no hay BatterySystem)")]
    public float maxBattery = 30f;
    [Tooltip("Legacy: ya no se usa directo (solo compat).")]
    public float drainRate = 1f;
    [Tooltip("Legacy: toggle por tecla (ya no se usa).")]
    public KeyCode toggleKey = KeyCode.F;

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

    // Estado
    private bool isOn = false;
    private LightMode currentMode = LightMode.Off;
    private Coroutine blinkRoutine;

    // Legacy
    private float currentBattery;

    // Integración
    private PlayerBatterySystem batteries;

    // Color runtime
    private Color currentLightColor;

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

        // Arranca apagada: solo enciende mientras mantienes el ratón
        isOn = false;
        lamp.enabled = false;
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

        // 1) Inputs: RMB = HIGH, LMB = LOW, ambos => HIGH
        bool wantHigh = Input.GetMouseButton(1);
        bool wantLow = Input.GetMouseButton(0);

        LightMode desiredMode = LightMode.Off;
        if (wantHigh) desiredMode = LightMode.High;
        else if (wantLow) desiredMode = LightMode.Low;

        // 2) Energía disponible
        float energyNorm = GetBatteryNormalized();
        bool hasMinEnergy = energyNorm > minEnergyToTurnOn;

        // 3) Encendido condicionado
        if (desiredMode == LightMode.Off || !hasMinEnergy)
        {
            SetOn(false, LightMode.Off);
        }
        else
        {
            SetOn(true, desiredMode);
        }

        // 4) Consumo + actualización visual
        if (isOn && currentMode != LightMode.Off)
        {
            float consumePerSec = (currentMode == LightMode.High) ? drainHigh : drainLow;
            float consume = consumePerSec * Time.deltaTime;
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
                SetOn(false, LightMode.Off);
            }
            else
            {
                // Ruido de rango
                float tNoise = 0.5f + 0.5f * Mathf.PerlinNoise(Time.time * flickerSpeed, 0f);
                if (currentMode == LightMode.High)
                    lamp.range = Mathf.Lerp(minRange, maxRange, tNoise);
                else
                    lamp.range = Mathf.Lerp(minRangeLow, maxRangeLow, tNoise);

                // Intensidad según batería y modo
                float tBattery = GetBatteryNormalized();
                float baseInt = (currentMode == LightMode.High) ? baseIntensity : (baseIntensity * lowIntensityMultiplier);
                lamp.intensity = Mathf.Lerp(baseInt * 0.6f, baseInt, tBattery);

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

                UpdateTintByBattery();
                AffectEnemiesInLight();
            }
        }
        else
        {
            StopBlinkIfRunning();
            lamp.enabled = false;
        }

        // 5) Seguir cámara
        transform.position = playerCamera.transform.position +
                             playerCamera.transform.TransformVector(localOffset);
        transform.rotation = playerCamera.transform.rotation;
    }

    private void SetOn(bool turnOn, LightMode modeIfOn)
    {
        bool wasOn = isOn;

        isOn = turnOn;
        currentMode = turnOn ? modeIfOn : LightMode.Off;

        if (isOn && !wasOn) PlayToggleSound(true);
        else if (!isOn && wasOn) PlayToggleSound(false);

        lamp.enabled = isOn; // el parpadeo puede modificar esto cuando crítico
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

            if (batteries != null)
                enemy.OnFlashlightHitByBattery(batteries.activeType);
            else
                enemy.OnFlashlightHit();
        }
    }

    // ======= API pública previa / compat =======
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
        // Ahora no forzamos encendido automático; el jugador decide con mouse.
        // Mantengo el método por compatibilidad.
    }

    public void RechargeBattery(float amount)
    {
        if (batteries != null) return;
        currentBattery = Mathf.Min(maxBattery, currentBattery + amount);
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

    // ======= API NUEVA para el HUD =======
    public FlashlightUIMode GetCurrentModeForHUD()
    {
        switch (currentMode)
        {
            case LightMode.Low: return FlashlightUIMode.Low;
            case LightMode.High: return FlashlightUIMode.High;
            default: return FlashlightUIMode.Off;
        }
    }

    public float GetCurrentDrainPerSecondForHUD()
    {
        switch (currentMode)
        {
            case LightMode.High: return drainHigh;
            case LightMode.Low: return drainLow;
            default:
                // Si está apagada, mostramos el costo del modo ahorro como referencia.
                return drainLow > 0f ? drainLow : Mathf.Max(0.0001f, drainRate);
        }
    }
}