using System.Collections.Generic;
using UnityEngine;

namespace CatSquad.RescateProfundo
{
    public enum RailStationKind { Caja, Cerrojo, Bidon, Escombro, Bomba, Muelle, Punto, Esclusa }

    /// <summary>Un objetivo del riel: dónde trabajar y qué hay que hacer ahí.</summary>
    public class RailStation
    {
        public RailStationKind kind;
        public Component target;
        public Vector3 point;
        public Vector2Int approach;
        public float score;

        public Vector3 TargetPosition
        {
            get
            {
                PumpStation pump = target as PumpStation;
                if (pump != null) return pump.intake != null ? pump.intake.position : pump.transform.position;
                LockStation lk = target as LockStation;
                if (lk != null) return lk.DockPosition;
                return target != null ? target.transform.position : point;
            }
        }

        public string Label
        {
            get
            {
                switch (kind)
                {
                    case RailStationKind.Caja: return "la chatarra brillante";
                    case RailStationKind.Cerrojo: return "la jaula del gatito";
                    case RailStationKind.Bidon: return "el bidón de combustible";
                    case RailStationKind.Escombro: return "el escombro que tapa el bidón";
                    case RailStationKind.Bomba: return "la " + ((PumpStation)target).displayName.ToLower();
                    case RailStationKind.Muelle: return "la balsa de rescate";
                    case RailStationKind.Esclusa: return "la " + ((LockStation)target).displayName.ToLower();
                    default: return "el punto de encuentro";
                }
            }
        }

        public bool StillValid()
        {
            switch (kind)
            {
                case RailStationKind.Caja:
                case RailStationKind.Cerrojo:
                {
                    Grabbable g = target as Grabbable;
                    return g != null && g.gameObject.activeInHierarchy && g.CanBeGrabbed;
                }
                case RailStationKind.Bidon:
                {
                    FuelCanister f = target as FuelCanister;
                    return f != null && f.gameObject.activeInHierarchy && !f.IsHeld && f.fuel > 0.001f;
                }
                case RailStationKind.Escombro:
                {
                    Grabbable g = target as Grabbable;
                    return g != null && g.gameObject.activeInHierarchy && g.DistanceFromSpawn < 1.5f;
                }
                case RailStationKind.Bomba:
                {
                    PumpStation p = target as PumpStation;
                    return p != null && !p.Complete;
                }
                case RailStationKind.Muelle:
                    return RescuedKitten.Followers.Count > 0;
                case RailStationKind.Esclusa:
                {
                    LockStation lk = target as LockStation;
                    return lk != null && !lk.Balanced;
                }
                default:
                    return true;
            }
        }
    }

    /// <summary>
    /// Modo AUTOMÁTICO y AYUDA DEL RIEL (primera persona).
    ///  - Automático: el submarino avanza despacio por los rieles de objetivo en objetivo; el niño solo
    ///    usa el brazo (y la palanca en las bombas). Pensado para quien tiene menos movilidad del lado afectado.
    ///  - Manual con ayuda: si la palanca queda quieta N segundos, el riel toma el control hasta el
    ///    siguiente objetivo; basta mover la palanca para recuperarlo.
    /// Elige el objetivo más cercano POR EL RIEL (Dijkstra en RailNetwork) y lo recorre con un
    /// PathFollower: Seek en los puntos intermedios y Arrive en el último. Al llegar, orienta la proa
    /// y ancla el submarino en la pose de trabajo (70–90 % del alcance) igual que en modo manual.
    /// Los cascabeles secretos quedan fuera de los rieles: solo se encuentran manejando.
    /// </summary>
    public class RailPilot : MonoBehaviour
    {
        public static RailPilot Instance { get; private set; }

        private enum Phase { Ninguna, Viajando, Orientando, Trabajando, Esperando }

        public RailNetwork network;

