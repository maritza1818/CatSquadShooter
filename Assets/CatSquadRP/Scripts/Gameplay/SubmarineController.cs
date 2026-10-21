using System;
using System.Collections.Generic;
using UnityEngine;

namespace CatSquad.RescateProfundo
{
    public enum SubState { Libre, Acoplado, Congelado, PilotoAutomatico }

    /// <summary>
    /// Submarino del gato. Lo mueve la MANO ASISTENTE (panel de mando: palancas de desplazamiento).
    /// - Solo puede estar bajo el agua: si la superficie está por debajo, flota/cae.
    /// - Límite de presión según la mejora de Profundidad (empuje suave hacia arriba, sin daño).
    /// - Deja un rastro de "migas" que usan los gatitos/guía para seguirlo y que el
    ///   piloto automático recorre al revés para emerger (Seek en migas + Arrive al final).
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class SubmarineController : MonoBehaviour
    {
        [Header("Movimiento (panel de mando)")]
        public float maxSpeed = 4.2f;
        public float acceleration = 7f;
        [Range(0.3f, 1.2f)] public float verticalFactor = 0.85f;

        [Header("Primera persona")]
        [Tooltip("Grados por segundo al girar con la palanca")]
        public float turnRate = 65f;
        [Tooltip("Fracción de la velocidad al retroceder")]
        public float reverseFactor = 0.6f;
        [Tooltip("Cabeceo máximo (grados): la proa mira hacia abajo al bajar y hacia arriba al subir")]
        public float maxDivePitch = 35f;
        [Tooltip("Qué tan rápido se inclina la proa (1/s)")]
        public float pitchResponse = 3f;

        [Header("Agua")]
        [Tooltip("Distancia del centro del submarino bajo la superficie cuando flota")]
        public float surfaceOffset = 0.4f;
        public float airGravity = 14f;

        [Header("Piloto automático (Arrive)")]
        public float autopilotSpeed = 4f;
        public float autopilotMaxForce = 12f;
        [Tooltip("Ganancia de respuesta del steering (1/segundos): más alto = gira más cerrado")]
        public float autopilotResponsiveness = 3.5f;
        public PathFollower autopilotPath = new PathFollower { waypointRadius = 1.3f, slowingRadius = 3.5f, stopRadius = 0.25f };

        [Header("Visual")]
        public Transform visual;
        [Tooltip("Yaw del modelo al mirar a la derecha (90 = perfil)")]
        public float yawRight = 100f;
        public float turnSpeed = 5f;
        public float maxPitch = 14f;
        public Transform propeller;
        public Light headlight;
        public ParticleSystem bubbles;

        [Header("Puntos")]
        [Tooltip("Hombro del brazo robótico")]
        public Transform armMount;
        [Tooltip("Compuerta de carga: aquí vuelan los fragmentos (Seek)")]
        public Transform cargoPort;

        [Header("Rastro")]
        public float breadcrumbSpacing = 1.1f;
        public int maxBreadcrumbs = 600;

        public SubState State { get; private set; }
        public PumpStation DockedPump { get; private set; }
        /// <summary>Objeto al que está anclado (bomba, caja, cerrojo...).</summary>
        public UnityEngine.Object AnchorOwner { get; private set; }
        public bool FirstPerson { get { return GameManager.FirstPerson; } }
        public float Yaw { get { return yaw; } }
        public float Pitch { get { return pitch; } }
        /// <summary>Anclado a algo que se maneja con la palanca (bomba o esclusa): subir la mano no suelta el anclaje.</summary>
        public bool AtLeverStation { get { return State == SubState.Acoplado && (DockedPump != null || AnchorOwner is LockStation); } }
        public Vector3 Forward { get { return Quaternion.Euler(0f, yaw, 0f) * Vector3.forward; } }
        public Vector3 Right { get { return Quaternion.Euler(0f, yaw, 0f) * Vector3.right; } }
        public bool InputEnabled { get; set; }
        public WaterZone CurrentZone { get; private set; }
        public Vector3 Velocity { get { return rb != null ? rb.linearVelocity : Vector3.zero; } }
        public float FacingSign { get { return facingSign; } }
        public bool IsSurfaced { get; private set; }
        /// <summary>Faro alto (botón LUCES del panel de la cabina): el foco llega más lejos.</summary>
        public bool HighBeam { get; set; }

        public event Action PressureWarning;
        public event Action<WaterZone> ZoneChanged;
        public event Action<PumpStation> Docked;
        public event Action Undocked;

        private Rigidbody rb;
        private float facingSign = 1f;
        private Vector3 holdPosition;
        private float undockTimer;
        private float redockCooldown;
        private float pressureCooldown;
        private Action autopilotArrived;
        private readonly List<Vector3> trail = new List<Vector3>();
        private float propellerAngle;
        private bool goingHome;
        private float yaw;
        private float pitch;
        private float anchorYaw;

        public IReadOnlyList<Vector3> Trail { get { return trail; } }

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            rb.useGravity = false;
            rb.constraints = GameManager.FirstPerson
                ? RigidbodyConstraints.FreezeRotation
                : RigidbodyConstraints.FreezePositionZ | RigidbodyConstraints.FreezeRotation;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            yaw = transform.eulerAngles.y;
            InputEnabled = true;
            State = SubState.Libre;
        }

