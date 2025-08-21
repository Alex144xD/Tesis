using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance;

    [Header("Pantallas (instancias de la escena, NO el Canvas)")]
    public GameObject victoryScreen;
    public GameObject defeatScreen;
    public GameObject pauseScreen;

    [Header("HUD (opcional, NO el Canvas)")]
    public GameObject hudRoot;

    [Header("Opciones")]
    public bool persistAcrossScenes = true;

    [Header("Depuración")]
    public bool debugLogs = false;
    public int overlaySortingOrder = 800;

    [Header("Canvases protegidos (opcional)")]
    public Canvas[] guardedCanvases; // Si lo dejas vacío, se auto-llenará

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;

            if (persistAcrossScenes)
            {
                DontDestroyOnLoad(gameObject);
                SceneManager.sceneLoaded += OnSceneLoaded_Rebind;
            }

            EnsureEventSystem();

            // Validación inicial: que no hayan asignado un Canvas como "panel"
            ValidateNotCanvas("victoryScreen", victoryScreen);
            ValidateNotCanvas("defeatScreen", defeatScreen);
            ValidateNotCanvas("pauseScreen", pauseScreen);
            ValidateNotCanvas("hudRoot", hudRoot);

            SafeHide(victoryScreen);
            SafeHide(defeatScreen);
            SafeHide(pauseScreen);
            if (hudRoot) hudRoot.SetActive(true);

            AutoFillGuardedCanvasesIfEmpty();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void LateUpdate()
    {
        // Guardia: si algún script apagó un Canvas, lo reactivamos y lo reportamos
        if (guardedCanvases == null) return;
        for (int i = 0; i < guardedCanvases.Length; i++)
        {
            var c = guardedCanvases[i];
            if (!c) continue;

            if (!c.gameObject.activeInHierarchy)
            {
                Debug.LogWarning($"[UIManager] Canvas '{c.name}' estaba desactivado (GO). Reactivando.");
                c.gameObject.SetActive(true);
            }
            if (!c.enabled)
            {
                Debug.LogWarning($"[UIManager] Canvas '{c.name}' estaba disabled (component). Rehabilitando.");
                c.enabled = true;
            }
        }
    }

    // ---------- API ----------
    public void ShowVictoryScreen()
    {
        RebindIfMissing();
        HideAllExcept(victoryScreen);
        if (hudRoot) hudRoot.SetActive(false);
        ShowPanel(victoryScreen);
    }

    public void ShowDefeatScreen()
    {
        RebindIfMissing();
        HideAllExcept(defeatScreen);
        if (hudRoot) hudRoot.SetActive(false);
        ShowPanel(defeatScreen);
    }

    public void ShowPauseScreen()
    {
        RebindIfMissing();
        HideAllExcept(pauseScreen);
        if (hudRoot) hudRoot.SetActive(false);
        ShowPanel(pauseScreen);
    }

    public void HidePauseScreen()
    {
        RebindIfMissing();
        HidePanel(pauseScreen);
        if (hudRoot) hudRoot.SetActive(true);
    }

    // ---------- Helpers ----------
    void HideAllExcept(GameObject except)
    {
        if (victoryScreen && victoryScreen != except) HidePanel(victoryScreen);
        if (defeatScreen && defeatScreen != except) HidePanel(defeatScreen);
        if (pauseScreen && pauseScreen != except) HidePanel(pauseScreen);
    }

    void ShowPanel(GameObject panel)
    {
        if (!panel) return;
        if (IsCanvasRoot(panel))
        {
            Debug.LogError($"[UIManager] '{panel.name}' parece ser un Canvas. Asigna un hijo (el panel), no el Canvas.");
            return;
        }

        EnsureParentsActive(panel);
        ForceCanvasVisible(panel);
        ForceCanvasGroupVisible(panel);

        panel.SetActive(true);

        var pop = panel.GetComponent<UIPopIn>();
        if (pop) pop.Show();

        panel.transform.SetAsLastSibling();
    }

    void HidePanel(GameObject panel)
    {
        if (!panel) return;
        if (IsCanvasRoot(panel)) return; // nunca apagar Canvas

        var cg = panel.GetComponent<CanvasGroup>();
        if (cg)
        {
            cg.alpha = 0f;
            cg.interactable = false;
            cg.blocksRaycasts = false;
        }

        var pop = panel.GetComponent<UIPopIn>();
        if (pop) pop.HideImmediate();
        else panel.SetActive(false);
    }

    void SafeHide(GameObject panel)
    {
        if (!panel) return;
        if (IsCanvasRoot(panel)) return; // nunca apagar Canvas

        var cg = panel.GetComponent<CanvasGroup>();
        if (!cg) cg = panel.AddComponent<CanvasGroup>();
        cg.alpha = 0f;
        cg.interactable = false;
        cg.blocksRaycasts = false;

        panel.SetActive(false);
    }

    void EnsureEventSystem()
    {
        var es = FindObjectOfType<EventSystem>();
        if (!es)
        {
            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            if (persistAcrossScenes) DontDestroyOnLoad(go);
        }
        else if (!es.gameObject.activeInHierarchy)
        {
            es.gameObject.SetActive(true);
        }
    }

    void ForceCanvasVisible(GameObject panel)
    {
        var canvas = panel.GetComponentInParent<Canvas>(true);
        if (!canvas) return;

        // Overlay fijo (no usas cámara)
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.worldCamera = null;
        canvas.sortingOrder = overlaySortingOrder;
        canvas.targetDisplay = 0;
        canvas.enabled = true;
        canvas.gameObject.SetActive(true);
    }

    void ForceCanvasGroupVisible(GameObject panel)
    {
        var cg = panel.GetComponent<CanvasGroup>();
        if (!cg) cg = panel.AddComponent<CanvasGroup>();
        cg.alpha = 1f;
        cg.interactable = true;
        cg.blocksRaycasts = true;

        var parents = panel.GetComponentsInParent<CanvasGroup>(true);
        foreach (var p in parents)
        {
            p.alpha = 1f;
            p.interactable = true;
            p.blocksRaycasts = true;
        }
    }

    void EnsureParentsActive(GameObject go)
    {
        Transform t = go.transform;
        while (t != null)
        {
            if (!t.gameObject.activeSelf)
                t.gameObject.SetActive(true);
            t = t.parent;
        }
    }

    void OnSceneLoaded_Rebind(Scene s, LoadSceneMode mode)
    {
        RebindIfMissing();
        EnsureEventSystem();
        AutoFillGuardedCanvasesIfEmpty(); // por si cambian de escena
    }

    void RebindIfMissing()
    {
        if (!victoryScreen) victoryScreen = GameObject.Find("VictoryScreen");
        if (!defeatScreen) defeatScreen = GameObject.Find("DefeatScreen");
        if (!pauseScreen) pauseScreen = GameObject.Find("PauseScreen");
        if (!hudRoot) hudRoot = GameObject.Find("HUD");

        // Validar otra vez por si fueron reasignados mal
        ValidateNotCanvas("victoryScreen", victoryScreen);
        ValidateNotCanvas("defeatScreen", defeatScreen);
        ValidateNotCanvas("pauseScreen", pauseScreen);
        ValidateNotCanvas("hudRoot", hudRoot);
    }

    // ------------ Utilidades anti-errores ------------
    bool IsCanvasRoot(GameObject go)
    {
        if (!go) return false;
        // Consideramos Canvas raíz si tiene Canvas y NO tiene Canvas padre
        var c = go.GetComponent<Canvas>();
        if (!c) return false;
        var parentCanvas = go.GetComponentInParent<Canvas>(true);
        return c && (parentCanvas == c); // ese Canvas es el más alto de su cadena
    }

    void ValidateNotCanvas(string fieldName, GameObject go)
    {
        if (go && IsCanvasRoot(go))
        {
            Debug.LogError($"[UIManager] '{fieldName}' apunta al Canvas raíz ('{go.name}'). Debe apuntar al panel hijo, no al Canvas.");
        }
    }

    void AutoFillGuardedCanvasesIfEmpty()
    {
        if (guardedCanvases != null && guardedCanvases.Length > 0) return;
        guardedCanvases = FindObjectsOfType<Canvas>(true);
        if (debugLogs)
            Debug.Log($"[UIManager] Canvases protegidos: {guardedCanvases.Length}");
    }
}
