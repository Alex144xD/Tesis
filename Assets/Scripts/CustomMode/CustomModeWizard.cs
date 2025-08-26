using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using UnityEngine.UI;

public class CustomModeWizard : MonoBehaviour
{
    [Header("UI (conéctalo luego)")]
    public TMP_InputField shipsRaidedInput; 
    public TMP_InputField chestsFoundInput; 
    public Toggle sharedLootToggle;         
    public Slider monsterFearSlider;        

    [Header("Siguiente escena")]
    public string gameplayScene = "Game";

    public void OnConfirm()
    {
 
        int ships = ParseInt(shipsRaidedInput ? shipsRaidedInput.text : null, 0);
        int chests = ParseInt(chestsFoundInput ? chestsFoundInput.text : null, 0);
        bool sharedLoot = sharedLootToggle && sharedLootToggle.isOn;
        float fear = monsterFearSlider ? monsterFearSlider.value : 0.5f;

        
        float greed = Mathf.Clamp01(chests / 50f); 
        float ruth = Mathf.Clamp01(ships / 50f); 
        float ethics = sharedLoot ? -0.15f : +0.15f;

       
        float fearMulEnemies = Mathf.Lerp(1.1f, 0.85f, fear);
        float fearMulBattery = Mathf.Lerp(0.9f, 1.25f, fear);


        var profile = ScriptableObject.CreateInstance<CustomModeProfile>();
        profile.enemyStatMul = Mathf.Clamp(1f + 0.3f * ruth + 0.2f * greed + ethics, 0.7f, 1.7f);
        profile.enemyDensityMul = Mathf.Clamp(fearMulEnemies * (1f + 0.2f * ruth), 0.6f, 1.6f);
        profile.batteryDrainMul = Mathf.Clamp(fearMulBattery * (1f + 0.15f * greed), 0.6f, 1.6f);
        profile.batteryDensityMul = Mathf.Clamp(1f - 0.25f * greed + (sharedLoot ? 0.1f : 0f), 0.6f, 1.4f);
        profile.fragmentDensityMul = Mathf.Clamp(1f + 0.1f * ruth, 0.8f, 1.3f);


        profile.enemy2DrainsBattery = (ruth + greed) > 0.8f;
        profile.enemy3ResistsLight = fear < 0.35f;

        profile.targetFloors = Mathf.Clamp(3 + Mathf.RoundToInt((ruth + greed) * 3f), 3, 9);

       
        if (CustomModeRuntime.Instance == null)
            new GameObject("CustomModeRuntime").AddComponent<CustomModeRuntime>();


        CustomModeRuntime.Instance.SetProfile(profile);


        if (GameManager.Instance) GameManager.Instance.StartCustomMode();

        if (!string.IsNullOrEmpty(gameplayScene))
            SceneManager.LoadScene(gameplayScene);
    }

    int ParseInt(string s, int def)
    {
        if (int.TryParse(s, out int v)) return Mathf.Max(0, v);
        return def;
    }
}