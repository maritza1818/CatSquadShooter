using System.Collections.Generic;
using UnityEngine;

namespace CatSquad.RescateProfundo
{
    /// <summary>
    /// Cálculo de la "pose de trabajo" en primera persona: dónde debe quedar el submarino para que
    /// un objetivo quede, respecto del hombro del brazo, a cierta FRACCIÓN del alcance en una DIRECCIÓN
    /// dada (arriba, a un lado, en diagonal...) y a una profundidad fija delante de la cabina.
    /// </summary>
    public static class WorkPose
    {
        private static readonly Collider[] overlaps = new Collider[32];

        public static readonly Vector2[] Directions =
        {
            new Vector2(0f, 1f), new Vector2(-0.71f, 0.71f), new Vector2(0.71f, 0.71f),
            new Vector2(-1f, 0f), new Vector2(1f, 0f),
            new Vector2(-0.71f, -0.71f), new Vector2(0.71f, -0.71f), new Vector2(0f, -1f)
        };

        public static bool Compute(SubmarineController sub, ManipulatorArm arm, Vector3 target, Vector2 dir,
                                   float fraction, float depth, out Vector3 pos, out float yaw)
        {
            // mirar hacia el objetivo (en horizontal)
            Vector3 to = target - sub.transform.position;
            to.y = 0f;
            if (to.sqrMagnitude < 0.01f) to = sub.Forward;
            yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            Quaternion q = Quaternion.Euler(0f, yaw, 0f);
            Vector3 f = q * Vector3.forward;
            Vector3 r = q * Vector3.right;

            // hombro del brazo en coordenadas locales del submarino
            Vector3 mountLocal = new Vector3(0f, -0.55f, 0.75f);
            if (arm != null && arm.mount != null)
            {
                mountLocal = Quaternion.Inverse(sub.transform.rotation) * (arm.mount.position - sub.transform.position);
            }

            float reach = arm != null ? arm.reach : 3.4f;
            Vector3 lateral = (r * dir.x + Vector3.up * dir.y).normalized * fraction * reach;
            pos = target - lateral - f * depth - q * mountLocal;
            return IsFree(pos, f, sub);
        }

        /// <summary>¿Cabe el casco (cápsula a lo largo de la proa) en esa posición, bajo el agua?</summary>
        public static bool IsFree(Vector3 pos, Vector3 forward, SubmarineController sub)
        {
            if (!WaterZone.PointIsUnderwater(pos + Vector3.up * 0.7f)) return false;
            Vector3 a = pos + forward * 0.8f;
            Vector3 b = pos - forward * 0.8f;
            int n = Physics.OverlapCapsuleNonAlloc(a, b, 0.75f, overlaps, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                Collider c = overlaps[i];
                if (sub != null && c.GetComponentInParent<SubmarineController>() == sub) continue;
                if (c.GetComponentInParent<Grabbable>() != null) continue;
                if (c.GetComponentInParent<RescuedKitten>() != null) continue;
                return false; // paredes, rejillas, bombas, jaulas...
            }
            return true;
        }

        /// <summary>Prueba las direcciones empezando por la preferida hasta encontrar una pose libre.</summary>
        public static bool FindPose(SubmarineController sub, ManipulatorArm arm, Vector3 target, int preferredIndex,
                                    float fraction, float depth, out Vector3 pos, out float yaw)
        {
            // Primero la dirección preferida a distintas profundidades, luego las demás
            float[] depths = { depth, depth + 0.7f, depth + 1.4f };
            for (int k = 0; k < Directions.Length; k++)
            {
                Vector2 d = Directions[(preferredIndex + k) % Directions.Length];
                foreach (float dd in depths)
                {
                    if (Compute(sub, arm, target, d, fraction, dd, out pos, out yaw)) return true;
                }
            }
            // última opción: justo delante, sin desplazamiento lateral
            return Compute(sub, arm, target, Vector2.down, 0.3f, depth, out pos, out yaw);
        }
    }

    /// <summary>
    /// Primera persona: cuando el niño se acerca a un objetivo (caja de chatarra, cerrojo de jaula),
    /// el submarino hace ARRIVE hasta una pose de trabajo y se ancla. Desde ahí el objetivo queda
    /// entre el 70 % y el 90 % del alcance del brazo, en una dirección que va variando
    /// (arriba, a los lados, en diagonal) para trabajar distintos movimientos.
    /// Reemplaza a las "corrientes" de la vista lateral.
    /// </summary>
    public class ReachAnchor : MonoBehaviour
    {
        public Grabbable target;
        public float triggerDistance = 5.5f;
        public float workDepth = 2.0f;
        [Range(0f, 1.2f)] public float minFraction = 0.72f;
        [Range(0f, 1.2f)] public float maxFraction = 0.88f;
        [Tooltip("Dirección preferida (índice en WorkPose.Directions); el constructor la va rotando")]
        public int preferredDirection;
        [Tooltip("Ángulo máximo entre la proa y el objetivo para anclarse (el niño debe ir hacia él)")]
        public float maxHeadingAngle = 75f;

        private static readonly List<ReachAnchor> all = new List<ReachAnchor>();
        public static IReadOnlyList<ReachAnchor> All { get { return all; } }

        private float retryTimer;

        private void OnEnable() { if (!all.Contains(this)) all.Add(this); }
        private void OnDisable() { all.Remove(this); }

        private bool TargetAvailable
        {
            get { return target != null && target.gameObject.activeInHierarchy && target.CanBeGrabbed; }
        }

        private void Update()
        {
            if (!GameManager.FirstPerson || GameManager.Instance == null) return;
            SubmarineController sub = GameManager.Instance.submarine;
            if (sub == null) return;

            // Objetivo usado: soltar el anclaje
            if (!TargetAvailable)
            {
                sub.ReleaseAnchor(this);
                return;
            }

            if (retryTimer > 0f) { retryTimer -= Time.deltaTime; return; }
            if (!sub.CanDock || !sub.InputEnabled) return;
            // con algo en la pinza no sirve anclarse (p. ej. llevando un bidón a la bomba)
            if (GameManager.Instance.arm != null && GameManager.Instance.arm.Held != null) return;

            Vector3 to = target.transform.position - sub.transform.position;
            if (to.magnitude > triggerDistance || sub.Velocity.magnitude > 2.6f) return;
            Vector3 flat = new Vector3(to.x, 0f, to.z);
            if (flat.sqrMagnitude > 0.25f && Vector3.Angle(sub.Forward, flat) > maxHeadingAngle) return;

            if (!TryAnchor(sub)) retryTimer = 1f;
        }

        /// <summary>
        /// Calcula la pose de trabajo y ancla el submarino (Arrive hasta ella). La usa también el riel
        /// del modo automático, que ya llegó y se orientó hacia el objetivo.
        /// </summary>
        public bool TryAnchor(SubmarineController sub)
        {
            if (!TargetAvailable || sub == null || !sub.CanDock) return false;
            Vector3 pos;
            float yaw;
            float fraction = Random.Range(minFraction, maxFraction);
            ManipulatorArm arm = GameManager.Instance != null ? GameManager.Instance.arm : null;
            if (!WorkPose.FindPose(sub, arm, target.transform.position, preferredDirection, fraction, workDepth, out pos, out yaw)) return false;
            sub.AnchorAt(pos, yaw, this);
            return sub.AnchorOwner == this;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.4f, 1f, 1f, 0.4f);
            Gizmos.DrawWireSphere(transform.position, triggerDistance);
        }
    }
}
