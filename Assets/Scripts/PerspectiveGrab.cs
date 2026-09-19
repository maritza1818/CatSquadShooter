using UnityEngine;

// ESCENA 1 - Mecánica estilo Superliminal.
// El objeto mantiene su tamaño APARENTE constante mientras lo sostienes.
// Al soltarlo, su tamaño REAL depende de la distancia donde lo dejaste.
// Va en el objeto del jugador (o en la cámara en primera persona).
public class PerspectiveGrab : MonoBehaviour
{
    [Header("Referencias")]
    public Camera cam;                 // tu cámara en primera persona
    public LayerMask grabbableMask;    // capa de los objetos agarrables (cajas)
    public LayerMask environmentMask;  // capa del escenario (suelo, paredes)

    [Header("Ajustes")]
    public float maxDistance = 30f;    // qué tan lejos puedes alejar el objeto
    public float minDistance = 1.5f;   // mínimo para que no se pegue a la cámara
    public KeyCode grabKey = KeyCode.Mouse0; // botón para agarrar/soltar (o KeyCode.E)

    private Transform held;
    private Rigidbody heldRb;
    private float scaleRatio;          // escala / distancia = tamaño aparente constante

    void Update()
    {
        if (held == null)
        {
            if (Input.GetKeyDown(grabKey)) TryGrab();
        }
        else
        {
            HoldObject();
            if (Input.GetKeyDown(grabKey)) Release();
        }
    }

    void TryGrab()
    {
        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)); // centro de pantalla
        if (Physics.Raycast(ray, out RaycastHit hit, maxDistance, grabbableMask))
        {
            held = hit.transform;
            heldRb = held.GetComponent<Rigidbody>();
            if (heldRb != null) heldRb.isKinematic = true;

            // guardamos la constante de tamaño aparente (escala entre distancia)
            scaleRatio = held.localScale.x / hit.distance;
        }
    }

    void HoldObject()
    {
        Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        float targetDistance = maxDistance;

        // si hay pared o suelo delante, dejamos el objeto justo antes
        if (Physics.Raycast(ray, out RaycastHit hit, maxDistance, environmentMask))
            targetDistance = hit.distance;

        targetDistance = Mathf.Clamp(targetDistance, minDistance, maxDistance);

        held.position = ray.GetPoint(targetDistance);

        // escala proporcional a la distancia -> se ve siempre del mismo tamaño
        float s = scaleRatio * targetDistance;
        held.localScale = new Vector3(s, s, s);
    }

    void Release()
    {
        if (heldRb != null) heldRb.isKinematic = false;
        held = null;
        heldRb = null;
    }
}
