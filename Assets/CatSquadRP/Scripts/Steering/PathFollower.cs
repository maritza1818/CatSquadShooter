using System.Collections.Generic;
using UnityEngine;

namespace CatSquad.RescateProfundo
{
    /// <summary>
    /// Seguimiento de ruta = combinación de los dos algoritmos del lab:
    ///   - SEEK hacia cada punto intermedio (no queremos frenar en cada esquina).
    ///   - ARRIVE en el último punto (queremos detenernos exactamente ahí).
    /// Lo usan el gato guía (rutas del tutorial) y el submarino al emerger solo.
    /// </summary>
    [System.Serializable]
    public class PathFollower
    {
        public float waypointRadius = 1.2f;
        public float slowingRadius = 3f;
        public float stopRadius = 0.15f;

        private readonly List<Vector3> points = new List<Vector3>();
        private int index;

        public bool HasPath { get { return points.Count > 0; } }
        public int CurrentIndex { get { return index; } }
        public int Count { get { return points.Count; } }
        public IReadOnlyList<Vector3> Points { get { return points; } }

        public bool IsFinalPoint { get { return index >= points.Count - 1; } }

        public Vector3 CurrentTarget
        {
            get { return points.Count == 0 ? Vector3.zero : points[Mathf.Clamp(index, 0, points.Count - 1)]; }
        }

        public Vector3 FinalTarget
        {
            get { return points.Count == 0 ? Vector3.zero : points[points.Count - 1]; }
        }

        public void SetPath(IEnumerable<Vector3> newPoints)
        {
            points.Clear();
            points.AddRange(newPoints);
            index = 0;
        }

        public void SetSingleTarget(Vector3 target)
        {
            points.Clear();
            points.Add(target);
            index = 0;
        }

        public void Clear()
        {
            points.Clear();
            index = 0;
        }

        /// <summary>¿Llegó al último punto y está casi quieto?</summary>
        public bool Arrived(Vector3 position, Vector3 velocity, float speedTolerance = 0.35f)
        {
            if (points.Count == 0) return true;
            if (!IsFinalPoint) return false;
            return (FinalTarget - position).magnitude <= Mathf.Max(stopRadius * 2f, 0.3f)
                   && velocity.magnitude <= speedTolerance;
        }

        /// <summary>Calcula la fuerza de steering para la posición/velocidad dadas.</summary>
        public Vector3 Steer(Vector3 position, Vector3 velocity, float maxSpeed)
        {
            if (points.Count == 0)
            {
                return -velocity;
            }

            // Avanza de punto intermedio en cuanto entramos en su radio.
            while (!IsFinalPoint && (points[index] - position).magnitude <= waypointRadius)
            {
                index++;
            }

            if (IsFinalPoint)
            {
                return SteeringBehaviours.Arrive(position, velocity, points[index], maxSpeed, slowingRadius, stopRadius);
            }

            return SteeringBehaviours.Seek(position, velocity, points[index], maxSpeed);
        }

        public void DrawGizmos(Color color)
        {
            if (points.Count == 0) return;
            Gizmos.color = color;
            for (int i = 0; i < points.Count; i++)
            {
                Gizmos.DrawWireSphere(points[i], i == points.Count - 1 ? 0.35f : 0.15f);
                if (i > 0) Gizmos.DrawLine(points[i - 1], points[i]);
            }
            Gizmos.color = new Color(color.r, color.g, color.b, 0.35f);
            Gizmos.DrawWireSphere(FinalTarget, slowingRadius);
        }
    }
}
