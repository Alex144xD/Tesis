using UnityEngine;

[CreateAssetMenu(fileName = "CustomModeProfile", menuName = "Game/Custom Mode Profile")]
public class CustomModeProfile : ScriptableObject
{
    [Header("Dificultad global")]
    [Range(0.5f, 2f)] public float enemyStatMul = 1f;
    [Range(0.5f, 2f)] public float batteryDrainMul = 1f;
    [Range(0.5f, 2f)] public float enemyDensityMul = 1f;
    [Range(0.5f, 2f)] public float batteryDensityMul = 1f;
    [Range(0.5f, 2f)] public float fragmentDensityMul = 1f;

    [Header("Reglas especiales")]
    public bool enemy2DrainsBattery = false;
    public bool enemy3ResistsLight = false;
    public bool torchesOnlyStartFew = true;

    [Header("Meta / Progresión")]
    [Range(1, 9)] public int targetFloors = 3;

    [Header("Mapa (tamaño)")]
    [Range(0.6f, 1.6f)] public float mapSizeMul = 1f;  
    [Tooltip("Límite superior impar para evitar que el Start del mapa lo suba a +1. 49 respeta tu 'no pasar de 50'.")]
    public int maxMapSize = 49;                       
}