using UnityEngine;

[RequireComponent(typeof(Collider))]
public class SoulFragmentPickup : MonoBehaviour
{
    [Header("Efectos opcionales")]
    public AudioClip pickupSound;
    public ParticleSystem pickupEffect;

    private bool isCollected = false;

    private void OnTriggerEnter(Collider other)
    {
        if (isCollected) return;

        if (other.CompareTag("Player") &&
            other.TryGetComponent<PlayerInventory>(out var inventory))
        {
            isCollected = true;
            inventory.AddSoulFragment();

            // Reproducir sonido
            if (pickupSound)
                AudioSource.PlayClipAtPoint(pickupSound, transform.position);

            // Partículas
            if (pickupEffect)
                Instantiate(pickupEffect, transform.position, Quaternion.identity);

            Destroy(gameObject);
        }
    }
}
