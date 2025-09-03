using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

[DisallowMultipleComponent]
public class CustomModeStoryPanel : MonoBehaviour
{
    public enum FillMode { Stretch, Fit, Cover } // NUEVO

    [Header("UI")]
    public TextMeshProUGUI titleText;
    public TextMeshProUGUI bodyText;
    public Button optionButtonPrefab;

    [Header("Historia")]
    public CustomModeStoryNode startNode;

    [Header("Aparición")]
    public bool autoOpenOnEnable = true;

    [Header("Fondos por nodo (múltiples capas)")]
    [Tooltip("Contenedor donde se crearán las capas (Images). Debe estar detrás del texto y estirado a toda la pantalla.")]
    public RectTransform backgroundContainer;
    [Tooltip("Plantilla opcional (desactivada) para instanciar cada capa. Si es null, se crearán Images simples.")]
    public Image backgroundImageTemplate;

    [Tooltip("Cómo rellenar el contenedor: Stretch (deforma), Fit (encaja), Cover (cubre recortando)")]
    public FillMode fillMode = FillMode.Cover; // por defecto: CUBRIR

    [Header("Layout (botones desde el centro hacia abajo)")]
    public float buttonHeight = 44f;
    public float spacing = 28f;
    public float sidePadding = 24f;
    public float maxButtonsWidth = 720f;
    public float firstButtonYOffset = 260f;
    public TextAlignmentOptions buttonTextAlign = TextAlignmentOptions.Midline;
    public Color buttonTextColor = new Color(1f, 0.84f, 0f);
    public Color buttonOutlineColor = Color.black;
    [Range(0, 1f)] public float buttonOutlineWidth = 0.22f;

    [Header("Aplicación")]
    public string gameplayScene = "Game";
    public bool autoLoadGameOnFinish = true;

    [Header("Límites finales")]
    public float mulMin = 0.6f, mulMax = 1.6f;
    [Tooltip("Mínimo y máximo de FRAGMENTOS objetivo (no pisos).")]
    public int fragmentsMin = 1, fragmentsMax = 9;

    [Header("Límites de tamaño de mapa (multiplicador)")]
    public float mapMulMin = 0.6f, mapMulMax = 1.6f;

    [Header("Debug")]
    public bool debugLogs = false;

    // Estado
    CustomModeStoryNode current;
    readonly List<GameObject> spawnedButtons = new List<GameObject>();

    // Acumuladores
    float accEnemyStat, accEnemyDensity, accBatteryDrain, accBatteryDensity, accFragmentDensity;
    int accFragments;
    float accMapSizePct;

    TriBool f_torchesFew = TriBool.Unset, f_enemy2Drain = TriBool.Unset, f_enemy3Resist = TriBool.Unset;
    bool _locked;

    RectTransform PanelRT => (RectTransform)transform;

    void Awake()
    {
        if (optionButtonPrefab && optionButtonPrefab.gameObject.activeSelf)
            optionButtonPrefab.gameObject.SetActive(false);

        if (backgroundImageTemplate && backgroundImageTemplate.gameObject.activeSelf)
            backgroundImageTemplate.gameObject.SetActive(false);
    }

    void OnEnable()
    {
        if (autoOpenOnEnable) Open();
    }

    void OnValidate()
    {
        buttonHeight = Mathf.Max(24f, buttonHeight);
        spacing = Mathf.Max(0f, spacing);
        sidePadding = Mathf.Max(0f, sidePadding);
        maxButtonsWidth = Mathf.Max(120f, maxButtonsWidth);
        firstButtonYOffset = Mathf.Max(0f, firstButtonYOffset);

        if (Application.isPlaying && spawnedButtons != null && spawnedButtons.Count > 0)
            LayoutButtonsFromCenterDown();

        fragmentsMin = Mathf.Clamp(fragmentsMin, 1, 9);
        fragmentsMax = Mathf.Clamp(fragmentsMax, fragmentsMin, 9);
    }

    public void Open()
    {
        ResetAcc();
        _locked = false;
        current = startNode;
        gameObject.SetActive(true);
        RenderCurrent();
    }

    public void Close() => gameObject.SetActive(false);

