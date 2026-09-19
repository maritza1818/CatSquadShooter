using UnityEngine;

// LAB 6 - Movimiento en primera persona para recorrer la habitación.
// Va en el objeto "Jugador". Necesita un CharacterController (se agrega solo).
// La cámara debe ser un objeto HIJO del jugador.
[RequireComponent(typeof(CharacterController))]
public class FirstPersonController : MonoBehaviour
{
    [Header("Movimiento")]
    public float speed = 4f;
    public float gravity = -20f;

    [Header("Cámara / Mouse")]
    public Transform cameraTransform;   // arrastra aquí la cámara hija
    public float mouseSensitivity = 2f;

    private CharacterController controller;
    private float verticalVel;
    private float pitch;

    void Start()
    {
        controller = GetComponent<CharacterController>();
        Cursor.lockState = CursorLockMode.Locked; // oculta y bloquea el cursor
    }

    void Update()
    {
        // mirar con el mouse
        float mx = Input.GetAxis("Mouse X") * mouseSensitivity;
        float my = Input.GetAxis("Mouse Y") * mouseSensitivity;
        transform.Rotate(0f, mx, 0f);
        pitch = Mathf.Clamp(pitch - my, -80f, 80f);
        if (cameraTransform != null)
            cameraTransform.localEulerAngles = new Vector3(pitch, 0f, 0f);

        // caminar con WASD
        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");
        Vector3 move = (transform.right * h + transform.forward * v) * speed;

        if (controller.isGrounded && verticalVel < 0f) verticalVel = -2f;
        verticalVel += gravity * Time.deltaTime;
        move.y = verticalVel;

        controller.Move(move * Time.deltaTime);
    }
}
