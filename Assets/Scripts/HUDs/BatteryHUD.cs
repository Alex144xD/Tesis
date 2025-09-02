using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

[DisallowMultipleComponent]
public class BatteryHUD : MonoBehaviour
{
    [Header("Refs")]
    public PlayerLightController flashlight;
    public PlayerBatterySystem batteries;

    [Header("UI")]
    public Image fillImage;
    public Image backgroundImage;
    public TextMeshProUGUI percentText;
    public TextMeshProUGUI secondsText;
    public CanvasGroup hudGroup;

    [Header("Visibilidad")]
    [Tooltip("Oculta el HUD cuando la linterna está apagada.")]
    public bool hideWhenFlashlightOff = true;
    [Tooltip("Oculta el HUD cuando la batería llega a 0.")]
    public bool hideWhenEmpty = true;
    [Tooltip("Tolerancia para considerar 'vacío'.")]
    [Range(0f, 0.1f)] public float emptyEpsilon = 0.01f;

    [Header("Umbrales (0..1)")]
    [Range(0f, 1f)] public float lowThreshold = 0.50f;
    [Range(0f, 1f)] public float criticalThreshold = 0.20f;

    [Header("Colores (fijos por nivel)")]
    public Color colorHigh = new Color32(80, 255, 140, 255);
    public Color colorMid = new Color32(250, 210, 60, 255);
    public Color colorLow = new Color32(235, 70, 70, 255);

    [Header("Suavizado")]
    public bool smoothFill = true;
    [Tooltip("Units/second; usa MoveTowards para ser determinista.")]
    public float fillSpeed = 2.2f;

    [Header("Fade HUD")]
    public bool smoothVisibility = true;
    public float fadeSpeed = 8f;

    [Header("Depuración")]
    public bool debugLogs = false;
    public bool preferFlashlightWhenBoth = true;

    // ===== Runtime =====
    float currentFill = 1f;
    float targetFill = 1f;

    // Suscripción a eventos (si PlayerBatterySystem mejorado está presente)
    bool _hasEvents;
    float _lastEventCur, _lastEventMax;
    BatteryType _lastEventType;

    // Rebind watchdog (p.ej. al cargar escena)
    float _rebindCooldown = 0f;
    const float REBIND_INTERVAL = 1.0f;

    void Awake()
    {
        if (!hudGroup) hudGroup = GetComponent<CanvasGroup>();
    }

    void OnEnable()
    {
        Rebind();
        ImmediateRefresh();
    }

    void OnDisable()
    {
        UnsubscribeEvents();
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (!Application.isPlaying && hudGroup == null)
            hudGroup = GetComponent<CanvasGroup>();
        criticalThreshold = Mathf.Clamp01(criticalThreshold);
        lowThreshold = Mathf.Clamp01(Mathf.Max(criticalThreshold, lowThreshold));
        fillSpeed = Mathf.Max(0.01f, fillSpeed);
        fadeSpeed = Mathf.Max(0.01f, fadeSpeed);
    }
#endif

    void Update()
    {
        // Rebindeo ocasional por si cambiaron refs en runtime
        if (_rebindCooldown > 0f) _rebindCooldown -= Time.unscaledDeltaTime;
        else { _rebindCooldown = REBIND_INTERVAL; Rebind(false); }

        // 1) Obtener fill objetivo (de eventos o polling)
        float t = GetBatteryNormalizedSmart();
        targetFill = t;

        // 2) Suavizado determinista del fill
        if (smoothFill)
            currentFill = Mathf.MoveTowards(currentFill, targetFill, fillSpeed * Time.unscaledDeltaTime);
        else
            currentFill = targetFill;

        currentFill = Mathf.Clamp01(currentFill);

        // 3) Pintar barra
        if (fillImage) fillImage.fillAmount = currentFill;

        // 4) Color por umbral
        ApplyColorByThreshold(currentFill);

        // 5) Textos
        if (percentText) percentText.text = Mathf.RoundToInt(currentFill * 100f) + "%";
        if (secondsText) secondsText.text = FormatSecondsLeft(currentFill);

        // 6) Visibilidad + fade
        ApplyVisibility(ComputeVisibility(currentFill));

        if (debugLogs)
        {
            float tf = flashlight ? flashlight.GetBatteryNormalized() : -1f;
            float tb = batteries ? batteries.GetActiveBatteryNormalized() : -1f;
            Debug.Log($"[BatteryHUD] cur={currentFill:0.00} (flash={tf:0.00}, batt={tb:0.00}) vis={hudGroup?.alpha:0.00}");
        }
    }

