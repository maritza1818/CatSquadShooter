using UnityEngine;

public class SimpleTacticalCamera : MonoBehaviour
{
    [Header("Target")]
    public Transform target;

    [Header("Event Target")]
    public Transform eventTarget;
    public bool focusEventTarget = false;

    [Header("Tactical View")]
    public Transform cameraTransform;
    public float height = 14f;
    public float backDistance = 8f;
    public float followSmooth = 8f;

    [Header("Look Settings")]
    public float lookHeight = 0.5f;

    void LateUpdate()
    {
        if (target == null || cameraTransform == null)
        {
            return;
        }

        Transform currentTarget = target;

        if (focusEventTarget && eventTarget != null)
        {
            currentTarget = eventTarget;
        }

        Vector3 targetPosition = currentTarget.position;

        transform.position = Vector3.Lerp(
            transform.position,
            targetPosition,
            followSmooth * Time.deltaTime
        );

        cameraTransform.localPosition = new Vector3(0f, height, -backDistance);

        Vector3 lookPoint = currentTarget.position + Vector3.up * lookHeight;
        cameraTransform.LookAt(lookPoint);
    }

    public void FocusOnEventTarget(Transform newTarget)
    {
        eventTarget = newTarget;
        focusEventTarget = true;
    }

    public void ClearEventTarget()
    {
        eventTarget = null;
        focusEventTarget = false;
    }
}