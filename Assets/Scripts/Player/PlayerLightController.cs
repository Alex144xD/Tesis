using UnityEngine;

[RequireComponent(typeof(Light))]
public class PlayerLightController : MonoBehaviour
{
    [Header("Referencia a C�mara")]
    public Camera playerCamera;

    [Header("Offset local (hacia adelante)")]
    public Vector3 localOffset = new Vector3(0, 0, 0.2f);

    [Header("Luz")]
    public float minRange = 5f;
    public float maxRange = 15f;
    public float flickerSpeed = 0.1f;

    [Header("Bater�a")]
    public float maxBattery = 30f;     // segundos de uso
    public float drainRate = 1f;      // unidades por segundo
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

        // Si no queda bater�a, fuerza apagar
        if (currentBattery <= 0f)
            isOn = false;

        lamp.enabled = isOn;

        if (isOn)
        {
            // Drenaje de bater�a
            currentBattery = Mathf.Max(0f, currentBattery - drainRate * Time.deltaTime);

            // Parpadeo
            float t = 0.5f + 0.5f * Mathf.PerlinNoise(Time.time * flickerSpeed, 0f);
            lamp.range = Mathf.Lerp(minRange, maxRange, t);
        }

        // Mover y rotar junto a la c�mara
        transform.position = playerCamera.transform.position +
                             playerCamera.transform.TransformVector(localOffset);
        transform.rotation = playerCamera.transform.rotation;
    }

    public void RechargeBattery(float amount)
    {
        currentBattery = Mathf.Min(maxBattery, currentBattery + amount);
        // si recargas y a�n tienes carga, deja la linterna encendida
        if (currentBattery > 0f) isOn = true;
    }

   
    public float GetBatteryNormalized()
    {
        return currentBattery / maxBattery;
    }
}

