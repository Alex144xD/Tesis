using UnityEngine;
using UnityEngine.Events;
using System.Collections;

public class PlayerInventory : MonoBehaviour
{
    [Header("Progreso del jugador")]
    public int soulFragmentsCollected = 0;

    [Tooltip("Se actualiza automáticamente desde el MapManager. Solo informativo.")]
    public int totalLevels;

    [Header("Eventos")]
    public UnityEvent<int, int> onFragmentCollected; 

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
    [Tooltip("Fragmentos requeridos para ganar. Se deriva de floors salvo que haya override.")]
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

        if (mapManager != null)
            totalLevels = mapManager.floors;

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

    
        if (!fragmentsOverrideActive)
            RecomputeRequiredFragmentsToWin();

        StartCoroutine(DeferredSyncRequiredFragments());

 
        SetupOrRefreshHUD();

  
        FireBatteryEvents();

        Debug.Log($"[PlayerInventory.Start] floors={totalLevels} required={requiredFragmentsToWin} override={fragmentsOverrideActive}");
    }

    private IEnumerator DeferredSyncRequiredFragments()
    {
 
        yield return null;


        if (!fragmentsOverrideActive)
        {
            int before = requiredFragmentsToWin;
            RecomputeRequiredFragmentsToWin();

            if (requiredFragmentsToWin != before)
            {
                Debug.Log($"[PlayerInventory] Deferred sync updated requiredFragmentsToWin {before} -> {requiredFragmentsToWin}");
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

    public void AddSoulFragment()
    {
        soulFragmentsCollected = Mathf.Max(0, soulFragmentsCollected + 1);

        Debug.Log($"[PlayerInventory] Fragmentos: {soulFragmentsCollected}/{requiredFragmentsToWin} (pisos:{totalLevels}, override:{fragmentsOverrideActive})");

        onFragmentCollected?.Invoke(soulFragmentsCollected, requiredFragmentsToWin);

        if (fragmentHUD != null && GameManager.Instance != null && GameManager.Instance.IsInCustomMode())
            fragmentHUD.UpdateFragmentCount(soulFragmentsCollected);

        if (usePersistence) SaveFragments();

        // ¿Aún faltan fragmentos?
        if (soulFragmentsCollected < requiredFragmentsToWin)
        {
            if (mapManager != null) mapManager.GoToNextFloor();
            else Debug.LogWarning("[PlayerInventory] No se encontró el MapManager para cambiar de piso.");
        }
        else
        {
            // Ganar
            Debug.Log("[PlayerInventory] ¡Has ganado el juego!");
            if (gameManager != null) gameManager.PlayerWin();
            else if (GameManager.Instance != null) GameManager.Instance.PlayerWin();
            else Debug.LogWarning("[PlayerInventory] No se encontró GameManager para procesar la victoria.");
        }
    }

    public void ResetInventory()
    {
        soulFragmentsCollected = 0;
    }

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

        // Refleja en totalLevels solo como info
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

        bool show = (GameManager.Instance != null && GameManager.Instance.IsInCustomMode());
        fragmentHUD.gameObject.SetActive(show);
        if (show)
        {
            fragmentHUD.totalFragments = requiredFragmentsToWin;
            fragmentHUD.UpdateFragmentCount(soulFragmentsCollected);
        }
    }
}