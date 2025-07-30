using UnityEngine;

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
    public float damagePerSecond = 10f; // Daño que causa a enemigos

    [Header("Batería")]
    public float maxBattery = 30f;
    public float drainRate = 1f;
    public KeyCode toggleKey = KeyCode.F;

    private Light lamp;
    private float currentBattery;
    private bool isOn = true;

    void Awake()
    {
        lamp = GetComponent<Light>();
        lamp.type = LightType.Spot;
        lamp.spotAngle = 60f;
        RenderSettings.ambientIntensity = 0.1f;

        currentBattery = maxBattery;

        if (playerCamera == null)
            Debug.LogError("Asigna la Main Camera al PlayerLightController.");
    }

    void Update()
    {
        if (playerCamera == null) return;

        // Encender/apagar la linterna
        if (Input.GetKeyDown(toggleKey))
            isOn = !isOn;

        if (currentBattery <= 0f)
            isOn = false;

        lamp.enabled = isOn;

        if (isOn)
        {
            currentBattery = Mathf.Max(0f, currentBattery - drainRate * Time.deltaTime);

            float t = 0.5f + 0.5f * Mathf.PerlinNoise(Time.time * flickerSpeed, 0f);
            lamp.range = Mathf.Lerp(minRange, maxRange, t);

            // 🔥 Dañar enemigos iluminados
            DamageEnemiesInLight();
        }

        // Mover y rotar junto a la cámara
        transform.position = playerCamera.transform.position +
                             playerCamera.transform.TransformVector(localOffset);
        transform.rotation = playerCamera.transform.rotation;
    }

    private void DamageEnemiesInLight()
    {
        Collider[] hitColliders = Physics.OverlapSphere(transform.position, lamp.range);
        foreach (var hit in hitColliders)
        {
            if (hit.CompareTag("Enemy"))
            {
                Vector3 dirToEnemy = (hit.transform.position - transform.position).normalized;
                float angle = Vector3.Angle(transform.forward, dirToEnemy);

                // Solo dañar si está dentro del cono de luz
                if (angle < lamp.spotAngle / 2f)
                {
                    EnemyFSM enemy = hit.GetComponent<EnemyFSM>();
                    if (enemy != null)
                    {
                        enemy.TakeFlashlightDamage(damagePerSecond * Time.deltaTime);
                    }
                }
            }
        }
    }

    public void RechargeBattery(float amount)
    {
        currentBattery = Mathf.Min(maxBattery, currentBattery + amount);
        if (currentBattery > 0f) isOn = true;
    }

    public float GetBatteryNormalized()
    {
        return currentBattery / maxBattery;
    }
}