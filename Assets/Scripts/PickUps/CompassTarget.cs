using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

[DisallowMultipleComponent]
public class CompassTarget : MonoBehaviour
{
    [Tooltip("Marca este objetivo como el principal (la brújula lo prioriza).")]
    public bool isPrimary = false;

    private static readonly List<CompassTarget> _all = new List<CompassTarget>(32);

    // ======= Arranque / Reset por escena =======

    // Limpia ANTES de que la nueva escena empiece a habilitar objetos
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void ResetStaticOnSceneLoad()
    {
        _all.Clear(); // Evita residuos al reintentar / cambiar de escena
        // Failsafe: si la escena activa cambia (aditiva o normal), limpia también
        SceneManager.activeSceneChanged += OnActiveSceneChanged;
    }

    static void OnActiveSceneChanged(Scene oldScene, Scene newScene)
    {
        _all.Clear();
    }

    void OnApplicationQuit()
    {
        _all.Clear();
    }

    void OnEnable()
    {
        PurgeNulls();

        // Evita duplicados del mismo componente (por si Unity llama doble habilitado)
        if (!_all.Contains(this))
            _all.Add(this);
    }

    void OnDisable()
    {
        _all.Remove(this);
        PurgeNulls();
    }

    void OnDestroy()
    {
        _all.Remove(this);
        PurgeNulls();
    }

    // ======= API pública =======

    /// Devuelve el objetivo principal si existe; si no, el más cercano a "from".
    public static CompassTarget GetPrimaryOrClosest(Vector3 from)
    {
        PurgeNulls();
        CompassTarget p = GetPrimary();
        if (p != null) return p;
        return GetClosest(from);
    }

    /// Devuelve el objetivo marcado como primary (único si se usa SetPrimaryExclusive).
    public static CompassTarget GetPrimary()
    {
        PurgeNulls();
        for (int i = 0; i < _all.Count; i++)
        {
            var t = _all[i];
            if (!IsUsable(t)) continue;
            if (t.isPrimary) return t;
        }
        return null;
    }

    /// Devuelve el objetivo más cercano a "from".
    public static CompassTarget GetClosest(Vector3 from)
    {
        PurgeNulls();
        float best = float.PositiveInfinity;
        CompassTarget bestT = null;
        for (int i = 0; i < _all.Count; i++)
        {
            var t = _all[i];
            if (!IsUsable(t)) continue;

            float d = (t.transform.position - from).sqrMagnitude;
            if (d < best) { best = d; bestT = t; }
        }
        return bestT;
    }

    /// Marca este como primary de forma exclusiva (apaga el resto).
    public void SetPrimaryExclusive()
    {
        SetPrimaryExclusive(this);
    }

    /// Marca "who" como primary y desmarca todos los demás.
    public static void SetPrimaryExclusive(CompassTarget who)
    {
        PurgeNulls();
        for (int i = 0; i < _all.Count; i++)
        {
            var t = _all[i];
            if (!IsUsable(t)) continue;
            t.isPrimary = (t == who);
        }
    }

    /// Marca/Desmarca este objetivo como primary (NO exclusivo).
    public void SetPrimary(bool value)
    {
        isPrimary = value;
    }

    /// Útil para depurar o forzar un refresh.
    public static void ForceRescan()
    {
        PurgeNulls();
    }

    // ======= Helpers internos =======

    static void PurgeNulls()
    {
        for (int i = _all.Count - 1; i >= 0; i--)
        {
            if (_all[i] == null)
                _all.RemoveAt(i);
        }
    }

    static bool IsUsable(CompassTarget t)
    {
        return t && t.gameObject && t.gameObject.activeInHierarchy;
    }
}