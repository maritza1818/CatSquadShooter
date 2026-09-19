using UnityEngine;

// LAB 6 - Hace que una luz parpadee suavemente (vela, foco viejo, ambiente de misterio).
// Va en el objeto que tiene el componente Light.
[RequireComponent(typeof(Light))]
public class LightFlicker : MonoBehaviour
{
    public float minIntensity = 0.4f;
    public float maxIntensity = 1.1f;
    public float speed = 8f;

    private Light targetLight;
    private float seed;

    void Start()
    {
        targetLight = GetComponent<Light>();
        seed = Random.value * 100f; // cada luz parpadea distinto
    }

    void Update()
    {
        float noise = Mathf.PerlinNoise(seed, Time.time * speed);
        targetLight.intensity = Mathf.Lerp(minIntensity, maxIntensity, noise);
    }
}
