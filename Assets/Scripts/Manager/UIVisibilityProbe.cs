using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class UIVisibilityProbe : MonoBehaviour
{
    [Tooltip("Asigna el Canvas raíz de tus HUDs (si lo dejas vacío, intenta encontrar uno).")]
    public Canvas rootCanvas;

    [Tooltip("Opcional: fuerza Overlay y orden alto para traer todo al frente")]
    public bool forceOverlay = true;
    public int sortingOrder = 2000;

    [ContextMenu("Probe Now")]
    public void ProbeNow()
    {
        if (!rootCanvas) rootCanvas = FindObjectOfType<Canvas>(true);
        if (!rootCanvas)
        {
            Debug.LogError("[UIVisibilityProbe] No encontré ningún Canvas en la escena.");
            return;
        }

        if (forceOverlay)
        {
            rootCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            rootCanvas.worldCamera = null;
            rootCanvas.sortingOrder = sortingOrder;
        }

        // 1) CanvasGroups → alpha/interacción
        var groups = rootCanvas.GetComponentsInChildren<CanvasGroup>(true);
        foreach (var g in groups)
        {
            if (g.alpha < 0.99f || !g.interactable || !g.blocksRaycasts)
            {
                Debug.LogWarning($"[Probe] CanvasGroup ajustado en: {g.name} (alpha {g.alpha} → 1)");
                g.alpha = 1f; g.interactable = true; g.blocksRaycasts = true;
            }
        }

        // 2) Habilitar imágenes y textos desactivados
        var imgs = rootCanvas.GetComponentsInChildren<Image>(true);
        int offImgs = 0;
        foreach (var i in imgs) { if (!i.enabled) { i.enabled = true; offImgs++; } }
        if (offImgs > 0) Debug.Log($"[Probe] Rehabilité {offImgs} Image(s).");

        var tmps = rootCanvas.GetComponentsInChildren<TMP_Text>(true);
        int offTmps = 0;
        foreach (var t in tmps) { if (!t.enabled) { t.enabled = true; offTmps++; } }
        if (offTmps > 0) Debug.Log($"[Probe] Rehabilité {offTmps} TMP_Text(s).");

        // 3) Escala y posición de paneles
        var rects = rootCanvas.GetComponentsInChildren<RectTransform>(true);
        foreach (var rt in rects)
        {
            if (rt.localScale.x == 0f || rt.localScale.y == 0f)
            {
                Debug.LogWarning($"[Probe] {rt.name} tenía scale 0. Lo ajusto a 1.");
                rt.localScale = Vector3.one;
            }
        }

        // 4) Capas y cámara (si no Overlay)
        if (rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
        {
            if (!rootCanvas.worldCamera)
                Debug.LogWarning("[Probe] Canvas en ScreenSpace-Camera pero sin cámara asignada.");
        }

        // 5) Paneles activados pero ocultos por padres
        var gos = rootCanvas.GetComponentsInChildren<Transform>(true);
        foreach (var tr in gos)
        {
            var go = tr.gameObject;
            if (go.activeInHierarchy && go.GetComponent<RectTransform>() != null)
            {
                // lo traemos al frente en su nivel
                go.transform.SetAsLastSibling();
            }
        }

        Debug.Log("[UIVisibilityProbe] Revisión y ajustes básicos completados.");
    }

    void Start()
    {
        ProbeNow();
    }
}
