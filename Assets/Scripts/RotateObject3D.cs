using UnityEngine;

public class RotateObject3D : MonoBehaviour
{
    public Vector3 rotationSpeed = new Vector3(0f, 180f, 0f);

    void Update()
    {
        transform.Rotate(rotationSpeed * Time.deltaTime);
    }
}   