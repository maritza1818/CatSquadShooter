using UnityEngine;

// LAB 6 - Linterna: prende/apaga un Spot Light con la tecla F.
// Va en el objeto "Jugador". El Spot Light debe ser hijo de la cámara.
public class FlashlightToggle : MonoBehaviour
{
    public Light flashlight;        // un Spot Light hijo de la cámara
    public KeyCode key = KeyCode.F;

    void Start()
    {
        if (flashlight != null) flashlight.enabled = false; // empieza apagada
    }

    void Update()
    {
        if (Input.GetKeyDown(key) && flashlight != null)
            flashlight.enabled = !flashlight.enabled;
    }
}
