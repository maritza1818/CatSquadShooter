using UnityEngine;

public class MuzzleFlashEffect : MonoBehaviour
{
    public float lifeTime = 0.08f;

    void Start()
    {
        Destroy(gameObject, lifeTime);
    }
}