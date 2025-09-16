using UnityEngine;

[CreateAssetMenu(fileName = "CustomModeProfile", menuName = "Game/Custom Mode Profile")]
public class CustomModeProfile : ScriptableObject
{
    [Header("Dificultad global")]
    [Range(0.5f, 2f)] public float enemyStatMul = 1f;     // velocidad/daño enemigos
    [Range(0.5f, 2f)] public float batteryDrainMul = 1f;  // drenaje linterna

    [Header("Salas (ajuste de probabilidad)")]
    [Range(0.25f, 2f)] public float room2ChanceMul = 1f; // escala prob sala roja (2x2)
    [Range(0.25f, 2f)] public float room3ChanceMul = 1f; // escala prob sala azul (3x3)

    [Header("Reglas especiales")]
    public bool enemy2DrainsBattery = false;
    public bool enemy3ResistsLight = false;
    public bool torchesOnlyStartFew = true;

    [Header("Meta / Progresión")]
    [Range(1, 9)] public int targetFloors = 3; // el mapa clamp a 1..9

    [Header("Mapa (tamaño)")]
    [Range(0.6f, 1.6f)] public float mapSizeMul = 1f;
    [Tooltip("Impar y tope razonable (31, 41, 49...).")]
    public int maxMapSize = 49;
}