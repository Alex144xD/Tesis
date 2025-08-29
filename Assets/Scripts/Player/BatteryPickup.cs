using UnityEngine;

[RequireComponent(typeof(Collider))]
public class BatteryPickup : MonoBehaviour
{
    [Header("Tipo y recarga")]
    public BatteryType type = BatteryType.Green;
    public float rechargeAmount = 10f;

    [Header("Feedback (opcional)")]
    public AudioClip pickupSfx;
    public float sfxVolume = 1f;
    public GameObject pickupVfx;
    public float destroyDelay = 0.05f;

    [Header("Seguridad")]
    public string playerTag = "Player";
    public bool requireIsTrigger = true;

    bool collected = false;
    AudioSource _oneShotSrc;

    void Reset()
    {
        var col = GetComponent<Collider>();
        if (col) col.isTrigger = true;
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
        // One-shot 2D
        _oneShotSrc = gameObject.AddComponent<AudioSource>();
        _oneShotSrc.playOnAwake = false;
        _oneShotSrc.spatialBlend = 0f;
    }

    void OnTriggerEnter(Collider other)
    {
        if (collected) return;
        if (!other.CompareTag(playerTag)) return;

        // Buscar sistemas en el objeto golpeado, su padre o sus hijos
        var system = other.GetComponent<PlayerBatterySystem>()
                     ?? other.GetComponentInParent<PlayerBatterySystem>()
                     ?? other.GetComponentInChildren<PlayerBatterySystem>();

        if (system != null)
        {
            system.Recharge(type, rechargeAmount);
            Collect();
            return;
        }

        // Legacy: recarga directa a la linterna
        var lightCtrl = other.GetComponentInChildren<PlayerLightController>()
                      ?? other.GetComponent<PlayerLightController>()
                      ?? other.GetComponentInParent<PlayerLightController>();

        if (lightCtrl != null)
        {
            lightCtrl.RechargeBattery(rechargeAmount);
            Collect();
        }
    }

    void Collect()
    {
        collected = true;

        // Apagar visual rápido para evitar re-pickups, pero dejar el collider fuera
        foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = false;
        var col = GetComponent<Collider>();
        if (col) col.enabled = false;

        if (pickupVfx) Instantiate(pickupVfx, transform.position, Quaternion.identity);
        if (pickupSfx) _oneShotSrc.PlayOneShot(pickupSfx, sfxVolume);

        // Destruir (si hay SFX, un pequeño delay)
        Destroy(gameObject, destroyDelay);
    }
}