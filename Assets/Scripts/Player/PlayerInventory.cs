using UnityEngine;
using UnityEngine.Events;
using System.Collections;

public class PlayerInventory : MonoBehaviour
{
    [Header("Progreso del jugador")]
    public int soulFragmentsCollected = 0;

    [Tooltip("Se mantiene por compatibilidad. Informativo.")]
    public int totalLevels;

    [Header("Eventos")]
    public UnityEvent<int, int> onFragmentCollected; // (current, required)

    [Header("Baterías")]
    public PlayerBatterySystem batterySystem;

    [Tooltip("Guardar cargas de baterías y fragmentos en PlayerPrefs")]
    public bool usePersistence = true;

    [Tooltip("Si está activo, al iniciar se limpia el conteo de fragmentos guardado para evitar estados viejos.")]
    public bool resetFragmentsOnNewRun = true;

    [System.Serializable] public class BatteryChangedEvent : UnityEvent<BatteryType, float, float> { }
    public BatteryChangedEvent onBatteryChanged;

    private GameManager gameManager;
    private MultiFloorDynamicMapManager mapManager;
    private FragmentHUD fragmentHUD;

    // --- Reglas de victoria ---
    [Tooltip("Fragmentos requeridos para ganar. Si el mapa usa modo secuencial, se sincroniza con targetFragments.")]
    [SerializeField] private int requiredFragmentsToWin = 1;
    private bool fragmentsOverrideActive = false;

    const string KEY_FRAGMENTS = "INV_Fragments";
    const string KEY_BAT_G = "INV_BatGreen";
    const string KEY_BAT_R = "INV_BatRed";
    const string KEY_BAT_B = "INV_BatBlue";

    private void Awake()
    {
        gameManager = FindObjectOfType<GameManager>();
        mapManager = FindObjectOfType<MultiFloorDynamicMapManager>();
        fragmentHUD = FindObjectOfType<FragmentHUD>();

        if (batterySystem == null)
            batterySystem = GetComponent<PlayerBatterySystem>();
    }

    private void OnEnable()
    {
        if (mapManager != null)
            mapManager.OnMapUpdated += HandleMapUpdated;
    }

    private void OnDisable()
    {
        if (mapManager != null)
            mapManager.OnMapUpdated -= HandleMapUpdated;
    }

    private void Start()
    {
        // Informativo
        if (mapManager != null)
            totalLevels = Mathf.Max(mapManager.floors, 1);
        else
            totalLevels = Mathf.Max(totalLevels, 1);

        // Estado persistente
        if (usePersistence)
        {
            if (resetFragmentsOnNewRun)
            {
                PlayerPrefs.DeleteKey(KEY_FRAGMENTS);
                PlayerPrefs.Save();
                soulFragmentsCollected = 0;
            }
            else
            {
                LoadState();
            }
        }
        else
        {
            ResetInventory();
        }

        // Sincronizar requisito con el sistema activo
        if (!fragmentsOverrideActive)
            RecomputeRequiredFragmentsToWin();

        StartCoroutine(DeferredSyncRequiredFragments());

        // HUD siempre visible + sincronizado
        SetupOrRefreshHUD();

        // Inicializar eventos de batería para HUDs
        FireBatteryEvents();

        Debug.Log($"[PlayerInventory.Start] floors={totalLevels} required={requiredFragmentsToWin} override={fragmentsOverrideActive}");
    }

    private IEnumerator DeferredSyncRequiredFragments()
    {
        // esperar 1 frame a que todo esté listo
        yield return null;

        if (!fragmentsOverrideActive)
        {
            int before = requiredFragmentsToWin;
            RecomputeRequiredFragmentsToWin();

            if (requiredFragmentsToWin != before)
            {
                Debug.Log($"[PlayerInventory] Deferred sync updated required {before} -> {requiredFragmentsToWin}");
                SetupOrRefreshHUD();
            }
        }
    }

    private void OnApplicationQuit()
    {
        if (usePersistence)
        {
            SaveFragments();
            SaveBatteries();
        }
    }

    // ================== LÓGICA DE FRAGMENTOS ==================

    public void AddSoulFragment()
    {
        soulFragmentsCollected = Mathf.Max(0, soulFragmentsCollected + 1);

        // Asegurar total requerido correcto (por si cambió el perfil o el mapa)
        if (!fragmentsOverrideActive)
            RecomputeRequiredFragmentsToWin();

        Debug.Log($"[PlayerInventory] Fragmentos: {soulFragmentsCollected}/{requiredFragmentsToWin}");

        // Dispara evento (HUD y otros)
        onFragmentCollected?.Invoke(soulFragmentsCollected, requiredFragmentsToWin);

        // HUD
        if (fragmentHUD != null)
            fragmentHUD.UpdateFragmentProgress(soulFragmentsCollected, requiredFragmentsToWin);

        // Persistencia
        if (usePersistence) SaveFragments();

        // ¿Ganó?
        if (soulFragmentsCollected >= requiredFragmentsToWin)
        {
            Debug.Log("[PlayerInventory] ¡Has ganado el juego!");
            if (gameManager != null) gameManager.PlayerWin();
            else if (GameManager.Instance != null) GameManager.Instance.PlayerWin();
            else Debug.LogWarning("[PlayerInventory] No se encontró GameManager para procesar la victoria.");
            return;
        }

        // Aún faltan fragmentos:
        // - Si hay sistema secuencial, pedir al Map que spawnee el siguiente fragmento lejos y regenere camino.
        // - Si es sistema legacy por pisos, usar GoToNextFloor (compat).
        if (mapManager != null && mapManager.useSequentialFragments)
        {
            mapManager.OnFragmentCollected(); // spawnea siguiente fragmento y regenera respetando anclas
        }
        else
        {
            if (mapManager != null) mapManager.GoToNextFloor(); // compatibilidad legacy
            else Debug.LogWarning("[PlayerInventory] No hay MapManager para avanzar (legacy).");
        }
    }

