using UnityEngine;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance;

    [Header("Pantallas")]
    public GameObject victoryScreen;
    public GameObject defeatScreen;
    public GameObject pauseScreen;

    [Header("HUD (opcional)")]
    public GameObject hudRoot; // puede ser null

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else { Destroy(gameObject); return; }

        SafeHide(victoryScreen);
        SafeHide(defeatScreen);
        SafeHide(pauseScreen);
        if (hudRoot) hudRoot.SetActive(true);
    }

    public void ShowVictoryScreen()
    {
        HideAllExcept(victoryScreen);
        if (hudRoot) hudRoot.SetActive(false);
        ShowWithPop(victoryScreen);
    }

    public void ShowDefeatScreen()
    {
        HideAllExcept(defeatScreen);
        if (hudRoot) hudRoot.SetActive(false);
        ShowWithPop(defeatScreen);
    }

    public void ShowPauseScreen()
    {
        HideAllExcept(pauseScreen);
        if (hudRoot) hudRoot.SetActive(false);
        ShowWithPop(pauseScreen);
    }

    public void HidePauseScreen()   // <-- existe y es PUBLIC
    {
        HidePanel(pauseScreen);
        if (hudRoot) hudRoot.SetActive(true);
    }

    // -------- Helpers --------
    void HideAllExcept(GameObject except)
    {
        if (victoryScreen && victoryScreen != except) HidePanel(victoryScreen);
        if (defeatScreen && defeatScreen != except) HidePanel(defeatScreen);
        if (pauseScreen && pauseScreen != except) HidePanel(pauseScreen);
    }

    void ShowWithPop(GameObject panel)
    {
        if (!panel) { Debug.LogWarning("Panel nulo en ShowWithPop"); return; }

        // Fuerza activo antes de animar
        panel.SetActive(true);

        var cg = panel.GetComponent<CanvasGroup>();
        if (!cg) cg = panel.AddComponent<CanvasGroup>();
        cg.alpha = 0f;

        var pop = panel.GetComponent<UIPopIn>();
        if (!pop) pop = panel.AddComponent<UIPopIn>();
        pop.Show();
    }

    void HidePanel(GameObject panel)
    {
        if (!panel) return;
        var pop = panel.GetComponent<UIPopIn>();
        if (pop) pop.HideImmediate();
        else panel.SetActive(false);
    }

    void SafeHide(GameObject panel)
    {
        if (!panel) return;
        var cg = panel.GetComponent<CanvasGroup>();
        if (!cg) cg = panel.AddComponent<CanvasGroup>();
        cg.alpha = 0f;
        panel.SetActive(false);
    }
}