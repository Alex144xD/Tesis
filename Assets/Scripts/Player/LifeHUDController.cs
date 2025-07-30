using UnityEngine;
using UnityEngine.UI;

public class LifeHUDController : MonoBehaviour
{
    [Header("Referencias")]
    public PlayerHealth playerHealth;
    public Image bloodOverlay;

    [Header("Overlay")]
    [Range(0f, 1f)] public float maxAlpha = 0.8f;
    public float fadeSpeed = 3f;
    public float pulseSpeed = 5f;

    private void Awake()
    {
        if (playerHealth == null)
        {
            playerHealth = FindObjectOfType<PlayerHealth>();
        }
    }

    void Update()
    {
        if (playerHealth == null || bloodOverlay == null)
            return;

        float hpNorm = playerHealth.GetHealthNormalized();

        // Opacidad basada en la vida
        float targetAlpha = (1f - hpNorm) * maxAlpha;

        // Efecto de pulso cuando la vida es muy baja (< 30%)
        if (hpNorm < 0.3f)
        {
            targetAlpha += Mathf.Sin(Time.time * pulseSpeed) * 0.1f;
        }

        // Suavizar transición
        Color col = bloodOverlay.color;
        col.a = Mathf.Lerp(col.a, Mathf.Clamp01(targetAlpha), Time.deltaTime * fadeSpeed);
        bloodOverlay.color = col;

        // Escalar overlay para sensación más agresiva
        float scale = 1f + (1f - hpNorm) * 0.2f;
        bloodOverlay.rectTransform.localScale = new Vector3(scale, scale, 1f);
    }
}
