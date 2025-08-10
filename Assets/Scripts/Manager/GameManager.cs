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

    //  Modo personalizado
    private bool inCustomMode = false;

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

    private void Update()
    {
        // ❌ Evitar pausa si el jugador está muerto o ganó
        if (Input.GetKeyDown(pauseKey) && !isGameOver && !isVictory)
        {
            if (!isPaused)
                PauseGame();
            else
                ResumeGame();
        }
    }

    public void PlayerWin()
    {
        if (isVictory) return;

        isVictory = true;
        Debug.Log("Juego ganado");

        onVictory?.Invoke();
        UIManager.Instance.ShowVictoryScreen();
        FreezeGame();
    }

    public void PlayerLose()
    {
        if (isGameOver) return;

        isGameOver = true;
        Debug.Log("Jugador ha muerto");

        onDefeat?.Invoke();
        UIManager.Instance.ShowDefeatScreen();
        FreezeGame();
    }

    public void PauseGame()
    {
        if (isPaused) return; // evita doble disparo
        isPaused = true;
        Time.timeScale = 0f;

        // Cursor visible y libre
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

        if (UIManager.Instance) UIManager.Instance.ShowPauseScreen();
        else Debug.LogWarning("UIManager no encontrado al pausar.");
    }

    public void ResumeGame()
    {
        if (!isPaused) return;
        isPaused = false;
        Time.timeScale = 1f;

        // Cursor oculto/bloqueado (si así juegas normalmente)
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;

        if (UIManager.Instance) UIManager.Instance.HidePauseScreen();
    }

    public void RestartGame()
    {
        Time.timeScale = 1f;
        isPaused = false;
        isGameOver = false;
        isVictory = false;

        onGameRestart?.Invoke();
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    public void QuitGame()
    {
        Debug.Log("Saliendo del juego...");
        Application.Quit();
    }

    private void FreezeGame()
    {
        isPaused = true;
        Time.timeScale = 0f;
    }

    // Activar modo personalizado
    public void StartCustomMode()
    {
        inCustomMode = true;
    }

    // Verificar si estamos en modo personalizado
    public bool IsInCustomMode()
    {
        return inCustomMode;
    }
}
