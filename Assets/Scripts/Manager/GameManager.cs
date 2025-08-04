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
        isPaused = true;
        Time.timeScale = 0f;
        UIManager.Instance.ShowPauseScreen();
    }

    public void ResumeGame()
    {
        isPaused = false;
        Time.timeScale = 1f;
        UIManager.Instance.HidePauseScreen();
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
}