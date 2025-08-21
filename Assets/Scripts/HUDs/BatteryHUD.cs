using UnityEngine;
using UnityEngine.UI;
using TMPro;

[DisallowMultipleComponent]
public class BatteryHUD : MonoBehaviour
{
    [Header("Refs")]
    public PlayerLightController flashlight;     // Arrastra la linterna
    public PlayerBatterySystem batteries;        // Arrastra el sistema de baterías (opcional)

    [Header("UI")]
    public Image fillImage;                      // Image -> Filled
    public Image backgroundImage;                // opcional
    public TextMeshProUGUI percentText;          // opcional
    public TextMeshProUGUI secondsText;          // opcional
    public CanvasGroup hudGroup;                 // opcional (recomendado en el root del HUD)

    [Header("Visibilidad")]
    [Tooltip("Oculta el HUD cuando la linterna está apagada.")]
    public bool hideWhenFlashlightOff = true;
    [Tooltip("Oculta el HUD cuando la batería llega a 0.")]
    public bool hideWhenEmpty = true;
    [Tooltip("Tolerancia para considerar 'vacío'.")]
    [Range(0f, 0.1f)] public float emptyEpsilon = 0.01f;

    [Header("Umbrales (0..1)")]
    [Range(0f, 1f)] public float lowThreshold = 0.50f;     // Verde -> Amarillo
    [Range(0f, 1f)] public float criticalThreshold = 0.20f; // Amarillo -> Rojo

    [Header("Colores (fijos por nivel)")]
    public Color colorHigh = new Color32(80, 255, 140, 255);  // Verde
    public Color colorMid = new Color32(250, 210, 60, 255);  // Amarillo
    public Color colorLow = new Color32(235, 70, 70, 255);  // Rojo

    [Header("Suavizado")]
    public bool smoothFill = true;
    public float fillLerpSpeed = 8f;

    [Header("Depuración")]
    public bool debugLogs = false;
    public bool preferFlashlightWhenBoth = true; // si hay PBS y linterna, prioriza la más "realista"

    float currentFill = 1f;

    void Awake()
    {
        if (!flashlight) flashlight = FindObjectOfType<PlayerLightController>(true);
        if (!batteries) batteries = FindObjectOfType<PlayerBatterySystem>(true);
        if (!hudGroup) hudGroup = GetComponent<CanvasGroup>();
        if (fillImage) currentFill = fillImage.fillAmount;
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (!Application.isPlaying)
        {
            if (!flashlight) flashlight = FindObjectOfType<PlayerLightController>(true);
            if (!batteries) batteries = FindObjectOfType<PlayerBatterySystem>(true);
            if (!hudGroup) hudGroup = GetComponent<CanvasGroup>();
        }
    }
#endif

