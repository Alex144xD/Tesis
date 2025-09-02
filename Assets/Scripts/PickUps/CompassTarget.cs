using UnityEngine;
using System.Collections.Generic;

[DisallowMultipleComponent]
public class CompassTarget : MonoBehaviour
{
    // Marca si este objetivo es el "principal" (por ejemplo, el fragmento activo).
    public bool isPrimary = false;

    private static readonly List<CompassTarget> _all = new List<CompassTarget>();

    void OnEnable()
    {
        if (!_all.Contains(this)) _all.Add(this);
    }

    void OnDisable()
    {
        _all.Remove(this);
    }

    /// Devuelve el objetivo principal si existe; si no, el más cercano a "from".
    public static CompassTarget GetPrimaryOrClosest(Vector3 from)
    {
        CompassTarget p = GetPrimary();
        if (p != null) return p;
        return GetClosest(from);
    }

    public static CompassTarget GetPrimary()
    {
        for (int i = 0; i < _all.Count; i++)
            if (_all[i] && _all[i].isPrimary) return _all[i];
        return null;
    }

    public static CompassTarget GetClosest(Vector3 from)
    {
        float best = float.PositiveInfinity;
        CompassTarget bestT = null;
        for (int i = 0; i < _all.Count; i++)
        {
            var t = _all[i];
            if (!t) continue;
            float d = Vector3.SqrMagnitude(t.transform.position - from);
            if (d < best) { best = d; bestT = t; }
        }
        return bestT;
    }

    /// Útil si tu sistema de mapa activa/desactiva el fragmento actual:
    public void SetPrimary(bool value) => isPrimary = value;
}