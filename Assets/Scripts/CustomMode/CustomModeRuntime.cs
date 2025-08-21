// Assets/Scripts/CustomMode/CustomModeRuntime.cs
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

public class CustomModeRuntime : MonoBehaviour
{
    public static CustomModeRuntime Instance { get; private set; }

    public CustomModeProfile ActiveProfile { get; private set; }

    // Caches (EVITA acumulaciones)
    struct MapBase { public float enemyDensity, batteryDensity, fragmentDensity; public int torchesMinPerFloor, torchesMaxPerFloor, floors; public bool has; }
    struct EnemyBase { public float patrolSpeed, chaseSpeed, attackDamage; public bool has; }
    struct LightBase { public float drainRate; public bool has; }

    readonly Dictionary<int, MapBase> mapBase = new Dictionary<int, MapBase>();
    readonly Dictionary<int, EnemyBase> enemyBase = new Dictionary<int, EnemyBase>();
    readonly Dictionary<int, LightBase> lightBase = new Dictionary<int, LightBase>();

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded_Reapply;
    }

    void OnDestroy()
    {
        if (Instance == this)
            SceneManager.sceneLoaded -= OnSceneLoaded_Reapply;
    }

    public void SetProfile(CustomModeProfile profile)
    {
        ActiveProfile = profile;
        if (!ActiveProfile) return;

        ClearCaches(); // importantísimo
        if (GameManager.Instance) GameManager.Instance.StartCustomMode();

        ApplyToCurrentScene();
        Debug.Log("[CustomMode] Perfil activo aplicado.");
    }

    void OnSceneLoaded_Reapply(Scene s, LoadSceneMode mode)
    {
        if (ActiveProfile != null) ApplyToCurrentScene();
    }

    public void ApplyToCurrentScene()
    {
        if (!ActiveProfile) return;

        foreach (var m in FindObjectsOfType<MultiFloorDynamicMapManager>(true)) ApplyToMap(m);
        foreach (var e in FindObjectsOfType<EnemyFSM>(true)) ApplyToEnemy(e);
        foreach (var l in FindObjectsOfType<PlayerLightController>(true)) ApplyToFlashlight(l);
    }

    public void ApplyToMap(MultiFloorDynamicMapManager map)
    {
        if (!ActiveProfile || !map) return;

        int id = map.GetInstanceID();
        if (!mapBase.TryGetValue(id, out var b) || !b.has)
        {
            b = new MapBase
            {
                enemyDensity = map.enemyDensity,
                batteryDensity = map.batteryDensity,
                fragmentDensity = map.fragmentDensity,
                torchesMinPerFloor = map.torchesMinPerFloor,
                torchesMaxPerFloor = map.torchesMaxPerFloor,
                floors = map.floors,
                has = true
            };
            mapBase[id] = b;
        }

        map.enemyDensity = b.enemyDensity * ActiveProfile.enemyDensityMul;
        map.batteryDensity = b.batteryDensity * ActiveProfile.batteryDensityMul;
        map.fragmentDensity = b.fragmentDensity * ActiveProfile.fragmentDensityMul;

        int min = b.torchesMinPerFloor, max = b.torchesMaxPerFloor;
        if (ActiveProfile.torchesOnlyStartFew)
        {
            min = Mathf.Max(0, min - 1);
            max = Mathf.Max(min, max - 1);
        }
        map.torchesMinPerFloor = min;
        map.torchesMaxPerFloor = max;

        map.floors = Mathf.Clamp(ActiveProfile.targetFloors, 1, 9);
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
    }

    public void ApplyToFlashlight(PlayerLightController lightCtrl)
    {
        if (!ActiveProfile || !lightCtrl) return;

        int id = lightCtrl.GetInstanceID();
        if (!lightBase.TryGetValue(id, out var b) || !b.has)
        {
            b = new LightBase { drainRate = lightCtrl.drainRate, has = true };
            lightBase[id] = b;
        }

        lightCtrl.drainRate = b.drainRate * ActiveProfile.batteryDrainMul;
    }

    public void ClearCaches()
    {
        mapBase.Clear();
        enemyBase.Clear();
        lightBase.Clear();
    }
}