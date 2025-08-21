using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Audio;
using UnityEngine.EventSystems;
using System.Collections;

[DisallowMultipleComponent]
public class OptionsHUD : MonoBehaviour
{
    [Header("Panel")]
    public CanvasGroup panel;                   // CanvasGroup del OptionsPanel
    public GameObject firstSelectedOnOpen;      // Botón/Slider a enfocar al abrir
    public float fadeDuration = 0.12f;

    [Header("Audio")]
    public AudioMixer mainMixer;                // Expone "MasterVol","MusicVol","SFXVol"
    public Slider masterSlider;                 // 0..1
    public Slider musicSlider;                  // 0..1
    public Slider sfxSlider;                    // 0..1
    [Tooltip("dB cuando slider=0 (suele -80dB)")]
    public float minDb = -80f;

    [Header("Gameplay")]
    public Slider sensitivitySlider;            // 0.1..10 por ejemplo
    public TMP_Text sensitivityLabel;
    public Toggle invertYToggle;

    [Header("Video")]
    public TMP_Dropdown qualityDropdown;        // Niveles de calidad de Unity
    public Toggle fullscreenToggle;
    public Toggle vSyncToggle;

    [Header("Idioma (opcional)")]
    public TMP_Dropdown languageDropdown;       // Llénalo si manejas localización

    [Header("Persistencia")]
    public bool loadOnStart = true;

    [Header("Debug")]
    public bool debugLogs = false;

    const string K_MASTER = "opt_master";
    const string K_MUSIC = "opt_music";
    const string K_SFX = "opt_sfx";
    const string K_SENS = "opt_sens";
    const string K_INVY = "opt_invy";
    const string K_QUAL = "opt_qual";
    const string K_FS = "opt_fs";
    const string K_VSYNC = "opt_vsync";
    const string K_LANG = "opt_lang";

    bool isOpen;
    float targetAlpha;

    void Awake()
    {
        if (!panel) panel = GetComponent<CanvasGroup>();
        if (panel)
        {
            panel.alpha = 0f;
            panel.interactable = false;
            panel.blocksRaycasts = false;
        }
        if (loadOnStart) LoadSettings(false);
        RefreshUIFromSettings();
    }

    void OnEnable()
    {
        // por si se entra a la escena ya abierta
        RefreshUIFromSettings();
    }

    void Update()
    {
        // actualizar label de sensibilidad en vivo
        if (sensitivityLabel && sensitivitySlider)
            sensitivityLabel.text = sensitivitySlider.value.ToString("0.0");
    }

    // ===== Open / Close =====
    public void Open()
    {
        isOpen = true;
        StopAllCoroutines();
        StartCoroutine(FadeTo(1f));
        if (firstSelectedOnOpen)
            EventSystem.current.SetSelectedGameObject(firstSelectedOnOpen);
    }

    public void Close()
    {
        isOpen = false;
        StopAllCoroutines();
        StartCoroutine(FadeTo(0f));
    }

