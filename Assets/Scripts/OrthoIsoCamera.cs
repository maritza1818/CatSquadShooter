using UnityEngine;

// ESCENA 3 - Cámara isométrica ortográfica que rota alrededor de la escena
// (estilo Monument Valley). Va en la cámara. Usa Q y E para girar.
[RequireComponent(typeof(Camera))]
public class OrthoIsoCamera : MonoBehaviour
{
    public Transform pivot;            // objeto vacío en el centro de la escena
    public float distance = 20f;
    public float height = 20f;
    public float rotateStep = 90f;     // gira de 90 en 90 grados
    public float rotateSpeed = 6f;

    private float targetAngle = 45f;
    private float currentAngle = 45f;

    void Start()
    {
        GetComponent<Camera>().orthographic = true; // ortográfica = colapsa la profundidad
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.E)) targetAngle += rotateStep;
        if (Input.GetKeyDown(KeyCode.Q)) targetAngle -= rotateStep;

        currentAngle = Mathf.LerpAngle(currentAngle, targetAngle, Time.deltaTime * rotateSpeed);

        float rad = currentAngle * Mathf.Deg2Rad;
        Vector3 offset = new Vector3(Mathf.Sin(rad) * distance, height, Mathf.Cos(rad) * distance);
        transform.position = pivot.position + offset;
        transform.LookAt(pivot.position);
    }
}
