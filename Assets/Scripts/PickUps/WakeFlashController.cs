using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections;

public class WakeFlashImageFader : MonoBehaviour
{
    [Header("UI Image a pantalla completa (blanca)")]
    public Image flashImage;

    [Header("Tiempos (seg)")]
    [Range(0f, 5f)] public float hold = 0.6f;
    [Range(0f, 5f)] public float fade = 1.6f;

    [Header("Opciones")]
    public bool playOnStart = true;           // disparar en Start (escena actual)
    public bool useUnscaledTime = true;       // ignora timeScale
    public bool persistAcrossScenes = false;  // si quieres que el objeto sobreviva a los loads
    public bool triggerOnSceneLoaded = false; // re-disparar al cargar una nueva escena

    Coroutine running;

    void Awake()
    {
        // Si quieres que sobreviva a los loads
        if (persistAcrossScenes) DontDestroyOnLoad(gameObject);

        // Asegurar referencia si no la arrastraron
        if (!flashImage) flashImage = GetComponent<Image>();

        // Reset duro para que nunca se quede pegado
        ResetOverlay();
    }

    void OnEnable()
    {
        // Siempre iniciar desactivado y transparente
        ResetOverlay();

        // Escuchar cambio de escena si corresponde
        if (triggerOnSceneLoaded)
            SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDisable()
    {
        if (running != null) StopCoroutine(running);
        running = null;

        // Desuscribir listener de escenas
        if (triggerOnSceneLoaded)
            SceneManager.sceneLoaded -= OnSceneLoaded;

        // Asegurar que no se quede visible
        ResetOverlay();
    }

    void Start()
    {
        if (playOnStart) SafeTrigger();
    }

    void OnDestroy()
    {
        if (triggerOnSceneLoaded)
            SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    // Llamado cuando carga una escena (si triggerOnSceneLoaded = true)
    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Al cargar escena, primero asegurar overlay oculto
        ResetOverlay();
        // Disparar al final del frame para que la UI esté lista
        if (triggerOnSceneLoaded) SafeTrigger();
    }

    // Dispara el efecto de forma segura (al final del frame)
    public void SafeTrigger()
    {
        if (!isActiveAndEnabled) return;
        if (running != null) StopCoroutine(running);
        running = StartCoroutine(CoTriggerEndOfFrame());
    }

    IEnumerator CoTriggerEndOfFrame()
    {
        // Espera un frame para evitar carreras con el layout/UI al cargar escena
        yield return new WaitForEndOfFrame();
        Trigger();
    }

    public void Trigger()
    {
        if (!flashImage) return;
        if (running != null) StopCoroutine(running);
        running = StartCoroutine(CoFlash());
    }

    IEnumerator CoFlash()
    {
        // Asegurar visible y alpha = 1
        var c = flashImage.color;
        c.a = 1f;
        flashImage.color = c;
        flashImage.enabled = true;
        flashImage.raycastTarget = false; // que no bloquee input

        float dt() => useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

        // Hold
        float t = 0f;
        while (t < hold)
        {
            t += dt();
            yield return null;
        }

        // Fade
        t = 0f;
        float denom = Mathf.Max(0.0001f, fade);
        while (t < fade)
        {
            t += dt();
            float k = Mathf.Clamp01(t / denom);
            c.a = 1f - k;
            flashImage.color = c;
            yield return null;
        }

        // Ocultar y limpiar
        ResetOverlay();
        running = null;
    }

    void ResetOverlay()
    {
        if (!flashImage) return;
        var c = flashImage.color;
        c.a = 0f;
        flashImage.color = c;
        flashImage.enabled = false;
        flashImage.raycastTarget = false;
    }
}