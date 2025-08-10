using UnityEngine;

[RequireComponent(typeof(CanvasGroup))]
public class UIPopIn : MonoBehaviour
{
    public float fadeSpeed = 8f;
    public float scaleFrom = 0.88f;

    CanvasGroup cg;
    bool playing;

    void Awake()
    {
        EnsureCG();
        gameObject.SetActive(false);
        cg.alpha = 0f;
        transform.localScale = Vector3.one * scaleFrom;
    }

    void EnsureCG()
    {
        if (!cg && !TryGetComponent(out cg))
            cg = gameObject.AddComponent<CanvasGroup>();
    }

    public void Show()
    {
        EnsureCG();
        gameObject.SetActive(true);
        cg.alpha = 0f;
        transform.localScale = Vector3.one * scaleFrom;
        playing = true;
    }

    public void HideImmediate()
    {
        EnsureCG();
        playing = false;
        cg.alpha = 0f;
        gameObject.SetActive(false);
    }


    void Update()
    {
        if (!playing) return;
        EnsureCG();
        cg.alpha = Mathf.Lerp(cg.alpha, 1f, Time.unscaledDeltaTime * fadeSpeed);
        transform.localScale = Vector3.Lerp(transform.localScale, Vector3.one, Time.unscaledDeltaTime * fadeSpeed);
        if (cg.alpha > 0.98f && (transform.localScale - Vector3.one).sqrMagnitude < 0.0004f)
            playing = false;
    }
}