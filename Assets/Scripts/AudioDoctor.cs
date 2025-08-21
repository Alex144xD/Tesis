// Assets/Scripts/Audio/AudioDoctor.cs
using UnityEngine;

public class AudioDoctor : MonoBehaviour
{
    [Header("Beep de prueba")]
    public bool playTestOnStart = true;
    public float testFrequency = 440f;
    public float testDuration = 0.35f; // segundos
    public float testVolume = 1f;

    void Start()
    {
        // 1) Asegura volumen global operativo (por si quedó guardado en 0)
        PlayerPrefs.DeleteKey("opt_masterVol");    // limpia tu key de SettingsManager
        AudioListener.pause = false;
        AudioListener.volume = 1f;

        // 2) Si hay más de un AudioListener, desactiva los extra (Unity silencia si hay 2)
        var listeners = FindObjectsOfType<AudioListener>(true);
        for (int i = 1; i < listeners.Length; i++) listeners[i].enabled = false;

        // 3) Asegura que existe un AudioSource 2D para sonar el test
        var src = GetComponent<AudioSource>();
        if (!src) src = gameObject.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.loop = false;
        src.spatialBlend = 0f; // 2D
        src.dopplerLevel = 0f;
        src.volume = Mathf.Clamp01(testVolume);

        // 4) Beep de prueba sin depender de ningún clip del proyecto
        if (playTestOnStart)
        {
            var beep = CreateSineClip(testFrequency, testDuration, 44100);
            src.PlayOneShot(beep, 1f);
        }

        Debug.Log($"[AudioDoctor] Listeners={listeners.Length}, volume={AudioListener.volume}, paused={AudioListener.pause}");
    }

    AudioClip CreateSineClip(float freq, float dur, int sampleRate)
    {
        int samples = Mathf.Max(1, Mathf.RoundToInt(dur * sampleRate));
        float[] data = new float[samples];
        float inc = 2f * Mathf.PI * freq / sampleRate;
        for (int i = 0; i < samples; i++)
            data[i] = Mathf.Sin(inc * i) * testVolume;

        var clip = AudioClip.Create("BeepTest", samples, 1, sampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }
}