        private void Start()
        {
            ResetTrail(GameManager.Instance != null && GameManager.Instance.dockPoint != null
                ? GameManager.Instance.dockPoint.position
                : transform.position);
        }

        // ------------------------------------------------------------------ API

        public void Freeze()
        {
            holdPosition = rb.position;
            anchorYaw = yaw;
            State = SubState.Congelado;
        }

        public void Unfreeze()
        {
            if (State == SubState.Congelado) State = SubState.Libre;
        }

        public void DockTo(PumpStation pump)
        {
            if (State != SubState.Libre || redockCooldown > 0f) return;
            if (FirstPerson)
            {
                Vector3 pos;
                float poseYaw;
                if (!pump.GetFirstPersonDockPose(this, out pos, out poseYaw)) return;
                AnchorAt(pos, poseYaw, pump);
                DockedPump = pump;
                if (Docked != null) Docked(pump);
                return;
            }
            DockedPump = pump;
            AnchorOwner = pump;
            State = SubState.Acoplado;
            undockTimer = 0f;
            if (pump.dockPoint != null)
            {
                holdPosition = pump.dockPoint.position;
                holdPosition.z = 0f;
                // Mirar hacia la toma de la bomba
                if (pump.intake != null) facingSign = Mathf.Sign(pump.intake.position.x - holdPosition.x + 0.001f);
            }
            if (Docked != null) Docked(pump);
        }

        /// <summary>
        /// Primera persona: el submarino llega con ARRIVE a una pose de trabajo y se queda anclado.
        /// Lo usan las bombas y los objetivos del brazo (ver ReachAnchor).
        /// </summary>
        public void AnchorAt(Vector3 position, float yawDegrees, UnityEngine.Object owner)
        {
            if (State != SubState.Libre || redockCooldown > 0f) return;
            holdPosition = position;
            anchorYaw = yawDegrees;
            AnchorOwner = owner;
            DockedPump = owner as PumpStation;
            State = SubState.Acoplado;
            undockTimer = 0f;
        }

        /// <summary>Mueve la pose de anclaje (p. ej. la esclusa sube o baja el agua con el submarino dentro).</summary>
        public void SetAnchorTarget(Vector3 position, UnityEngine.Object owner)
        {
            if (State == SubState.Acoplado && AnchorOwner == owner) holdPosition = position;
        }

        public void Undock()
        {
            if (State != SubState.Acoplado) return;
            DockedPump = null;
            AnchorOwner = null;
            State = SubState.Libre;
            redockCooldown = 2f;
            if (Undocked != null) Undocked();
        }

        public bool CanDock { get { return State == SubState.Libre && redockCooldown <= 0f; } }

        /// <summary>Suelta el anclaje sin castigo cuando el objetivo ya se usó (caja abierta, jaula abierta).</summary>
        public void ReleaseAnchor(UnityEngine.Object owner)
        {
            if (State == SubState.Acoplado && AnchorOwner == owner)
            {
                Undock();
                redockCooldown = 0.5f;
            }
        }

