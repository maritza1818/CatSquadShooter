using UnityEngine;

public class PulseScale3D : MonoBehaviour
{
    public Vector3 minScale = new Vector3(1.5f, 0.03f, 1.5f);
    public Vector3 maxScale = new Vector3(2.5f, 0.03f, 2.5f);
    public float pulseSpeed = 3f;

    void Update()
    {
        float t = (Mathf.Sin(Time.time * pulseSpeed) + 1f) / 2f;
        transform.localScale = Vector3.Lerp(minScale, maxScale, t);
    }
}