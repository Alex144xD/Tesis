using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class MenuIntroAnimator : MonoBehaviour
{
    [Header("Canvas Group del menú (raíz)")]
    public CanvasGroup canvasGroup;

    [Header("Elementos a animar (en orden)")]
    public List<RectTransform> items;

    [Header("Intro")]
    public float startDelay = 0.2f;
    public float itemStagger = 0.08f;      
    public Vector2 slideOffset = new Vector2(-120f, 0f);
    public float moveDuration = 0.35f;
    public float fadeDuration = 0.35f;
    public AnimationCurve ease = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Interactividad")]
    public bool setInteractableAtEnd = true;

    [Header("Reproducir en OnEnable")]
    public bool playOnEnable = true;

    // Guarda posiciones originales
    private Vector2[] _originalPos;

    void Reset()
    {
        canvasGroup = GetComponentInChildren<CanvasGroup>();
        if (!canvasGroup && GetComponent<CanvasGroup>())
            canvasGroup = GetComponent<CanvasGroup>();
    }

    void OnEnable()
    {
        if (playOnEnable)
            PlayIntro();
    }

    public void PlayIntro()
    {
        StopAllCoroutines();
        StartCoroutine(IntroCo());
    }

    IEnumerator IntroCo()
    {
        if (!canvasGroup) canvasGroup = GetComponentInChildren<CanvasGroup>();
        if (!canvasGroup)
        {
            // Si no hay CanvasGroup, crea uno para el fade
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }


        if (items == null || items.Count == 0)
        {
            items = new List<RectTransform>(GetComponentsInChildren<RectTransform>(true));
            // El primero será la raíz; lo quitamos
            if (items.Count > 0 && items[0].gameObject == gameObject)
                items.RemoveAt(0);
        }

        _originalPos = new Vector2[items.Count];
        for (int i = 0; i < items.Count; i++)
        {
            if (!items[i]) continue;
            _originalPos[i] = items[i].anchoredPosition;
            items[i].anchoredPosition = _originalPos[i] + slideOffset;
        }

    
        if (setInteractableAtEnd)
        {
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }

  
        canvasGroup.alpha = 0f;
        float t = 0f;
        while (t < fadeDuration)
        {
            t += Time.unscaledDeltaTime;
            canvasGroup.alpha = Mathf.SmoothStep(0f, 1f, t / Mathf.Max(0.0001f, fadeDuration));
            yield return null;
        }
        canvasGroup.alpha = 1f;

   
        if (startDelay > 0f)
            yield return new WaitForSecondsRealtime(startDelay);


        for (int i = 0; i < items.Count; i++)
        {
            if (!items[i]) continue;
            StartCoroutine(SlideOne(items[i], _originalPos[i]));
            if (itemStagger > 0f)
                yield return new WaitForSecondsRealtime(itemStagger);
        }

        yield return new WaitForSecondsRealtime(moveDuration + 0.02f);

        if (setInteractableAtEnd)
        {
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
        }
    }

    IEnumerator SlideOne(RectTransform rt, Vector2 target)
    {
        Vector2 start = rt.anchoredPosition;
        float t = 0f;
        while (t < moveDuration)
        {
            t += Time.unscaledDeltaTime;
            float k = ease.Evaluate(Mathf.Clamp01(t / Mathf.Max(0.0001f, moveDuration)));
            rt.anchoredPosition = Vector2.LerpUnclamped(start, target, k);
            yield return null;
        }
        rt.anchoredPosition = target;
    }
}