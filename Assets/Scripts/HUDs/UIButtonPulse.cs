using UnityEngine;
using UnityEngine.EventSystems;

public class UIButtonPulse : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    public float hoverScale = 1.06f, clickScale = 0.96f, speed = 10f;
    Vector3 baseScale, targetScale;

    void Awake() { baseScale = transform.localScale; targetScale = baseScale; }
    void Update() { transform.localScale = Vector3.Lerp(transform.localScale, targetScale, Time.unscaledDeltaTime * speed); }
    public void OnPointerEnter(PointerEventData _) => targetScale = baseScale * hoverScale;
    public void OnPointerExit(PointerEventData _) => targetScale = baseScale;
    public void OnPointerDown(PointerEventData _) => targetScale = baseScale * clickScale;
    public void OnPointerUp(PointerEventData _) => targetScale = baseScale * hoverScale;
}