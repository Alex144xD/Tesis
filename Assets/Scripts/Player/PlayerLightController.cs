using UnityEngine;
using System.Collections;

[RequireComponent(typeof(Light))]
public class PlayerLightController : MonoBehaviour
{
    [Header("Referencia a Cámara")]
    public Camera playerCamera;

    [Header("Offset local (hacia adelante)")]
    public Vector3 localOffset = new Vector3(0, 0, 0.2f);

    [Header("Luz")]
    public float minRange = 5f;
    public float maxRange = 15f;
    public float flickerSpeed = 0.1f;
    public float baseIntensity = 1.2f; // intensidad base

    [Header("Batería")]
    public float maxBattery = 30f;
    public float drainRate = 1f;
    public KeyCode toggleKey = KeyCode.F;

    [Header("Umbrales batería (para HUD y parpadeo)")]
    [Range(0f, 1f)] public float lowBatteryThreshold = 0.50f;    
    [Range(0f, 1f)] public float criticalBatteryThreshold = 0.20f; 

    [Header("Parpadeo crítico")]
    public float blinkMin = 0.06f;
    public float blinkMax = 0.14f;

    private Light lamp;
    private float currentBattery;
    private bool isOn = true;
    private Coroutine blinkRoutine;

    void Awake()
    {
        lamp = GetComponent<Light>();
        lamp.type = LightType.Spot;
        lamp.spotAngle = 60f;
        lamp.intensity = baseIntensity;
        RenderSettings.ambientIntensity = 0.1f;

        currentBattery = maxBattery;

        if (playerCamera == null)
            Debug.LogError("Asigna la Main Camera al PlayerLightController.");
    }

    void Update()
    {
        if (playerCamera == null) return;

        // Encender/apagar manual
        if (Input.GetKeyDown(toggleKey))
            isOn = !isOn;

        if (currentBattery <= 0f)
            isOn = false;

        // Drenaje y flicker de alcance
        if (isOn)
        {
            currentBattery = Mathf.Max(0f, currentBattery - drainRate * Time.deltaTime);

            float t = 0.5f + 0.5f * Mathf.PerlinNoise(Time.time * flickerSpeed, 0f);
            lamp.range = Mathf.Lerp(minRange, maxRange, t);

            // Ajuste de intensidad según batería (sensación de agotamiento)
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
                lamp.enabled = true; // encendida estable si está On y no crítica
            }

            AffectEnemiesInLight();
        }
        else
        {
            StopBlinkIfRunning();
            lamp.enabled = false;
        }

        // Seguir cámara
        transform.position = playerCamera.transform.position +
                             playerCamera.transform.TransformVector(localOffset);
        transform.rotation = playerCamera.transform.rotation;
    }

    private IEnumerator BlinkLoop()
    {
        // Parpadeo on/off mientras la batería es crítica y la linterna está encendida
        while (true)
        {
            if (!isOn || currentBattery <= 0f) { lamp.enabled = false; yield break; }
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

        Collider[] hitColliders = Physics.OverlapSphere(transform.position, lamp.range);
        foreach (var hit in hitColliders)
        {
            if (hit.CompareTag("Enemy"))
            {
                Vector3 dirToEnemy = (hit.transform.position - transform.position).normalized;
                float angle = Vector3.Angle(transform.forward, dirToEnemy);

                if (angle < lamp.spotAngle / 2f)
                {
                    EnemyFSM enemy = hit.GetComponent<EnemyFSM>();
                    if (enemy != null)
                    {
                        enemy.OnFlashlightHit();
                    }
                }
            }
        }
    }

    public void RechargeBattery(float amount)
    {
        currentBattery = Mathf.Min(maxBattery, currentBattery + amount);
        if (currentBattery > 0f)
        {
            isOn = true;
        }
    }

    public float GetBatteryNormalized()
    {
        return currentBattery / maxBattery;
    }

    public bool IsOn() => isOn;
    public float GetLowThreshold() => lowBatteryThreshold;
    public float GetCriticalThreshold() => criticalBatteryThreshold;
}