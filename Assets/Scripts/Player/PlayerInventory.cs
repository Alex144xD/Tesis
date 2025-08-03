using UnityEngine;
using UnityEngine.Events;

public class PlayerInventory : MonoBehaviour
{
    [Header("Progreso del jugador")]
    public int soulFragmentsCollected = 0;
    public int totalLevels; // Se asigna dinámicamente desde el MapManager

    [Header("Eventos")]
    public UnityEvent<int, int> onFragmentCollected; // Fragmentos actuales, total

    private GameManager gameManager;
    private MultiFloorDynamicMapManager mapManager;

    void Start()
    {
        // Buscar GameManager en la escena
        gameManager = FindObjectOfType<GameManager>();

        // Obtener referencia del MapManager
        mapManager = FindObjectOfType<MultiFloorDynamicMapManager>();
        if (mapManager != null)
        {
            totalLevels = mapManager.floors; // Ajustar automáticamente
        }

        ResetInventory();
    }

    public void AddSoulFragment()
    {
        soulFragmentsCollected++;
        Debug.Log($"Fragmentos recogidos: {soulFragmentsCollected}/{totalLevels}");

        // Disparar evento para HUD u otros sistemas
        onFragmentCollected?.Invoke(soulFragmentsCollected, totalLevels);

        // Si no es el último fragmento → cambiar de piso
        if (soulFragmentsCollected < totalLevels)
        {
            if (mapManager != null)
            {
                mapManager.GoToNextFloor();
            }
            else
            {
                Debug.LogWarning("No se encontró el MapManager para cambiar de piso.");
            }
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