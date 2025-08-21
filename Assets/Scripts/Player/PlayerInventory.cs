using UnityEngine;
using UnityEngine.Events;

public class PlayerInventory : MonoBehaviour
{
    [Header("Progreso del jugador")]
    public int soulFragmentsCollected = 0;
    public int totalLevels; // Se asigna dinámicamente desde el MapManager

    [Header("Eventos")]
    public UnityEvent<int, int> onFragmentCollected; // (actual, total)

    // --- Baterías / Integración ---
    [Header("Baterías")]
    public PlayerBatterySystem batterySystem; // se auto-asigna en Start si es null

    [Tooltip("Guardar cargas de baterías y fragmentos en PlayerPrefs")]
    public bool usePersistence = true;

    // Eventos opcionales por si quieres enganchar HUD/toasts
    [System.Serializable] public class BatteryChangedEvent : UnityEvent<BatteryType, float, float> { } // (tipo, actual, max)
    public BatteryChangedEvent onBatteryChanged;

    private GameManager gameManager;
    private MultiFloorDynamicMapManager mapManager;
    private FragmentHUD fragmentHUD;

    // --- Claves PlayerPrefs ---
    const string KEY_FRAGMENTS = "INV_Fragments";
    const string KEY_BAT_G = "INV_BatGreen";
    const string KEY_BAT_R = "INV_BatRed";
    const string KEY_BAT_B = "INV_BatBlue";

    void Start()
    {
        gameManager = FindObjectOfType<GameManager>();
        mapManager = FindObjectOfType<MultiFloorDynamicMapManager>();
        fragmentHUD = FindObjectOfType<FragmentHUD>();

        // Autoconectar sistema de baterías si vive en el mismo GO
        if (batterySystem == null)
            batterySystem = GetComponent<PlayerBatterySystem>();

        // Tomar total de niveles desde el MapManager si existe
        if (mapManager != null)
            totalLevels = mapManager.floors;
        // Evitar 0 (seguro por si el manager no estaba listo)
        totalLevels = Mathf.Max(totalLevels, 1);

        // Carga inicial (o reset si no queremos persistir)
        if (usePersistence) LoadState();
        else ResetInventory();

        // HUD de fragmentos según modo (opcional)
        if (fragmentHUD != null)
        {
            if (GameManager.Instance != null && GameManager.Instance.IsInCustomMode())
            {
                fragmentHUD.gameObject.SetActive(true);
                fragmentHUD.UpdateFragmentCount(soulFragmentsCollected);
            }
            else
            {
                fragmentHUD.gameObject.SetActive(false);
            }
        }

        // Notificar HUD de baterías si usas el flujo por eventos (opcional)
        FireBatteryEvents();
    }

    // ------------------- Fragmentos -------------------

    public void AddSoulFragment()
    {
        soulFragmentsCollected++;
        Debug.Log($"Fragmentos recogidos: {soulFragmentsCollected}/{totalLevels}");

        // Evento opcional para HUD u otros sistemas
        onFragmentCollected?.Invoke(soulFragmentsCollected, totalLevels);

        if (fragmentHUD != null && GameManager.Instance != null && GameManager.Instance.IsInCustomMode())
            fragmentHUD.UpdateFragmentCount(soulFragmentsCollected);

        if (usePersistence) SaveFragments();

        if (soulFragmentsCollected < totalLevels)
        {
            if (mapManager != null) mapManager.GoToNextFloor();
            else Debug.LogWarning("No se encontró el MapManager para cambiar de piso.");
        }
        else
        {
            Debug.Log("¡Has ganado el juego!");
            // Fallbacks por si gameManager no está referenciado
            if (gameManager != null) gameManager.PlayerWin();
            else if (GameManager.Instance != null) GameManager.Instance.PlayerWin();
            else Debug.LogWarning("No se encontró GameManager para procesar la victoria.");
        }
    }

    public void ResetInventory()
    {
        soulFragmentsCollected = 0;
        // Si quieres resetear baterías también (opcional)
        // ResetBatteriesToStart();
    }

