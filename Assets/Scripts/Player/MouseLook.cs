using UnityEngine;

public class MouseLook : MonoBehaviour
{
    public float mouseSensitivity = 100f;   
    public Transform playerBody;

    private float xRotation = 0f;

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
    }

    void Update()
    {
        float sensFactor = PlayerPrefs.GetFloat("opt_mouseSens", 1f);

        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity * sensFactor * Time.deltaTime;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity * sensFactor * Time.deltaTime;

        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -90f, 90f);

        transform.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
        playerBody.Rotate(Vector3.up * mouseX);
    }
}