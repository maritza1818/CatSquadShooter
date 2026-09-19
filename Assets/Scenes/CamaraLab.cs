using UnityEngine;

public class CamaraLab : MonoBehaviour
{
    public Transform objetivo;
    public Vector3 offset = new Vector3(0, 18, -14);
    public float suavidad = 3f;

    void LateUpdate()
    {
        if (objetivo == null) return;

        Vector3 posicionDeseada = objetivo.position + offset;
        transform.position = Vector3.Lerp(transform.position, posicionDeseada,
                                          Time.deltaTime * suavidad);
        transform.LookAt(objetivo.position);
    }
}