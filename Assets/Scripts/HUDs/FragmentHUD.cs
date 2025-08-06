using UnityEngine;
using TMPro;

public class FragmentHUD : MonoBehaviour
{
    public TextMeshProUGUI counterText;
    public GameObject rootHUD;
    public int totalFragments = 9;

    void Start()
    {
        // Solo mostrar si estamos en modo personalizado
        if (!GameManager.Instance.IsInCustomMode())
        {
            rootHUD.SetActive(false);
        }
    }

    public void UpdateFragmentCount(int current)
    {
        counterText.text = $"{current} / {totalFragments}";
    }
}
