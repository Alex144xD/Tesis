using UnityEngine;
using UnityEngine.SceneManagement;

public class CambiarScenePorColision : MonoBehaviour
{
    [Header("Configuración")]
    public string nombreSiguienteScene; 
    public bool usarIndice = false;    
    public int indiceSiguienteScene = 0;

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            CargarSiguiente();
        }
    }

    void CargarSiguiente()
    {
        if (usarIndice)
        {
            SceneManager.LoadScene(indiceSiguienteScene);
        }
        else
        {
            if (nombreSiguienteScene == "MainMenu")
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }

            SceneManager.LoadScene(nombreSiguienteScene);
        }
    }
}