using UnityEngine;
using UnityEngine.UI;

public class StaminaUI : MonoBehaviour
{
    public PlayerMovement playerMovement;
    public Slider staminaSlider;
    public GameObject hudRoot; // Asigna el contenedor del slider

    private void Update()
    {
        staminaSlider.value = playerMovement.GetStaminaNormalized();

        if (playerMovement.IsRunning())
        {
            if (!hudRoot.activeSelf)
                hudRoot.SetActive(true);
        }
        else
        {
            if (hudRoot.activeSelf)
                hudRoot.SetActive(false);
        }
    }
}


