using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

public class CustomModeRuntime : MonoBehaviour
{
    public static CustomModeRuntime Instance { get; private set; }

    public CustomModeProfile ActiveProfile { get; private set; }

    bool _customModeStarted;

    struct MapBase
    {
        public int decorativeTorchesNearPath;
        public float room2Chance, room3Chance;
        public int width, height;
        public bool has;
    }

    struct EnemyBase
    {
        public float patrolSpeed, chaseSpeed, attackDamage;
        public bool has;
    }

    struct LightBase
    {
        public float baseDrainPercentPerSecond;
        public bool has;
    }

    readonly Dictionary<int, MapBase> mapBase = new Dictionary<int, MapBase>();
    readonly Dictionary<int, EnemyBase> enemyBase = new Dictionary<int, EnemyBase>();
    readonly Dictionary<int, LightBase> lightBase = new Dictionary<int, LightBase>();

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded_Reapply;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        SceneManager.sceneLoaded -= OnSceneLoaded_Reapply;
    }

    public void SetProfile(CustomModeProfile profile)
    {
        ActiveProfile = profile;
        if (!ActiveProfile) return;

        ClearCaches();
        _customModeStarted = false;

        ApplyToCurrentScene();
        Debug.Log("[CustomMode] Perfil activo aplicado en la escena actual.");

        if (GameManager.Instance && !_customModeStarted)
        {
            _customModeStarted = true;
            GameManager.Instance.StartCustomMode();
        }
    }

    void OnSceneLoaded_Reapply(Scene s, LoadSceneMode mode)
    {
        if (ActiveProfile != null)
        {
            ApplyToCurrentScene();
            Debug.Log($"[CustomMode] Reaplicado perfil al cargar escena: {s.name}");
        }
    }

    public void ApplyToCurrentScene()
    {
        if (!ActiveProfile) return;

        var maps = FindObjectsOfType<MultiFloorDynamicMapManager>(true);
        var enemies = FindObjectsOfType<EnemyFSM>(true);
        var lights = FindObjectsOfType<PlayerLightController>(true);

        foreach (var m in maps) ApplyToMap(m);
        foreach (var e in enemies) ApplyToEnemy(e);
        foreach (var l in lights) ApplyToFlashlight(l);

        Debug.Log($"[CustomMode] Aplicación -> maps:{maps.Length}, enemies:{enemies.Length}, lights:{lights.Length}");
    }

    public void ApplyToMap(MultiFloorDynamicMapManager map)
    {
        if (!ActiveProfile || !map) return;

        int id = map.GetInstanceID();
        if (!mapBase.TryGetValue(id, out var b) || !b.has)
        {
            b = new MapBase
            {
                decorativeTorchesNearPath = map.decorativeTorchesNearPath,
                width = map.width,
                height = map.height,
                has = true
            };
            mapBase[id] = b;
        }

        // --- Tamaño de mapa (mapSizeMul + maxMapSize), impar y mínimo 7 ---
        int MaxOdd(int v, int cap)
        {
            v = Mathf.Clamp(v, 7, Mathf.Max(7, cap));
            if (v % 2 == 0) v++;
            return v;
        }
        int newW = Mathf.RoundToInt(b.width * ActiveProfile.mapSizeMul);
        int newH = Mathf.RoundToInt(b.height * ActiveProfile.mapSizeMul);
        newW = MaxOdd(newW, ActiveProfile.maxMapSize);
        newH = MaxOdd(newH, ActiveProfile.maxMapSize);
        if (newW != map.width || newH != map.height)

        // Torches
        map.decorativeTorchesNearPath = ActiveProfile.torchesOnlyStartFew
            ? Mathf.Max(0, b.decorativeTorchesNearPath - 1)
            : b.decorativeTorchesNearPath;


        // Meta fragmentos (el mapa hace clamp 1..9)
        int targetFragments = Mathf.Max(1, ActiveProfile.targetFloors);
        map.SetTargetFragments(targetFragments);

        var inv = FindObjectOfType<PlayerInventory>(true);
        if (inv)
        {
            inv.OverrideFragmentsToWin(targetFragments);
        }
    }

    public void ApplyToEnemy(EnemyFSM enemy)
    {
        if (!ActiveProfile || !enemy) return;

        int id = enemy.GetInstanceID();
        if (!enemyBase.TryGetValue(id, out var b) || !b.has)
        {
            b = new EnemyBase
            {
                patrolSpeed = enemy.patrolSpeed,
                chaseSpeed = enemy.chaseSpeed,
                attackDamage = enemy.attackDamage,
                has = true
            };
            enemyBase[id] = b;
        }

        enemy.patrolSpeed = b.patrolSpeed * ActiveProfile.enemyStatMul;
        enemy.chaseSpeed = b.chaseSpeed * ActiveProfile.enemyStatMul;
        enemy.attackDamage = b.attackDamage * ActiveProfile.enemyStatMul;

        if (enemy.TryGetComponent<IEnemyBatteryDrainer>(out var drainer))
            drainer.SetDrainsBattery(ActiveProfile.enemy2DrainsBattery);

        if (enemy.TryGetComponent<ILightResistant>(out var resist))
            resist.SetResistsLight(ActiveProfile.enemy3ResistsLight);
    }

    public void ApplyToFlashlight(PlayerLightController lightCtrl)
    {
        if (!ActiveProfile || !lightCtrl) return;

        int id = lightCtrl.GetInstanceID();
        if (!lightBase.TryGetValue(id, out var b) || !b.has)
        {
            b = new LightBase
            {
                baseDrainPercentPerSecond = lightCtrl.baseDrainPercentPerSecond,
                has = true
            };
            lightBase[id] = b;
        }

        lightCtrl.baseDrainPercentPerSecond = b.baseDrainPercentPerSecond * ActiveProfile.batteryDrainMul;
    }

    public void ClearCaches()
    {
        mapBase.Clear();
        enemyBase.Clear();
        lightBase.Clear();
    }
}

public interface IEnemyBatteryDrainer
{
    void SetDrainsBattery(bool value);
}

public interface ILightResistant
{
    void SetResistsLight(bool value);
}