using UnityEngine;

namespace CatSquad.Muelle
{
    /// <summary>
    /// Blanco del minijuego (la "taza" del lab y el segundo objetivo del ejercicio 2).
    /// Cesta: va sobre una balsa que flota (Rigidbody2D cinemático movido con MovePosition: sube y baja con las
    /// olas y deriva de lado a lado). Balde: quieto en una azotea, más difícil, vale más puntos.
    /// La forma de U es un EdgeCollider2D (el ovillo entra y rebota adentro); un trigger interior cuenta el acierto.
    /// </summary>
    public class MuelleTarget : MonoBehaviour
    {
        public string displayName = "Cesta";
        public int points = 1;
        [Tooltip("Gatito que espera (salta al acertar)")]
        public Transform kitten;

        [Header("Balsa que flota (opcional)")]
        public bool floating;
        public float bobAmplitude = 0.12f;
        public float bobFrequency = 1.1f;
        public float driftAmplitude = 1.6f;
        public float driftFrequency = 0.12f;

        [Tooltip("Centro del blanco (para la asistencia: solución de disparo)")]
        public Transform aimPoint;

        public int Hits { get; private set; }
        public bool Ready { get { return cooldown <= 0f; } }
        public Vector2 AimPoint { get { return aimPoint != null ? (Vector2)aimPoint.position : (Vector2)transform.position; } }

        private Rigidbody2D rb;
        private Vector2 basePos;
        private float cooldown;
        private float happy;
        private Vector3 kittenBase;

        private void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            basePos = transform.position;
            if (kitten != null) kittenBase = kitten.localPosition;
        }

        /// <summary>Posición de la balsa en un instante (la misma fórmula que la mueve): permite predecir dónde estará.</summary>
        public Vector2 PositionAtTime(float t)
        {
            if (!floating) return transform.position;
            return basePos + new Vector2(Mathf.Sin(t * driftFrequency * 2f * Mathf.PI) * driftAmplitude,
                                         Mathf.Sin(t * bobFrequency * 2f * Mathf.PI) * bobAmplitude);
        }

        /// <summary>Centro del blanco dentro de <paramref name="secondsAhead"/> segundos.</summary>
        public Vector2 PredictAimPoint(float secondsAhead)
        {
            if (!floating) return AimPoint;
            Vector2 offset = AimPoint - (Vector2)transform.position;
            return PositionAtTime(Time.time + secondsAhead) + offset;
        }

        private void FixedUpdate()
        {
            if (!floating || rb == null) return;
            float t = Time.time;
            Vector2 p = PositionAtTime(t);
            rb.MovePosition(p);
            rb.MoveRotation(Mathf.Sin(t * bobFrequency * 2f * Mathf.PI + 1f) * 3f);
        }

        private void Update()
        {
            if (cooldown > 0f) cooldown -= Time.deltaTime;
            if (happy > 0f) happy -= Time.deltaTime;
            if (kitten != null)
            {
                float hop = happy > 0f ? Mathf.Abs(Mathf.Sin(Time.time * 10f)) * 0.35f : 0f;
                kitten.localPosition = kittenBase + Vector3.up * hop;
            }
        }

        /// <summary>Devuelve true si el acierto cuenta (evita contar dos veces el mismo ovillo).</summary>
        public bool RegisterHit()
        {
            if (!Ready) return false;
            Hits++;
            cooldown = 1.5f;
            happy = 1.8f;
            return true;
        }
    }
}
