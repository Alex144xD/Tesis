using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MenuController : MonoBehaviour
{
    [Header("Escenas")]
    public string tutorialScene = "Tutorial";
    public string customModeScene = "CustomMode";
    public string optionsScene = "Options";    

    [Header("Botones")]
    public Button btnPlay;
    public Button btnCustomMode;
    public Button btnOptions;
    public Button btnQuit;

    void Start()
    {
        if (btnPlay) btnPlay.onClick.AddListener(PlayTutorial);
        if (btnCustomMode) btnCustomMode.onClick.AddListener(StartCustomMode);
        if (btnOptions) btnOptions.onClick.AddListener(OpenOptions);
        if (btnQuit) btnQuit.onClick.AddListener(QuitGame);

        bool customUnlocked = PlayerPrefs.GetInt("CustomUnlocked", 0) == 1;
        if (btnCustomMode) btnCustomMode.interactable = customUnlocked;
    }

    void PlayTutorial()
    {
        SceneManager.LoadScene(tutorialScene);
    }

    void StartCustomMode()
    {
        SceneManager.LoadScene(customModeScene);
    }

    void OpenOptions()
    {
        SceneManager.LoadScene(optionsScene);
    }

    public static void UnlockCustomMode()
    {
        PlayerPrefs.SetInt("CustomUnlocked", 1);
        PlayerPrefs.Save();
    }

    void QuitGame()
    {
        Application.Quit();
    }
}