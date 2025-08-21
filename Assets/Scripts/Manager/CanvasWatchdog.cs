using UnityEngine;

[DisallowMultipleComponent]
public class CanvasWatchdog : MonoBehaviour
{
    Canvas _canvas;

    void Awake()
    {
        _canvas = GetComponent<Canvas>();
        if (!_canvas) Debug.LogError("[CanvasWatchdog] No hay Canvas en este GO.");
    }

    void OnEnable()
    {
        Debug.Log($"[CanvasWatchdog] OnEnable -> {name}\n{new System.Diagnostics.StackTrace(1, true)}");
    }

    void OnDisable()
    {
        Debug.LogWarning($"[CanvasWatchdog] OnDisable -> {name}\n{new System.Diagnostics.StackTrace(1, true)}");
    }

    void Update()
    {
        // Si alguien deshabilita solo el componente Canvas (no el GO), lo detectamos:
        if (_canvas && !_canvas.enabled)
        {
            Debug.LogWarning($"[CanvasWatchdog] Canvas component DISABLED -> {name}\n{new System.Diagnostics.StackTrace(1, true)}");
            _canvas.enabled = true; // opcional: reactivarlo
        }
    }
}