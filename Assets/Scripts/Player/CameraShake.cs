using System.Collections;
using UnityEngine;

public class CameraShake : MonoBehaviour
{
    public static CameraShake instance; // Para poder llamarlo desde otros scripts

    private Vector3 baseLocalPos;       // La posición base real
    private Coroutine currentShake;     // Para evitar varias corrutinas al mismo tiempo

    void Awake()
    {
        instance = this;
        baseLocalPos = transform.localPosition; // Guardar una sola vez
    }

    public void Shake(float duration = 0.2f, float magnitude = 0.2f)
    {
        if (currentShake != null) StopCoroutine(currentShake); // Cancela shakes previos
        currentShake = StartCoroutine(ShakeRoutine(duration, magnitude));
    }

    private IEnumerator ShakeRoutine(float duration, float magnitude)
    {
        float elapsed = 0f;

        while (elapsed < duration)
        {
            float x = Random.Range(-1f, 1f) * magnitude;
            float y = Random.Range(-1f, 1f) * magnitude;

            transform.localPosition = baseLocalPos + new Vector3(x, y, 0);

            elapsed += Time.deltaTime;
            yield return null;
        }

        // Aseguramos volver siempre al lugar correcto
        transform.localPosition = baseLocalPos;
        currentShake = null;
    }
}