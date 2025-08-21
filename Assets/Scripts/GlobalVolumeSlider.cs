using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class GlobalVolumeSlider : MonoBehaviour
{
    public Slider slider;
    public TMP_Text valueLabel; // opcional: muestra "80%"

    const string KEY_VOL = "opt_masterVol";

    void Awake()
    {
        if (!slider) slider = GetComponentInChildren<Slider>();
    }

    void Start()
    {
        float v = PlayerPrefs.HasKey(KEY_VOL) ? PlayerPrefs.GetFloat(KEY_VOL) : AudioListener.volume;
        v = Mathf.Clamp01(v);
        AudioListener.pause = false;
        AudioListener.volume = v;

        if (slider)
        {
            slider.minValue = 0f; slider.maxValue = 1f;
            slider.value = v;
            slider.onValueChanged.AddListener(OnChanged);
        }
        UpdateLabel(v);
    }

    void OnChanged(float v)
    {
        v = Mathf.Clamp01(v);
        AudioListener.volume = v;           // baja/sube TODO el audio del juego
        PlayerPrefs.SetFloat(KEY_VOL, v);   // guarda para próximas escenas
        UpdateLabel(v);
    }

    void UpdateLabel(float v)
    {
        if (valueLabel) valueLabel.text = Mathf.RoundToInt(v * 100f) + "%";
    }
}