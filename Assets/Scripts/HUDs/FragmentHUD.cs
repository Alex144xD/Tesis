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
    public float minAlpha = 0.2f;
    public float maxAlpha = 1f;

    int lastCurrent = 0;

    void Awake()
    {

        if (!rootHUD) rootHUD = gameObject;
    }

    void OnEnable()
    {

        StartCoroutine(DelayedInit());
    }

    IEnumerator DelayedInit()
    {

        yield return null;

        bool inCustom = (GameManager.Instance != null && GameManager.Instance.IsInCustomMode());

        if (rootHUD) rootHUD.SetActive(inCustom);


        var inv = FindObjectOfType<PlayerInventory>();
        if (inv != null)
        {

            totalFragments = Mathf.Max(1, inv.totalLevels);


            lastCurrent = Mathf.Max(0, inv.soulFragmentsCollected);

            UpdateFragmentCount(lastCurrent);


            inv.onFragmentCollected.RemoveListener(OnFragmentsChanged); 
            inv.onFragmentCollected.AddListener(OnFragmentsChanged);
        }
        else
        {

            UpdateFragmentCount(0);
        }
    }

    void OnDisable()
    {

        var inv = FindObjectOfType<PlayerInventory>();
        if (inv != null)
            inv.onFragmentCollected.RemoveListener(OnFragmentsChanged);
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
        if (rootHUD) rootHUD.SetActive(visible);
    }


    void OnFragmentsChanged(int current, int total)
    {
        UpdateFragmentProgress(current, total);
    }
}