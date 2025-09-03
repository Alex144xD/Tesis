using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class TutorialTexto : MonoBehaviour
{
    [Header("Referencias")]
    public TextMeshProUGUI textoUI;

    [Header("Configuración")]
    [TextArea(2, 4)] public List<string> mensajes = new List<string>();
    public float tiempoPorTexto = 3f; 
    public float fadeSpeed = 2f; 

    private void Start()
    {
        if (textoUI != null && mensajes.Count > 0)
        {
            StartCoroutine(MostrarMensajes());
        }
    }

    IEnumerator MostrarMensajes()
    {
        foreach (string msg in mensajes)
        {
            // Fade in
            yield return StartCoroutine(FadeText(msg, 1f));

            // Mantener visible
            yield return new WaitForSeconds(tiempoPorTexto);

            // Fade out
            yield return StartCoroutine(FadeText("", 0f));
        }

        // Al terminar, ocultamos el texto
        textoUI.text = "";
    }

    IEnumerator FadeText(string nuevoTexto, float alphaObjetivo)
    {
        if (nuevoTexto != "") textoUI.text = nuevoTexto;

        float alphaInicial = textoUI.alpha;
        float t = 0f;

        while (t < 1f)
        {
            t += Time.deltaTime * fadeSpeed;
            textoUI.alpha = Mathf.Lerp(alphaInicial, alphaObjetivo, t);
            yield return null;
        }

        textoUI.alpha = alphaObjetivo;

        if (alphaObjetivo == 0f) textoUI.text = "";
    }
}