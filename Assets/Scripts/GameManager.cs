using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    [Header("Estado del juego")]
    public bool isPaused = false;
    public bool isGameOver = false;
    public bool isVictory = false;

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

    public void PlayerWin()
    {
        if (isVictory) return;

        isVictory = true;
        Debug.Log("Juego ganado");

        // TODO: Mostrar pantalla de victoria
        // UIManager.Instance.ShowVictoryScreen();
        PauseGame();
    }

    public void PlayerLose()
    {
        if (isGameOver) return;

        isGameOver = true;
        Debug.Log("Jugador ha muerto");

        // TODO: Mostrar pantalla de derrota
        // UIManager.Instance.ShowDefeatScreen();
        PauseGame();
    }

    public void PauseGame()
    {
        isPaused = true;
        Time.timeScale = 0f;
    }

    public void ResumeGame()
    {
        isPaused = false;
        Time.timeScale = 1f;
    }

    public void RestartGame()
    {
        Time.timeScale = 1f;
        isPaused = false;
        isGameOver = false;
        isVictory = false;

        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    public void QuitGame()
    {
        Debug.Log("Saliendo del juego...");
        Application.Quit();
    }
}