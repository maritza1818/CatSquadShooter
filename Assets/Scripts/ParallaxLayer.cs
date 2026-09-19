using UnityEngine;

public class ParallaxLayer : MonoBehaviour
{
    public Transform target;

    public float xMultiplier = 0.1f;
    public float zMultiplier = 0.1f;
    public float smoothSpeed = 5f;

    private Vector3 initialLayerPosition;
    private Vector3 initialTargetPosition;

    void Start()
    {
        initialLayerPosition = transform.position;

        if (target != null)
        {
            initialTargetPosition = target.position;
        }
    }

    void LateUpdate()
    {
        if (target == null)
        {
            return;
        }

        Vector3 delta = target.position - initialTargetPosition;

        Vector3 desiredPosition = initialLayerPosition + new Vector3(
            delta.x * xMultiplier,
            0f,
            delta.z * zMultiplier
        );

        transform.position = Vector3.Lerp(transform.position, desiredPosition, smoothSpeed * Time.deltaTime);
    }
}