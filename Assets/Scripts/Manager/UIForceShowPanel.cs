using UnityEngine;
using UnityEngine.UI;
using TMPro;

[DisallowMultipleComponent]
public class UIForceShowPanel : MonoBehaviour
{
    [Header("Panel objetivo (este GameObject si lo dejas vacío)")]
    public GameObject targetPanel;

    [Header("Canvas destino (lo crea/usa automáticamente si lo dejas vacío)")]
    public Canvas targetCanvas;

    [Header("Opciones")]
    public int sortingOrder = 3000; // arriba de todo
    public bool forceEveryFrame = false; // ON si algún Animator lo revienta después

    void Awake()
    {
        if (!targetPanel) targetPanel = gameObject;
        Force();
    }

    void Update()
    {
        if (forceEveryFrame) Force();
        if (Input.GetKeyDown(KeyCode.F1)) Force(); // tecla para re-forzar manualmente
    }

    public void Force()
    {
        if (!targetPanel) { Debug.LogWarning("[UIForceShowPanel] No targetPanel."); return; }

        // 1) Garantizar Canvas Overlay funcional
        var panelCanvas = targetPanel.GetComponentInParent<Canvas>(true);
        if (!panelCanvas)
        {
            if (!targetCanvas)
            {
                var go = new GameObject("Canvas_Overlay_For_Panel", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                targetCanvas = go.GetComponent<Canvas>();
                targetCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                targetCanvas.sortingOrder = sortingOrder;
            }
            targetPanel.transform.SetParent(targetCanvas.transform, false);
            panelCanvas = targetCanvas;
        }
        else
        {
            // Si existe pero no es Overlay, lo paso a Overlay y orden alto
            panelCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            panelCanvas.worldCamera = null;
            panelCanvas.sortingOrder = sortingOrder;
        }
        panelCanvas.targetDisplay = 0;
        panelCanvas.gameObject.SetActive(true);
        panelCanvas.enabled = true;

        Debug.Log($"[UIForceShowPanel] Usando canvas: {panelCanvas.name} mode={panelCanvas.renderMode} order={panelCanvas.sortingOrder}");

        // 2) CanvasGroup visible y clickeable
        var cg = targetPanel.GetComponent<CanvasGroup>();
        if (!cg) cg = targetPanel.AddComponent<CanvasGroup>();
        cg.alpha = 1f;
        cg.interactable = true;
        cg.blocksRaycasts = true;

        // 3) RectTransform centrado, con escala válida
        var rt = targetPanel.GetComponent<RectTransform>();
        if (rt)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            if (rt.localScale.x == 0f || rt.localScale.y == 0f) rt.localScale = Vector3.one;
            if (rt.sizeDelta == Vector2.zero) rt.sizeDelta = new Vector2(900, 600);
        }

        // 4) Habilitar todos los gráficos y subir alpha de colores
        int offImgs = 0, offTxt = 0, lowAlpha = 0;
        var imgs = targetPanel.GetComponentsInChildren<Image>(true);
        foreach (var i in imgs)
        {
            if (!i.enabled) { i.enabled = true; offImgs++; }
            var c = i.color; if (c.a < 0.98f) { c.a = 1f; i.color = c; lowAlpha++; }
        }
        var tmps = targetPanel.GetComponentsInChildren<TMP_Text>(true);
        foreach (var t in tmps)
        {
            if (!t.enabled) { t.enabled = true; offTxt++; }
            var c = t.color; if (c.a < 0.98f) { c.a = 1f; t.color = c; lowAlpha++; }
        }

        // 5) Traerlo al frente
        targetPanel.transform.SetAsLastSibling();
        targetPanel.SetActive(true);

        Debug.Log($"[UIForceShowPanel] Panel={targetPanel.name} listo. Rehabilitadas: {offImgs} Image(s), {offTxt} TMP_Text(s), alphas corregidos={lowAlpha}.");
    }
}