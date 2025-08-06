using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UI;

public class BatteryHUD : MonoBehaviour
{
    public PlayerLightController flashlight; // arrástralo desde el Player
    public Image fillImage; // el fill interior
    public Image backgroundImage; // opcional

    void Update()
    {
        if (flashlight == null || fillImage == null) return;

        float batteryPercent = flashlight.GetBatteryNormalized();
        fillImage.fillAmount = batteryPercent;

        // Color dinámico (verde → rojo)
        if (batteryPercent > 0.5f)
            fillImage.color = Color.green;
        else if (batteryPercent > 0.2f)
            fillImage.color = Color.yellow;
        else
            fillImage.color = Color.red;

        // Mostrar u ocultar según si la linterna está activa
        fillImage.enabled = flashlight.IsOn();
        if (backgroundImage != null)
            backgroundImage.enabled = flashlight.IsOn();
    }
}