    public void ResetInventory()
    {
        soulFragmentsCollected = 0;
    }

    // ================== BATERÍAS ==================

    public void AddBatteryCharge(BatteryType type, float amount)
    {
        if (batterySystem == null) return;

        batterySystem.Recharge(type, amount);
        if (usePersistence) SaveBatteries();
        FireBatteryEvents(type);
    }

    public void SetActiveBattery(BatteryType type)
    {
        if (batterySystem == null) return;
        batterySystem.SetActive(type);
        FireBatteryEvents(type);
    }

    public (float current, float max) GetBatteryInfo(BatteryType type)
    {
        if (batterySystem == null) return (0f, 1f);
        return (batterySystem.GetCharge(type), batterySystem.GetMax(type));
    }

    // ================== PERSISTENCIA ==================

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
            if (PlayerPrefs.HasKey(KEY_BAT_G)) batterySystem.curGreen = Mathf.Clamp(PlayerPrefs.GetFloat(KEY_BAT_G), 0f, batterySystem.maxGreen);
            if (PlayerPrefs.HasKey(KEY_BAT_R)) batterySystem.curRed = Mathf.Clamp(PlayerPrefs.GetFloat(KEY_BAT_R), 0f, batterySystem.maxRed);
            if (PlayerPrefs.HasKey(KEY_BAT_B)) batterySystem.curBlue = Mathf.Clamp(PlayerPrefs.GetFloat(KEY_BAT_B), 0f, batterySystem.maxBlue);
        }
    }

    public void ResetBatteriesToStart()
    {
        if (batterySystem == null) return;
        batterySystem.curGreen = Mathf.Clamp(batterySystem.startGreen, 0, batterySystem.maxGreen);
        batterySystem.curRed = Mathf.Clamp(batterySystem.startRed, 0, batterySystem.maxRed);
        batterySystem.curBlue = Mathf.Clamp(batterySystem.startBlue, 0, batterySystem.maxBlue);
    }

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

    // ================== SINCRONIZACIONES ==================

    private void HandleMapUpdated()
    {
        if (!fragmentsOverrideActive)
        {
            int before = requiredFragmentsToWin;
            RecomputeRequiredFragmentsToWin();
            if (requiredFragmentsToWin != before)
                Debug.Log($"[PlayerInventory] MapUpdated -> required {before} -> {requiredFragmentsToWin}");
        }

        SetupOrRefreshHUD();
    }

    private void RecomputeRequiredFragmentsToWin()
    {
        if (fragmentsOverrideActive) return;

        // Si el mapa usa el modo secuencial, tomar su objetivo directamente.
        if (mapManager != null && mapManager.useSequentialFragments)
        {
            requiredFragmentsToWin = Mathf.Max(1, mapManager.targetFragments);
            totalLevels = requiredFragmentsToWin; // informativo
            return;
        }

        // Legacy: igualar a floors
        int floorsNow = (mapManager != null) ? mapManager.floors : totalLevels;
        floorsNow = Mathf.Max(floorsNow, 1);
        requiredFragmentsToWin = floorsNow;
        totalLevels = floorsNow;
    }

    public void SyncRequiredWithFloorsNow()
    {
        bool hadOverride = fragmentsOverrideActive;
        fragmentsOverrideActive = false;
        RecomputeRequiredFragmentsToWin();
        fragmentsOverrideActive = hadOverride;
        SetupOrRefreshHUD();
        Debug.Log($"[PlayerInventory] SyncRequiredWithFloorsNow -> required={requiredFragmentsToWin}, floors={totalLevels}, override={fragmentsOverrideActive}");
    }

    public void OverrideFragmentsToWin(int required)
    {
        fragmentsOverrideActive = true;
        requiredFragmentsToWin = Mathf.Max(1, required);

        // Reflejar en totalLevels solo como info
        totalLevels = requiredFragmentsToWin;

        SetupOrRefreshHUD();

        Debug.Log($"[PlayerInventory] OverrideFragmentsToWin -> {requiredFragmentsToWin}");
    }

    public void ClearFragmentsOverride()
    {
        fragmentsOverrideActive = false;
        RecomputeRequiredFragmentsToWin();
        SetupOrRefreshHUD();

        Debug.Log($"[PlayerInventory] ClearFragmentsOverride -> {requiredFragmentsToWin}");
    }

    public int GetRequiredFragmentsToWin() => requiredFragmentsToWin;

    public void RefreshBatteryHUDEvents()
    {
        FireBatteryEvents();
    }

    private void SetupOrRefreshHUD()
    {
        if (fragmentHUD == null) return;

        // SIEMPRE visible
        fragmentHUD.gameObject.SetActive(true);

        // Total = sistema nuevo (MapManager targetFragments) o requiredFragmentsToWin
        int total = requiredFragmentsToWin;
        if (mapManager != null && mapManager.useSequentialFragments)
            total = Mathf.Max(1, mapManager.targetFragments);

        fragmentHUD.totalFragments = total;
        fragmentHUD.UpdateFragmentCount(soulFragmentsCollected);
    }
}