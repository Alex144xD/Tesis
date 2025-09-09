using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Game/Custom Mode Story/Node")]
public class CustomModeStoryNode : ScriptableObject
{
    [Header("Narrativa")]
    [Tooltip("Texto mostrado en el panel. Se permite Rich Text de TMP.")]
    [TextArea(3, 12)]
    public string narrative;

    [Header("Fondos visuales (por nodo)")]
    [Tooltip("Compat: si llenas este campo, también se usará como primera capa.")]
    public Sprite backgroundSprite; // opcional (legacy/compat)
    [Tooltip("Lista de sprites a mostrar, en orden (0 = más atrás).")]
    public List<Sprite> backgroundSprites = new List<Sprite>();
    [Tooltip("Tinte aplicado a TODAS las capas de este nodo.")]
    public Color bgTint = Color.white;

    [Header("Opciones")]
    [Tooltip("Lista de respuestas/ramas. Dejar vacío convierte este nodo en final (si no hay 'next').")]
    public Option[] options;

    [System.Serializable]
    public class Option
    {
        [Tooltip("Texto que se muestra en el botón.")]
        [TextArea(1, 4)]
        public string text;

        [Tooltip("Efectos que acumula esta elección.")]
        public StoryEffects effects;

        [Tooltip("Siguiente nodo. Dejar NULL para finalizar la historia y aplicar el perfil.")]
        public CustomModeStoryNode next;
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        // Recortar narrativa
        if (!string.IsNullOrEmpty(narrative))
            narrative = narrative.Trim();


        if (backgroundSprite && (backgroundSprites == null || backgroundSprites.Count == 0))
        {
            if (backgroundSprites == null) backgroundSprites = new List<Sprite>();
            if (!backgroundSprites.Contains(backgroundSprite))
                backgroundSprites.Insert(0, backgroundSprite);
        }

        if (options == null) return;

        bool anyNext = false;
        for (int i = 0; i < options.Length; i++)
        {
            var opt = options[i];
            if (opt == null) continue;

  
            if (!string.IsNullOrEmpty(opt.text))
                opt.text = opt.text.Trim();

            if (opt.next) anyNext = true;


            if (opt.next == this)
            {
                Debug.LogWarning($"[StoryNode] En '{name}' la opción #{i} apunta a SÍ MISMA (bucle). Revísalo.", this);
            }
            if (string.IsNullOrWhiteSpace(opt.text))
            {
                Debug.LogWarning($"[StoryNode] En '{name}' la opción #{i} no tiene texto.", this);
            }
        }

        // nodo terminal sin opciones: válido
        _ = anyNext;
    }
#endif
}

[System.Serializable]
public class StoryEffects
{
    [Header("Multiplicadores relativos (se suman y luego 1+acc => clamp)")]
    [Range(-1f, 1f)] public float enemyStat;
    [Range(-1f, 1f)] public float enemyDensity;
    [Range(-1f, 1f)] public float batteryDrain;
    [Range(-1f, 1f)] public float batteryDensity;
    [Range(-1f, 1f)] public float fragmentDensity;

    [Header("Fragmentos")]
    [Tooltip("Delta sobre el valor base (1). Se clamp a los límites configurados en el Panel (1..9).")]
    public int fragmentsDelta;

    [Header("Tamaño del mapa (% relativo)")]
    [Range(-0.5f, 0.5f)] public float mapSizePercent;

    [Header("Flags tri-estado (Unset = no cambia)")]
    public TriBool torchesOnlyStartFew;
    public TriBool enemy2DrainsBattery;
    public TriBool enemy3ResistsLight;

    public bool IsNeutral()
    {
        return Mathf.Approximately(enemyStat, 0f)
            && Mathf.Approximately(enemyDensity, 0f)
            && Mathf.Approximately(batteryDrain, 0f)
            && Mathf.Approximately(batteryDensity, 0f)
            && Mathf.Approximately(fragmentDensity, 0f)
            && fragmentsDelta == 0
            && Mathf.Approximately(mapSizePercent, 0f)
            && torchesOnlyStartFew == TriBool.Unset
            && enemy2DrainsBattery == TriBool.Unset
            && enemy3ResistsLight == TriBool.Unset;
    }
}

public enum TriBool { Unset, True, False }