        [Header("Conducción por el riel")]
        [Tooltip("Velocidad si no hay ajuste del terapeuta (m/s). La manual es ~4.2")]
        public float defaultSpeed = 1.6f;
        [Tooltip("La ayuda por inactividad va un poco más rápido que el modo automático")]
        public float assistSpeedFactor = 1.25f;
        public float maxForce = 5f;
        [Tooltip("Ganancia de respuesta del steering (1/s)")]
        public float responsiveness = 2.5f;
        public float turnRate = 90f;
        public float replanSeconds = 2.5f;
        [Tooltip("Carriles en profundidad (Z) que se prueban si el casco se atasca")]
        public float[] lanes = { 0f, 2.4f, -2.4f };

        [Header("Visual")]
        public LineRenderer railLine;
        [Tooltip("El riel se dibuja un poco por debajo de la cabina")]
        public float lineDrop = 1.1f;
        public Transform beacon;

        public bool Active { get; private set; }
        public bool AssistActive { get; private set; }
        public RailStation Current { get { return current; } }

        public bool AutoMode
        {
            get { return GameManager.Instance != null && GameManager.Instance.Settings.driveMode == DriveMode.Automatico; }
        }

        /// <summary>El submarino debe usar Steer() en vez de la palanca.</summary>
        public bool IsDriving
        {
            get { return Active && current != null && (phase == Phase.Viajando || phase == Phase.Orientando || phase == Phase.Esperando); }
        }

        /// <summary>En automático, mover la palanca no suelta el anclaje: lo decide el riel.</summary>
        public bool SuppressHelmUndock { get { return Active && AutoMode; } }

        public int AssistActivations { get; private set; }

        private RailStation current;
        private Phase phase;
        private readonly PathFollower follower = new PathFollower { waypointRadius = 0.6f, slowingRadius = 2.5f, stopRadius = 0.15f };
        private readonly List<Vector3> path = new List<Vector3>();
        private readonly Dictionary<Component, float> skipUntil = new Dictionary<Component, float>();
        private float replanTimer, idleTimer, orientTimer, stuckTimer, afterPumpTimer, noTargetToastTimer;
        private int laneIndex;
        private bool lockEntryWestOpen;
        private LockStation lockEntryLock;
        private Component focusTarget;
        private bool hasFocusPoint;
        private Vector3 focusPoint;
        private Vector3[] linePoints = new Vector3[64];

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ------------------------------------------------------------------ API

        public void ToggleDriveMode()
        {
            GameManager gm = GameManager.Instance;
            if (gm == null) return;
            gm.Settings.driveMode = gm.Settings.driveMode == DriveMode.Manual ? DriveMode.Automatico : DriveMode.Manual;
            gm.Save();
            Toast(gm.Settings.driveMode == DriveMode.Automatico
                ? "Modo AUTOMÁTICO: el riel te lleva de objetivo en objetivo"
                : "Modo MANUAL: ¡tú manejas la palanca!");
            ResetStation();
        }

        /// <summary>El tutorial fija el objetivo de cada paso.</summary>
        public void SetFocus(Component target)
        {
            focusTarget = target;
            hasFocusPoint = false;
            ResetStation();
        }

        public void SetFocusPoint(Vector3 p)
        {
            focusTarget = null;
            hasFocusPoint = true;
            focusPoint = p;
            ResetStation();
        }

        public void ClearFocus()
        {
            focusTarget = null;
            hasFocusPoint = false;
            ResetStation();
        }

        /// <summary>Con el riel activo, las bombas solo acoplan si hay algo que hacer en ellas.</summary>
        public bool AllowsDock(PumpStation pump)
        {
            if (!Active) return true;
            ManipulatorArm arm = GameManager.Instance != null ? GameManager.Instance.arm : null;
            if (arm != null && arm.Held is FuelCanister) return true;
            if (pump.Tank > 0.01f) return true;
            return current != null && current.target == pump;
        }

        public bool TryGetTarget(out Vector3 position)
        {
            position = Vector3.zero;
            if (!Active || current == null) return false;
            position = current.TargetPosition;
            return true;
        }

        public string PromptText
        {
            get
            {
                if (!Active) return "";
                string head = AutoMode ? "Modo automático" : "Ayuda del riel";
                string tail = AutoMode ? "" : " · mueve la palanca para tomar el control";
                if (current == null) return AutoMode ? head + ": no hay objetivos al alcance del riel" : "";
                switch (phase)
                {
                    case Phase.Viajando: return head + ": vamos a " + current.Label + tail;
                    case Phase.Orientando: return head + ": llegando a " + current.Label + tail;
                    case Phase.Esperando: return current.kind == RailStationKind.Muelle ? "¡En la balsa! Los gatitos bajan a salvo" : "";
                    default: return "";
                }
            }
        }

