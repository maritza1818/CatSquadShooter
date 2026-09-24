using UnityEngine;

namespace CatSquad.RescateProfundo
{
    /// <summary>
    /// Algoritmos de steering del Lab 2 (Craig Reynolds, "Steering Behaviors For Autonomous Characters").
    ///
    /// En el lab movíamos el objeto con:  posición += dirección.normalized * speed * dt
    /// y nos deteníamos de golpe a una "distanciaDeParada". Eso es un Seek cinemático.
    ///
    /// Aquí están las versiones con fuerzas (steering), que son las que usa todo el juego:
    ///
    ///   SEEK
    ///     velocidadDeseada = normalizar(objetivo - posición) * velocidadMáxima
    ///     fuerzaSteering   = velocidadDeseada - velocidadActual
    ///   El agente siempre quiere ir a máxima velocidad hacia el objetivo, así que
    ///   llega rápido pero lo pasa de largo y "orbita" (overshoot). Ideal cuando
    ///   el objetivo es ATRAPAR/ALCANZAR algo (fragmentos que vuelan al submarino).
    ///
    ///   ARRIVE
    ///     d = |objetivo - posición|
    ///     si d < radioDeFrenado:  rapidezDeseada = velocidadMáxima * (d / radioDeFrenado)
    ///     si no:                  rapidezDeseada = velocidadMáxima          (igual que Seek)
    ///     velocidadDeseada = normalizar(objetivo - posición) * rapidezDeseada
    ///     fuerzaSteering   = velocidadDeseada - velocidadActual
    ///   Es un Seek que frena proporcionalmente a la distancia: se detiene suave y
    ///   exacto sobre el objetivo. Ideal cuando hay que LLEGAR Y QUEDARSE en un punto
    ///   (gato guía, pinza del brazo, gatitos en fila, auto-emersión).
    ///
    /// Las funciones son puras (sin estado) para poder reutilizarlas en cualquier
    /// cuerpo: agentes cinemáticos, la pinza del brazo o el Rigidbody del submarino.
    /// </summary>
    public static class SteeringBehaviours
    {
        /// <summary>Fuerza de SEEK: ir a máxima velocidad hacia el objetivo.</summary>
        public static Vector3 Seek(Vector3 position, Vector3 velocity, Vector3 target, float maxSpeed)
        {
            Vector3 toTarget = target - position;
            if (toTarget.sqrMagnitude < 1e-8f)
            {
                return -velocity; // ya estamos encima: frenar
            }

            Vector3 desiredVelocity = toTarget.normalized * maxSpeed;
            return desiredVelocity - velocity;
        }

        /// <summary>Fuerza de FLEE (Seek invertido). Solo se usa para que los peces no choquen con el submarino.</summary>
        public static Vector3 Flee(Vector3 position, Vector3 velocity, Vector3 threat, float maxSpeed)
        {
            Vector3 away = position - threat;
            if (away.sqrMagnitude < 1e-8f)
            {
                away = Vector3.up;
            }

            Vector3 desiredVelocity = away.normalized * maxSpeed;
            return desiredVelocity - velocity;
        }

        /// <summary>
        /// Fuerza de ARRIVE: Seek con desaceleración dentro del radio de frenado.
        /// stopRadius: dentro de este radio se considera que ya llegó (se frena del todo).
        /// </summary>
        public static Vector3 Arrive(Vector3 position, Vector3 velocity, Vector3 target,
                                     float maxSpeed, float slowingRadius, float stopRadius = 0.05f)
        {
            Vector3 toTarget = target - position;
            float distance = toTarget.magnitude;

            if (distance <= stopRadius)
            {
                return -velocity; // llegó: la velocidad deseada es cero
            }

            float desiredSpeed = maxSpeed;
            if (slowingRadius > 0.0001f && distance < slowingRadius)
            {
                desiredSpeed = maxSpeed * (distance / slowingRadius);
            }

            Vector3 desiredVelocity = (toTarget / distance) * desiredSpeed;
            return desiredVelocity - velocity;
        }

        /// <summary>Limita la magnitud de un vector (truncate de Reynolds).</summary>
        public static Vector3 Truncate(Vector3 v, float max)
        {
            float sqr = v.sqrMagnitude;
            if (sqr > max * max && sqr > 0f)
            {
                return v * (max / Mathf.Sqrt(sqr));
            }
            return v;
        }

        /// <summary>Proyecta un vector al plano XY del juego (vista 2.5D).</summary>
        public static Vector3 Flatten(Vector3 v)
        {
            v.z = 0f;
            return v;
        }
    }
}
