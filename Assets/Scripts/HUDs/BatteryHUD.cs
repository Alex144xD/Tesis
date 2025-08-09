using UnityEngine;
using UnityEngine.UI;

public class BatteryHUD : MonoBehaviour
{
    [Header("Refs")]
    public PlayerLightController flashlight;   // arrástralo desde el Player
    public Image fillImage;                    // el fill interior
    public Image backgroundImage;              // opcional

    [Header("Umbrales de color (0..1)")]
    [Range(0f, 1f)] public float lowThreshold = 0.50f;       // pasa de verde a amarillo
    [Range(0f, 1f)] public float criticalThreshold = 0.20f;  // pasa de amarillo a rojo

    [Header("Colores")]
    public Color highColor = new Color32(90, 210, 120, 255); // verde
    public Color mediumColor = new Color32(250, 200, 90, 255); // amarillo
    public Color lowColor = new Color32(235, 100, 100, 255); // rojo

    [Header("Suavizado (opcional)")]
    public bool smoothFill = true;
    public float fillLerpSpeed = 8f;

    float currentFill = 1f;

    void Awake()
    {
        if (fillImage != null) currentFill = fillImage.fillAmount;
    }

    void Update()
    {
        if (flashlight == null || fillImage == null) return;

        float t = Mathf.Clamp01(flashlight.GetBatteryNormalized());

        currentFill = smoothFill ? Mathf.Lerp(currentFill, t, Time.deltaTime * fillLerpSpeed) : t;
        fillImage.fillAmount = currentFill;

        if (t > lowThreshold) fillImage.color = highColor;
        else if (t > criticalThreshold) fillImage.color = mediumColor;
        else fillImage.color = lowColor;

        bool visible = flashlight.IsOn();
        fillImage.enabled = visible;
        if (backgroundImage != null) backgroundImage.enabled = visible;
    }
}