    IEnumerator FadeTo(float a)
    {
        if (!panel) yield break;
        targetAlpha = a;
        float start = panel.alpha;
        float t = 0f;
        panel.blocksRaycasts = true; // para recibir eventos durante fade-in

        while (t < fadeDuration)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / fadeDuration);
            panel.alpha = Mathf.Lerp(start, a, k);
            yield return null;
        }
        panel.alpha = a;
        panel.interactable = (a > 0.999f);
        panel.blocksRaycasts = (a > 0.001f);
        if (!panel.interactable) EventSystem.current.SetSelectedGameObject(null);
    }

    // ===== Botones =====
    public void OnApply()
    {
        ApplySettingsFromUI();
        SaveSettings();
        if (debugLogs) Debug.Log("[OptionsHUD] Ajustes aplicados y guardados.");
    }

    public void OnResetDefaults()
    {
        // valores ejemplo
        SetMixerFrom01("MasterVol", 0.8f);
        SetMixerFrom01("MusicVol", 0.7f);
        SetMixerFrom01("SFXVol", 0.8f);

        QualitySettings.SetQualityLevel(2, true); // "Medium"
        QualitySettings.vSyncCount = 0;
        Screen.fullScreen = true;

        PlayerPrefs.DeleteKey(K_MASTER);
        PlayerPrefs.DeleteKey(K_MUSIC);
        PlayerPrefs.DeleteKey(K_SFX);
        PlayerPrefs.DeleteKey(K_SENS);
        PlayerPrefs.DeleteKey(K_INVY);
        PlayerPrefs.DeleteKey(K_QUAL);
        PlayerPrefs.DeleteKey(K_FS);
        PlayerPrefs.DeleteKey(K_VSYNC);
        PlayerPrefs.DeleteKey(K_LANG);

        LoadSettings(false);
        RefreshUIFromSettings();
    }

    public void OnBack()
    {
        // Solo cerrar, el PausePanel sigue detrás
        Close();
    }

    // ===== Persistencia =====
    void LoadSettings(bool applyToUI = true)
    {
        // Audio
        float m = PlayerPrefs.GetFloat(K_MASTER, 0.8f);
        float mu = PlayerPrefs.GetFloat(K_MUSIC, 0.7f);
        float s = PlayerPrefs.GetFloat(K_SFX, 0.8f);
        SetMixerFrom01("MasterVol", m);
        SetMixerFrom01("MusicVol", mu);
        SetMixerFrom01("SFXVol", s);

        // Gameplay
        float sens = PlayerPrefs.GetFloat(K_SENS, 2.5f);
        bool invY = PlayerPrefs.GetInt(K_INVY, 0) == 1;

        // Video
        int qual = PlayerPrefs.GetInt(K_QUAL, 2);
        bool fs = PlayerPrefs.GetInt(K_FS, 1) == 1;
        bool vs = PlayerPrefs.GetInt(K_VSYNC, 0) == 1;

        // Idioma
        int lang = PlayerPrefs.GetInt(K_LANG, 0);

        // Aplicar a sistemas
        QualitySettings.SetQualityLevel(Mathf.Clamp(qual, 0, QualitySettings.names.Length - 1), true);
        QualitySettings.vSyncCount = vs ? 1 : 0;
        Screen.fullScreen = fs;

        // Notificar a tu controlador de input (si tienes uno) la sensibilidad/invert
        // InputManager.Instance?.SetMouseSensitivity(sens);
        // InputManager.Instance?.SetInvertY(invY);

        if (applyToUI)
        {
            if (masterSlider) masterSlider.value = m;
            if (musicSlider) musicSlider.value = mu;
            if (sfxSlider) sfxSlider.value = s;
            if (sensitivitySlider) sensitivitySlider.value = sens;
            if (invertYToggle) invertYToggle.isOn = invY;
            if (qualityDropdown) qualityDropdown.value = qual;
            if (fullscreenToggle) fullscreenToggle.isOn = fs;
            if (vSyncToggle) vSyncToggle.isOn = vs;
            if (languageDropdown) languageDropdown.value = lang;
        }
    }

    void SaveSettings()
    {
        // Audio
        if (masterSlider) PlayerPrefs.SetFloat(K_MASTER, masterSlider.value);
        if (musicSlider) PlayerPrefs.SetFloat(K_MUSIC, musicSlider.value);
        if (sfxSlider) PlayerPrefs.SetFloat(K_SFX, sfxSlider.value);

        // Gameplay
        if (sensitivitySlider) PlayerPrefs.SetFloat(K_SENS, sensitivitySlider.value);
        if (invertYToggle) PlayerPrefs.SetInt(K_INVY, invertYToggle.isOn ? 1 : 0);

        // Video
        if (qualityDropdown) PlayerPrefs.SetInt(K_QUAL, qualityDropdown.value);
        if (fullscreenToggle) PlayerPrefs.SetInt(K_FS, fullscreenToggle.isOn ? 1 : 0);
        if (vSyncToggle) PlayerPrefs.SetInt(K_VSYNC, vSyncToggle.isOn ? 1 : 0);

        // Idioma
        if (languageDropdown) PlayerPrefs.SetInt(K_LANG, languageDropdown.value);

        PlayerPrefs.Save();
    }

    void ApplySettingsFromUI()
    {
        if (masterSlider) SetMixerFrom01("MasterVol", masterSlider.value);
        if (musicSlider) SetMixerFrom01("MusicVol", musicSlider.value);
        if (sfxSlider) SetMixerFrom01("SFXVol", sfxSlider.value);

        if (qualityDropdown) QualitySettings.SetQualityLevel(qualityDropdown.value, true);
        if (vSyncToggle) QualitySettings.vSyncCount = vSyncToggle.isOn ? 1 : 0;
        if (fullscreenToggle) Screen.fullScreen = fullscreenToggle.isOn;

        // InputManager.Instance?.SetMouseSensitivity(sensitivitySlider.value);
        // InputManager.Instance?.SetInvertY(invertYToggle.isOn);

        // Localización: llama a tu sistema si lo tienes
        // LocalizationService.Instance?.SetLanguage(languageDropdown.value);
    }

    void SetMixerFrom01(string exposedParam, float v01)
    {
        if (!mainMixer) return;
        float db = (v01 <= 0.0001f) ? minDb : Mathf.Lerp(minDb, 0f, Mathf.Log10(Mathf.Lerp(0.001f, 1f, v01)));
        mainMixer.SetFloat(exposedParam, db);
    }

    void RefreshUIFromSettings()
    {
        // vuelve a sincronizar UI con lo aplicado
        LoadSettings(true);
    }
}