    // ------------------- Baterías (API de inventario) -------------------

    /// <summary>Recarga una batería por tipo (p.ej. desde un pickup).</summary>
    public void AddBatteryCharge(BatteryType type, float amount)
    {
        if (batterySystem == null) return;

        batterySystem.Recharge(type, amount);
        if (usePersistence) SaveBatteries();
        FireBatteryEvents(type); // opcional
    }

    /// <summary>Cambia el tipo activo (puedes llamarlo si haces cambio desde UI).</summary>
    public void SetActiveBattery(BatteryType type)
    {
        if (batterySystem == null) return;
        batterySystem.SetActive(type);
        FireBatteryEvents(type); // opcional
    }

    /// <summary>Devuelve (actual, max) de un tipo.</summary>
    public (float current, float max) GetBatteryInfo(BatteryType type)
    {
        if (batterySystem == null) return (0f, 1f);
        return (batterySystem.GetCharge(type), batterySystem.GetMax(type));
    }

    // ------------------- Persistencia -------------------

    public void SaveFragments()
    {
        if (!usePersistence) return;
        PlayerPrefs.SetInt(KEY_FRAGMENTS, soulFragmentsCollected);
        PlayerPrefs.Save();
    }

    public void SaveBatteries()
    {
        if (!usePersistence || batterySystem == null) return;

        PlayerPrefs.SetFloat(KEY_BAT_G, batterySystem.curGreen);
        PlayerPrefs.SetFloat(KEY_BAT_R, batterySystem.curRed);
        PlayerPrefs.SetFloat(KEY_BAT_B, batterySystem.curBlue);
        PlayerPrefs.Save();
    }

    public void LoadState()
    {
        // Fragmentos
        soulFragmentsCollected = PlayerPrefs.GetInt(KEY_FRAGMENTS, 0);

        // Baterías
        if (batterySystem != null)
        {
            // Si no existen claves, se respetan los valores iniciales del PlayerBatterySystem
            if (PlayerPrefs.HasKey(KEY_BAT_G)) batterySystem.curGreen = Mathf.Clamp(PlayerPrefs.GetFloat(KEY_BAT_G), 0f, batterySystem.maxGreen);
            if (PlayerPrefs.HasKey(KEY_BAT_R)) batterySystem.curRed = Mathf.Clamp(PlayerPrefs.GetFloat(KEY_BAT_R), 0f, batterySystem.maxRed);
            if (PlayerPrefs.HasKey(KEY_BAT_B)) batterySystem.curBlue = Mathf.Clamp(PlayerPrefs.GetFloat(KEY_BAT_B), 0f, batterySystem.maxBlue);
        }
    }

    // Opcional: restaurar las cargas a los valores "start" del PlayerBatterySystem
    public void ResetBatteriesToStart()
    {
        if (batterySystem == null) return;
        batterySystem.curGreen = Mathf.Clamp(batterySystem.startGreen, 0, batterySystem.maxGreen);
        batterySystem.curRed = Mathf.Clamp(batterySystem.startRed, 0, batterySystem.maxRed);
        batterySystem.curBlue = Mathf.Clamp(batterySystem.startBlue, 0, batterySystem.maxBlue);
    }

    void OnApplicationQuit()
    {
        if (usePersistence)
        {
            SaveFragments();
            SaveBatteries();
        }
    }

    void OnDestroy()
    {
        if (usePersistence)
        {
            SaveFragments();
            SaveBatteries();
        }
    }

    // ------------------- Utilidades internas -------------------

    private void FireBatteryEvents()
    {
        FireBatteryEvents(BatteryType.Green);
        FireBatteryEvents(BatteryType.Red);
        FireBatteryEvents(BatteryType.Blue);
    }

    private void FireBatteryEvents(BatteryType type)
    {
        if (onBatteryChanged == null || batterySystem == null) return;
        onBatteryChanged.Invoke(type, batterySystem.GetCharge(type), batterySystem.GetMax(type));
    }
}