    // ====================== RENDER ======================
    void RenderCurrent()
    {
        for (int i = 0; i < spawnedButtons.Count; i++)
            if (spawnedButtons[i]) Destroy(spawnedButtons[i]);
        spawnedButtons.Clear();

        if (!current) { FinishAndApply(); return; }

        string narr = current.narrative ?? string.Empty;
        SplitFirstLine(narr, out string firstLine, out string rest);
        if (titleText) titleText.text = firstLine;
        if (bodyText) bodyText.text = string.IsNullOrEmpty(rest) ? (string.IsNullOrEmpty(firstLine) ? "<i>(Sin texto)</i>" : firstLine) : rest;

        ApplyBackgroundLayersForNode(current);

        if (current.options == null || current.options.Length == 0)
        {
            if (debugLogs) Debug.LogWarning("[Story] Nodo sin opciones, finalizando.");
            FinishAndApply();
            return;
        }

        foreach (var opt in current.options)
        {
            var captured = opt;
            Button btn = optionButtonPrefab
                ? Instantiate(optionButtonPrefab, transform)
                : CreateRuntimeButton(captured.text, transform);

            if (!btn.gameObject.activeSelf) btn.gameObject.SetActive(true);

            StyleButtonLabelText(btn.transform, captured.text);
            SetupButtonRectCenterAnchored(btn);

            btn.onClick.AddListener(() => OnChoose(captured));
            spawnedButtons.Add(btn.gameObject);
        }

        LayoutButtonsFromCenterDown();
    }

    // ====================== FONDOS MÚLTIPLES ======================
    void ApplyBackgroundLayersForNode(CustomModeStoryNode node)
    {
        if (!backgroundContainer) return;

        // Limpiar hijos (conserva template si es hijo directo)
        for (int i = backgroundContainer.childCount - 1; i >= 0; i--)
        {
            var child = backgroundContainer.GetChild(i);
            if (backgroundImageTemplate && child == backgroundImageTemplate.rectTransform) continue;
            Destroy(child.gameObject);
        }

        List<Sprite> sprites = null;
        if (node.backgroundSprites != null && node.backgroundSprites.Count > 0)
            sprites = node.backgroundSprites;
        else if (node.backgroundSprite != null)
            sprites = new List<Sprite> { node.backgroundSprite };

        if (sprites == null || sprites.Count == 0) return;

        // Medidas del contenedor
        var contSize = backgroundContainer.rect.size;
        if (contSize.x <= 0f || contSize.y <= 0f)
            contSize = new Vector2(Screen.width, Screen.height);

        for (int i = 0; i < sprites.Count; i++)
        {
            var sp = sprites[i];
            if (!sp) continue;

            Image img;
            if (backgroundImageTemplate)
            {
                img = Instantiate(backgroundImageTemplate, backgroundContainer);
                img.gameObject.SetActive(true);
            }
            else
            {
                var go = new GameObject($"BG_Layer_{i}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                img = go.GetComponent<Image>();
                go.transform.SetParent(backgroundContainer, false);
            }

            img.sprite = sp;
            img.color = node.bgTint;
            img.type = Image.Type.Simple;
            img.preserveAspect = false; // controlaremos el tamaño nosotros

            SizeImageToFill(img.rectTransform, sp, contSize, fillMode);
        }
    }

    // Coloca el RectTransform para Stretch/Fit/Cover manteniendo centrado
    void SizeImageToFill(RectTransform rt, Sprite sprite, Vector2 containerSize, FillMode mode)
    {
        if (!rt || !sprite) return;

        // ancla centrada para poder cambiar sizeDelta libremente
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);

        var sprSize = sprite.rect.size; // px del sprite
        if (sprSize.x <= 0f || sprSize.y <= 0f) sprSize = containerSize;

        float contW = containerSize.x;
        float contH = containerSize.y;
        float sprW = sprSize.x;
        float sprH = sprSize.y;

        float scaleX = contW / sprW;
        float scaleY = contH / sprH;

        Vector2 target;

        switch (mode)
        {
            case FillMode.Stretch:
                // deforma para llenar exacto
                target = containerSize;
                break;

            case FillMode.Fit:
                // encaja dentro manteniendo relación (pueden quedar bandas)
                float fit = Mathf.Min(scaleX, scaleY);
                target = new Vector2(sprW * fit, sprH * fit);
                break;

            default: // Cover
                // cubre todo manteniendo relación (puede recortar exceso)
                float cover = Mathf.Max(scaleX, scaleY);
                target = new Vector2(sprW * cover, sprH * cover);
                break;
        }

        rt.sizeDelta = target;
        rt.anchoredPosition = Vector2.zero;
    }

