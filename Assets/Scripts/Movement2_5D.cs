using UnityEngine;

// ESCENA 2 - Movimiento 2.5D (el gato se mueve solo en el plano X-Y).
// Va en el jugador. Necesita Rigidbody + Collider.
[RequireComponent(typeof(Rigidbody))]
public class Movement2_5D : MonoBehaviour
{
    [Header("Movimiento")]
    public float speed = 6f;
    public float jumpForce = 7f;

    [Header("Chequeo de suelo")]
    public Transform groundCheck;      // un objeto vacío en los pies del gato
    public float groundRadius = 0.25f;
    public LayerMask groundMask;       // capa del suelo/plataformas

    private Rigidbody rb;
    private float fixedZ;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        fixedZ = transform.position.z; // bloqueamos la profundidad
        rb.constraints = RigidbodyConstraints.FreezePositionZ | RigidbodyConstraints.FreezeRotation;
    }

    void Update()
    {
        float x = Input.GetAxisRaw("Horizontal");
        Vector3 v = rb.linearVelocity;       // si usas Unity 6 cambia "velocity" por "linearVelocity"
        v.x = x * speed;
        rb.linearVelocity = v;

        bool grounded = Physics.CheckSphere(groundCheck.position, groundRadius, groundMask);
        if (grounded && Input.GetButtonDown("Jump"))
            rb.linearVelocity = new Vector3(rb.linearVelocity.x, jumpForce, 0f);
    }
}
