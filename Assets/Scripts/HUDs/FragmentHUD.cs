using UnityEngine;
using TMPro;
using UnityEngine.UI;

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

    void Start()
    {
        if (!GameManager.Instance.IsInCustomMode())
        {
            rootHUD.SetActive(false);
            return;
        }
        UpdateFragmentCount(0);
    }

    public void UpdateFragmentCount(int current)
    {
        counterText.text = $"{current} / {totalFragments}";

        float progress = Mathf.Clamp01((float)current / totalFragments);
        float alpha = Mathf.Lerp(minAlpha, maxAlpha, progress);

        if (fragmentIcon != null)
        {
            Color c = fragmentIcon.color;
            c.a = alpha;
            fragmentIcon.color = c;
        }
    }
}
