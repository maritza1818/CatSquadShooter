using System;
using System.Collections.Generic;
using UnityEngine;

namespace CatSquad.RescateProfundo
{
    /// <summary>
    /// Brazo manipulador (MANO HÁBIL / manipuladora): alcanzar, sujetar, trasladar, verter y soltar.
    ///
    /// Uso de ARRIVE: la mano del niño define un OBJETIVO para la pinza
    /// (base + dirección * extensión * alcance). La pinza no salta a ese punto:
    /// llega con Arrive, así
    ///   1) se detiene con precisión encima del objeto (desacelera al acercarse), y
    ///   2) filtra el temblor/ruido de la estimación de pose de la webcam.
    /// </summary>
    public class ManipulatorArm : MonoBehaviour
    {
        [Header("Referencias")]
        public SubmarineController submarine;
        [Tooltip("Hombro del brazo (punto de anclaje en el submarino)")]
        public Transform mount;

        [Header("Alcance")]
        [Tooltip("Alcance del brazo en metros. 100 % del alcance calibrado del niño = este valor.")]
        public float reach = 3.4f;
        [Tooltip("Pose de reposo de la pinza (dirección * fracción del alcance) cuando no hay mano detectada")]
        public Vector2 restOffset = new Vector2(0.35f, -0.35f);

        [Header("Primera persona")]
        [Tooltip("Profundidad de trabajo por defecto delante del hombro (m)")]
        public float restDepth = 2.0f;
        [Tooltip("Hasta dónde se extiende sola la pinza buscando un objeto delante (m)")]
        public float maxDepth = 4.5f;
        public float probeRadius = 0.45f;

        [Header("Arrive de la pinza")]
        public SteeringBody tip = new SteeringBody { maxSpeed = 11f, maxForce = 90f, mass = 0.1f };
        public float slowingRadius = 1.4f;
        public float stopRadius = 0.02f;

        [Header("Agarre")]
        public float grabRadius = 0.8f;
        [Tooltip("Modo sin detección de puño: mantener la pinza quieta sobre el objeto este tiempo para agarrar")]
        public float dwellGrabTime = 0.8f;
        public float dwellReleaseTime = 1.1f;
        [Tooltip("Tiempo con compensación de tronco antes de que el objeto resbale")]
        public float slipDelay = 0.25f;

        [Header("Visual")]
        public Transform upperArm;
        public Transform foreArm;
        public Transform elbowJoint;
        public Transform claw;
        public Transform fingerA;
        public Transform fingerB;
        public float fingerOpenAngle = 38f;
        public float fingerClosedAngle = 6f;
        public float segmentThickness = 0.22f;
        public Transform targetMarker;

        public Grabbable Held { get; private set; }
        public float HeldExtension { get; private set; }
        public Vector3 TipPosition { get { return tip.position; } }
        public Vector3 GripPoint { get { return tip.position; } }
        public Vector3 BasePosition { get { return basePos; } }
        public float WristAngle { get; private set; }
        public bool GripClosed { get; private set; }
        public bool ArmEnabled { get; set; }
        /// <summary>El lanzador usa la mano del brazo: la pinza se queda en reposo mientras tanto.</summary>
        public bool Suspended { get; set; }
        public Grabbable HoverCandidate { get; private set; }
        public float DwellProgress { get; private set; }

        public event Action<Grabbable, float> OnGrab;
        public event Action<Grabbable, bool> OnRelease;

        private Vector3 basePos;
        private bool needRegrip;
        private float dwellTimer;
        private float compensationTimer;
        private float fingerAngle;
        private readonly RaycastHit[] hits = new RaycastHit[16];
        private readonly Collider[] overlaps = new Collider[24];
        private readonly HashSet<Collider> ignoredColliders = new HashSet<Collider>();

        private void Awake()
        {
            ArmEnabled = true;
        }

        private void Start()
        {
            if (submarine == null) submarine = GetComponentInParent<SubmarineController>();
            if (submarine != null)
            {
                foreach (Collider c in submarine.GetComponentsInChildren<Collider>()) ignoredColliders.Add(c);
            }
            basePos = MountPosition();
            tip.Teleport(basePos + (Vector3)(restOffset * reach));
        }

        private bool FirstPerson { get { return GameManager.FirstPerson; } }

        private Vector3 MountPosition()
        {
            Vector3 p = mount != null ? mount.position : transform.position;
            if (!FirstPerson) p.z = 0f;
            return p;
        }

