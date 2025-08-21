using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class GlobalMouseSensitivitySlider : MonoBehaviour
{
    public Slider slider;
    public TMP_Text valueLabel;          // opcional (muestra "1.25x")

    const string KEY = "opt_mouseSens";
    public float min = 0.1f;
    public float max = 3f;
    public float defaultValue = 1f;

    void Awake()
    {
        if (!slider) slider = GetComponentInChildren<Slider>();
    }

    void Start()
    {
        float v = PlayerPrefs.GetFloat(KEY, defaultValue);
        v = Mathf.Clamp(v, min, max);

        if (slider)
        {
            slider.minValue = min;
            slider.maxValue = max;
            slider.value = v;
            slider.onValueChanged.AddListener(OnChanged);
        }
        Apply(v);
    }

    void OnChanged(float v)
    {
        v = Mathf.Clamp(v, min, max);
        PlayerPrefs.SetFloat(KEY, v);
        Apply(v);
    }

    void Apply(float v)
    {
        // Guardamos y mostramos
        if (valueLabel) valueLabel.text = v.ToString("0.00") + "x";
        // Nada más: los scripts de cámara leerán este valor de PlayerPrefs
    }
}
