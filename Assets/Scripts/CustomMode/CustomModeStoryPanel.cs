using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

[DisallowMultipleComponent]
public class CustomModeStoryPanel : MonoBehaviour
{
    [Header("UI mínimos")]
    public TextMeshProUGUI bodyText;          // Texto de historia/pregunta
    public Button optionButtonPrefab;         // Prefab de botón; si es null, creo uno simple

    [Header("Historia")]
    public CustomModeStoryNode startNode;     // Primer nodo

    [Header("Aparición")]
    public bool autoOpenOnEnable = true;

    [Header("Layout manual")]
    public float buttonHeight = 48f;
    public float spacing = 8f;
    public float paddingLeft = 12f;
    public float paddingTop = 12f;
    public float paddingRight = 12f;          // se respeta para calcular ancho
    // Nota: el contenedor es el mismo GameObject con este script

    [Header("Aplicación")]
    public string gameplayScene = "Game";
    public bool autoLoadGameOnFinish = true;

    [Header("Límites finales")]
    public float mulMin = 0.6f, mulMax = 1.6f;
    public int floorsMin = 1, floorsMax = 9;

    [Header("Debug")]
    public bool debugLogs = false;

    // Estado
    CustomModeStoryNode current;
    readonly List<GameObject> spawnedButtons = new List<GameObject>();

    // Acumuladores
    float accEnemyStat, accEnemyDensity, accBatteryDrain, accBatteryDensity, accFragmentDensity;
    int accFloors;
    TriBool f_torchesFew = TriBool.Unset, f_enemy2Drain = TriBool.Unset, f_enemy3Resist = TriBool.Unset;

    RectTransform PanelRT => (RectTransform)transform;

    void Awake()
    {
        // Mantén oculta la PLANTILLA para no verla duplicada en escena
        if (optionButtonPrefab && optionButtonPrefab.gameObject.activeSelf)
            optionButtonPrefab.gameObject.SetActive(false);
    }

    void OnEnable()
    {
        if (autoOpenOnEnable) Open();
    }

    // === API ===
    public void Open()
    {
        ResetAcc();
        current = startNode;
        gameObject.SetActive(true);
        RenderCurrent();
    }

    public void Close()
    {
        gameObject.SetActive(false);
    }

    // === Render ===
    void RenderCurrent()
    {
        // Limpia lo instanciado
        for (int i = 0; i < spawnedButtons.Count; i++)
            if (spawnedButtons[i]) Destroy(spawnedButtons[i]);
        spawnedButtons.Clear();

        if (!current)
        {
            FinishAndApply();
            return;
        }

        if (bodyText)
            bodyText.text = string.IsNullOrEmpty(current.narrative)
                ? "<i>(Este nodo no tiene texto en 'narrative')</i>"
                : current.narrative;

        if (current.options == null || current.options.Length == 0)
        {
            if (debugLogs) Debug.LogWarning("[Story] Nodo sin opciones, finalizando.");
            FinishAndApply();
            return;
        }

        if (debugLogs) Debug.Log($"[Story] Opciones en el nodo: {current.options.Length}");

        // Crea un botón por opción como hijo de este Panel (layout manual)
        foreach (var opt in current.options)
        {
            var captured = opt; // evitar cierre

            Button btn = optionButtonPrefab
                ? Instantiate(optionButtonPrefab, transform)
                : CreateRuntimeButton(captured.text, transform);

            if (!btn.gameObject.activeSelf) btn.gameObject.SetActive(true);

            AssignAllLabelsText(btn.transform, captured.text);

            // Asegurar rect para layout manual
            SetupButtonRectForManualLayout(btn);

            btn.onClick.AddListener(() => OnChoose(captured));
            spawnedButtons.Add(btn.gameObject);

            if (debugLogs) Debug.Log("[Story] Botón creado: " + captured.text);
        }

        // Apilar por código
        LayoutButtonsManually();
    }

    // Asigna el texto a todos los labels encontrados en el botón
    void AssignAllLabelsText(Transform root, string text)
    {
        if (!root) return;
        int hits = 0;

        var tmps = root.GetComponentsInChildren<TextMeshProUGUI>(true);
        for (int i = 0; i < tmps.Length; i++) { tmps[i].text = text; hits++; }

        var ugui = root.GetComponentsInChildren<Text>(true);
        for (int i = 0; i < ugui.Length; i++) { ugui[i].text = text; hits++; }

        if (hits == 0 && debugLogs)
            Debug.LogWarning("[Story] El prefab instanciado no tiene TextMeshProUGUI ni Text como hijos.");
        else if (debugLogs)
            Debug.Log($"[Story] Etiquetas actualizadas: {hits}.");
    }

    // Configura anclas/pivote/tamaño para que el layout manual funcione
    void SetupButtonRectForManualLayout(Button btn)
    {
        var rt = (RectTransform)btn.transform;
        // Anclado al TOP-LEFT (coordenadas en pixeles con y hacia abajo)
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        // width lo calculamos luego; aquí dejamos una altura base
        rt.sizeDelta = new Vector2(rt.sizeDelta.x, buttonHeight);

        // Asegura un LayoutElement si luego quieres usar layouts mixtos
        var le = btn.GetComponent<LayoutElement>();
        if (!le) le = btn.gameObject.AddComponent<LayoutElement>();
        le.minHeight = buttonHeight;
        le.preferredHeight = buttonHeight;
        le.flexibleWidth = 0f;
        le.flexibleHeight = 0f;
    }