        // ------------------------------------------------------------------ bucle

        private void Update()
        {
            float dt = Time.deltaTime;
            GameManager gm = GameManager.Instance;
            SubmarineController sub = gm != null ? gm.submarine : null;
            bool modal = GameHUD.Instance != null && GameHUD.Instance.ModalOpen;
            if (noTargetToastTimer > 0f) noTargetToastTimer -= dt;

            if (GameManager.FirstPerson && !modal && Input.GetKeyDown(KeyCode.R)) ToggleDriveMode();

            bool canRun = GameManager.FirstPerson && sub != null && sub.InputEnabled && !sub.IsOnAutopilot && !modal && PhaseAllows(gm);

            // ---- ayuda por inactividad (solo en manual)
            BimanualFrame frame = BimanualInput.Instance != null ? BimanualInput.Instance.Current : new BimanualFrame();
            HelmFrame h = frame.helm;
            bool helmActive = h.tracked && (Mathf.Abs(h.turn) > 0.3f || Mathf.Abs(h.throttle) > 0.3f || Mathf.Abs(h.vertical) > 0.3f);
            if (AutoMode || !canRun)
            {
                AssistActive = false;
                idleTimer = 0f;
            }
            else if (helmActive)
            {
                if (AssistActive && (sub.State == SubState.Libre)) Toast("¡Tomaste el control!");
                AssistActive = false;
                idleTimer = 0f;
            }
            else if (sub.State == SubState.Libre)
            {
                float wait = gm.Settings.autoAssistAfterSeconds;
                if (wait > 0f)
                {
                    idleTimer += dt;
                    if (!AssistActive && idleTimer >= wait)
                    {
                        AssistActive = true;
                        AssistActivations++;
                        ResetStation();
                        Toast("El riel te ayuda a llegar al siguiente objetivo");
                    }
                }
            }

            bool wasActive = Active;
            Active = canRun && (AutoMode || AssistActive);
            if (!Active)
            {
                if (wasActive) ResetStation();
                ShowVisuals(false, false);
                return;
            }

            switch (sub.State)
            {
                case SubState.Acoplado:
                    HandleAnchored(sub, gm.arm, dt);
                    ShowVisuals(false, false);
                    return;
                case SubState.Congelado:
                case SubState.PilotoAutomatico:
                    ShowVisuals(false, false);
                    return;
            }

            // ---- elegir / mantener el objetivo
            if (phase == Phase.Trabajando) ResetStation(); // volvimos a quedar libres: siguiente objetivo
            replanTimer -= dt;
            bool reselect = current == null
                ? replanTimer <= 0f                       // sin objetivo: volver a buscar cada tanto
                : (!current.StillValid() || StationChangedByHands(gm.arm));
            if (reselect)
            {
                SelectStation(sub, gm);
                if (current == null) replanTimer = 1f;
            }
            else if (replanTimer <= 0f && phase == Phase.Viajando)
            {
                RebuildPath(sub);
            }

            if (current == null)
            {
                if (AutoMode && noTargetToastTimer <= 0f && (gm.arm == null || gm.arm.Held == null))
                {
                    noTargetToastTimer = 25f;
                    Toast("No quedan objetivos al alcance del riel: ¡puedes manejar tú o mejorar el submarino!");
                }
                ShowVisuals(false, false);
                return;
            }

            Vector3 pos = sub.transform.position;
            Vector3 v = sub.Velocity;

            if (phase == Phase.Viajando)
            {
                if (follower.Arrived(pos, v, 0.5f))
                {
                    phase = IsDestinationOnly(current) ? Phase.Esperando : Phase.Orientando;
                    if (current.kind == RailStationKind.Esclusa) phase = Phase.Orientando;
                    orientTimer = 0f;
                    stuckTimer = 0f;
                }
                else
                {
                    // ¿atascado contra algo? probar otro carril en profundidad
                    stuckTimer = v.magnitude < 0.25f ? stuckTimer + dt : 0f;
                    if (stuckTimer > 2.5f)
                    {
                        stuckTimer = 0f;
                        laneIndex = (laneIndex + 1) % Mathf.Max(1, lanes.Length);
                        RebuildPath(sub);
                    }
                }
            }
            else if (phase == Phase.Orientando)
            {
                orientTimer += dt;
                Vector3 to = current.kind == RailStationKind.Esclusa ? Vector3.forward : current.TargetPosition - pos;
                to.y = 0f;
                bool facing = to.sqrMagnitude < 0.25f || Vector3.Angle(sub.Forward, to) < 15f;
                if (sub.CanDock && ((facing && v.magnitude < 0.6f) || orientTimer > 3f)) Engage(sub, gm.arm);
            }

            ShowVisuals(phase == Phase.Viajando || phase == Phase.Orientando, phase != Phase.Esperando);
        }

