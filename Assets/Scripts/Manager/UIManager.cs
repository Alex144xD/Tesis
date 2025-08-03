using UnityEngine;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance;

    [Header("Pantallas")]
    public GameObject victoryScreen;
    public GameObject defeatScreen;
    public GameObject pauseScreen;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public void ShowVictoryScreen()
    {
        HideAllScreens();
        if (victoryScreen != null)
            victoryScreen.SetActive(true);
    }

    public void ShowDefeatScreen()
    {
        HideAllScreens();
        if (defeatScreen != null)
            defeatScreen.SetActive(true);
    }

    public void ShowPauseScreen()
    {
        HideAllScreens();
        if (pauseScreen != null)
            pauseScreen.SetActive(true);
    }

    public void HidePauseScreen()
    {
        if (pauseScreen != null)
            pauseScreen.SetActive(false);
    }

    private void HideAllScreens()
    {
        if (victoryScreen != null) victoryScreen.SetActive(false);
        if (defeatScreen != null) defeatScreen.SetActive(false);
        if (pauseScreen != null) pauseScreen.SetActive(false);
    }
}