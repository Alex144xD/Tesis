using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Events;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    [Header("Estado del juego")]
    public bool isPaused = false;
    public bool isGameOver = false;
    public bool isVictory = false;

    [Header("Eventos")]
    public UnityEvent onVictory;
    public UnityEvent onDefeat;
    public UnityEvent onGameRestart;

    [Header("Controles")]
    public KeyCode pauseKey = KeyCode.Escape;

    private bool inCustomMode = false;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += OnSceneLoaded_Reset;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            SceneManager.sceneLoaded -= OnSceneLoaded_Reset;
            Instance = null;
        }
    }

    void Update()
    {
        // Evita pausar si ya terminó la partida
        if (Input.GetKeyDown(pauseKey) && !isGameOver && !isVictory)
        {
            if (!isPaused) PauseGame();
            else ResumeGame();
        }
    }

    public void PlayerWin()
    {
        if (isVictory) return;
        isVictory = true;
        onVictory?.Invoke();

        if (UIManager.Instance) UIManager.Instance.ShowVictoryScreen();
        FreezeGame();
        ShowCursor(true);
    }

    public void PlayerLose()
    {
        if (isGameOver) return;
        isGameOver = true;
        onDefeat?.Invoke();

        if (UIManager.Instance) UIManager.Instance.ShowDefeatScreen();
        FreezeGame();
        ShowCursor(true);
    }

    public void PauseGame()
    {
        if (isPaused) return;
        isPaused = true;

        if (UIManager.Instance) UIManager.Instance.ShowPauseScreen();

        Time.timeScale = 0f;
        ShowCursor(true);
    }

    public void ResumeGame()
    {
        if (!isPaused) return;
        isPaused = false;

        Time.timeScale = 1f;

        if (UIManager.Instance) UIManager.Instance.HidePauseScreen();
        ShowCursor(false);
    }

    public void RestartGame()
    {
        // Estado limpio antes del reload
        Time.timeScale = 1f;
        isPaused = false;
        isGameOver = false;
        isVictory = false;

        // ========== NUEVO: apagar paneles ANTES de cargar la escena ==========
        if (UIManager.Instance) UIManager.Instance.PreSceneChangeCleanup();

        onGameRestart?.Invoke();
        Scene current = SceneManager.GetActiveScene();
        SceneManager.LoadScene(current.name);
    }

    public void QuitGame()
    {
        Application.Quit();
    }

    private void FreezeGame()
    {
        isPaused = true;
        Time.timeScale = 0f;
    }

    public void StartCustomMode()
    {
        inCustomMode = true;

        // Estado consistente para gameplay
        isPaused = false;
        isGameOver = false;
        isVictory = false;

        Time.timeScale = 1f;
        ShowCursor(false);
    }

    public bool IsInCustomMode() => inCustomMode;

    public void ExitCustomMode()
    {
        inCustomMode = false;
    }

    void ShowCursor(bool visible)
    {
        Cursor.visible = visible;
        Cursor.lockState = visible ? CursorLockMode.None : CursorLockMode.Locked;
    }

    void OnSceneLoaded_Reset(Scene s, LoadSceneMode mode)
    {
        // Si acabamos de ganar/perder, respeta el freeze de sus pantallas
        if (!isVictory && !isGameOver)
        {
            if (inCustomMode)
            {
                // En Custom Mode siempre arrancamos "jugando"
                isPaused = false;
                Time.timeScale = 1f;
                ShowCursor(false);

                if (CustomModeRuntime.Instance && CustomModeRuntime.Instance.ActiveProfile)
                    CustomModeRuntime.Instance.ApplyToCurrentScene();
            }
            else
            {
                Time.timeScale = isPaused ? 0f : 1f;
                ShowCursor(isPaused);
            }
        }
    }
}