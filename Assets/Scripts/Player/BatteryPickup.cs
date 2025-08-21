using UnityEngine;

public class BatteryPickup : MonoBehaviour
{
    [Header("Tipo y recarga")]
    public BatteryType type = BatteryType.Green;
    public float rechargeAmount = 10f;

    private bool collected = false;

    void OnTriggerEnter(Collider other)
    {
        if (collected) return;
        if (!other.CompareTag("Player")) return;

        var system = other.GetComponent<PlayerBatterySystem>();
        if (system == null)
        {
            var lightCtrl = other.GetComponentInChildren<PlayerLightController>();
            if (lightCtrl != null) lightCtrl.RechargeBattery(rechargeAmount);
        }
        else
        {
            system.Recharge(type, rechargeAmount);
        }

        collected = true;
        Destroy(gameObject);
    }
}
