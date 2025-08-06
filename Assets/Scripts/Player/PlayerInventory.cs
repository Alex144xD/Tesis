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
    private FragmentHUD fragmentHUD;

    void Start()
    {
        gameManager = FindObjectOfType<GameManager>();
        mapManager = FindObjectOfType<MultiFloorDynamicMapManager>();
        fragmentHUD = FindObjectOfType<FragmentHUD>();

        if (mapManager != null)
        {
            totalLevels = mapManager.floors;
        }

        ResetInventory();

        // Mostrar u ocultar HUD de fragmentos según modo
        if (fragmentHUD != null)
        {
            if (GameManager.Instance.IsInCustomMode())
            {
                fragmentHUD.gameObject.SetActive(true);
                fragmentHUD.UpdateFragmentCount(0); // Mostrar desde 0
            }
            else
            {
                fragmentHUD.gameObject.SetActive(false);
            }
        }
    }

    public void AddSoulFragment()
    {
        soulFragmentsCollected++;
        Debug.Log($"Fragmentos recogidos: {soulFragmentsCollected}/{totalLevels}");

        onFragmentCollected?.Invoke(soulFragmentsCollected, totalLevels);

        // 🔁 Actualizar HUD visual si existe
        if (fragmentHUD != null && GameManager.Instance.IsInCustomMode())
        {
            fragmentHUD.UpdateFragmentCount(soulFragmentsCollected);
        }

        if (soulFragmentsCollected < totalLevels)
        {
            if (mapManager != null)
                mapManager.GoToNextFloor();
            else
                Debug.LogWarning("No se encontró el MapManager para cambiar de piso.");
        }
        else
        {
            Debug.Log("¡Has ganado el juego!");
            if (gameManager != null)
                gameManager.PlayerWin();
        }
    }

    public void ResetInventory()
    {
        soulFragmentsCollected = 0;
    }
}