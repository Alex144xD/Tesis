using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class LifeHUDController : MonoBehaviour
{
    [Header("Referencias")]
    public PlayerHealth playerHealth;
    public Image bloodOverlay;

    [Header("Overlay")]
    [Range(0f, 1f)] public float maxAlpha = 0.8f;
    public float fadeSpeed = 3f;
    public float pulseSpeed = 5f;

    [Header("Robustez")]
    [Tooltip("Usa tiempo no escalado para que el HUD se actualice aunque timeScale=0.")]
    public bool useUnscaledTime = true;

    [Tooltip("Ignora muestras de vida (posibles 0s falsos) durante este tiempo al entrar.")]
    public float initialGraceSeconds = 0.35f;

    [Tooltip("Reintenta enlazar PlayerHealth al cargar escena o si se pierde.")]
    public bool autoRebindPlayerHealth = true;

    private float _graceTimer;
    private bool _gotValidSample;
    private float _lastRebindAttemptTime;

    private void Awake()
    {
        if (playerHealth == null)
            TryBindPlayerHealth();

        if (bloodOverlay != null)
            bloodOverlay.raycastTarget = false; 
    }

    private void OnEnable()
    {
        _graceTimer = initialGraceSeconds;
        _gotValidSample = false;

        if (bloodOverlay != null)
        {
            var c = bloodOverlay.color; c.a = 0f; bloodOverlay.color = c;
            if (bloodOverlay.rectTransform != null)
                bloodOverlay.rectTransform.localScale = Vector3.one;
        }

        SceneManager.sceneLoaded += OnSceneLoaded_Rebind;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded_Rebind;
    }

    private void OnSceneLoaded_Rebind(Scene s, LoadSceneMode mode)
    {
        if (autoRebindPlayerHealth) TryBindPlayerHealth(true);
        
        _graceTimer = initialGraceSeconds;
        _gotValidSample = false;

        if (bloodOverlay != null)
        {
            var c = bloodOverlay.color; c.a = 0f; bloodOverlay.color = c;
            if (bloodOverlay.rectTransform != null)
                bloodOverlay.rectTransform.localScale = Vector3.one;
        }
    }

    private void TryBindPlayerHealth(bool force = false)
    {
        if (!force)
        {
           
            float now = Time.unscaledTime;
            if (now - _lastRebindAttemptTime < 0.1f) return;
            _lastRebindAttemptTime = now;
        }

        var ph = FindObjectOfType<PlayerHealth>();
        if (ph != null) playerHealth = ph;
    }

    void Update()
    {
        if (autoRebindPlayerHealth && playerHealth == null)
            TryBindPlayerHealth();

        if (playerHealth == null || bloodOverlay == null)
            return;

        float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        float tt = useUnscaledTime ? Time.unscaledTime : Time.time;

        float hpNorm = Mathf.Clamp01(playerHealth.GetHealthNormalized());

       
        if (_graceTimer > 0f)
        {
            _graceTimer -= dt;

           
            if (hpNorm <= 0f && !_gotValidSample)
            {
                FadeTowards(0f, dt);
               
                if (bloodOverlay.rectTransform != null)
                {
                    var cur = bloodOverlay.rectTransform.localScale;
                    bloodOverlay.rectTransform.localScale =
                        Vector3.Lerp(cur, Vector3.one, dt * fadeSpeed);
                }
                return;
            }
        }

        if (hpNorm > 0f && hpNorm <= 1f) _gotValidSample = true;

        // Opacidad basada en la vida
        float targetAlpha = (1f - hpNorm) * maxAlpha;

        
        if (hpNorm < 0.3f)
        {
            targetAlpha += Mathf.Sin(tt * pulseSpeed) * 0.1f;
        }

        targetAlpha = Mathf.Clamp01(targetAlpha);

   
        FadeTowards(targetAlpha, dt);

       
        float targetScale = 1f + (1f - hpNorm) * 0.2f;
        if (bloodOverlay.rectTransform != null)
        {
            var cur = bloodOverlay.rectTransform.localScale;
            bloodOverlay.rectTransform.localScale =
                Vector3.Lerp(cur, new Vector3(targetScale, targetScale, 1f), dt * fadeSpeed);
        }
    }

    private void FadeTowards(float targetAlpha, float dt)
    {
        Color col = bloodOverlay.color;
        col.a = Mathf.Lerp(col.a, targetAlpha, dt * fadeSpeed);
        bloodOverlay.color = col;
    }


    public void ResetOverlayImmediate()
    {
        if (bloodOverlay == null) return;
        var c = bloodOverlay.color; c.a = 0f; bloodOverlay.color = c;
        if (bloodOverlay.rectTransform != null)
            bloodOverlay.rectTransform.localScale = Vector3.one;

        _graceTimer = initialGraceSeconds;
        _gotValidSample = false;
    }
}