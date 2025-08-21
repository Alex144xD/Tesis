// Assets/Scripts/UI/OptionsMenuController.cs
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class OptionsMenuController : MonoBehaviour
{
    [Header("UI")]
    public Slider volumeSlider;           // 0..1
    public TMP_Text volumeValueText;      // opcional ("80%")
    public Slider sensitivitySlider;      // 0.1..3
    public TMP_Text sensitivityValueText; // opcional ("1.00x")

    [Header("Aparición")]
    public bool startHidden = true;

    // PlayerPrefs keys
    const string KEY_VOL = "opt_masterVol";
    const string KEY_SENS = "opt_mouseSens";

    void Awake()
    {
        if (startHidden) gameObject.SetActive(false);
    }

    void OnEnable()
    {
        // ----- VOLUMEN -----
        if (volumeSlider)
        {
            volumeSlider.minValue = 0f;
            volumeSlider.maxValue = 1f;

            float savedVol = PlayerPrefs.GetFloat(KEY_VOL, Mathf.Clamp01(AudioListener.volume));
            savedVol = Mathf.Clamp01(savedVol);

            // Aplica y muestra
            AudioListener.pause = false;
            AudioListener.volume = savedVol;
            volumeSlider.SetValueWithoutNotify(savedVol);
            UpdateVolumeLabel(savedVol);
        }

        // ----- SENSIBILIDAD -----
        if (sensitivitySlider)
        {
            sensitivitySlider.minValue = 0.1f;
            sensitivitySlider.maxValue = 3f;

            float savedSens = PlayerPrefs.GetFloat(KEY_SENS, 1f);
            savedSens = Mathf.Clamp(savedSens, 0.1f, 3f);

            sensitivitySlider.SetValueWithoutNotify(savedSens);
            UpdateSensitivityLabel(savedSens);
        }
    }

    // Hooks para OnValueChanged de los sliders (asígnalos en el inspector)
    public void OnVolumeChanged(float v)
    {
        v = Mathf.Clamp01(v);
        AudioListener.pause = false;
        AudioListener.volume = v;
        PlayerPrefs.SetFloat(KEY_VOL, v);
        UpdateVolumeLabel(v);
    }

    public void OnSensitivityChanged(float s)
    {
        s = Mathf.Clamp(s, 0.1f, 3f);
        PlayerPrefs.SetFloat(KEY_SENS, s);
        UpdateSensitivityLabel(s);
        // Nota: tu MouseLook debe leer PlayerPrefs.GetFloat("opt_mouseSens", 1f) cada frame (ya te pasé ese ajuste)
    }

    void UpdateVolumeLabel(float v)
    {
        if (volumeValueText) volumeValueText.text = Mathf.RoundToInt(v * 100f) + "%";
    }

    void UpdateSensitivityLabel(float s)
    {
        if (sensitivityValueText) sensitivityValueText.text = s.ToString("0.00") + "x";
    }

    // Botones del panel
    public void Open() => gameObject.SetActive(true);
    public void Close() => gameObject.SetActive(false);
    public void Toggle() => gameObject.SetActive(!gameObject.activeSelf);
}