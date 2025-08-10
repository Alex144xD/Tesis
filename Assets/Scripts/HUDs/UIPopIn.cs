using UnityEngine;

public class UIPopIn : MonoBehaviour
{
    public float fadeSpeed = 6f;
    public float scaleFrom = 0.85f;

    CanvasGroup cg;
    bool playing;

    void Awake()
    {
        cg = GetComponent<CanvasGroup>();
        cg.alpha = 0f;
        transform.localScale = Vector3.one * scaleFrom;
        gameObject.SetActive(false);
    }

    public void Show()
    {
        gameObject.SetActive(true);
        cg.alpha = 0f;
        transform.localScale = Vector3.one * scaleFrom;
        playing = true;
    }

    public void HideImmediate()
    {
        playing = false;
        cg.alpha = 0f;
        gameObject.SetActive(false);
    }

    void Update()
    {
        if (!playing) return;
        cg.alpha = Mathf.Lerp(cg.alpha, 1f, Time.unscaledDeltaTime * fadeSpeed);
        transform.localScale = Vector3.Lerp(transform.localScale, Vector3.one, Time.unscaledDeltaTime * fadeSpeed);
        if (Mathf.Abs(1f - cg.alpha) < 0.02f && Vector3.Distance(transform.localScale, Vector3.one) < 0.02f)
            playing = false;
    }
}