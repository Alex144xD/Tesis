using UnityEngine;

public class CustomModeRuntime : MonoBehaviour
{
    public static CustomModeRuntime Instance { get; private set; }

    public CustomModeProfile ActiveProfile { get; private set; }

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void SetProfile(CustomModeProfile profile)
    {
        ActiveProfile = profile;
        Debug.Log("[CustomMode] Perfil activo aplicado.");
    }

    public void ApplyToMap(MultiFloorDynamicMapManager map)
    {
        if (!ActiveProfile || !map) return;

        map.enemyDensity *= ActiveProfile.enemyDensityMul;
        map.batteryDensity *= ActiveProfile.batteryDensityMul;
        map.fragmentDensity *= ActiveProfile.fragmentDensityMul;

        if (ActiveProfile.torchesOnlyStartFew)
        {
            map.torchesMinPerFloor = Mathf.Max(0, map.torchesMinPerFloor - 1);
            map.torchesMaxPerFloor = Mathf.Max(map.torchesMinPerFloor, map.torchesMaxPerFloor - 1);
        }

        map.floors = Mathf.Clamp(ActiveProfile.targetFloors, 1, 9);
    }

    public void ApplyToEnemy(EnemyFSM enemy)
    {
        if (!ActiveProfile || !enemy) return;

        enemy.patrolSpeed *= ActiveProfile.enemyStatMul;
        enemy.chaseSpeed *= ActiveProfile.enemyStatMul;
        enemy.attackDamage *= ActiveProfile.enemyStatMul;
    }

    public void ApplyToFlashlight(PlayerLightController lightCtrl)
    {
        if (!ActiveProfile || !lightCtrl) return;
        lightCtrl.drainRate *= ActiveProfile.batteryDrainMul;
    }
}