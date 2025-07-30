using UnityEngine;

public class PlayerInventory : MonoBehaviour
{
    [Header("Progreso del jugador")]
    public int soulFragmentsCollected = 0;
    public int totalLevels; // Se asigna dinámicamente desde el MapManager

    private GameManager gameManager;

    void Start()
    {
        // Buscar GameManager en la escena
        gameManager = FindObjectOfType<GameManager>();

        // Resetear fragmentos al iniciar
        soulFragmentsCollected = 0;
    }

    public void AddSoulFragment()
    {
        soulFragmentsCollected++;
        Debug.Log($"Fragmentos recogidos: {soulFragmentsCollected}/{totalLevels}");

        // Si no es el último fragmento → cambiar de piso
        if (soulFragmentsCollected < totalLevels)
        {
            MultiFloorDynamicMapManager.Instance.GoToNextFloor();
        }
        else
        {
            Debug.Log("¡Has ganado el juego!");
            if (gameManager != null)
            {
                gameManager.PlayerWin();
            }
        }
    }

    public void ResetInventory()
    {
        soulFragmentsCollected = 0;
    }
}