        private static bool PhaseAllows(GameManager gm)
        {
            if (gm == null || gm.session == null) return true;
            if (gm.session.Phase == SessionPhase.Jugando) return true;
            return gm.session.Phase == SessionPhase.Inicio && gm.session.tutorial != null && gm.session.tutorial.Running;
        }

        private static bool IsDestinationOnly(RailStation s)
        {
            return s.kind == RailStationKind.Muelle || s.kind == RailStationKind.Punto;
        }

        /// <summary>Se llega a un punto (no a una celda de trabajo junto al objetivo).</summary>
        private static bool ArrivesAtPoint(RailStation s)
        {
            return IsDestinationOnly(s) || s.kind == RailStationKind.Esclusa;
        }

        /// <summary>Si el niño agarra un bidón o suelta lo que llevaba, cambia lo que conviene hacer.</summary>
        private bool StationChangedByHands(ManipulatorArm arm)
        {
            if (arm == null || current == null) return false;
            bool holdingFuel = arm.Held is FuelCanister;
            if (holdingFuel && current.kind != RailStationKind.Bomba) return true;
            if (!holdingFuel && arm.Held != null) return true; // lleva otra cosa: esperar a que la suelte
            if (current.kind == RailStationKind.Bomba && !holdingFuel && ((PumpStation)current.target).Tank <= 0.01f) return true; // soltó el bidón
            return false;
        }

        // ------------------------------------------------------------------ steering (lo llama el submarino)

        public Vector3 Steer(SubmarineController sub, Vector3 pos, Vector3 v, float dt, ref float yaw)
        {
            GameManager gm = GameManager.Instance;
            float speedSetting = gm != null && gm.Settings.railSpeed > 0.1f ? gm.Settings.railSpeed : defaultSpeed;
            float speed = speedSetting * (AutoMode ? 1f : assistSpeedFactor) * (gm != null ? gm.SpeedMultiplier : 1f);

            // ---- orientación de la proa
            float targetYaw = yaw;
            if (phase == Phase.Orientando && current != null && current.kind == RailStationKind.Esclusa)
            {
                targetYaw = 0f; // mirando a la boya de mando
            }
            else if (phase == Phase.Orientando && current != null)
            {
                Vector3 to = current.TargetPosition - pos;
                to.y = 0f;
                if (to.sqrMagnitude > 0.04f) targetYaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            }
            else if (phase == Phase.Viajando)
            {
                Vector3 seg = follower.CurrentTarget - pos;
                float horizontal = new Vector2(seg.x, seg.z).magnitude;
                if (Mathf.Abs(seg.y) > 1.5f * horizontal)
                {
                    // Tramo vertical (pozos): proa a lo largo del pasillo (±Z) para que el casco largo quepa
                    targetYaw = Mathf.Abs(Mathf.DeltaAngle(yaw, 0f)) <= 90f ? 0f : 180f;
                }
                else if (horizontal > 0.3f)
                {
                    targetYaw = Mathf.Atan2(seg.x, seg.z) * Mathf.Rad2Deg;
                }
            }
            yaw = Mathf.MoveTowardsAngle(yaw, targetYaw, turnRate * dt);

            // Mientras la proa no apunta a donde va, avanza más lento (no barre las paredes al girar)
            if (phase == Phase.Viajando)
            {
                float error = Mathf.Abs(Mathf.DeltaAngle(yaw, targetYaw));
                speed *= Mathf.Clamp(1f - error / 90f, 0.3f, 1f);
            }

            // ---- steering: Seek en los puntos del riel, Arrive en el último (y al orientarse/esperar)
            Vector3 steer = phase == Phase.Viajando
                ? follower.Steer(pos, v, speed)
                : SteeringBehaviours.Arrive(pos, v, follower.FinalTarget, speed, 2.5f, 0.05f);
            steer = SteeringBehaviours.Truncate(steer * responsiveness, maxForce);
            v = SteeringBehaviours.Truncate(v + steer * dt, speed);
            return v;
        }

