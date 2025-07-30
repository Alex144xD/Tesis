using UnityEngine;

public class BatteryPickup : MonoBehaviour
{
    [Header("Recarga")]
    public float rechargeAmount = 10f;

    private bool isCollected = false;

    private void OnTriggerEnter(Collider other)
    {
        if (isCollected) return; // Evitar doble recogida

        if (other.CompareTag("Player"))
        {
            PlayerLightController lightController = other.GetComponentInChildren<PlayerLightController>();
            if (lightController != null)
            {
                lightController.RechargeBattery(rechargeAmount);
                isCollected = true;

                // 🔊 TODO: reproducir sonido o efecto visual
                // AudioSource.PlayClipAtPoint(pickupSound, transform.position);

                Destroy(gameObject);
            }
        }
    }
}