        // Base del plano de trabajo en primera persona
        private Vector3 ArmForward { get { return submarine != null ? submarine.Forward : Vector3.forward; } }
        private Vector3 ArmRight { get { return submarine != null ? submarine.Right : Vector3.right; } }

        /// <summary>Profundidad actual de la pinza (se extiende sola hasta el objeto que tiene delante).</summary>
        public float CurrentDepth { get; private set; }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            basePos = MountPosition();
            BimanualInput input = BimanualInput.Instance;
            if (input != null)
            {
                if (FirstPerson) input.SetArmContextFirstPerson(basePos, reach, ArmRight, Vector3.up, ArmForward, restDepth);
                else input.SetArmContext(basePos, reach, 0f);
            }

            BimanualFrame frame = input != null ? input.Current : new BimanualFrame();
            bool active = ArmEnabled && !Suspended && frame.manip.tracked;

            // ---- 1. Objetivo de la pinza a partir de la mano
            Vector3 desired;
            if (FirstPerson)
            {
                // La mano mueve la pinza en el plano frente a la cabina (arriba/abajo/lados = alcance medido);
                // la profundidad se ajusta sola hasta el objeto que haya delante.
                Vector2 offset = active ? frame.manip.offset : new Vector2(0f, -0.25f);
                if (offset.magnitude > 1f) offset = offset.normalized;
                Vector3 planar = (ArmRight * offset.x + Vector3.up * offset.y) * reach;
                CurrentDepth = Mathf.Lerp(CurrentDepth <= 0f ? restDepth : CurrentDepth, ProbeDepth(basePos + planar), 1f - Mathf.Exp(-6f * dt));
                desired = basePos + planar + ArmForward * CurrentDepth;
            }
            else
            {
                Vector2 offset = active ? frame.manip.offset : new Vector2(restOffset.x * FacingSign(), restOffset.y);
                if (offset.magnitude > 1f) offset = offset.normalized;
                desired = basePos + (Vector3)(offset * reach);
            }
            desired = ClampByWalls(basePos, desired);

            // ---- 2. ARRIVE hacia el objetivo
            if (FirstPerson) tip.lockToPlane = false;
            else tip.planeZ = 0f;
            tip.Integrate(tip.Arrive(desired, slowingRadius, stopRadius), dt);

            // La pinza nunca atraviesa paredes ni supera el alcance
            float maxLength = FirstPerson ? Mathf.Sqrt(reach * reach + maxDepth * maxDepth) : reach;
            Vector3 fromBase = tip.position - basePos;
            if (fromBase.magnitude > maxLength) tip.position = basePos + fromBase.normalized * maxLength;
            tip.position = ClampByWalls(basePos, tip.position);

            if (targetMarker != null)
            {
                targetMarker.gameObject.SetActive(active);
                if (FirstPerson)
                {
                    targetMarker.position = desired;
                    targetMarker.rotation = Quaternion.LookRotation(ArmForward) * Quaternion.Euler(90f, 0f, 0f);
                }
                else
                {
                    targetMarker.position = desired + Vector3.back * 0.2f;
                }
            }

            // ---- 3. Muñeca
            float wristTarget = active ? frame.manip.wristAngle : 0f;
            WristAngle = Mathf.Lerp(WristAngle, wristTarget, 1f - Mathf.Exp(-12f * dt));

            // ---- 4. Agarre / suelta
            HoverCandidate = Held == null ? FindCandidate() : null;
            if (active) HandleGrip(frame, dt);
            else if (Held != null && !ArmEnabled) Release(false);

            // ---- 5. Compensación de tronco: el objeto resbala (sin mensajes de error)
            bool compensationOn = GameManager.Instance == null || GameManager.Instance.Settings.compensationEnabled;
            if (Held != null && compensationOn && frame.compensating)
            {
                compensationTimer += dt;
                if (compensationTimer >= slipDelay) Release(true);
            }
            else
            {
                compensationTimer = 0f;
            }

            if (Held != null)
            {
                if (FirstPerson) Held.SetHeldRotation(Quaternion.LookRotation(ArmForward) * Quaternion.Euler(0f, 0f, WristAngle));
                else Held.SetHeldAngle(WristAngle);
            }

            UpdateVisuals(dt);
        }

        private float FacingSign()
        {
            return submarine != null ? submarine.FacingSign : 1f;
        }

