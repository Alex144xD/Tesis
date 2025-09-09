using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;
using System.Collections;
using UnityEngine.Events;

[DisallowMultipleComponent]
public class CustomModeHUD : MonoBehaviour
{
    [Header("Panel")]
    public CanvasGroup panel;                     
    public GameObject firstSelectedOnOpen;
    public float fadeDuration = 0.12f;

    [Header("Opciones de modo")]
    public Slider levelsSlider;                   
    public TMP_Text levelsLabel;
    public Toggle randomSeedToggle;
    public TMP_InputField seedInput;              
    public TMP_Dropdown difficultyDropdown;       
    public Toggle permadeathToggle;             
    public Toggle miniMapToggle;                 
    public Toggle limitedStaminaToggle;         

    [Header("Acciones")]
    public Button startButton;
    public Button backButton;

    [Header("Eventos")]
    public UnityEvent<CustomModeConfig> onApplyConfig; 

    [Header("Persistencia")]
    public bool loadOnStart = true;

    [Header("Debug")]
    public bool debugLogs = false;

    const string K_CM_LVLS = "cm_lvls";
    const string K_CM_RND = "cm_rnd";
    const string K_CM_SEED = "cm_seed";
    const string K_CM_DIFF = "cm_diff";
    const string K_CM_PERMA = "cm_perma";
    const string K_CM_MM = "cm_minimap";
    const string K_CM_STAM = "cm_limitedstamina";

    bool isOpen;

    void Awake()
    {
        if (!panel) panel = GetComponent<CanvasGroup>();
        if (panel)
        {
            panel.alpha = 0f;
            panel.interactable = false;
            panel.blocksRaycasts = false;
        }
        if (loadOnStart) LoadConfig(true);
        WireUI();
        UpdateLabels();
    }

    void OnEnable()
    {
        WireUI();
        UpdateLabels();
    }

    void WireUI()
    {
        if (levelsSlider) levelsSlider.onValueChanged.AddListener(_ => UpdateLabels());
        if (seedInput) seedInput.onValueChanged.AddListener(_ => ValidateSeed());
        if (randomSeedToggle) randomSeedToggle.onValueChanged.AddListener(_ => ToggleSeedInput());
        if (startButton) startButton.onClick.AddListener(OnStart);
        if (backButton) backButton.onClick.AddListener(Close);
    }

    void UpdateLabels()
    {
        if (levelsLabel && levelsSlider)
            levelsLabel.text = Mathf.RoundToInt(levelsSlider.value).ToString();
        ToggleSeedInput();
    }

    void ToggleSeedInput()
    {
        if (!seedInput || !randomSeedToggle) return;
        bool editable = !randomSeedToggle.isOn;
        seedInput.interactable = editable;
        seedInput.placeholder.color = new Color(seedInput.placeholder.color.r, seedInput.placeholder.color.g, seedInput.placeholder.color.b, editable ? 1f : 0.35f);
    }

    void ValidateSeed()
    {
        if (!seedInput) return;
        
    }

  
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

    System.Collections.IEnumerator FadeTo(float a)
    {
        if (!panel) yield break;
        float start = panel.alpha;
        float t = 0f;
        panel.blocksRaycasts = true;

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

    public void OnStart()
    {
        var cfg = GatherConfigFromUI();
        SaveConfig(cfg);

   
        if (GameManager.Instance) GameManager.Instance.StartCustomMode();

        // Lanza tu lógica (generador, etc.)
        onApplyConfig?.Invoke(cfg);

        if (debugLogs)
            Debug.Log($"[CustomModeHUD] START -> {cfg}");

    
        Close();
    }

    CustomModeConfig GatherConfigFromUI()
    {
        var cfg = new CustomModeConfig();
        cfg.levelsOrFragments = levelsSlider ? Mathf.RoundToInt(levelsSlider.value) : 3;
        cfg.randomSeed = randomSeedToggle && randomSeedToggle.isOn;

        if (!cfg.randomSeed && seedInput && !string.IsNullOrEmpty(seedInput.text))
            cfg.seed = seedInput.text.Trim();
        else
            cfg.seed = System.DateTime.Now.Ticks.ToString(); 

        cfg.difficulty = difficultyDropdown ? difficultyDropdown.value : 1;
        cfg.permadeath = permadeathToggle && permadeathToggle.isOn;
        cfg.miniMap = miniMapToggle && miniMapToggle.isOn;
        cfg.limitedStamina = limitedStaminaToggle && limitedStaminaToggle.isOn;

        return cfg;
    }


    void LoadConfig(bool applyToUI)
    {
        int lvls = PlayerPrefs.GetInt(K_CM_LVLS, 3);
        bool rnd = PlayerPrefs.GetInt(K_CM_RND, 1) == 1;
        string sd = PlayerPrefs.GetString(K_CM_SEED, "");
        int diff = PlayerPrefs.GetInt(K_CM_DIFF, 1);
        bool per = PlayerPrefs.GetInt(K_CM_PERMA, 0) == 1;
        bool mm = PlayerPrefs.GetInt(K_CM_MM, 1) == 1;
        bool st = PlayerPrefs.GetInt(K_CM_STAM, 0) == 1;

        if (applyToUI)
        {
            if (levelsSlider) levelsSlider.value = lvls;
            if (randomSeedToggle) randomSeedToggle.isOn = rnd;
            if (seedInput) seedInput.text = sd;
            if (difficultyDropdown) difficultyDropdown.value = diff;
            if (permadeathToggle) permadeathToggle.isOn = per;
            if (miniMapToggle) miniMapToggle.isOn = mm;
            if (limitedStaminaToggle) limitedStaminaToggle.isOn = st;
        }
    }

    void SaveConfig(CustomModeConfig cfg)
    {
        PlayerPrefs.SetInt(K_CM_LVLS, cfg.levelsOrFragments);
        PlayerPrefs.SetInt(K_CM_RND, cfg.randomSeed ? 1 : 0);
        PlayerPrefs.SetString(K_CM_SEED, cfg.seed ?? "");
        PlayerPrefs.SetInt(K_CM_DIFF, cfg.difficulty);
        PlayerPrefs.SetInt(K_CM_PERMA, cfg.permadeath ? 1 : 0);
        PlayerPrefs.SetInt(K_CM_MM, cfg.miniMap ? 1 : 0);
        PlayerPrefs.SetInt(K_CM_STAM, cfg.limitedStamina ? 1 : 0);
        PlayerPrefs.Save();
    }
}

[System.Serializable]
public class CustomModeConfig
{
    public int levelsOrFragments = 3;
    public bool randomSeed = true;
    public string seed = "";
    public int difficulty = 1; 
    public bool permadeath = false;
    public bool miniMap = true;
    public bool limitedStamina = false;

    public override string ToString()
    {
        return $"Lvls={levelsOrFragments}, Rnd={randomSeed}, Seed='{seed}', Diff={difficulty}, Perma={permadeath}, MiniMap={miniMap}, LimStam={limitedStamina}";
    }
}