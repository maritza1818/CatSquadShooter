using UnityEngine;

namespace CatSquad.Muelle
{
    /// <summary>
    /// LAB 05 · Ball (el ovillo). Igual que en el proyecto del lab:
    ///   Push(fuerza)   → aplica un impulso (ForceMode2D.Impulse): con masa 1, v0 = fuerza.
    ///   ActivateRb()   → el Rigidbody2D pasa a dinámico: lo mueve la física (gravedad, choques, rebote).
    ///   DesactivateRb()→ lo deja quieto y cinemático mientras se apunta.
    /// Necesita Rigidbody2D + CircleCollider2D con el Physics Material 2D "Rebote".
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D), typeof(CircleCollider2D))]
    public class Ball : MonoBehaviour
    {
        [HideInInspector] public Rigidbody2D rb;
        [HideInInspector] public CircleCollider2D col;

        public Vector3 pos { get { return transform.position; } }

        public bool InFlight { get; private set; }

        private void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            col = GetComponent<CircleCollider2D>();
        }

        public void Push(Vector2 force)
        {
            InFlight = true;
            rb.AddForce(force, ForceMode2D.Impulse);
        }

        public void ActivateRb()
        {
            rb.bodyType = RigidbodyType2D.Dynamic;
        }

        public void DesactivateRb()
        {
            rb.linearVelocity = Vector2.zero;
            rb.angularVelocity = 0f;
            rb.bodyType = RigidbodyType2D.Kinematic;
            InFlight = false;
        }

        public void PlaceAt(Vector2 position)
        {
            DesactivateRb();
            rb.position = position;
            transform.position = position;
            transform.rotation = Quaternion.identity;
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            MuelleTarget target = other.GetComponentInParent<MuelleTarget>();
            if (target == null || MuelleGameManager.Instance == null) return;
            MuelleGameManager.Instance.OnBallInTarget(target);
            // el gatito "atrapa" el ovillo: lo frena para que no rebote fuera del recipiente
            rb.linearVelocity *= 0.15f;
            rb.angularVelocity = 0f;
        }

        private void OnCollisionEnter2D(Collision2D c)
        {
            if (MuelleGameManager.Instance != null && c.relativeVelocity.magnitude > 2f) MuelleGameManager.Instance.OnBounce(c.relativeVelocity.magnitude);
        }
    }
}
