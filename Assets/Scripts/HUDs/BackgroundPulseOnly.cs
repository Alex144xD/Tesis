using UnityEngine;
using UnityEngine.UI;

public class BackgroundPulseOnly : MonoBehaviour
{
    [Header("Objetivo (si lo dejas null usa este mismo GO)")]
    public RectTransform target;
    [Tooltip("Si asignas un CanvasGroup, hace fade en alpha sin mover nada.")]
    public CanvasGroup canvasGroup;
    [Tooltip("Si NO tienes CanvasGroup, puedes tintar un Graphic (Image/Text).")]
    public Graphic graphicToTint;

    [Header("Pulso de escala (no mueve posición)")]
    public bool scalePulse = true;
    public float scaleAmplitude = 0.02f;   // 2%
    public float scaleSpeed = 1.2f;

    [Header("Pulso de alpha (opcional)")]
    public bool alphaPulse = false;
    public float alphaMin = 0.85f;
    public float alphaMax = 1.00f;
    public float alphaSpeed = 1.3f;

    Vector3 baseScale;
    float baseAlpha;

    void Awake()
    {
        if (!target) target = GetComponent<RectTransform>();
        if (canvasGroup) baseAlpha = canvasGroup.alpha;
        else if (graphicToTint) baseAlpha = graphicToTint.color.a;
        if (target) baseScale = target.localScale;
    }

    void Update()
    {
        if (target && scalePulse)
        {
            float s = 1f + Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f * scaleSpeed) * scaleAmplitude;
            target.localScale = baseScale * s; // ¡no tocamos posición!
        }

        if (alphaPulse)
        {
            float t = (Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f * alphaSpeed) * 0.5f) + 0.5f;
            float a = Mathf.Lerp(alphaMin, alphaMax, t);

            if (canvasGroup) canvasGroup.alpha = a;
            else if (graphicToTint)
            {
                var c = graphicToTint.color; c.a = a; graphicToTint.color = c;
            }
        }
    }
}