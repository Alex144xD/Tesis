// Tu MouseLook original + 1 línea para factor global
using UnityEngine;

public class MouseLook : MonoBehaviour
{
    public float mouseSensitivity = 100f;   // sensibilidad base (tu valor)
    public Transform playerBody;

    private float xRotation = 0f;

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
    }

    void Update()
    {
        // LEE el factor global (0.1..3) del slider
        float sensFactor = PlayerPrefs.GetFloat("opt_mouseSens", 1f);

        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity * sensFactor * Time.deltaTime;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity * sensFactor * Time.deltaTime;

        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -90f, 90f);

        transform.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
        playerBody.Rotate(Vector3.up * mouseX);
    }
}