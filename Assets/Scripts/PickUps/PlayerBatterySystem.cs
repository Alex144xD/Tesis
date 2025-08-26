using UnityEngine;

[RequireComponent(typeof(PlayerHealth))] 
public class PlayerBatterySystem : MonoBehaviour
{
    [Header("Referencias")]
    [Tooltip("Arrastra aquí el PlayerLightController del objeto 'Linterna'.")]
    public PlayerLightController lamp;   

    [Header("Batería activa")]
    public BatteryType activeType = BatteryType.Green;

    [Header("Capacidades máximas (segundos aprox.)")]
    public float maxGreen = 30f;
    public float maxRed = 25f;
    public float maxBlue = 20f;

    [Header("Cargas iniciales")]
    public float startGreen = 30f;
    public float startRed = 10f;
    public float startBlue = 10f;

    [Header("Efectos extra")]
    public float redHealthDrainPerSecond = 4f; 
    public bool blueBlocksSprint = true;       
    public float curGreen { get; set; }
    public float curRed { get; set; }
    public float curBlue { get; set; }

    private PlayerHealth health;
    private PlayerMovement movement;

    void Awake()
    {

        health = GetComponent<PlayerHealth>();
        movement = GetComponent<PlayerMovement>();


        if (!lamp)
        {
            lamp = GetComponentInChildren<PlayerLightController>();
            if (!lamp) lamp = GetComponentInParent<PlayerLightController>();
            if (!lamp) lamp = FindObjectOfType<PlayerLightController>();
        }

        // Cargas iniciales
        curGreen = Mathf.Clamp(startGreen, 0, maxGreen);
        curRed = Mathf.Clamp(startRed, 0, maxRed);
        curBlue = Mathf.Clamp(startBlue, 0, maxBlue);
    }

    void Start()
    {

        if (lamp != null)
        {
            lamp.OnBatterySwitched(activeType);
        }
    }

    void Update()
    {
        // Cambio rápido con 1/2/3
        if (Input.GetKeyDown(KeyCode.Alpha1)) SetActive(BatteryType.Green);
        if (Input.GetKeyDown(KeyCode.Alpha2)) SetActive(BatteryType.Red);
        if (Input.GetKeyDown(KeyCode.Alpha3)) SetActive(BatteryType.Blue);


        bool lightOn = lamp != null && lamp.IsOn();

        if (lightOn)
        {
            if (activeType == BatteryType.Red && health != null)
                health.TakeDamage(redHealthDrainPerSecond * Time.deltaTime);

            if (movement != null)
                movement.SetSprintBlocked(blueBlocksSprint && activeType == BatteryType.Blue);
        }
        else
        {
            if (movement != null) movement.SetSprintBlocked(false);
        }
    }

 
    public bool ConsumeActiveBattery(float amount)
    {
        switch (activeType)
        {
            case BatteryType.Green:
                curGreen = Mathf.Max(0f, curGreen - amount);
                return curGreen > 0f;
            case BatteryType.Red:
                curRed = Mathf.Max(0f, curRed - amount);
                return curRed > 0f;
            case BatteryType.Blue:
                curBlue = Mathf.Max(0f, curBlue - amount);
                return curBlue > 0f;
        }
        return false;
    }

    public float GetActiveBatteryNormalized()
    {
        switch (activeType)
        {
            case BatteryType.Green: return maxGreen > 0 ? curGreen / maxGreen : 0f;
            case BatteryType.Red: return maxRed > 0 ? curRed / maxRed : 0f;
            case BatteryType.Blue: return maxBlue > 0 ? curBlue / maxBlue : 0f;
        }
        return 0f;
    }



    public void Recharge(BatteryType type, float amount)
    {
        switch (type)
        {
            case BatteryType.Green: curGreen = Mathf.Min(maxGreen, curGreen + amount); break;
            case BatteryType.Red: curRed = Mathf.Min(maxRed, curRed + amount); break;
            case BatteryType.Blue: curBlue = Mathf.Min(maxBlue, curBlue + amount); break;
        }


        if (lamp != null && !lamp.IsOn() && GetActiveBatteryNormalized() > 0f)
            lamp.ForceOnIfHasBattery();
    }

    public float GetCharge(BatteryType type)
    {
        switch (type)
        {
            case BatteryType.Green: return curGreen;
            case BatteryType.Red: return curRed;
            case BatteryType.Blue: return curBlue;
        }
        return 0f;
    }

    public float GetMax(BatteryType type)
    {
        switch (type)
        {
            case BatteryType.Green: return maxGreen;
            case BatteryType.Red: return maxRed;
            case BatteryType.Blue: return maxBlue;
        }
        return 1f;
    }

    public void SetActive(BatteryType type)
    {
        activeType = type;

        if (lamp != null) lamp.OnBatterySwitched(activeType);
    }

    public void SetCharge(BatteryType type, float value, bool clampToMax = true)
    {
        switch (type)
        {
            case BatteryType.Green:
                curGreen = clampToMax ? Mathf.Clamp(value, 0f, maxGreen) : value; break;
            case BatteryType.Red:
                curRed = clampToMax ? Mathf.Clamp(value, 0f, maxRed) : value; break;
            case BatteryType.Blue:
                curBlue = clampToMax ? Mathf.Clamp(value, 0f, maxBlue) : value; break;
        }
    }
}