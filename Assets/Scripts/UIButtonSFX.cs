using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

[RequireComponent(typeof(Button))]
public class UIButtonSFX : MonoBehaviour, IPointerEnterHandler, ISelectHandler
{
    [Header("Clips")]
    public AudioClip clickClip;
    public AudioClip hoverClip;

    [Header("Volúmenes")]
    [Range(0f, 1f)] public float clickVolume = 1f;
    [Range(0f, 1f)] public float hoverVolume = 1f;

    [Header("Cuándo reproducir hover")]
    public bool playHoverOnPointerEnter = true;
    public bool playHoverOnSelect = true;

    AudioSource _src;
    Button _btn;

    void Awake()
    {
        _btn = GetComponent<Button>();

        // Crea/usa un AudioSource 2D local
        _src = GetComponent<AudioSource>();
        if (!_src) _src = gameObject.AddComponent<AudioSource>();
        _src.playOnAwake = false;
        _src.loop = false;
        _src.spatialBlend = 0f;   // 2D
        _src.dopplerLevel = 0f;

        _btn.onClick.AddListener(() =>
        {
            if (!_btn.interactable) return;
            if (clickClip) _src.PlayOneShot(clickClip, clickVolume);
        });
    }
    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!playHoverOnPointerEnter || !_btn.interactable) return;
        if (hoverClip) _src.PlayOneShot(hoverClip, hoverVolume);
    }

    // Selección con teclado/mandos
    public void OnSelect(BaseEventData eventData)
    {
        if (!playHoverOnSelect || !_btn.interactable) return;
        if (hoverClip) _src.PlayOneShot(hoverClip, hoverVolume);
    }
}