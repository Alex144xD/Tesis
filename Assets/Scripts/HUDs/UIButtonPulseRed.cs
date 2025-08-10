using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class UIButtonPulseRed : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    public float hoverScale = 1.06f, clickScale = 0.96f, speed = 10f;
    public Color normalColor = new Color(1f, 0.298f, 0.298f); 
    public Color hoverColor = new Color(1f, 0.4f, 0.4f); 
    public Color clickColor = new Color(0.9f, 0.1f, 0.1f); 

    private Vector3 baseScale, targetScale;
    private Image buttonImage;

    void Awake()
    {
        baseScale = transform.localScale;
        targetScale = baseScale;
        buttonImage = GetComponent<Image>();
        if (buttonImage != null)
            buttonImage.color = normalColor;
    }

    void Update()
    {
        transform.localScale = Vector3.Lerp(transform.localScale, targetScale, Time.unscaledDeltaTime * speed);
    }

    public void OnPointerEnter(PointerEventData _)
    {
        targetScale = baseScale * hoverScale;
        if (buttonImage != null) buttonImage.color = hoverColor;
    }

    public void OnPointerExit(PointerEventData _)
    {
        targetScale = baseScale;
        if (buttonImage != null) buttonImage.color = normalColor;
    }

    public void OnPointerDown(PointerEventData _)
    {
        targetScale = baseScale * clickScale;
        if (buttonImage != null) buttonImage.color = clickColor;
    }

    public void OnPointerUp(PointerEventData _)
    {
        targetScale = baseScale * hoverScale;
        if (buttonImage != null) buttonImage.color = hoverColor;
    }
}