    // Apila los botones manualmente con padding/spacing
    void LayoutButtonsManually()
    {
        float panelWidth = PanelRT.rect.width;
        // Si el rect aún no está listo (p.ej. al entrar en Play), provee un fallback
        if (panelWidth <= 0f) panelWidth = 800f;

        float x = paddingLeft;
        float y = paddingTop; // comenzamos desde el top dentro del panel
        float usableWidth = Mathf.Max(0f, panelWidth - paddingLeft - paddingRight);

        for (int i = 0; i < spawnedButtons.Count; i++)
        {
            var go = spawnedButtons[i];
            if (!go) continue;

            var rt = (RectTransform)go.transform;
            // set size (ancho definido por padding, alto fijo)
            rt.sizeDelta = new Vector2(usableWidth, buttonHeight);
            // posición top-left (como anclas y pivot están al top-left)
            rt.anchoredPosition = new Vector2(x, -y);

            y += buttonHeight + spacing;
        }

        if (debugLogs) Debug.Log($"[Story] Layout manual aplicado. botones={spawnedButtons.Count}, ancho={usableWidth}, startY={paddingTop}");
    }

    Button CreateRuntimeButton(string text, Transform parent)
    {
        var go = new GameObject("OptionButton", typeof(RectTransform), typeof(Image), typeof(Button));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);

        // Anclado para layout manual (se ajustará en SetupButtonRectForManualLayout)
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.sizeDelta = new Vector2(200f, buttonHeight);

        var img = go.GetComponent<Image>();
        img.color = new Color(0.18f, 0.18f, 0.18f, 1f);

        var tgo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        var trt = (RectTransform)tgo.transform;
        trt.SetParent(go.transform, false);
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(12, 8); trt.offsetMax = new Vector2(-12, -8);

        var tmp = tgo.GetComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = 22f;
        tmp.enableAutoSizing = true; tmp.fontSizeMin = 18f; tmp.fontSizeMax = 24f;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;

        var btn = go.GetComponent<Button>();
        var cb = btn.colors;
        cb.normalColor = img.color;
        cb.highlightedColor = new Color(0.24f, 0.24f, 0.24f, 1f);
        cb.pressedColor = new Color(0.15f, 0.15f, 0.15f, 1f);
        cb.selectedColor = cb.highlightedColor;
        cb.disabledColor = new Color(0.1f, 0.1f, 0.1f, 0.6f);
        btn.colors = cb;

        var le = go.AddComponent<LayoutElement>();
        le.minHeight = buttonHeight; le.preferredHeight = buttonHeight;

        return btn;
    }

    void OnChoose(CustomModeStoryNode.Option opt)
    {
        ApplyEffects(opt.effects);
        current = opt.next;
        if (debugLogs) Debug.Log($"[Story] Elegida: {opt.text}");
        RenderCurrent();
    }

    // === Acumular efectos ===
    void ApplyEffects(StoryEffects e)
    {
        if (e == null) return;
        accEnemyStat += e.enemyStat;
        accEnemyDensity += e.enemyDensity;
        accBatteryDrain += e.batteryDrain;
        accBatteryDensity += e.batteryDensity;
        accFragmentDensity += e.fragmentDensity;
        accFloors += e.floorsDelta;

        if (e.torchesOnlyStartFew != TriBool.Unset) f_torchesFew = e.torchesOnlyStartFew;
        if (e.enemy2DrainsBattery != TriBool.Unset) f_enemy2Drain = e.enemy2DrainsBattery;
        if (e.enemy3ResistsLight != TriBool.Unset) f_enemy3Resist = e.enemy3ResistsLight;
    }

    // === Terminar y aplicar perfil ===
    void FinishAndApply()
    {
        var p = ScriptableObject.CreateInstance<CustomModeProfile>();
        p.enemyStatMul = Mathf.Clamp(1f + accEnemyStat, mulMin, mulMax);
        p.enemyDensityMul = Mathf.Clamp(1f + accEnemyDensity, mulMin, mulMax);
        p.batteryDrainMul = Mathf.Clamp(1f + accBatteryDrain, mulMin, mulMax);
        p.batteryDensityMul = Mathf.Clamp(1f + accBatteryDensity, mulMin, mulMax);
        p.fragmentDensityMul = Mathf.Clamp(1f + accFragmentDensity, mulMin, mulMax);
        p.targetFloors = Mathf.Clamp(3 + accFloors, floorsMin, floorsMax);

        if (f_torchesFew != TriBool.Unset) p.torchesOnlyStartFew = (f_torchesFew == TriBool.True);
        if (f_enemy2Drain != TriBool.Unset) p.enemy2DrainsBattery = (f_enemy2Drain == TriBool.True);
        if (f_enemy3Resist != TriBool.Unset) p.enemy3ResistsLight = (f_enemy3Resist == TriBool.True);

        if (CustomModeRuntime.Instance == null)
            new GameObject("CustomModeRuntime").AddComponent<CustomModeRuntime>();
        CustomModeRuntime.Instance.SetProfile(p);

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
        accFloors = 0;
        f_torchesFew = f_enemy2Drain = f_enemy3Resist = TriBool.Unset;
    }
}