    void LateUpdate()
    {
        if (!isActiveAndEnabled || fillImage == null) return;

        // Re-vincular si se perdieron referencias (cambios de escena)
        if (!flashlight) flashlight = FindObjectOfType<PlayerLightController>(true);
        if (!batteries) batteries = FindObjectOfType<PlayerBatterySystem>(true);

        // 1) Nivel 0..1 (fuente “inteligente”)
        float t = GetBatteryNormalizedSmart();

        // 2) Relleno (suavizado opcional)
        currentFill = smoothFill
            ? Mathf.Lerp(currentFill, t, Time.unscaledDeltaTime * Mathf.Max(0.01f, fillLerpSpeed))
            : t;

        fillImage.fillAmount = currentFill;

        // 3) Colores por umbrales usando el MISMO valor que ves (currentFill)
        float v = currentFill;
        if (v <= criticalThreshold) fillImage.color = colorLow;    // rojo
        else if (v <= lowThreshold) fillImage.color = colorMid;    // amarillo
        else fillImage.color = colorHigh;                          // verde

        // 4) Textos
        if (percentText) percentText.text = Mathf.RoundToInt(v * 100f) + "%";
        if (secondsText)
        {
            float drain = flashlight ? Mathf.Max(0.0001f, flashlight.drainRate) : 1f;
            float secondsLeft = 0f;
            if (batteries)
            {
                float max = Mathf.Max(0.0001f, batteries.GetMax(batteries.activeType));
                secondsLeft = (v * max) / drain;
            }
            else if (flashlight)
            {
                float max = Mathf.Max(0.0001f, flashlight.maxBattery);
                secondsLeft = (v * max) / drain;
            }
            secondsText.text = Mathf.CeilToInt(secondsLeft) + "s";
        }

        // 5) Visibilidad: linterna OFF y/o batería vacía
        bool wantVisible = true;

        if (hideWhenFlashlightOff)
            wantVisible = flashlight && flashlight.isActiveAndEnabled && flashlight.IsOn();

        if (hideWhenEmpty && v <= emptyEpsilon)
            wantVisible = false;

        ApplyVisibility(wantVisible);

        if (debugLogs)
        {
            float tFlash = flashlight ? Mathf.Clamp01(flashlight.GetBatteryNormalized()) : -1f;
            float tInv = batteries ? Mathf.Clamp01(batteries.GetActiveBatteryNormalized()) : -1f;
            Debug.Log($"[BatteryHUD] v={v:0.00}, tSmart={t:0.00} (flash={tFlash:0.00}, inv={tInv:0.00}) visible={wantVisible}");
        }
    }

    // ---------- Helpers ----------
    float GetBatteryNormalizedSmart()
    {
        float tFlash = flashlight ? Mathf.Clamp01(flashlight.GetBatteryNormalized()) : -1f;
        float tInv = batteries ? Mathf.Clamp01(batteries.GetActiveBatteryNormalized()) : -1f;

        if (tInv < 0f && tFlash >= 0f) return tFlash;
        if (tFlash < 0f && tInv >= 0f) return tInv;
        if (tInv < 0f && tFlash < 0f) return 0f;

        if (preferFlashlightWhenBoth)
        {
            // Conservador: usa el menor si difieren con claridad
            if (Mathf.Abs(tFlash - tInv) > 0.03f) return Mathf.Min(tFlash, tInv);
            return tFlash; // casi iguales
        }
        else
        {
            return Mathf.Min(tFlash, tInv);
        }
    }

    void ApplyVisibility(bool visible)
    {
        if (hudGroup)
        {
            hudGroup.alpha = visible ? 1f : 0f;
            hudGroup.interactable = visible;
            hudGroup.blocksRaycasts = visible;
        }
        else
        {
            if (fillImage) fillImage.enabled = visible;
            if (backgroundImage) backgroundImage.enabled = visible;
            if (percentText) percentText.enabled = visible;
            if (secondsText) secondsText.enabled = visible;
        }
    }

    // Evento opcional si ya lo usabas
    public void OnBatteryChangedUI(BatteryType type, float current, float max)
    {
        float t = (max > 0f) ? Mathf.Clamp01(current / max) : 0f;
        currentFill = t;
        fillImage.fillAmount = t;

        float v = currentFill;
        if (v <= criticalThreshold) fillImage.color = colorLow;
        else if (v <= lowThreshold) fillImage.color = colorMid;
        else fillImage.color = colorHigh;

        if (percentText) percentText.text = Mathf.RoundToInt(v * 100f) + "%";

        if (secondsText)
        {
            float drain = flashlight ? Mathf.Max(0.0001f, flashlight.drainRate) : 1f;
            float secondsLeft = (max > 0f) ? (v * max) / drain : 0f;
            secondsText.text = Mathf.CeilToInt(secondsLeft) + "s";
        }

        // Visibilidad por batería vacía también aquí
        bool wantVisible = !(hideWhenEmpty && v <= emptyEpsilon);
        if (hideWhenFlashlightOff)
            wantVisible &= flashlight && flashlight.isActiveAndEnabled && flashlight.IsOn();
        ApplyVisibility(wantVisible);
    }
}