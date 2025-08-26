using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class GameplayHUDActivator : MonoBehaviour
{
    [Header("Escenas donde el HUD debe estar encendido")]
    public string[] gameplayScenes = { "Game", "Tutorial" };

    [Header("Cómo identificar el HUD de gameplay")]
    [Tooltip("Nombre exacto del GameObject raíz del HUD en la escena")]
    public string hudName = "GameplayHUD";
    [Tooltip("Tag del HUD (opcional). Crea el tag y asígnalo al HUD si quieres usarlo.")]
    public string hudTag = "GameplayHUD";

    [Header("Orden de dibujo (HUD debajo de paneles meta)")]
    [Tooltip("Menor que el overlaySortingOrder del UIManager. Si UIManager usa 800, aquí 790.")]
    public int sortingOrder = 790;

    [Header("Comportamiento general del HUD")]
    [Tooltip("Convierte el HUD en no-bloqueante (no intercepta clics).")]
    public bool makeHudNonBlocking = true;
    [Tooltip("Fuerza alpha=1 en todos los CanvasGroup del HUD al activarlo.")]
    public bool forceAlphaOne = true;
    [Tooltip("Asegura Time.timeScale=1 al mostrar el HUD (por si vienes de pausa/menú).")]
    public bool normalizeTimeScale = true;

    [Header("Reset de overlays de VIDA/DAÑO (estilo Gears)")]
    [Tooltip("Activar para resetear overlays de daño/vida al entrar a la escena.")]
    public bool resetLifeOverlays = true;

    [Tooltip("Pistas de nombre para encontrar overlays (case-insensitive).")]
    public string[] overlayNameHints = new string[]
    {
        "Life","Health","Blood","Damage","Vignette","Hurt","LowHealth","Hit"
    };

    [Tooltip("Tags opcionales para marcar overlays (si los usas).")]
    public string[] overlayTags = new string[] { "HUDOverlay", "LifeOverlay" };

    [Tooltip("Si el overlay tiene CanvasGroup, ponerlo a alpha=0 al resetear.")]
    public bool overlaySetCanvasGroupAlphaZero = true;

    [Tooltip("Poner el alpha de TODOS los Graphics del overlay en 0 al resetear.")]
    public bool overlaySetGraphicsAlphaZero = true;

    [Tooltip("Desactivar raycasts en los gráficos del overlay al resetear.")]
    public bool overlayDisableRaycasts = true;

    [Tooltip("Reiniciar animadores dentro del overlay (Rebind + Update(0)).")]
    public bool overlayResetAnimators = true;

    [Header("Debug")]
    public bool debugLogs = true;

    void Awake()
    {
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;
        OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (ShouldEnable(scene.name))
            EnableHudInScene(scene);
        else
            DisableHudInScene(scene);
    }

    bool ShouldEnable(string sceneName)
    {
        if (gameplayScenes == null) return false;
        for (int i = 0; i < gameplayScenes.Length; i++)
        {
            var s = gameplayScenes[i];
            if (!string.IsNullOrEmpty(s) &&
                string.Equals(sceneName, s, System.StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    void EnableHudInScene(Scene s)
    {
        var hud = FindInSceneEvenIfInactive(s, hudName, hudTag);
        if (!hud)
        {
            if (debugLogs) Debug.LogWarning($"[HUDActivator] No encontré HUD '{hudName}'/tag '{hudTag}' en escena '{s.name}'.");
            return;
        }

        // Activa toda la cadena de padres
        for (Transform t = hud.transform; t != null; t = t.parent)
            if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);

        // Habilita y ordena todos los Canvas del HUD
        var canvases = hud.GetComponentsInChildren<Canvas>(true);
        foreach (var c in canvases)
        {
            c.enabled = true;
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.worldCamera = null;
            c.targetDisplay = 0;
            c.sortingOrder = sortingOrder;
        }

        // Fuerza visibilidad global del HUD (por si quedó alpha=0 tras pausa/derrota)
        if (forceAlphaOne)
        {
            var groups = hud.GetComponentsInChildren<CanvasGroup>(true);
            foreach (var g in groups) g.alpha = 1f;
        }

        // Que el HUD no bloquee interacción general (recomendado si es “tipo Gears”)
        if (makeHudNonBlocking)
        {
            var rootCg = hud.GetComponent<CanvasGroup>();
            if (!rootCg) rootCg = hud.AddComponent<CanvasGroup>();
            rootCg.interactable = false;
            rootCg.blocksRaycasts = false;

            var graphics = hud.GetComponentsInChildren<Graphic>(true);
            foreach (var gr in graphics) gr.raycastTarget = false;
        }

        // --- RESET de overlays de vida/daño estilo Gears ---
        if (resetLifeOverlays)
            ResetLifeOverlays(hud);

        Canvas.ForceUpdateCanvases();

        if (normalizeTimeScale && Time.timeScale != 1f)
            Time.timeScale = 1f;

        if (debugLogs) Debug.Log($"[HUDActivator] '{hud.name}' ACTIVADO en '{s.name}' (sorting={sortingOrder}).");
    }

    void DisableHudInScene(Scene s)
    {
        var hud = FindInSceneEvenIfInactive(s, hudName, hudTag);
        if (!hud) return;
        if (hud.activeSelf) hud.SetActive(false);
        if (debugLogs) Debug.Log($"[HUDActivator] '{hud.name}' DESACTIVADO en '{s.name}'.");
    }

    // ---------- RESET OVERLAYS ----------
    void ResetLifeOverlays(GameObject hudRoot)
    {
        var allTransforms = hudRoot.GetComponentsInChildren<Transform>(true);
        int count = 0;

        for (int i = 0; i < allTransforms.Length; i++)
        {
            var go = allTransforms[i].gameObject;
            if (!MatchesOverlay(go)) continue;

            // 1) CanvasGroup -> alpha 0 (overlay oculto)
            if (overlaySetCanvasGroupAlphaZero)
            {
                var cg = go.GetComponent<CanvasGroup>();
                if (cg) { cg.alpha = 0f; cg.interactable = false; cg.blocksRaycasts = false; }
            }

            // 2) Graphics -> alpha 0 y sin raycasts
            if (overlaySetGraphicsAlphaZero || overlayDisableRaycasts)
            {
                var graphics = go.GetComponentsInChildren<Graphic>(true);
                for (int g = 0; g < graphics.Length; g++)
                {
                    var gr = graphics[g];
                    if (overlaySetGraphicsAlphaZero)
                    {
                        var col = gr.color;
                        col.a = 0f;
                        gr.color = col;
                    }
                    if (overlayDisableRaycasts)
                        gr.raycastTarget = false;
                }
            }

            // 3) Animators -> reset a estado inicial
            if (overlayResetAnimators)
            {
                var anims = go.GetComponentsInChildren<Animator>(true);
                for (int a = 0; a < anims.Length; a++)
                {
                    anims[a].Rebind();
                    anims[a].Update(0f);
                }
            }

            // 4) Aviso a scripts (si implementan estos métodos)
            go.SendMessage("ResetOverlay", SendMessageOptions.DontRequireReceiver);
            go.SendMessage("ResetHUD", SendMessageOptions.DontRequireReceiver);

            count++;
        }

        if (debugLogs)
            Debug.Log($"[HUDActivator] Reset de overlays: {count} nodos coincidieron con pistas/tags.");
    }

    bool MatchesOverlay(GameObject go)
    {
        // Match por tag
        if (overlayTags != null)
        {
            for (int i = 0; i < overlayTags.Length; i++)
            {
                var tag = overlayTags[i];
                if (!string.IsNullOrEmpty(tag))
                {
                    try { if (go.CompareTag(tag)) return true; } catch { /* tag no definido */ }
                }
            }
        }

        // Match por nombre
        if (overlayNameHints != null)
        {
            string n = go.name.ToLowerInvariant();
            for (int i = 0; i < overlayNameHints.Length; i++)
            {
                var hint = overlayNameHints[i];
                if (!string.IsNullOrEmpty(hint) && n.Contains(hint.ToLowerInvariant()))
                    return true;
            }
        }
        return false;
    }

    // ---- Búsqueda que incluye objetos INACTIVOS en la escena ----
    GameObject FindInSceneEvenIfInactive(Scene s, string name, string tag)
    {
        if (!s.IsValid() || !s.isLoaded) return null;

        // 1) Recorrer TODOS los root objects de la escena (activos e inactivos)
        var roots = s.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            var found = FindRecursiveByNameOrTag(roots[i].transform, name, tag);
            if (found) return found;
        }

        // 2) Fallback: Resources (incluye inactivos no ocultos)
        var all = Resources.FindObjectsOfTypeAll<GameObject>();
        foreach (var go in all)
        {
            if (go == null) continue;
            if (go.hideFlags != HideFlags.None) continue;         // ignora ocultos/editor
            if (!go.scene.IsValid() || go.scene.name != s.name) continue;

            if ((!string.IsNullOrEmpty(name) && go.name == name) ||
                (!string.IsNullOrEmpty(tag) && SafeHasTag(go, tag)))
                return go;
        }

        return null;
    }

    GameObject FindRecursiveByNameOrTag(Transform t, string name, string tag)
    {
        if (t == null) return null;

        if ((!string.IsNullOrEmpty(name) && t.name == name) ||
            (!string.IsNullOrEmpty(tag) && SafeHasTag(t.gameObject, tag)))
            return t.gameObject;

        for (int i = 0; i < t.childCount; i++)
        {
            var res = FindRecursiveByNameOrTag(t.GetChild(i), name, tag);
            if (res) return res;
        }
        return null;
    }

    bool SafeHasTag(GameObject go, string tag)
    {
        try { return go.CompareTag(tag); }
        catch { return false; } // por si el Tag no existe en Project Settings
    }
}