using UnityEngine;

public class SimpleThirdPersonCamera : MonoBehaviour
{
    [Header("Target")]
    public Transform target;

    [Header("Follow")]
    public Vector3 targetOffset = new Vector3(0f, 1.5f, 0f);
    public float followSmooth = 12f;

    [Header("Mouse Look")]
    public float mouseSensitivity = 3f;
    public float minPitch = -20f;
    public float maxPitch = 55f;

    [Header("Distance")]
    public Transform cameraTransform;
    public float cameraDistance = 4f;
    public float cameraHeight = 1.2f;

    [Header("Player Rotation")]
    public bool rotatePlayerWithCamera = true;
    public float playerRotationSmooth = 12f;

    private float yaw;
    private float pitch = 15f;

    void Start()
    {
        if (target != null)
        {
            yaw = target.eulerAngles.y;
        }
    }

    void LateUpdate()
    {
        if (target == null || cameraTransform == null)
        {
            return;
        }

        // Si la cámara de tercera persona no está activa, no controla nada
        if (!cameraTransform.gameObject.activeInHierarchy)
        {
            return;
        }

        // Rotación de cámara con mouse
        yaw += Input.GetAxis("Mouse X") * mouseSensitivity;
        pitch -= Input.GetAxis("Mouse Y") * mouseSensitivity;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

        // El rig sigue al jugador
        Vector3 targetPosition = target.position + targetOffset;

        transform.position = Vector3.Lerp(
            transform.position,
            targetPosition,
            followSmooth * Time.deltaTime
        );

        // El rig rota con el mouse
        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);

        // La cámara se coloca detrás del rig
        cameraTransform.localPosition = new Vector3(0f, cameraHeight, -cameraDistance);
        cameraTransform.localRotation = Quaternion.identity;

        // El jugador rota en Y siguiendo la cámara
        if (rotatePlayerWithCamera)
        {
            Quaternion targetRotation = Quaternion.Euler(0f, yaw, 0f);

            target.rotation = Quaternion.Lerp(
                target.rotation,
                targetRotation,
                playerRotationSmooth * Time.deltaTime
            );
        }
    }
}