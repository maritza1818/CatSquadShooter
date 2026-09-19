using UnityEngine;

// ESCENA 3 - Movimiento simple del gato en la fortaleza isométrica.
// Camina relativo a la cámara: W va "hacia el fondo", S hacia ti, A/D a los lados.
// Necesita un CharacterController (se agrega solo al poner este script).
[RequireComponent(typeof(CharacterController))]
public class SimpleIsoWalker : MonoBehaviour
{
    public Camera cam;            // arrastra aquí la cámara isométrica
    public float speed = 4f;
    public float gravity = -20f;

    private CharacterController controller;
    private float verticalVel;

    void Start()
    {
        controller = GetComponent<CharacterController>();
        if (cam == null) cam = Camera.main;
    }

    void Update()
    {
        // direcciones de la cámara, pero "aplanadas" al suelo
        Vector3 fwd = cam.transform.forward;  fwd.y = 0;  fwd.Normalize();
        Vector3 right = cam.transform.right;  right.y = 0; right.Normalize();

        float h = Input.GetAxisRaw("Horizontal"); // A / D
        float v = Input.GetAxisRaw("Vertical");   // W / S
        Vector3 move = (fwd * v + right * h).normalized * speed;

        // gravedad simple para que se quede pegado al piso
        if (controller.isGrounded && verticalVel < 0) verticalVel = -2f;
        verticalVel += gravity * Time.deltaTime;
        move.y = verticalVel;

        controller.Move(move * Time.deltaTime);

        // que el gato mire hacia donde camina
        Vector3 flat = new Vector3(move.x, 0, move.z);
        if (flat.magnitude > 0.1f)
            transform.rotation = Quaternion.Slerp(
                transform.rotation, Quaternion.LookRotation(flat), Time.deltaTime * 10f);
    }
}