        // ------------------------------------------------------------------ llegar y anclar

        private void Engage(SubmarineController sub, ManipulatorArm arm)
        {
            bool ok = false;
            switch (current.kind)
            {
                case RailStationKind.Caja:
                case RailStationKind.Cerrojo:
                {
                    ReachAnchor ra = current.target != null ? current.target.GetComponent<ReachAnchor>() : null;
                    ok = ra != null && ra.TryAnchor(sub);
                    break;
                }
                case RailStationKind.Bidon:
                case RailStationKind.Escombro:
                {
                    Vector3 pos;
                    float yaw;
                    Vector3 target = current.TargetPosition;
                    if (WorkPose.FindPose(sub, arm, target, PreferredDirection(sub, target), 0.8f, 2.0f, out pos, out yaw))
                    {
                        sub.AnchorAt(pos, yaw, current.target);
                        ok = sub.AnchorOwner == (Object)current.target;
                    }
                    break;
                }
                case RailStationKind.Bomba:
                {
                    PumpStation pump = (PumpStation)current.target;
                    sub.DockTo(pump);
                    ok = sub.DockedPump == pump;
                    break;
                }
                case RailStationKind.Esclusa:
                {
                    LockStation lk = (LockStation)current.target;
                    lockEntryWestOpen = lk.WestOpen;
                    lockEntryLock = lk;
                    ok = lk.TryDock(sub);
                    break;
                }
            }

            if (ok)
            {
                phase = Phase.Trabajando;
            }
            else
            {
                // no hubo pose libre: saltarlo un rato y seguir con otro
                if (current.target != null) skipUntil[current.target] = Time.time + 20f;
                ResetStation();
            }
        }

        /// <summary>Dirección de WorkPose más parecida a donde está el objetivo respecto de la proa.</summary>
        private static int PreferredDirection(SubmarineController sub, Vector3 target)
        {
            Vector3 d = target - sub.transform.position;
            Vector2 local = new Vector2(Vector3.Dot(d, sub.Right), d.y);
            if (local.sqrMagnitude < 0.01f) return 7;
            local.Normalize();
            int best = 0;
            float bestDot = -2f;
            for (int i = 0; i < WorkPose.Directions.Length; i++)
            {
                float dot = Vector2.Dot(local, WorkPose.Directions[i].normalized);
                if (dot > bestDot) { bestDot = dot; best = i; }
            }
            return best;
        }