        /// <summary>Vuelve a casa recorriendo su propio rastro al revés.</summary>
        public void StartAutopilotHome(Action onArrived)
        {
            if (State == SubState.Acoplado) Undock();
            List<Vector3> path = new List<Vector3>();
            for (int i = trail.Count - 1; i >= 0; i--) path.Add(trail[i]);
            if (path.Count == 0 && GameManager.Instance != null && GameManager.Instance.dockPoint != null)
            {
                path.Add(GameManager.Instance.dockPoint.position);
            }
            StartAutopilot(path, onArrived);
            goingHome = true;
        }

        /// <summary>Recorre una ruta sola (Seek en puntos intermedios + Arrive en el último).</summary>
        public void StartAutopilot(List<Vector3> path, Action onArrived)
        {
            if (State == SubState.Acoplado) Undock();
            autopilotPath.SetPath(path);
            autopilotArrived = onArrived;
            State = SubState.PilotoAutomatico;
            goingHome = false;
        }

        public bool IsOnAutopilot { get { return State == SubState.PilotoAutomatico; } }

        /// <summary>Lleva el submarino a un punto al instante (inicio de sesión en la balsa de un barrio).</summary>
        public void TeleportTo(Vector3 position, float yawDegrees)
        {
            if (rb == null) rb = GetComponent<Rigidbody>();
            rb.position = position;
            transform.position = position;
            rb.linearVelocity = Vector3.zero;
            yaw = yawDegrees;
            pitch = 0f;
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            rb.rotation = transform.rotation;
            ResetTrail(position);
        }

        public void ResetTrail(Vector3 origin)
        {
            trail.Clear();
            if (!FirstPerson) origin.z = 0f;
            trail.Add(origin);
            Vector3 p = transform.position;
            if (!FirstPerson) p.z = 0f;
            if ((p - origin).magnitude > 0.2f) trail.Add(p);
        }

        /// <summary>Punto del rastro a cierta distancia DETRÁS del submarino (para seguidores).</summary>
        public Vector3 GetPointBehind(float distance)
        {
            Vector3 current = transform.position;
            if (!FirstPerson) current.z = 0f;
            float remaining = distance;
            for (int i = trail.Count - 1; i >= 0; i--)
            {
                float segment = Vector3.Distance(current, trail[i]);
                if (segment >= remaining)
                {
                    return Vector3.Lerp(current, trail[i], remaining / Mathf.Max(segment, 0.0001f));
                }
                remaining -= segment;
                current = trail[i];
            }
            return current;
        }

        // ------------------------------------------------------------------ física

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            Vector3 pos = rb.position;
            Vector3 v = rb.linearVelocity;
            BimanualFrame frame = BimanualInput.Instance != null ? BimanualInput.Instance.Current : new BimanualFrame();
            Vector2 stick = InputEnabled && frame.panel.tracked ? frame.panel.joystick : Vector2.zero;
            float speedMul = GameManager.Instance != null ? GameManager.Instance.SpeedMultiplier : 1f;

            if (redockCooldown > 0f) redockCooldown -= dt;
            if (pressureCooldown > 0f) pressureCooldown -= dt;

