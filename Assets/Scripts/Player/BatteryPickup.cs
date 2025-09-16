using UnityEngine;
using TMPro;

public class BatteryPickup : MonoBehaviour
{
    [Header("Tipo y recarga")]
    public BatteryType type = BatteryType.Green;

    [Header("Recarga por porcentaje")]
    public bool usePercent = true;
    [Range(0f, 1f)] public float percentToRecharge = 0.25f;

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

    [Header("UI (arrastra tu TMP del Canvas de la escena)")]
    [SerializeField] private TextMeshProUGUI interactionText; // <- TextMeshProUGUI exacto
    [TextArea] public string message = "Presiona E para recoger batería";

    [Header("Fade del mensaje")]
    [Tooltip("Velocidad del fade (1 = muy lento, 8 = rápido)")]
    [Range(0.5f, 10f)] public float fadeSpeed = 6f;

    // Interno
    private bool _collected;
    private int _collectorInstanceId = -1;
    private GameObject _playerInRange;
    private Coroutine _fadeCo;

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

        // Dejar el texto oculto (alpha 0) al inicio
        if (interactionText)
        {
            interactionText.text = "";
            interactionText.alpha = 0f;
            // No desactivamos el GameObject para que el layout no parpadee
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (!string.IsNullOrEmpty(playerTag) && other.CompareTag(playerTag))
        {
            _playerInRange = other.gameObject;
            ShowPrompt(true);
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (!string.IsNullOrEmpty(playerTag) && other.CompareTag(playerTag))
        {
            if (_playerInRange == other.gameObject) _playerInRange = null;
            ShowPrompt(false);
        }
    }

    void Update()
    {
        if (_collected) return;

        if (_playerInRange != null && Input.GetKeyDown(KeyCode.E))
        {
            TryRecharge(_playerInRange);
            ShowPrompt(false);
        }
    }

    private void ShowPrompt(bool show)
    {
        if (!interactionText) return;

        if (show)
        {
            interactionText.text = message;
            StartFade(1f); // fade in
        }
        else
        {
            StartFade(0f, clearOnEnd: true); // fade out y limpiar texto al final
        }
    }

    private void StartFade(float targetAlpha, bool clearOnEnd = false)
    {
        if (_fadeCo != null) StopCoroutine(_fadeCo);
        _fadeCo = StartCoroutine(CoFadeText(targetAlpha, clearOnEnd));
    }

    private System.Collections.IEnumerator CoFadeText(float targetAlpha, bool clearOnEnd)
    {
        if (!interactionText) yield break;

        float start = interactionText.alpha;
        float t = 0f;

        while (!Mathf.Approximately(interactionText.alpha, targetAlpha))
        {
            t += Time.deltaTime * fadeSpeed;
            interactionText.alpha = Mathf.Lerp(start, targetAlpha, t);
            yield return null;
        }

        interactionText.alpha = targetAlpha;
        if (clearOnEnd && Mathf.Approximately(targetAlpha, 0f))
        {
            interactionText.text = "";
        }
        _fadeCo = null;
    }

    private void TryRecharge(GameObject root)
    {
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