        private void HandleGrip(BimanualFrame frame, float dt)
        {
            if (frame.manip.gripKnown)
            {
                bool grip = frame.manip.grip;
                GripClosed = grip;
                DwellProgress = 0f;

                if (!grip) needRegrip = false;

                if (Held == null && grip && !needRegrip && HoverCandidate != null)
                {
                    Grab(HoverCandidate, frame.manip.extension);
                    // Si el objeto se "consumió" (caja abierta), hay que abrir y cerrar la mano otra vez
                    if (Held == null) needRegrip = true;
                }
                else if (Held != null && !grip)
                {
                    Release(false);
                }
                return;
            }

            // Sin detección de puño: agarrar manteniendo la pinza quieta sobre el objeto,
            // soltar manteniéndola quieta lejos de donde se agarró.
            bool still = tip.velocity.magnitude < 0.6f;
            if (Held == null)
            {
                GripClosed = false;
                if (HoverCandidate != null && still) dwellTimer += dt; else dwellTimer = 0f;
                DwellProgress = Mathf.Clamp01(dwellTimer / dwellGrabTime);
                if (dwellTimer >= dwellGrabTime)
                {
                    dwellTimer = 0f;
                    Grab(HoverCandidate, frame.manip.extension);
                }
            }
            else
            {
                GripClosed = true;
                bool farFromStart = Vector3.Distance(Held.transform.position, Held.GrabStartPosition) > 1.2f;
                if (still && farFromStart && Held.kind != GrabKind.Combustible) dwellTimer += dt; else dwellTimer = 0f;
                DwellProgress = Mathf.Clamp01(dwellTimer / dwellReleaseTime);
                if (dwellTimer >= dwellReleaseTime)
                {
                    dwellTimer = 0f;
                    Release(false);
                }
            }
        }