    // ====================== BOTONES / LAYOUT ======================
    void SplitFirstLine(string s, out string firstLine, out string rest)
    {
        if (string.IsNullOrEmpty(s)) { firstLine = ""; rest = ""; return; }
        int idx = s.IndexOf('\n');
        if (idx < 0) { firstLine = s; rest = ""; }
        else { firstLine = s.Substring(0, idx).TrimEnd(); rest = s.Substring(idx + 1).TrimStart(); }
    }

    void SetupButtonRectCenterAnchored(Button btn)
    {
        var rt = (RectTransform)btn.transform;
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(rt.sizeDelta.x, buttonHeight);

        var le = btn.GetComponent<LayoutElement>();
        if (!le) le = btn.gameObject.AddComponent<LayoutElement>();
        le.minHeight = buttonHeight;
        le.preferredHeight = buttonHeight;
        le.flexibleWidth = 0f;
        le.flexibleHeight = 0f;
    }

    void LayoutButtonsFromCenterDown()
    {
        float panelWidth = PanelRT.rect.width;
        if (panelWidth <= 0f) panelWidth = 800f;

        float width = Mathf.Min(panelWidth - sidePadding * 2f, maxButtonsWidth);

        for (int i = 0; i < spawnedButtons.Count; i++)
        {
            var go = spawnedButtons[i];
            if (!go) continue;

            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(width, buttonHeight);

            float y = -firstButtonYOffset - i * (buttonHeight + spacing);
            rt.anchoredPosition = new Vector2(0f, y);
        }

        if (debugLogs) Debug.Log($"[Story] Layout centro->abajo: {spawnedButtons.Count} botones, width={width}, startOffset={firstButtonYOffset}, spacing={spacing}");
    }

    Button CreateRuntimeButton(string text, Transform parent)
    {
        var go = new GameObject("OptionButton", typeof(RectTransform), typeof(Image), typeof(Button));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);

        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = new Vector2(300f, buttonHeight);

        var img = go.GetComponent<Image>();
        img.type = Image.Type.Sliced;
        img.color = Color.white;

        var tgo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        var trt = (RectTransform)tgo.transform;
        trt.SetParent(go.transform, false);
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(16, 6); trt.offsetMax = new Vector2(-16, -6);

        var tmp = tgo.GetComponent<TextMeshProUGUI>();
        tmp.enableAutoSizing = true; tmp.fontSizeMin = 16; tmp.fontSizeMax = 28;
        tmp.alignment = buttonTextAlign;
        tmp.text = text;
        tmp.color = buttonTextColor;

        Material mat = tmp.fontMaterial;
        if (mat != null)
        {
            mat.SetFloat(TMPro.ShaderUtilities.ID_OutlineWidth, buttonOutlineWidth);
            mat.SetColor(TMPro.ShaderUtilities.ID_OutlineColor, buttonOutlineColor);
        }

