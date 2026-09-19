using UnityEngine;

public class BulletTrail : MonoBehaviour
{
    public float lifeTime = 0.05f;

    void Start()
    {
        Destroy(gameObject, lifeTime);
    }

    public void SetPositions(Vector3 startPoint, Vector3 endPoint)
    {
        LineRenderer line = GetComponent<LineRenderer>();
        line.SetPosition(0, startPoint);
        line.SetPosition(1, endPoint);
    }
}