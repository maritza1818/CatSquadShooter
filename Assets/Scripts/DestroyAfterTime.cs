using UnityEngine;

public class DestroyAfterTime : MonoBehaviour
{
    public float lifeTime = 0.2f;

    void Start()
    {
        Destroy(gameObject, lifeTime);
    }
}