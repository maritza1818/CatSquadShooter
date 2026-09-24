using UnityEngine;

namespace CatSquad.RescateProfundo
{
    /// <summary>
    /// Cuerpo de steering "puro": posición + velocidad + límites.
    /// Se integra con Euler semi-implícito, igual que en el lab pero con aceleración:
    ///     aceleración = truncar(fuerza, maxForce) / masa
    ///     velocidad   = truncar(velocidad + aceleración * dt, maxSpeed)
    ///     posición   += velocidad * dt
    /// Lo usan la pinza del brazo, los fragmentos, los peces y el SteeringAgent.
    /// </summary>
    [System.Serializable]
    public class SteeringBody
    {
        public Vector3 position;
        public Vector3 velocity;

        [Tooltip("Velocidad máxima (m/s)")]
        public float maxSpeed = 5f;

        [Tooltip("Fuerza máxima de steering: qué tan rápido puede cambiar de dirección")]
        public float maxForce = 12f;

        [Tooltip("Masa: a más masa, más 'inercia' tiene el giro")]
        public float mass = 1f;

        [Tooltip("Si está activo, el cuerpo vive en el plano Z = planeZ (vista 2.5D)")]
        public bool lockToPlane = true;
        public float planeZ = 0f;

        public Vector3 Seek(Vector3 target)
        {
            return SteeringBehaviours.Seek(position, velocity, target, maxSpeed);
        }

        public Vector3 Arrive(Vector3 target, float slowingRadius, float stopRadius = 0.05f)
        {
            return SteeringBehaviours.Arrive(position, velocity, target, maxSpeed, slowingRadius, stopRadius);
        }

        public Vector3 Flee(Vector3 threat)
        {
            return SteeringBehaviours.Flee(position, velocity, threat, maxSpeed);
        }

        public void Integrate(Vector3 steering, float dt)
        {
            Vector3 acceleration = SteeringBehaviours.Truncate(steering, maxForce) / Mathf.Max(0.01f, mass);
            velocity = SteeringBehaviours.Truncate(velocity + acceleration * dt, maxSpeed);

            if (lockToPlane)
            {
                velocity.z = 0f;
            }

            position += velocity * dt;

            if (lockToPlane)
            {
                position.z = planeZ;
            }
        }

        public void Teleport(Vector3 newPosition)
        {
            position = newPosition;
            velocity = Vector3.zero;
        }
    }
}
