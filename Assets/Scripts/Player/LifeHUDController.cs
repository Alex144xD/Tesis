using UnityEngine;
using UnityEngine.UI;

public class LifeHUDController : MonoBehaviour
{
    [Tooltip("Referencia al script PlayerHealth")]
    public PlayerHealth playerHealth;

    [Tooltip("Overlay de daño (imagen que cubre la pantalla)")]
    public Image damageOverlay;

    [Range(0f, 1f)]
    [Tooltip("Alpha máximo cuando estás a 0% de vida")]
    public float maxAlpha = 0.6f;

    [Tooltip("Velocidad de desvanecimiento")]
    public float fadeSpeed = 2f;

    void Start()
    {
        // Protecciones tempranas por si olvidaste asignar en el Inspector
        if (playerHealth == null)
            Debug.LogError("LifeHUDController: falta asignar playerHealth en el Inspector.", this);
        if (damageOverlay == null)
            Debug.LogError("LifeHUDController: falta asignar damageOverlay en el Inspector.", this);
    }

    void Update()
    {
        if (playerHealth == null || damageOverlay == null)
            return;

        // Usa el método GetHealthNormalized() para obtener 0..1
        float hpNorm = playerHealth.GetHealthNormalized();
        float targetAlpha = (1f - hpNorm) * maxAlpha;

        // la transición de alpha
        Color col = damageOverlay.color;
        col.a = Mathf.Lerp(col.a, targetAlpha, Time.deltaTime * fadeSpeed);
        damageOverlay.color = col;
    }
}