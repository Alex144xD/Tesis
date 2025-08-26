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
    public Canvas[] guardedCanvases; 
    [Header("HUD auto ON por escena (nombres exactos, case-insensitive)")]
    public string[] hudEnabledScenes = new string[] { "Game", "Tutorial" };

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

            ValidateNotCanvas("victoryScreen", victoryScreen);
            ValidateNotCanvas("defeatScreen", defeatScreen);
            ValidateNotCanvas("pauseScreen", pauseScreen);
            ValidateNotCanvas("hudRoot", hudRoot);

            SafeHide(victoryScreen);
            SafeHide(defeatScreen);
            SafeHide(pauseScreen);

            AutoFillGuardedCanvasesIfEmpty();


            RebindIfMissing();
            ApplyHUDVisibilityForScene(SceneManager.GetActiveScene());
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void LateUpdate()
    {

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

    public void PreSceneChangeCleanup()
    {
        HidePanel(victoryScreen);
        HidePanel(defeatScreen);
        HidePanel(pauseScreen);
    }

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

        ApplyHUDVisibilityForScene(SceneManager.GetActiveScene());
    }

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
        AutoFillGuardedCanvasesIfEmpty(); 

  
        SafeHide(victoryScreen);
        SafeHide(defeatScreen);
        SafeHide(pauseScreen);

 
        ApplyHUDVisibilityForScene(s);
    }

    void RebindIfMissing()
    {
        if (!victoryScreen) victoryScreen = GameObject.Find("VictoryScreen");
        if (!defeatScreen) defeatScreen = GameObject.Find("DefeatScreen");
        if (!pauseScreen) pauseScreen = GameObject.Find("PauseScreen");

     
        if (!hudRoot)
        {
            hudRoot = GameObject.Find("HUD");
            if (!hudRoot) hudRoot = GameObject.Find("GameplayHUD");             
            if (!hudRoot) hudRoot = GameObject.FindWithTag("HUD");              
            if (!hudRoot) hudRoot = GameObject.FindWithTag("GameplayHUD");      
        }


        ValidateNotCanvas("victoryScreen", victoryScreen);
        ValidateNotCanvas("defeatScreen", defeatScreen);
        ValidateNotCanvas("pauseScreen", pauseScreen);
        ValidateNotCanvas("hudRoot", hudRoot);
    }

    // ------------ Utilidades anti-errores ------------
    bool IsCanvasRoot(GameObject go)
    {
        if (!go) return false;
        var c = go.GetComponent<Canvas>();
        if (!c) return false;
        var parentCanvas = go.GetComponentInParent<Canvas>(true);
        return c && (parentCanvas == c);
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

  
    void ApplyHUDVisibilityForScene(Scene s)
    {
        RebindIfMissing(); 

        if (!hudRoot) return;

        bool show = IsHudEnabledForSceneName(s.name);
        if (debugLogs)
            Debug.Log($"[UIManager] Escena '{s.name}' -> HUD {(show ? "ON" : "OFF")}");

        hudRoot.SetActive(show);

        if (show)
        {
            var canvas = hudRoot.GetComponent<Canvas>();
            if (canvas != null && !canvas.enabled) canvas.enabled = true;
        }
    }

    bool IsHudEnabledForSceneName(string sceneName)
    {
        if (hudEnabledScenes == null || hudEnabledScenes.Length == 0) return false;
        for (int i = 0; i < hudEnabledScenes.Length; i++)
        {
            var target = hudEnabledScenes[i];
            if (string.IsNullOrEmpty(target)) continue;
            if (string.Equals(sceneName, target, System.StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}