        /// <summary>Anclados: el riel decide cuándo se terminó la tarea (bidón, escombro, bomba).</summary>
        private void HandleAnchored(SubmarineController sub, ManipulatorArm arm, float dt)
        {
            Object owner = sub.AnchorOwner;
            FuelCanister fuel = owner as FuelCanister;
            PumpStation pump = owner as PumpStation;
            Grabbable grab = owner as Grabbable;
            LockStation lk = owner as LockStation;

            if (lk != null)
            {
                if (lockEntryLock != lk) { lockEntryLock = lk; lockEntryWestOpen = lk.WestOpen; }
                // listo cuando se abre la compuerta que estaba cerrada al entrar
                bool done = lockEntryWestOpen ? lk.EastOpen : lk.WestOpen;
                if (current == null || current.target != lk) done = lk.Balanced || done;
                if (done)
                {
                    afterPumpTimer += dt;
                    if (afterPumpTimer > 1.0f)
                    {
                        afterPumpTimer = 0f;
                        sub.Undock();
                        ResetStation();
                    }
                }
                return;
            }

            if (fuel != null)
            {
                if (fuel.IsHeld || !fuel.gameObject.activeInHierarchy)
                {
                    sub.ReleaseAnchor(fuel);
                    ResetStation();
                }
            }
            else if (grab != null && !(grab is MaterialCrate) && !(grab is CageLatch))
            {
                if (!grab.IsHeld && grab.DistanceFromSpawn > 1.5f)
                {
                    sub.ReleaseAnchor(grab);
                    ResetStation();
                }
            }
            else if (pump != null)
            {
                bool holdingFuel = arm != null && arm.Held is FuelCanister;
                if (!holdingFuel && pump.Tank <= 0.01f && !pump.IsPumping)
                {
                    afterPumpTimer += dt;
                    if (afterPumpTimer > 1.2f)
                    {
                        afterPumpTimer = 0f;
                        sub.Undock();
                        ResetStation();
                    }
                }
                else afterPumpTimer = 0f;
            }
        }

        // ------------------------------------------------------------------ elección del objetivo

        private void ResetStation()
        {
            current = null;
            phase = Phase.Ninguna;
            follower.Clear();
            laneIndex = 0;
            stuckTimer = 0f;
            replanTimer = 0f;
            lockEntryLock = null;
        }

        private void SelectStation(SubmarineController sub, GameManager gm)
        {
            ResetStation();
            if (network == null || !network.ComputeFrom(sub.transform.position)) return;

            ManipulatorArm arm = gm.arm;
            Grabbable held = arm != null ? arm.Held : null;
            RailStation best = null;

            // 1) el tutorial manda
            RailStation focus = FocusStation();
            if (focus != null && Evaluate(focus, 0f, ref best))
            {
                Commit(sub, best);
                return;
            }

            if (held is FuelCanister)
            {
                // 2) con un bidón en la pinza: a la bomba que lo necesita
                foreach (PumpStation p in PumpStation.All)
                {
                    if (p != null && !p.Complete) Evaluate(new RailStation { kind = RailStationKind.Bomba, target = p }, 0f, ref best);
                }
            }
            else if (held == null)
            {
                bool pumpPending = false;
                foreach (PumpStation p in PumpStation.All)
                {
                    if (p == null || p.Complete) continue;
                    pumpPending = true;
                    if (p.Tank > 0.01f) Evaluate(new RailStation { kind = RailStationKind.Bomba, target = p }, -15f, ref best);
                }

                bool cageAvailable = false;
                foreach (ReachAnchor ra in ReachAnchor.All)
                {
                    if (ra == null || ra.target == null || !ra.target.gameObject.activeInHierarchy || !ra.target.CanBeGrabbed) continue;
                    bool latch = ra.target is CageLatch;
                    RailStation s = new RailStation { kind = latch ? RailStationKind.Cerrojo : RailStationKind.Caja, target = ra.target };
                    if (Evaluate(s, 0f, ref best) && latch) cageAvailable = true;
                }

                if (pumpPending)
                {
                    foreach (Grabbable g in Grabbable.All)
                    {
                        FuelCanister f = g as FuelCanister;
                        if (f == null || !f.gameObject.activeInHierarchy || f.IsHeld || f.fuel <= 0.001f) continue;
                        Grabbable blocker = BlockingDebris(f);
                        if (blocker != null) Evaluate(new RailStation { kind = RailStationKind.Escombro, target = blocker }, 0f, ref best);
                        else Evaluate(new RailStation { kind = RailStationKind.Bidon, target = f }, 0f, ref best);
                    }
                }

                if (RescuedKitten.Followers.Count > 0)
                {
                    // los gatitos quieren ir a la balsa del barrio, pero primero liberar a los demás si hay jaulas cerca
                    Dock raft = Dock.FindForZone(WaterZone.FindAt(sub.transform.position), sub.transform.position);
                    Vector3 raftPoint = raft != null ? raft.SubPoint : (gm.dockPoint != null ? gm.dockPoint.position : sub.transform.position);
                    Evaluate(new RailStation { kind = RailStationKind.Muelle, point = raftPoint }, cageAvailable ? 40f : -10f, ref best);
                }
            }

            // Sin nada al alcance: usar una esclusa hacia el nivel donde sí hay trabajo
            if (best == null) EvaluateLocks(sub, held, ref best);
            // si lleva otra cosa (un escombro), se espera a que la suelte

            if (best != null) Commit(sub, best);
        }

