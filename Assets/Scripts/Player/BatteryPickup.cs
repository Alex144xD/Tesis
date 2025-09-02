using UnityEngine;

[RequireComponent(typeof(Collider))]
public class BatteryPickup : MonoBehaviour
{
    [Header("Tipo y recarga")]
    public BatteryType type = BatteryType.Green;

    [Header("Recarga por porcentaje")]
    [Tooltip("Usar porcentaje en vez de cantidad fija.")]
    public bool usePercent = true;
    [Range(0f, 1f)] public float percentToRecharge = 0.25f; // 25%

    [Header("Recarga absoluta (legacy)")]
    public float rechargeAmount = 10f;

    [Header("Feedback (opcional)")]
    public AudioClip pickupSfx;
    [Range(0f, 1f)] public float sfxVolume = 1f;
    public GameObject pickupVfx;
    public float destroyDelay = 0.02f;

    [Header("Seguridad")]
    public string playerTag = "Player";
    public bool requireIsTrigger = true;
    public bool useActiveBattery = false;
    public bool onlyIfAddsCharge = true;

    // Interno
    private bool _collected;
    private int _collectorInstanceId = -1;

    void Reset()
    {
        var col = GetComponent<Collider>();
        if (col) col.isTrigger = true;
    }

    void OnValidate()
    {
        if (requireIsTrigger)
        {
            var col = GetComponent<Collider>();
            if (col) col.isTrigger = true;
        }
        percentToRecharge = Mathf.Clamp01(percentToRecharge);
    }

    void Awake()
    {
        if (requireIsTrigger)
        {
            var col = GetComponent<Collider>();
            if (col && !col.isTrigger)
            {
                Debug.LogWarning("[BatteryPickup] El collider no es Trigger, lo forzaré a Trigger.");
                col.isTrigger = true;
            }
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (_collected) return;
        if (!string.IsNullOrEmpty(playerTag) && !other.CompareTag(playerTag)) return;

        var root = other.attachedRigidbody ? other.attachedRigidbody.gameObject : other.gameObject;
        int id = root.GetInstanceID();
        if (_collectorInstanceId == -1) _collectorInstanceId = id;
        else if (_collectorInstanceId != id) return;

        var system = root.GetComponent<PlayerBatterySystem>()
                  ?? root.GetComponentInParent<PlayerBatterySystem>()
                  ?? root.GetComponentInChildren<PlayerBatterySystem>();

        bool didRecharge = false;

        if (system != null)
        {
            BatteryType t = useActiveBattery ? system.activeType : type;

            if (usePercent)
            {
                float before = system.GetCharge(t);
                float max = system.GetMax(t);

                if (!onlyIfAddsCharge || before < max - 0.001f)
                {
                    system.RechargePercent(t, percentToRecharge);
                    didRecharge = true;
                }
            }
            else
            {
                if (!onlyIfAddsCharge || system.GetCharge(t) < system.GetMax(t) - 0.001f)
                {
                    system.Recharge(t, rechargeAmount);
                    didRecharge = true;
                }
            }
        }
        else
        {
            // Fallback legacy: recarga directa a la linterna si no hay sistema
            var lightCtrl = root.GetComponentInChildren<PlayerLightController>()
                        ?? root.GetComponent<PlayerLightController>()
                        ?? root.GetComponentInParent<PlayerLightController>();

            if (lightCtrl != null && !usePercent)
            {
                lightCtrl.RechargeBattery(rechargeAmount);
                didRecharge = true;
            }
        }

        if (didRecharge) Collect();
    }

    private void Collect()
    {
        if (_collected) return;
        _collected = true;

        foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = false;
        var col = GetComponent<Collider>();
        if (col) col.enabled = false;

        if (pickupVfx) Instantiate(pickupVfx, transform.position, Quaternion.identity);
        if (pickupSfx) AudioSource.PlayClipAtPoint(pickupSfx, transform.position, sfxVolume);

        if (destroyDelay <= 0f) Destroy(gameObject);
        else Destroy(gameObject, destroyDelay);
    }
}