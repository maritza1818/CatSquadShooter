using UnityEngine;

// ESCENA 3 - Geometría imposible (estilo Monument Valley).
// Cuando dos bordes de camino se ven ALINEADOS en pantalla (la proyección
// ortográfica ignora la profundidad), se activa un puente invisible para cruzar.
// Va en un objeto vacío "GameManager".
public class IllusionConnector : MonoBehaviour
{
    public Camera cam;
    public Transform anchorA;     // borde de un camino
    public Transform anchorB;     // borde del otro camino
    public GameObject connector;  // puente con collider (empieza desactivado)
    public float threshold = 25f; // tolerancia en pixeles para considerarlo "alineado"

    void Update()
    {
        Vector3 a = cam.WorldToScreenPoint(anchorA.position);
        Vector3 b = cam.WorldToScreenPoint(anchorB.position);

        // solo comparamos X-Y en pantalla: el eje de profundidad no importa
        float dist = Vector2.Distance(new Vector2(a.x, a.y), new Vector2(b.x, b.y));

        bool aligned = dist < threshold;
        if (connector.activeSelf != aligned)
            connector.SetActive(aligned);
    }
}