        /// <summary>
        /// Esclusas con una compuerta cerrada cuyo lado cerrado (o más allá) tiene objetivos.
        /// El riel lleva al submarino a la boya; el niño sostiene la palanca para igualar el agua.
        /// </summary>
        private void EvaluateLocks(SubmarineController sub, Grabbable held, ref RailStation best)
        {
            foreach (LockStation lk in LockStation.All)
            {
                if (lk == null || lk.west == null || lk.east == null) continue;
                bool w = lk.WestOpen, e = lk.EastOpen;
                if (w == e) continue; // equilibrada (nada que hacer) o ambas cerradas
                WaterZone far = w ? lk.east : lk.west;
                bool farIsEast = far == lk.east;
                if (HasWorkBeyond(far.order, farIsEast, held)) Evaluate(new RailStation { kind = RailStationKind.Esclusa, target = lk }, 25f, ref best);
            }
        }

        private static bool Beyond(Vector3 p, float farOrder, bool east)
        {
            WaterZone z = WaterZone.FindAt(p);
            if (z == null) return false;
            return east ? z.order >= farOrder - 0.01f : z.order <= farOrder + 0.01f;
        }

        private static bool HasWorkBeyond(float farOrder, bool east, Grabbable held)
        {
            if (held is FuelCanister)
            {
                foreach (PumpStation p in PumpStation.All)
                    if (p != null && !p.Complete && Beyond(p.transform.position, farOrder, east)) return true;
                return false;
            }
            foreach (ReachAnchor ra in ReachAnchor.All)
            {
                if (ra == null || ra.target == null || !ra.target.gameObject.activeInHierarchy || !ra.target.CanBeGrabbed) continue;
                if (Beyond(ra.target.transform.position, farOrder, east)) return true;
            }
            bool pumpPending = false;
            foreach (PumpStation p in PumpStation.All) if (p != null && !p.Complete) pumpPending = true;
            if (pumpPending)
            {
                foreach (Grabbable g in Grabbable.All)
                {
                    FuelCanister f = g as FuelCanister;
                    if (f != null && f.gameObject.activeInHierarchy && !f.IsHeld && f.fuel > 0.001f && Beyond(f.transform.position, farOrder, east)) return true;
                }
            }
            return false;
        }

        /// <summary>Con el riel activo, las esclusas solo acoplan cuando el riel va a ellas.</summary>
        public bool AllowsLock(LockStation lk)
        {
            if (!Active) return true;
            return current != null && current.target == lk;
        }

        private RailStation FocusStation()
        {
            if (hasFocusPoint) return new RailStation { kind = RailStationKind.Punto, point = focusPoint };
            if (focusTarget == null) return null;

            RailStation s = null;
            if (focusTarget is PumpStation)
            {
                // a la bomba solo si hay algo que hacer en ella (bidón en la pinza o combustible en el tanque)
                ManipulatorArm arm = GameManager.Instance != null ? GameManager.Instance.arm : null;
                bool useful = (arm != null && arm.Held is FuelCanister) || ((PumpStation)focusTarget).Tank > 0.01f;
                if (useful) s = new RailStation { kind = RailStationKind.Bomba, target = focusTarget };
            }
            else if (focusTarget is CageLatch) s = new RailStation { kind = RailStationKind.Cerrojo, target = focusTarget };
            else if (focusTarget is MaterialCrate) s = new RailStation { kind = RailStationKind.Caja, target = focusTarget };
            else if (focusTarget is FuelCanister) s = new RailStation { kind = RailStationKind.Bidon, target = focusTarget };
            else if (focusTarget is Grabbable) s = new RailStation { kind = RailStationKind.Escombro, target = focusTarget };
            else if (focusTarget is LockStation) s = new RailStation { kind = RailStationKind.Esclusa, target = focusTarget };
            return s != null && s.StillValid() ? s : null;
        }

