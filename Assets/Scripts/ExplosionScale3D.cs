using UnityEngine;

public class ExplosionScale3D : MonoBehaviour
{
    public Vector3 startScale = new Vector3(0.5f, 0.5f, 0.5f);
    public Vector3 endScale = new Vector3(4f, 4f, 4f);
    public float duration = 0.4f;

    private float timer = 0f;

    void Start()
    {
        transform.localScale = startScale;
    }

    void Update()
    {
        timer += Time.deltaTime;
        float t = timer / duration;
        transform.localScale = Vector3.Lerp(startScale, endScale, t);
    }
}