        private Grabbable FindCandidate()
        {
            int count = Physics.OverlapSphereNonAlloc(tip.position, grabRadius, overlaps, ~0, QueryTriggerInteraction.Collide);
            Grabbable best = null;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                Grabbable g = overlaps[i].GetComponentInParent<Grabbable>();
                if (g == null || !g.CanBeGrabbed) continue;
                float d = Vector3.Distance(overlaps[i].ClosestPoint(tip.position), tip.position);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = g;
                }
            }
            return best;
        }

        public void Grab(Grabbable g, float extension)
        {
            if (g == null || Held != null) return;
            HeldExtension = extension;
            compensationTimer = 0f;

            if (OnGrab != null) OnGrab(g, extension);

            if (g.ConsumeOnGrab)
            {
                g.NotifyGrabbed(this, extension);
                g.NotifyReleased(false);
                return;
            }

            Held = g;
            g.NotifyGrabbed(this, extension);
        }

        public void Release(bool slipped)
        {
            if (Held == null) return;
            Grabbable g = Held;
            Held = null;
            if (slipped) needRegrip = true; // resbaló: para volver a sujetar hay que abrir y cerrar la mano
            g.NotifyReleased(slipped);
            if (OnRelease != null) OnRelease(g, slipped);
        }

        /// <summary>Primera persona: busca delante (a lo largo de la mirada) un objeto sujetable y devuelve su profundidad.</summary>
        private float ProbeDepth(Vector3 from)
        {
            RaycastHit[] probe = Physics.SphereCastAll(from, probeRadius, ArmForward, maxDepth, ~0, QueryTriggerInteraction.Collide);
            float best = restDepth;
            float bestDist = float.MaxValue;
            foreach (RaycastHit h in probe)
            {
                if (h.collider == null || ignoredColliders.Contains(h.collider)) continue;
                Grabbable g = h.collider.GetComponentInParent<Grabbable>();
                bool isTarget = (g != null && (g.CanBeGrabbed || g == Held)) || h.collider.GetComponentInParent<PumpStation>() != null;
                if (!isTarget) continue;
                if (h.distance < bestDist)
                {
                    bestDist = h.distance;
                    best = Mathf.Clamp(h.distance + 0.35f, 0.8f, maxDepth);
                }
            }
            return best;
        }

        /// <summary>Recorta un punto para que la línea base→punto no atraviese paredes (las rejillas sí se atraviesan).</summary>
        private Vector3 ClampByWalls(Vector3 from, Vector3 to)
        {
            Vector3 dir = to - from;
            float dist = dir.magnitude;
            if (dist < 0.001f) return to;
            dir /= dist;

            int count = Physics.RaycastNonAlloc(from, dir, hits, dist, ~0, QueryTriggerInteraction.Ignore);
            float nearest = dist;
            for (int i = 0; i < count; i++)
            {
                Collider c = hits[i].collider;
                if (c == null || ignoredColliders.Contains(c)) continue;
                if (c.GetComponentInParent<Grabbable>() != null) continue;
                if (c.GetComponentInParent<Grate>() != null) continue;
                if (c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic) continue;
                if (hits[i].distance < nearest) nearest = hits[i].distance;
            }

            if (nearest < dist)
            {
                return from + dir * Mathf.Max(0f, nearest - 0.3f);
            }
            return to;
        }

        // ------------------------------------------------------------------ visual (IK de 2 huesos en el plano)

        private void UpdateVisuals(float dt)
        {
            Vector3 a = basePos;
            Vector3 c = tip.position;
            // En 3D los segmentos se ajustan a la distancia para que el codo no tape la vista
            float l1 = FirstPerson ? Mathf.Max((c - a).magnitude * 0.58f, 0.8f) : reach * 0.52f;
            float l2 = l1;
            Vector3 ac = c - a;
            float d = Mathf.Clamp(ac.magnitude, 0.05f, l1 + l2 - 0.001f);
            Vector3 dir = ac.sqrMagnitude > 1e-6f ? ac.normalized : Vector3.right;

            // ley de cosenos
            float along = (l1 * l1 - l2 * l2 + d * d) / (2f * d);
            float h = Mathf.Sqrt(Mathf.Max(0f, l1 * l1 - along * along));
            Vector3 perp;
            if (FirstPerson)
            {
                // el codo se abre hacia el costado y un poco abajo, para no tapar la vista del piloto
                Vector3 bend = ArmRight * 0.9f - Vector3.up * 0.35f;
                perp = bend - dir * Vector3.Dot(bend, dir);
                if (perp.sqrMagnitude < 1e-4f) perp = ArmRight;
                perp.Normalize();
            }
            else
            {
                perp = new Vector3(-dir.y, dir.x, 0f);
                // el codo se dobla hacia arriba (lejos del casco)
                if (perp.y < 0f) perp = -perp;
            }
            Vector3 elbow = a + dir * along + perp * h;

            PlaceSegment(upperArm, a, elbow);
            PlaceSegment(foreArm, elbow, c);
            if (elbowJoint != null) elbowJoint.position = elbow;

            if (claw != null)
            {
                claw.position = c;
                Vector3 forearmDir = (c - elbow).normalized;
                if (FirstPerson)
                {
                    // el eje +Y de la pinza apunta como el antebrazo; la muñeca gira sobre ese eje
                    claw.rotation = Quaternion.LookRotation(forearmDir, Vector3.up) * Quaternion.Euler(90f, 0f, 0f) * Quaternion.Euler(0f, WristAngle, 0f);
                }
                else
                {
                    float baseAngle = Mathf.Atan2(forearmDir.y, forearmDir.x) * Mathf.Rad2Deg;
                    // la pinza apunta en la dirección del antebrazo y gira con la muñeca (rotación de antebrazo = rollo sobre su eje)
                    claw.rotation = Quaternion.Euler(0f, 0f, baseAngle - 90f) * Quaternion.Euler(0f, WristAngle, 0f);
                }
            }

            float targetFinger = (GripClosed || Held != null) ? fingerClosedAngle : fingerOpenAngle;
            fingerAngle = Mathf.Lerp(fingerAngle, targetFinger, 1f - Mathf.Exp(-14f * dt));
            if (fingerA != null) fingerA.localRotation = Quaternion.Euler(0f, 0f, fingerAngle);
            if (fingerB != null) fingerB.localRotation = Quaternion.Euler(0f, 0f, -fingerAngle);
        }

        private void PlaceSegment(Transform segment, Vector3 from, Vector3 to)
        {
            if (segment == null) return;
            Vector3 delta = to - from;
            float length = delta.magnitude;
            segment.position = (from + to) * 0.5f;
            segment.rotation = length > 1e-4f ? Quaternion.FromToRotation(Vector3.up, delta / length) : Quaternion.identity;
            // Se asume un cilindro primitivo (alto 2 en Y)
            segment.localScale = new Vector3(segmentThickness, length * 0.5f, segmentThickness);
        }

        private void OnDrawGizmos()
        {
            Vector3 b = Application.isPlaying ? basePos : MountPosition();
            Gizmos.color = new Color(1f, 1f, 1f, 0.25f);
            Gizmos.DrawWireSphere(b, reach);
            Gizmos.color = new Color(1f, 1f, 1f, 0.15f);
            Gizmos.DrawWireSphere(b, reach * 0.7f);
            Gizmos.DrawWireSphere(b, reach * 0.9f);

            if (!Application.isPlaying) return;
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(tip.position, grabRadius);
        }
    }
}
