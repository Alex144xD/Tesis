using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Collections;

public class FragmentHUD : MonoBehaviour
{
    [Header("Referencias UI")]
    public TextMeshProUGUI counterText;
    public Image fragmentIcon;
    public GameObject rootHUD; 

    [Header("Configuración")]
    public int totalFragments = 9;
    [Range(0f, 1f)] public float minAlpha = 0.2f;
    [Range(0f, 1f)] public float maxAlpha = 1f;

    int lastCurrent = 0;

    private PlayerInventory _inv;
    private MultiFloorDynamicMapManager _map;

    void Awake()
    {
        if (!rootHUD) rootHUD = gameObject;

        if (rootHUD) rootHUD.SetActive(true);
    }

    void OnEnable()
    {
        StartCoroutine(DelayedInit());
    }

    IEnumerator DelayedInit()
    {
        yield return null;

        _inv = FindObjectOfType<PlayerInventory>(true);
        _map = FindObjectOfType<MultiFloorDynamicMapManager>(true);

        if (rootHUD) rootHUD.SetActive(true);


        totalFragments = ResolveTotalFragments();


        lastCurrent = (_inv != null) ? Mathf.Max(0, _inv.soulFragmentsCollected) : 0;
        UpdateFragmentCount(lastCurrent);

        if (_inv != null)
        {
            _inv.onFragmentCollected.RemoveListener(OnFragmentsChanged);
            _inv.onFragmentCollected.AddListener(OnFragmentsChanged);
        }
        if (_map != null)
        {
            _map.OnMapUpdated -= HandleMapUpdated;
            _map.OnMapUpdated += HandleMapUpdated;
        }
    }

    void OnDisable()
    {
        if (_inv != null)
            _inv.onFragmentCollected.RemoveListener(OnFragmentsChanged);
        if (_map != null)
            _map.OnMapUpdated -= HandleMapUpdated;
    }

    public void UpdateFragmentProgress(int current, int total)
    {
        totalFragments = Mathf.Max(1, total);
        UpdateFragmentCount(current);
    }

    public void UpdateFragmentCount(int current)
    {
        lastCurrent = Mathf.Max(0, current);

        if (counterText != null)
            counterText.text = $"{lastCurrent} / {totalFragments}";

        float progress = Mathf.Clamp01(totalFragments > 0 ? (float)lastCurrent / totalFragments : 0f);
        float alpha = Mathf.Lerp(minAlpha, maxAlpha, progress);

        if (fragmentIcon != null)
        {
            Color c = fragmentIcon.color;
            c.a = alpha;
            fragmentIcon.color = c;
        }
    }

    public void SetVisible(bool visible)
    {
        if (rootHUD) rootHUD.SetActive(true); 
    }

    void OnFragmentsChanged(int current, int totalFromInventory)
    {
        int total = ResolveTotalFragments(fallback: totalFromInventory);
        UpdateFragmentProgress(current, total);
    }

    void HandleMapUpdated()
    {
        int total = ResolveTotalFragments(fallback: totalFragments);
        UpdateFragmentProgress(lastCurrent, total);
    }

    int ResolveTotalFragments(int? fallback = null)
    {
        if (_map != null && _map.useSequentialFragments)
            return Mathf.Max(1, _map.targetFragments);

        if (_inv != null)
            return Mathf.Max(1, _inv.GetRequiredFragmentsToWin());

        return Mathf.Max(1, fallback ?? totalFragments);
    }
}