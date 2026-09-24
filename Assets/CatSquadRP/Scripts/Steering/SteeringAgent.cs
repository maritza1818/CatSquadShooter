using UnityEngine;

namespace CatSquad.RescateProfundo
{
    public enum SteeringMode { Idle, Seek, Arrive, Path, Flee, Wander }

    /// <summary>
    /// Agente cinemático que se mueve con fuerzas de steering (Seek / Arrive).
    /// El comportamiento concreto (guía, gatito, pez) decide QUÉ fuerza aplicar
    /// y llama a Step(); este componente se encarga de integrar, orientar el modelo
    /// hacia donde nada y dibujar los gizmos para poder mostrar el algoritmo en clase.
    /// </summary>
    public class SteeringAgent : MonoBehaviour
    {
        public SteeringBody body = new SteeringBody();

        [Header("Visual")]
        [Tooltip("Hijo que se rota/balancea. Si está vacío se usa este transform.")]
        public Transform visual;
        public bool faceMovement = true;
        [Tooltip("Yaw del modelo cuando nada hacia la derecha (+X). 90 = perfil puro; más de 90 = gira un poco hacia la cámara.")]
        public float yawWhenMovingRight = 115f;
        public float turnSpeed = 6f;
        [Tooltip("Inclinación (grados) según la velocidad vertical")]
        public float pitchByVerticalSpeed = 12f;
        [Tooltip("Flotación suave en reposo")]
        public float bobAmplitude = 0.08f;
        public float bobFrequency = 1.4f;

        [Header("Debug (gizmos)")]
        public bool drawGizmos = true;

        // Datos de depuración que escribe el comportamiento
        [HideInInspector] public SteeringMode debugMode = SteeringMode.Idle;
        [HideInInspector] public Vector3 debugTarget;
        [HideInInspector] public float debugSlowingRadius;
        [HideInInspector] public Vector3 debugSteering;

        private float facingSign = 1f;
        private float bobPhase;

        public Vector3 Position { get { return body.position; } }
        public Vector3 Velocity { get { return body.velocity; } }

        protected virtual void Awake()
        {
            body.position = transform.position;
            body.planeZ = transform.position.z;
            if (visual == null) visual = transform;
            bobPhase = Random.value * 10f;
        }

        public void Teleport(Vector3 position)
        {
            body.Teleport(position);
            body.planeZ = position.z;
            transform.position = position;
        }

        /// <summary>Integra una fuerza de steering y actualiza el transform.</summary>
        public void Step(Vector3 steering, float dt)
        {
            debugSteering = steering;
            body.Integrate(steering, dt);

            Vector3 bob = Vector3.zero;
            if (bobAmplitude > 0f)
            {
                // El balanceo es mayor cuando el agente está quieto (flotando).
                float calm = 1f - Mathf.Clamp01(body.velocity.magnitude / Mathf.Max(0.1f, body.maxSpeed));
                bob.y = Mathf.Sin((Time.time + bobPhase) * bobFrequency * Mathf.PI * 2f) * bobAmplitude * calm;
            }

            transform.position = body.position + bob;
            UpdateFacing(dt);
        }

        // Atajos que usan los comportamientos --------------------------------

        public void SeekTo(Vector3 target, float dt)
        {
            debugMode = SteeringMode.Seek;
            debugTarget = target;
            debugSlowingRadius = 0f;
            Step(body.Seek(target), dt);
        }

        public void ArriveAt(Vector3 target, float slowingRadius, float stopRadius, float dt)
        {
            debugMode = SteeringMode.Arrive;
            debugTarget = target;
            debugSlowingRadius = slowingRadius;
            Step(body.Arrive(target, slowingRadius, stopRadius), dt);
        }

        public void FollowPath(PathFollower path, float dt)
        {
            debugMode = SteeringMode.Path;
            debugTarget = path.CurrentTarget;
            debugSlowingRadius = path.IsFinalPoint ? path.slowingRadius : 0f;
            Step(path.Steer(body.position, body.velocity, body.maxSpeed), dt);
        }

        public void Brake(float dt)
        {
            debugMode = SteeringMode.Idle;
            Step(-body.velocity * 2f, dt);
        }

        /// <summary>Gira el modelo hacia una dirección sin moverse (p. ej. mirar al jugador).</summary>
        public void LookTowardsX(float xDirection)
        {
            if (Mathf.Abs(xDirection) > 0.01f) facingSign = Mathf.Sign(xDirection);
        }

        private void UpdateFacing(float dt)
        {
            if (!faceMovement || visual == null) return;

            if (Mathf.Abs(body.velocity.x) > 0.25f)
            {
                facingSign = Mathf.Sign(body.velocity.x);
            }

            float yaw = facingSign > 0f ? yawWhenMovingRight : -yawWhenMovingRight;
            float pitch = 0f;
            if (body.maxSpeed > 0.01f)
            {
                pitch = -Mathf.Clamp(body.velocity.y / body.maxSpeed, -1f, 1f) * pitchByVerticalSpeed;
            }

            Quaternion target = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(pitch, 0f, 0f);
            visual.rotation = Quaternion.Slerp(visual.rotation, target, 1f - Mathf.Exp(-turnSpeed * dt));
        }

        protected virtual void OnDrawGizmosSelected()
        {
            if (!drawGizmos) return;
            DrawSteeringGizmos();
        }

        public void DrawSteeringGizmos()
        {
            Vector3 p = Application.isPlaying ? body.position : transform.position;

            // velocidad (verde) y fuerza de steering (amarillo)
            Gizmos.color = Color.green;
            Gizmos.DrawLine(p, p + body.velocity * 0.5f);
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(p, p + debugSteering * 0.2f);

            if (debugMode == SteeringMode.Idle) return;

            Gizmos.color = debugMode == SteeringMode.Seek ? Color.red : Color.cyan;
            Gizmos.DrawLine(p, debugTarget);
            Gizmos.DrawWireSphere(debugTarget, 0.25f);

            if (debugSlowingRadius > 0f)
            {
                Gizmos.color = new Color(0f, 1f, 1f, 0.4f);
                Gizmos.DrawWireSphere(debugTarget, debugSlowingRadius);
            }
        }
    }
}
