using UnityEngine;

// ESCENA 2 - Alterna la cámara entre proyección ORTOGRÁFICA (vista 2D)
// y PERSPECTIVA (vista 3D). Va en la cámara de la escena.
public class ProjectionToggle : MonoBehaviour
{
    public Camera cam;
    public KeyCode toggleKey = KeyCode.Q;
    public float orthoSize = 5f;       // "zoom" en modo ortográfico
    public float perspFOV = 60f;       // campo de visión en modo perspectiva

    void Start()
    {
        if (cam == null) cam = GetComponent<Camera>();
        cam.orthographic = true;       // arrancamos en modo 2D
        cam.orthographicSize = orthoSize;
    }

    void Update()
    {
        if (Input.GetKeyDown(toggleKey))
        {
            cam.orthographic = !cam.orthographic;
            if (cam.orthographic) cam.orthographicSize = orthoSize;
            else cam.fieldOfView = perspFOV;
        }
    }
}
