// Assets/Scripts/CustomMode/CustomModeStoryNode.cs
using UnityEngine;

[CreateAssetMenu(menuName = "Game/Custom Mode Story/Node")]
public class CustomModeStoryNode : ScriptableObject
{
    [TextArea(3, 12)]
    public string narrative;            // Texto de historia/pregunta (TMP rich text)

    public Option[] options;            // Respuestas

    [System.Serializable]
    public class Option
    {
        public string text;             // Texto del botón
        public StoryEffects effects;    // Efectos sobre el perfil
        public CustomModeStoryNode next;// Siguiente nodo (null = terminar)
    }
}

[System.Serializable]
public class StoryEffects
{
    // Cambios relativos (1.0 = neutro). +0.2 => 1.2 al final
    [Range(-1f, 1f)] public float enemyStat;
    [Range(-1f, 1f)] public float enemyDensity;
    [Range(-1f, 1f)] public float batteryDrain;
    [Range(-1f, 1f)] public float batteryDensity;
    [Range(-1f, 1f)] public float fragmentDensity;

    public int floorsDelta;                         // +/− pisos (se clamp a 1..9)

    // Flags tri-estado
    public TriBool torchesOnlyStartFew;
    public TriBool enemy2DrainsBattery;
    public TriBool enemy3ResistsLight;
}

public enum TriBool { Unset, True, False }