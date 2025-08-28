using UnityEngine;
using UnityEngine.SceneManagement;

public class UIHub : MonoBehaviour
{
    [Header("Panels (asignar si existen en la escena)")]
    public GameObject customModePanel;   // Panel del Custom Mode (historia)
    public GameObject optionsPanel;      // Panel de Opciones (si no lo asignas, se buscará por nombre)
    public GameObject pausePanel;        // Panel de Pausa

    [Header("Scenes")]
    public string tutorialScene = "Tutorial"; // Nombre exacto de tu escena de tutorial
    public string mainMenuScene = "MainMenu"; // Nombre exacto de tu escena de menú

    [Header("Opciones (fallback por nombre)")]
    public string optionsPanelName = "OptionsPanel";
    public bool bringOptionsToFront = true;

    // ================== MENÚ PRINCIPAL ==================

    // Botón: Empezar / Tutorial
    public void OnPlayTutorial()
    {
        if (!string.IsNullOrEmpty(tutorialScene))
            SceneManager.LoadScene(tutorialScene);
    }

    // Botón: Custom Mode (abre el panel)
    public void OnOpenCustomMode()
    {
        if (customModePanel) customModePanel.SetActive(true);
    }

    // Botón: Opciones (MENÚ o PAUSA) → abre el mismo panel, sin tocar timeScale
    public void OnOpenOptions()
    {
        var panel = ResolveOptionsPanel();
        if (!panel) return;

        panel.SetActive(true);
        if (bringOptionsToFront)
        {
            var rt = panel.transform as RectTransform;
            if (rt) rt.SetAsLastSibling();
        }
    }

    // Botón dentro del panel de Opciones: Volver (cierra opciones)
    public void OnCloseOptions()
    {
        var panel = ResolveOptionsPanel();
        if (!panel) return;

        panel.SetActive(false);
    }

    // Botón: Salir del juego
    public void OnQuit()
    {
        Application.Quit();
#if UNITY_EDITOR
        Debug.Log("[UIFlowSimple] Quit (Editor)");
#endif
    }

    // ================== PAUSA (EN JUEGO) ==================

    // Botón: Continuar (cierra pausa y reanuda)
    public void OnPauseContinue()
    {
        if (GameManager.Instance) GameManager.Instance.ResumeGame();
        else Time.timeScale = 1f;

        if (pausePanel) pausePanel.SetActive(false);
    }

    // (Opcional) Abrir el panel de pausa desde código
    public void OnOpenPause()
    {
        if (GameManager.Instance) GameManager.Instance.PauseGame();
        else Time.timeScale = 0f;

        if (pausePanel) pausePanel.SetActive(true);
    }

    // ================== REINTENTAR / MENÚ ==================

    // Botón: Reintentar (sirve para pausa/derrota/etc.)
    public void OnRetry()
    {
        Time.timeScale = 1f;
        if (GameManager.Instance)
            GameManager.Instance.RestartGame();
        else
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    // Botón: Ir al Menú (sirve para pausa/victoria/derrota/etc.)
    public void OnGoToMenu()
    {
        Time.timeScale = 1f;
        if (!string.IsNullOrEmpty(mainMenuScene))
            SceneManager.LoadScene(mainMenuScene);
    }

    // ======== Helper Opciones ========
    GameObject ResolveOptionsPanel()
    {
        if (optionsPanel) return optionsPanel;

        var found = GameObject.Find(optionsPanelName);
        if (!found)
        {
            Debug.LogWarning("[UIHub] No se encontró un GameObject llamado '" + optionsPanelName + "'. Asigna 'optionsPanel' o corrige el nombre.");
            return null;
        }
        optionsPanel = found; // cache
        return optionsPanel;
    }
}