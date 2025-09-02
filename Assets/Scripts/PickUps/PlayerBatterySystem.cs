using System;
using UnityEngine;

[RequireComponent(typeof(PlayerHealth))]
public class PlayerBatterySystem : MonoBehaviour
{
    [Header("Referencias")]
    [Tooltip("Arrastra aquí el PlayerLightController del objeto 'Linterna'.")]
    public PlayerLightController lamp;

    [Header("Batería activa")]
    public BatteryType activeType = BatteryType.Green;

    [Header("Capacidades máximas (energía total)")]
    [Tooltip("Capacidad total (unidades de energía) de la batería Verde.")]
    public float maxGreen = 30f;
    [Tooltip("Capacidad total (unidades de energía) de la batería Roja.")]
    public float maxRed = 25f;
    [Tooltip("Capacidad total (unidades de energía) de la batería Azul.")]
    public float maxBlue = 20f;

    [Header("Cargas iniciales")]
    public float startGreen = 30f;
    public float startRed = 10f;
    public float startBlue = 10f;

    [Header("Efectos extra")]
    [Tooltip("Drenaje de vida por segundo cuando la batería activa es Roja y la linterna está encendida.")]
    public float redHealthDrainPerSecond = 4f;
    [Tooltip("Bloquear sprint cuando la batería activa es Azul y la linterna está encendida.")]
    public bool blueBlocksSprint = true;

    // Cargas actuales (propiedades públicas como tenías)
    public float curGreen { get; set; }
    public float curRed { get; set; }
    public float curBlue { get; set; }

    // Referencias
    private PlayerHealth health;
    private PlayerMovement movement;

    // ===================== EVENTOS =====================
    // (current, max, type) cuando cambia la batería activa (valor)
    public event Action<float, float, BatteryType> OnActiveBatteryChanged;
    // Se emite al cambiar de tipo activo
    public event Action<BatteryType> OnBatterySwitched;
    // Se emite cuando alguna batería activa llega a 0 desde >0
    public event Action<BatteryType> OnBatteryDepleted;

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

        // Cargas iniciales (clamp a su capacidad)
        curGreen = Mathf.Clamp(startGreen, 0, maxGreen);
        curRed = Mathf.Clamp(startRed, 0, maxRed);
        curBlue = Mathf.Clamp(startBlue, 0, maxBlue);

        // Notificar estado inicial a cualquier suscriptor
        RaiseActiveChanged();
    }

    void Start()
    {
        if (lamp != null)
        {
            lamp.OnBatterySwitched(activeType);
        }

        // Anunciar el tipo activo al inicio
        OnBatterySwitched?.Invoke(activeType);
        RaiseActiveChanged();
    }

    void Update()
    {
        // Entrada rápida 1/2/3 para cambiar tipo activo
        if (Input.GetKeyDown(KeyCode.Alpha1)) SetActive(BatteryType.Green);
        if (Input.GetKeyDown(KeyCode.Alpha2)) SetActive(BatteryType.Red);
        if (Input.GetKeyDown(KeyCode.Alpha3)) SetActive(BatteryType.Blue);

        bool lightOn = lamp != null && lamp.IsOn();

        if (lightOn)
        {
            // Efecto rojo: drenaje de vida por segundo
            if (activeType == BatteryType.Red && health != null && redHealthDrainPerSecond > 0f)
                health.TakeDamage(redHealthDrainPerSecond * Time.deltaTime);

            // Efecto azul: bloquear sprint
            if (movement != null)
                movement.SetSprintBlocked(blueBlocksSprint && activeType == BatteryType.Blue);
        }
        else
        {
            if (movement != null) movement.SetSprintBlocked(false);
        }
    }

    // ===================== API principal =====================

    /// <summary>Consume una cantidad absoluta (unidades de energía) de la batería activa.</summary>
    public bool ConsumeActiveBattery(float amount)
    {
        if (amount <= 0f) { RaiseActiveChanged(); return GetCharge(activeType) > 0f; }

        switch (activeType)
        {
            case BatteryType.Green:
                {
                    float before = curGreen;
                    curGreen = Mathf.Max(0f, curGreen - amount);
                    RaiseActiveChanged();
                    if (curGreen <= 0f && before > 0f) OnBatteryDepleted?.Invoke(BatteryType.Green);
                    return curGreen > 0f;
                }
            case BatteryType.Red:
                {
                    float before = curRed;
                    curRed = Mathf.Max(0f, curRed - amount);
                    RaiseActiveChanged();
                    if (curRed <= 0f && before > 0f) OnBatteryDepleted?.Invoke(BatteryType.Red);
                    return curRed > 0f;
                }
            case BatteryType.Blue:
                {
                    float before = curBlue;
                    curBlue = Mathf.Max(0f, curBlue - amount);
                    RaiseActiveChanged();
                    if (curBlue <= 0f && before > 0f) OnBatteryDepleted?.Invoke(BatteryType.Blue);
                    return curBlue > 0f;
                }
        }
        return false;
    }

    /// <summary>Devuelve 0..1 del tipo activo.</summary>
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

    /// <summary>Recarga en unidades de energía el tipo indicado.</summary>
    public void Recharge(BatteryType type, float amount)
    {
        if (amount <= 0f) return;

        switch (type)
        {
            case BatteryType.Green: curGreen = Mathf.Min(maxGreen, curGreen + amount); break;
            case BatteryType.Red: curRed = Mathf.Min(maxRed, curRed + amount); break;
            case BatteryType.Blue: curBlue = Mathf.Min(maxBlue, curBlue + amount); break;
        }

        // Si la recarga afecta a la activa, avisar
        if (type == activeType) RaiseActiveChanged();

        // Si la linterna estaba apagada y hay batería, encenderla forzosamente si así lo requiere el controller
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
        if (activeType == type) return;

        activeType = type;

        if (lamp != null) lamp.OnBatterySwitched(activeType);
        OnBatterySwitched?.Invoke(activeType);
        RaiseActiveChanged();
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

        if (type == activeType) RaiseActiveChanged();
    }

    // ===================== Helpers por porcentaje =====================

    /// <summary>Recarga un porcentaje (0..1) de la capacidad del tipo indicado.</summary>
    public void RechargePercent(BatteryType type, float percent01)
    {
        percent01 = Mathf.Clamp01(percent01);
        float add = GetMax(type) * percent01;
        Recharge(type, add);
    }

    /// <summary>Consume de la activa un porcentaje (0..1) de su capacidad.</summary>
    public bool ConsumeActiveBatteryPercent(float percent01)
    {
        percent01 = Mathf.Clamp01(percent01);
        float amount = GetMax(activeType) * percent01;
        return ConsumeActiveBattery(amount);
    }

    // ===================== Internos =====================

    private void RaiseActiveChanged()
    {
        float cur = GetCharge(activeType);
        float max = GetMax(activeType);
        OnActiveBatteryChanged?.Invoke(cur, max, activeType);
    }
}