    // ====== Eventos del BatterySystem (si existen) ======
    void SubscribeEvents()
    {
        if (!batteries) return;

        try
        {
            batteries.OnActiveBatteryChanged += OnActiveChanged;
            batteries.OnBatterySwitched += OnSwitched;
            _hasEvents = true;
        }
        catch
        {
            _hasEvents = false; // versión sin eventos
        }
    }

    void UnsubscribeEvents()
    {
        if (!_hasEvents || !batteries) return;
        try
        {
            batteries.OnActiveBatteryChanged -= OnActiveChanged;
            batteries.OnBatterySwitched -= OnSwitched;
        }
        catch { /* ignorar */ }
        _hasEvents = false;
    }

    void OnActiveChanged(float cur, float max, BatteryType type)
    {
        _lastEventCur = cur;
        _lastEventMax = Mathf.Max(0.0001f, max);
        _lastEventType = type;

        float t = Mathf.Clamp01(_lastEventCur / _lastEventMax);
        targetFill = t;
    }

    void OnSwitched(BatteryType newType)
    {
        _lastEventType = newType;
    }

    // ====== Helpers ======

    void Rebind(bool subscribe = true)
    {
        if (!flashlight) flashlight = FindObjectOfType<PlayerLightController>(true);
        if (!batteries) batteries = FindObjectOfType<PlayerBatterySystem>(true);

        if (subscribe)
        {
            UnsubscribeEvents();
            SubscribeEvents();
        }
    }

    void ImmediateRefresh()
    {
        float t = GetBatteryNormalizedSmart();
        currentFill = targetFill = t;
        PaintImmediate(t);
        ApplyVisibility(ComputeVisibility(t), immediate: true);
    }

    void PaintImmediate(float t)
    {
        if (fillImage) fillImage.fillAmount = t;
        ApplyColorByThreshold(t);

        if (percentText) percentText.text = Mathf.RoundToInt(t * 100f) + "%";
        if (secondsText) secondsText.text = FormatSecondsLeft(t);
    }

    void ApplyColorByThreshold(float v01)
    {
        if (!fillImage) return;
        if (v01 <= criticalThreshold) fillImage.color = colorLow;
        else if (v01 <= lowThreshold) fillImage.color = colorMid;
        else fillImage.color = colorHigh;
    }

    string FormatSecondsLeft(float normalized)
    {
        float drain = GetCurrentDrainPerSecondSafe();
        if (drain <= 0.0001f) return "—"; // evita división por 0

        float maxSeconds;
        if (batteries)
        {
            float max = Mathf.Max(0.0001f, batteries.GetMax(batteries.activeType));
            maxSeconds = normalized * max;
        }
        else if (flashlight)
        {
            float max = Mathf.Max(0.0001f, flashlight.maxBattery);
            maxSeconds = normalized * max;
        }
        else
        {
            return "—";
        }

        float secondsLeft = maxSeconds / drain;
        if (float.IsInfinity(secondsLeft) || float.IsNaN(secondsLeft)) return "—";
        int s = Mathf.Clamp(Mathf.CeilToInt(secondsLeft), 0, 35999); // tope ~10h
        return s + "s";
    }

    bool ComputeVisibility(float v01)
    {
        bool wantVisible = true;

        if (hideWhenEmpty && v01 <= emptyEpsilon)
            wantVisible = false;

        if (hideWhenFlashlightOff)
            wantVisible &= flashlight && flashlight.isActiveAndEnabled && flashlight.IsOn();

        return wantVisible;
    }