        /// <summary>Escombro sin mover que tapa un bidón (hay que apartarlo primero).</summary>
        private static Grabbable BlockingDebris(FuelCanister f)
        {
            foreach (Grabbable g in Grabbable.All)
            {
                if (g == null || g.kind != GrabKind.Escombro || !g.gameObject.activeInHierarchy || g.IsHeld) continue;
                if (g.DistanceFromSpawn > 1.5f) continue;
                Vector3 d = g.transform.position - f.transform.position;
                d.z = 0f;
                if (d.magnitude < 2.6f) return g;
            }
            return null;
        }

        private bool Evaluate(RailStation s, float bonus, ref RailStation best)
        {
            float until;
            if (s.target != null && skipUntil.TryGetValue(s.target, out until) && Time.time < until) return false;

            Vector2Int cell;
            float score;
            if (s.kind == RailStationKind.Esclusa) s.point = ((LockStation)s.target).DockPosition;
            bool ok = ArrivesAtPoint(s)
                ? network.FindArrival(s.point, out cell, out score)
                : network.FindApproach(s.TargetPosition, 1.5f, 5f, 3f, out cell, out score);
            if (!ok) return false;
            s.approach = cell;
            s.score = score + bonus;
            if (best == null || s.score < best.score) best = s;
            return true;
        }

        private void Commit(SubmarineController sub, RailStation s)
        {
            current = s;
            phase = Phase.Viajando;
            RebuildPath(sub);
            if (current == null) return;
        }

        private void RebuildPath(SubmarineController sub)
        {
            replanTimer = replanSeconds;
            if (current == null || network == null) return;
            Vector3 pos = sub.transform.position;
            if (!network.ComputeFrom(pos)) { ResetStation(); return; }
            float lane = lanes != null && lanes.Length > 0 ? lanes[laneIndex % lanes.Length] : 0f;
            if (!network.BuildPath(pos, current.approach, lane, path))
            {
                ResetStation();
                return;
            }
            if (current.kind == RailStationKind.Esclusa) current.point = ((LockStation)current.target).DockPosition;
            if (ArrivesAtPoint(current)) path.Add(current.point);
            // Fuera de la avenida (sobre las azoteas): primero volver de lado a la avenida, a la misma altura
            if (Mathf.Abs(pos.z - lane) > 4f) path.Insert(0, new Vector3(pos.x, pos.y, lane));
            follower.SetPath(path);
        }

        // ------------------------------------------------------------------ visual

        private void ShowVisuals(bool showLine, bool showBeacon)
        {
            if (railLine != null)
            {
                bool show = showLine && follower.HasPath;
                railLine.enabled = show;
                if (show)
                {
                    SubmarineController sub = GameManager.Instance.submarine;
                    int count = 0;
                    Vector3 drop = Vector3.down * lineDrop;
                    linePoints[count++] = sub.transform.position + sub.Forward * 1.5f + drop;
                    for (int i = follower.CurrentIndex; i < follower.Count && count < linePoints.Length; i++)
                    {
                        linePoints[count++] = follower.Points[i] + drop;
                    }
                    railLine.positionCount = count;
                    for (int i = 0; i < count; i++) railLine.SetPosition(i, linePoints[i]);
                }
            }

            if (beacon != null)
            {
                bool show = showBeacon && current != null && !IsDestinationOnly(current);
                if (beacon.gameObject.activeSelf != show) beacon.gameObject.SetActive(show);
                if (show)
                {
                    float bob = Mathf.Sin(Time.time * 3f) * 0.12f;
                    beacon.position = current.TargetPosition + Vector3.up * (1.1f + bob);
                    beacon.localScale = Vector3.one * (0.3f + 0.05f * Mathf.Sin(Time.time * 6f));
                }
            }
        }

        private static void Toast(string text)
        {
            if (GameHUD.Instance != null) GameHUD.Instance.Toast(text, 3.5f);
        }

        private void OnDrawGizmosSelected()
        {
            if (follower.HasPath) follower.DrawGizmos(new Color(0.3f, 1f, 0.9f));
        }
    }
}
