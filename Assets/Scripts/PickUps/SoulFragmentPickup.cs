using UnityEngine;

public class SoulFragmentPickup : MonoBehaviour
{
    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            var inventory = other.GetComponent<PlayerInventory>();
            if (inventory != null)
            {
                inventory.AddSoulFragment();
                Destroy(gameObject);
            }
        }
    }
}