    void ApplyVisibility(bool visible, bool immediate = false)
    {
        if (!hudGroup)
        {
            if (fillImage) fillImage.enabled = visible;
            if (backgroundImage) backgroundImage.enabled = visible;
            if (percentText) percentText.enabled = visible;
            if (secondsText) secondsText.enabled = visible;
            return;
        }

        if (!smoothVisibility || immediate)
        {
            hudGroup.alpha = visible ? 1f : 0f;
            hudGroup.interactable = visible;
            hudGroup.blocksRaycasts = visible;
        }
        else
        {
            float target = visible ? 1f : 0f;
            hudGroup.alpha = Mathf.MoveTowards(hudGroup.alpha, target, fadeSpeed * Time.unscaledDeltaTime);
            bool done = Mathf.Approximately(hudGroup.alpha, target);
            hudGroup.interactable = visible && done;
            hudGroup.blocksRaycasts = visible && done;
        }
    }

    // === Drenaje actual sin usar 'drainRate' legacy ===
    float GetCurrentDrainPerSecondSafe()
    {
        if (flashlight == null) return 1f;

        // Si el controller expone el método nuevo, úsalo (más exacto)
        try
        {
            float v = flashlight.GetCurrentDrainPerSecondForHUD();
            if (v > 0f) return v;
        }
        catch { /* método no existe en alguna variante */ }

        // Estimación basada en el modelo por porcentaje (sin usar drainRate)
        // Requiere: baseDrainPercentPerSecond + low/highPercentMult + green/red/blue mults + capacidad
        var mode = flashlight.GetCurrentMode();
        if (mode == PlayerLightController.FlashlightUIMode.Off) return 0f;

        float modeMult = (mode == PlayerLightController.FlashlightUIMode.High)
            ? flashlight.highPercentMult
            : flashlight.lowPercentMult;

        // Tipo de batería actual
        BatteryType bType = batteries ? batteries.activeType : BatteryType.Green;

        float battMult = 1f;
        switch (bType)
        {
            case BatteryType.Green: battMult = flashlight.greenDrainMult; break;
            case BatteryType.Red: battMult = flashlight.redDrainMult; break;
            case BatteryType.Blue: battMult = flashlight.blueDrainMult; break;
        }

        float pctPerSec = flashlight.baseDrainPercentPerSecond * Mathf.Max(0.01f, modeMult) * battMult;

        float maxCap = batteries
            ? Mathf.Max(0.0001f, batteries.GetMax(bType))
            : Mathf.Max(0.0001f, flashlight.maxBattery);

        return maxCap * pctPerSec; // unidades de energía/segundo
    }

    float GetBatteryNormalizedSmart()
    {
        // Si tenemos eventos válidos, priorízalos
        if (_hasEvents && _lastEventMax > 0f)
            return Mathf.Clamp01(_lastEventCur / _lastEventMax);

        // Polling seguro
        float tFlash = (flashlight != null) ? Mathf.Clamp01(flashlight.GetBatteryNormalized()) : -1f;
        float tBatt = (batteries != null) ? Mathf.Clamp01(batteries.GetActiveBatteryNormalized()) : -1f;

        if (tBatt < 0f && tFlash >= 0f) return tFlash;
        if (tFlash < 0f && tBatt >= 0f) return tBatt;
        if (tBatt < 0f && tFlash < 0f) return 0f;

        // Si ambas fuentes existen y difieren, muestra la menor (más conservador).
        if (preferFlashlightWhenBoth)
        {
            if (Mathf.Abs(tFlash - tBatt) > 0.03f) return Mathf.Min(tFlash, tBatt);
            return tFlash; // casi iguales
        }
        else
        {
            return Mathf.Min(tFlash, tBatt);
        }
    }

    // Hook opcional (mantengo tu firma)
    public void OnBatteryChangedUI(BatteryType type, float current, float max)
    {
        _lastEventType = type;
        _lastEventCur = current;
        _lastEventMax = Mathf.Max(0.0001f, max);
        float t = Mathf.Clamp01(current / _lastEventMax);

        targetFill = t;
        if (!smoothFill) currentFill = t;

        PaintImmediate(smoothFill ? currentFill : t);
        ApplyVisibility(ComputeVisibility(smoothFill ? currentFill : t));
    }
}