            switch (State)
            {
                case SubState.Libre:
                {
                    Vector3 desired;
                    if (FirstPerson && RailPilot.Instance != null && RailPilot.Instance.IsDriving)
                    {
                        // Modo automático / ayuda del riel: Seek por los puntos del riel + Arrive al objetivo
                        v = RailPilot.Instance.Steer(this, pos, v, dt, ref yaw);
                        break;
                    }
                    if (FirstPerson)
                    {
                        HelmFrame h = InputEnabled && frame.helm.tracked ? frame.helm : new HelmFrame();
                        yaw += h.turn * turnRate * dt;
                        float throttle = h.throttle < 0f ? h.throttle * reverseFactor : h.throttle;
                        desired = (Forward * throttle + Vector3.up * h.vertical * verticalFactor) * maxSpeed * speedMul;
                    }
                    else
                    {
                        desired = new Vector3(stick.x, stick.y * verticalFactor, 0f) * maxSpeed * speedMul;
                    }
                    v = Vector3.MoveTowards(v, desired, acceleration * dt);
                    break;
                }
                case SubState.Acoplado:
                case SubState.Congelado:
                {
                    // ARRIVE hacia la pose de anclaje (bomba, objetivo del brazo o punto de calibración)
                    Vector3 steerHold = SteeringBehaviours.Arrive(pos, v, holdPosition, maxSpeed, 2.5f, 0.03f);
                    v += SteeringBehaviours.Truncate(steerHold * 4f, acceleration * 1.5f) * dt;

                    if (FirstPerson) yaw = Mathf.MoveTowardsAngle(yaw, anchorYaw, 110f * dt);

                    if (State == SubState.Acoplado)
                    {
                        bool wantsOut;
                        if (FirstPerson)
                        {
                            // Girar o avanzar/retroceder sostenido = soltar el anclaje.
                            // Subir sostenido es la palanca de la bomba, así que solo suelta si no es bomba.
                            HelmFrame h = InputEnabled && frame.helm.tracked ? frame.helm : new HelmFrame();
                            wantsOut = Mathf.Abs(h.turn) > 0.6f || Mathf.Abs(h.throttle) > 0.6f
                                       || (!AtLeverStation && Mathf.Abs(h.vertical) > 0.6f);
                            // En modo automático el riel decide cuándo soltar el anclaje
                            if (RailPilot.Instance != null && RailPilot.Instance.SuppressHelmUndock) wantsOut = false;
                        }
                        else
                        {
                            // Palanca horizontal sostenida = desacoplar (la vertical es la palanca de bomba)
                            wantsOut = Mathf.Abs(stick.x) > 0.6f;
                        }
                        if (wantsOut) undockTimer += dt; else undockTimer = 0f;
                        if (undockTimer > 0.45f) Undock();
                    }
                    break;
                }
                case SubState.PilotoAutomatico:
                {
                    // Seek en las migas intermedias + Arrive en el muelle (ver PathFollower)
                    Vector3 steer = autopilotPath.Steer(pos, v, autopilotSpeed);
                    steer = SteeringBehaviours.Truncate(steer * autopilotResponsiveness, autopilotMaxForce);
                    v = SteeringBehaviours.Truncate(v + steer * dt, autopilotSpeed);
                    if (FirstPerson)
                    {
                        Vector3 flat = new Vector3(v.x, 0f, v.z);
                        if (flat.magnitude > 0.4f)
                        {
                            float targetYaw = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
                            yaw = Mathf.MoveTowardsAngle(yaw, targetYaw, 140f * dt);
                        }
                    }
                    if (autopilotPath.Arrived(pos, v, 0.6f))
                    {
                        State = SubState.Congelado;
                        holdPosition = pos;
                        anchorYaw = yaw;
                        Action cb = autopilotArrived;
                        autopilotArrived = null;
                        if (cb != null) cb();
                    }
                    break;
                }
            }

            // ---- superficie del agua: el submarino no puede salir del agua
            float surfaceY = WaterZone.SurfaceAt(pos);
            float maxY = surfaceY - surfaceOffset;
            IsSurfaced = pos.y > maxY - 0.25f;
            if (pos.y > maxY + 0.05f)
            {
                v.y -= airGravity * dt;            // en el aire: cae al agua
                v.x *= 0.98f;
            }
            else if (pos.y > maxY - 0.2f && v.y > 0f)
            {
                v.y = Mathf.Min(v.y, (maxY - pos.y) * 5f); // flota en la superficie
            }

            // ---- límite de presión (mejora de Profundidad)
            if (GameManager.Instance != null && State != SubState.PilotoAutomatico)
            {
                float limit = GameManager.Instance.DepthLimitAt(pos);
                if (pos.y < limit)
                {
                    v.y = Mathf.Max(v.y, (limit - pos.y) * 3f + 1f);
                    if (pressureCooldown <= 0f)
                    {
                        pressureCooldown = 6f;
                        if (PressureWarning != null) PressureWarning();
                    }
                }
            }

            if (FirstPerson)
            {
                // Cabeceo: al bajar la proa mira hacia abajo (y al subir, hacia arriba) para ver a dónde se va.
                // Anclado o quieto, se nivela solo.
                float targetPitch = 0f;
                if (State == SubState.Libre || State == SubState.PilotoAutomatico)
                {
                    float verticalMax = Mathf.Max(0.5f, maxSpeed * verticalFactor);
                    targetPitch = Mathf.Clamp(-v.y / verticalMax, -1f, 1f) * maxDivePitch;
                    if (IsSurfaced && targetPitch < 0f) targetPitch = 0f; // en la superficie no apunta al cielo
                }
                pitch = Mathf.Lerp(pitch, targetPitch, 1f - Mathf.Exp(-pitchResponse * dt));
                rb.MoveRotation(Quaternion.Euler(pitch, yaw, 0f));
            }
            else
            {
                v.z = 0f;
            }
            rb.linearVelocity = v;

