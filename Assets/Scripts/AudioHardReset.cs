using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AudioHardReset : MonoBehaviour
{
    void Start()
    {
        // Borra el volumen guardado por SettingsManager u OptionsettingsManager u Options
        PlayerPrefs.DeleteKey("opt_masterVol");

        // Asegura que el sistema de audio no esté pausado y sube volumen global
        AudioListener.pause = false;
        AudioListener.volume = 1f;

        Debug.Log("[AudioHardReset] opt_masterVol borrado. AudioListener.volume=1");
        Destroy(this); // ya no hace falta después del primer Play
    }
}