        var btn = go.GetComponent<Button>();
        return btn;
    }

    void StyleButtonLabelText(Transform root, string text)
    {
        if (!root) return;

        var tmps = root.GetComponentsInChildren<TextMeshProUGUI>(true);
        foreach (var t in tmps)
        {
            t.enableAutoSizing = true;
            t.fontSizeMin = 16;
            t.fontSizeMax = 28;
            t.alignment = buttonTextAlign;
            t.text = text;
            t.color = buttonTextColor;

            Material mat = t.fontMaterial;
            if (mat != null)
            {
                mat.SetFloat(TMPro.ShaderUtilities.ID_OutlineWidth, buttonOutlineWidth);
                mat.SetColor(TMPro.ShaderUtilities.ID_OutlineColor, buttonOutlineColor);
            }
        }

        var ugui = root.GetComponentsInChildren<Text>(true);
        foreach (var u in ugui)
        {
            u.alignment = TextAnchor.MiddleCenter;
            u.text = text;
            u.color = buttonTextColor;

            var shadow = u.gameObject.GetComponent<Shadow>();
            if (!shadow) shadow = u.gameObject.AddComponent<Shadow>();
            shadow.effectColor = Color.black;
            shadow.effectDistance = new Vector2(1f, -1f);
        }
    }

    // ====================== ELECCIÓN ======================
    void OnChoose(CustomModeStoryNode.Option opt)
    {
        if (_locked) return;
        _locked = true;

        ApplyEffects(opt.effects);
        current = opt.next;

        if (debugLogs) Debug.Log($"[Story] Elegida: {opt.text}");
        RenderCurrent();

        if (current != null) _locked = false;
    }

    // === Efectos / Final ===
    void ApplyEffects(StoryEffects e)
    {
        if (e == null) return;
        accEnemyStat += e.enemyStat;
        accEnemyDensity += e.enemyDensity;
        accBatteryDrain += e.batteryDrain;
        accBatteryDensity += e.batteryDensity;
        accFragmentDensity += e.fragmentDensity;

        accFragments += e.fragmentsDelta;
        accMapSizePct += e.mapSizePercent;

        if (e.torchesOnlyStartFew != TriBool.Unset) f_torchesFew = e.torchesOnlyStartFew;
        if (e.enemy2DrainsBattery != TriBool.Unset) f_enemy2Drain = e.enemy2DrainsBattery;
        if (e.enemy3ResistsLight != TriBool.Unset) f_enemy3Resist = e.enemy3ResistsLight;
    }

    void FinishAndApply()
    {
        var p = ScriptableObject.CreateInstance<CustomModeProfile>();
        p.enemyStatMul = Mathf.Clamp(1f + accEnemyStat, mulMin, mulMax);
        p.enemyDensityMul = Mathf.Clamp(1f + accEnemyDensity, mulMin, mulMax);
        p.batteryDrainMul = Mathf.Clamp(1f + accBatteryDrain, mulMin, mulMax);
        p.batteryDensityMul = Mathf.Clamp(1f + accBatteryDensity, mulMin, mulMax);
        p.fragmentDensityMul = Mathf.Clamp(1f + accFragmentDensity, mulMin, mulMax);

        p.mapSizeMul = Mathf.Clamp(1f + accMapSizePct, mapMulMin, mapMulMax);
        p.maxMapSize = 49;

        p.targetFloors = Mathf.Clamp(1 + accFragments, fragmentsMin, fragmentsMax);

        if (f_torchesFew != TriBool.Unset) p.torchesOnlyStartFew = (f_torchesFew == TriBool.True);
        if (f_enemy2Drain != TriBool.Unset) p.enemy2DrainsBattery = (f_enemy2Drain == TriBool.True);
        if (f_enemy3Resist != TriBool.Unset) p.enemy3ResistsLight = (f_enemy3Resist == TriBool.True);

        if (CustomModeRuntime.Instance == null)
            new GameObject("CustomModeRuntime").AddComponent<CustomModeRuntime>();
        CustomModeRuntime.Instance.SetProfile(p);

        if (debugLogs)
        {
            Debug.Log($"[Story] Perfil final -> " +
                $"enemyStatMul={p.enemyStatMul:F2}, enemyDensityMul={p.enemyDensityMul:F2}, " +
                $"batteryDrainMul={p.batteryDrainMul:F2}, batteryDensityMul={p.batteryDensityMul:F2}, " +
                $"fragmentDensityMul={p.fragmentDensityMul:F2}, mapSizeMul={p.mapSizeMul:F2}, " +
                $"maxMapSize={p.maxMapSize}, targetFragments={p.targetFloors}, " +
                $"flags: torchesFew={p.torchesOnlyStartFew}, e2Drain={p.enemy2DrainsBattery}, e3Resist={p.enemy3ResistsLight}");
        }

        if (GameManager.Instance) GameManager.Instance.StartCustomMode();

        if (autoLoadGameOnFinish && !string.IsNullOrEmpty(gameplayScene))
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene(gameplayScene);
        }

        Close();
    }

    void ResetAcc()
    {
        accEnemyStat = accEnemyDensity = accBatteryDrain = accBatteryDensity = accFragmentDensity = 0f;
        accFragments = 0;
        accMapSizePct = 0f;
        f_torchesFew = f_enemy2Drain = f_enemy3Resist = TriBool.Unset;
    }
}