            UpdateZone(pos);
            UpdateTrail(pos);
        }

        private void UpdateZone(Vector3 pos)
        {
            WaterZone z = WaterZone.FindAt(pos);
            if (z != null && z != CurrentZone)
            {
                CurrentZone = z;
                if (ZoneChanged != null) ZoneChanged(z);
            }
        }

        private void UpdateTrail(Vector3 pos)
        {
            if (goingHome) return;
            if (!FirstPerson) pos.z = 0f;
            Vector3 last = trail.Count > 0 ? trail[trail.Count - 1] : pos;
            if (trail.Count > 0 && (pos - last).magnitude < breadcrumbSpacing) return;

            // Poda de bucles: si volvemos cerca de una miga vieja, cortamos el rodeo.
            for (int i = 0; i < trail.Count - 3; i++)
            {
                if ((trail[i] - pos).magnitude < breadcrumbSpacing * 0.9f)
                {
                    trail.RemoveRange(i + 1, trail.Count - (i + 1));
                    break;
                }
            }

            trail.Add(pos);
            if (trail.Count > maxBreadcrumbs) trail.RemoveAt(1); // conserva el origen (muelle)
        }

        // ------------------------------------------------------------------ visual

        private void Update()
        {
            float dt = Time.deltaTime;
            Vector3 v = Velocity;

            if (!FirstPerson && (State == SubState.Libre || State == SubState.PilotoAutomatico))
            {
                if (Mathf.Abs(v.x) > 0.3f) facingSign = Mathf.Sign(v.x);
            }

            if (visual != null && FirstPerson)
            {
                visual.localRotation = Quaternion.identity; // en primera persona el casco gira con la raíz
            }
            else if (visual != null)
            {
                float yaw = facingSign > 0f ? yawRight : -yawRight;
                float pitch = -Mathf.Clamp(v.y / Mathf.Max(0.1f, maxSpeed), -1f, 1f) * maxPitch;
                float bob = IsSurfaced ? Mathf.Sin(Time.time * 2.1f) * 3f : 0f;
                Quaternion target = Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(pitch + bob, 0f, 0f);
                visual.rotation = Quaternion.Slerp(visual.rotation, target, 1f - Mathf.Exp(-turnSpeed * dt));
            }

            if (propeller != null)
            {
                propellerAngle += (120f + v.magnitude * 260f) * dt;
                propeller.localRotation = Quaternion.Euler(0f, 0f, propellerAngle);
            }

            if (headlight != null)
            {
                // El foco apunta hacia delante y un poco hacia la pared del fondo, para que
                // su cono ilumine el escenario (nunca hacia la cámara, mire a donde mire).
                if (FirstPerson)
                {
                    headlight.transform.rotation = Quaternion.LookRotation((transform.forward - Vector3.up * 0.12f).normalized, Vector3.up);
                }
                else
                {
                    float fx = visual != null ? visual.forward.x : facingSign;
                    Vector3 aim = new Vector3(fx, -0.18f, 0.6f);
                    headlight.transform.rotation = Quaternion.LookRotation(aim.normalized, Vector3.up);
                }
                if (GameManager.Instance != null)
                {
                    headlight.range = GameManager.Instance.HeadlightRange * (HighBeam ? 1.6f : 1f);
                    headlight.intensity = GameManager.Instance.HeadlightIntensity * (HighBeam ? 1.5f : 1f);
                }
            }

            if (bubbles != null)
            {
                ParticleSystem.EmissionModule emission = bubbles.emission;
                emission.rateOverTime = IsSurfaced ? 0f : 2f + v.magnitude * 6f;
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.6f);
            for (int i = 1; i < trail.Count; i++) Gizmos.DrawLine(trail[i - 1], trail[i]);
            if (State == SubState.PilotoAutomatico) autopilotPath.DrawGizmos(Color.cyan);
        }
    }
}
