using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
public class StaminaUI : MonoBehaviour
{
    public PlayerMovement playerMovement;
    public Slider staminaSlider;

    private void Update()
    {
        staminaSlider.value = playerMovement.GetStaminaNormalized();
    }
}


