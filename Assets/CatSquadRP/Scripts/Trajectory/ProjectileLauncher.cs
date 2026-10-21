using System.Collections.Generic;
using UnityEngine;

namespace CatSquad.RescateProfundo
{
    /// <summary>Ayuda de puntería que fija el terapeuta.</summary>
    public enum LaunchAssist
    {
        Ninguna = 0,     // solo los puntos de la trayectoria
        Guia = 1,        // además, el arco ideal (solución de disparo) como línea fantasma
        Iman = 2,        // si el tiro se parece al ideal, se corrige solo al soltar
        Automatica = 3   // la puntería la calcula el juego: el niño solo agarra, tira y suelta
    }

    /// <summary>
    /// LANZADOR NEUMÁTICO de la escotilla (primera persona). LAB 05: predicción de trayectoria.
    ///
    ///  · En la SUPERFICIE lanza latas de atún a las cestas de los gatitos con hambre de las azoteas (aire: parábola).
    ///  · BAJO EL AGUA elige solo el proyectil según el blanco: LASTRE (cae) para una placa del fondo o BOYA
    ///    (sube) para un gancho; la gravedad efectiva sale de la flotabilidad y el agua frena (arrastre).
    ///
    /// Control como la resortera del lab (arrastrar y soltar), pero bimanual:
    ///  · la mano del PANEL se apoya (estabiliza el lanzador);
    ///  · la mano del BRAZO se CIERRA para agarrar la manija, TIRA en sentido contrario al tiro
    ///    (abajo = más alto, a un lado = hacia el otro) y se ABRE para soltar. Más tirón = más fuerza.
    /// Mientras tira se ven los puntos de la trayectoria predicha y el anillo donde caerá.
    /// </summary>
    public class ProjectileLauncher : MonoBehaviour
    {
        public static ProjectileLauncher Instance { get; private set; }

        public SubmarineController submarine;
        public ManipulatorArm arm;
        [Tooltip("Boca del lanzador (sobre la escotilla)")]
        public Transform muzzle;
        public TrajectoryDots dots;
        [Tooltip("Plantillas desactivadas con Projectile (se clonan al disparar)")]
        public Projectile lataTemplate;
        public Projectile lastreTemplate;
        public Projectile boyaTemplate;
        [Tooltip("Cañón de la torreta: apunta hacia donde se va a disparar")]
        public Transform barrel;

        [Header("Alcance")]
        public float surfaceRange = 40f;
        public float underwaterRange = 22f;

        [Header("Rapidez de salida (m/s)")]
        public float surfaceMinSpeed = 6f;
        public float surfaceMaxSpeed = 30f;
        public float underwaterMinSpeed = 3f;
        public float underwaterMaxSpeed = 14f;

        [Header("Resortera")]
        [Tooltip("Grados de giro por unidad de tirón lateral")]
        public float yawGain = 110f;
        public float maxYaw = 75f;
        [Tooltip("Grados de elevación por unidad de tirón vertical")]
        public float pitchGain = 110f;
        public float surfaceBaseElevation = 20f;
        public float underwaterBaseElevation = 0f;
        [Tooltip("Tirón mínimo (fracción de la fuerza) para que cuente como disparo al soltar")]
        public float minPowerToFire = 0.12f;
        public float cooldown = 0.6f;

        [Header("Predicción")]
        public float predictionTime = 5f;
        public int recordEvery = 2;
        public LayerMask predictionMask = ~0;
        [Tooltip("Asistencia Imán: diferencia máxima de ángulo (°) y de rapidez (fracción) para corregir el tiro")]
        public float magnetAngle = 14f;
        public float magnetSpeed = 0.35f;

        public bool IsOpen { get; private set; }
        public bool Aiming { get; private set; }
        public float Power { get; private set; }
        public LaunchTarget Target { get; private set; }
        public ProjectileKind Ammo { get; private set; }
        public int Launches { get; private set; }
        public int Hits { get; private set; }

        private readonly List<Vector3> points = new List<Vector3>();
        private readonly List<Vector3> ghostPoints = new List<Vector3>();
        private Vector2 anchor;
        private bool lastGrip;
        private float cooldownTimer;
        private float aimStart;
        private Vector3 currentVelocity;
        private TrajectoryMath.Prediction currentPrediction;
        private bool hasSolution;
        private Vector3 solution;
        private LaunchTarget solutionTarget;
        private Vector3 solutionFrom;
        private bool introSaid;

        private void Awake() { Instance = this; }
        private void OnDestroy() { if (Instance == this) Instance = null; }

        private GameSettings Settings { get { return GameManager.Instance != null ? GameManager.Instance.Settings : null; } }
        private bool Surface { get { return submarine != null && submarine.IsSurfaced; } }
        private float Range { get { return Surface ? surfaceRange : underwaterRange; } }
        private Vector3 MuzzlePos { get { return muzzle != null ? muzzle.position : (submarine != null ? submarine.transform.position + Vector3.up : transform.position); } }

        private Projectile TemplateFor(ProjectileKind k)
        {
            switch (k)
            {
                case ProjectileKind.Lastre: return lastreTemplate;
                case ProjectileKind.Boya: return boyaTemplate;
                default: return lataTemplate;
            }
        }

        // ------------------------------------------------------------------ abrir / cerrar

        public bool TargetsInRange
        {
            get
            {
                if (submarine == null) return false;
                return LaunchTarget.NearestReady(MuzzlePos, Range, Surface) != null;
            }
        }

        public bool CanOpen
        {
            get
            {
                if (!GameManager.FirstPerson || submarine == null || IsOpen) return false;
                if (submarine.State != SubState.Libre || submarine.IsOnAutopilot) return false;
                if (arm != null && arm.Held != null) return false;
                GameManager gm = GameManager.Instance;
                if (gm != null && gm.session != null && gm.session.Phase != SessionPhase.Jugando) return false;
                return TargetsInRange;
            }
        }

        public string StatusLabel
        {
            get
            {
                if (IsOpen) return "CERRAR";
                if (CanOpen) return Surface ? "¡GATITOS!" : "LISTO";
                return "LEJOS";
            }
        }

        public string Toggle()
        {
            if (IsOpen) { Close(); return "LANZADOR GUARDADO"; }
            if (!CanOpen)
            {
                if (arm != null && arm.Held != null) return "SUELTA LO QUE LLEVAS";
                return Surface ? "NO HAY CESTAS CERCA" : "NO HAY BLANCOS CERCA";
            }
            Open();
            return "¡LANZADOR LISTO!";
        }

        public void Open()
        {
            IsOpen = true;
            Aiming = false;
            lastGrip = true;  // hay que abrir la mano antes del primer tiro
            if (arm != null) arm.Suspended = true;
            if (submarine != null) submarine.Freeze();
            SelectTarget(null);
            GameManager gm = GameManager.Instance;
            if (!introSaid && gm != null && gm.guide != null)
            {
                introSaid = true;
                bool webcam = BimanualInput.Instance != null && BimanualInput.Instance.UsingWebcam;
                gm.guide.Say(Surface
                    ? (webcam ? "¡Hay gatitos con hambre en las azoteas! Apoya la otra mano, cierra la del brazo, tira hacia atrás y ábrela para lanzar la lata."
                              : "¡Hay gatitos con hambre en las azoteas! Haz clic, arrastra hacia atrás y suelta para lanzar la lata. Los puntos te muestran por dónde irá.")
                    : "Bajo el agua el lanzador usa lastres que caen y boyas que suben. ¡Mira cómo se curva la trayectoria!");
            }
        }

        public void Close()
        {
            IsOpen = false;
            Aiming = false;
            if (arm != null) arm.Suspended = false;
            if (submarine != null && submarine.State == SubState.Congelado) submarine.Unfreeze();
            if (dots != null) dots.Hide();
        }

        // ------------------------------------------------------------------ bucle

        private void Update()
        {
            float dt = Time.deltaTime;
            if (cooldownTimer > 0f) cooldownTimer -= dt;

            if (Input.GetKeyDown(KeyCode.G) && (GameHUD.Instance == null || !GameHUD.Instance.ModalOpen))
            {
                string msg = Toggle();
                if (GameHUD.Instance != null) GameHUD.Instance.Toast(msg, 1.6f);
            }

            // la torreta se levanta al abrir y sigue la puntería
            if (barrel != null && submarine != null)
            {
                Vector3 want = IsOpen && Aiming && currentVelocity.sqrMagnitude > 0.01f
                    ? currentVelocity.normalized
                    : Quaternion.AngleAxis(IsOpen ? -25f : 0f, submarine.Right) * submarine.Forward;
                Quaternion target = Quaternion.LookRotation(want, Vector3.up);
                barrel.rotation = Quaternion.Slerp(barrel.rotation, target, 1f - Mathf.Exp(-12f * dt));
            }

            if (!IsOpen) return;
            GameManager gm = GameManager.Instance;
            if (submarine == null || (gm != null && gm.session != null && gm.session.Phase != SessionPhase.Jugando)
                || submarine.IsOnAutopilot || (submarine.State != SubState.Congelado && submarine.State != SubState.Libre))
            {
                Close();
                return;
            }

            BimanualFrame f = BimanualInput.Instance != null ? BimanualInput.Instance.Current : new BimanualFrame();
            GameSettings s = Settings;
            bool panelOk = s != null && s.launchNeedsLever ? f.panel.lever : f.panel.tracked;
            bool grip = f.manip.tracked && f.manip.grip;

            if (!Aiming) SelectTarget(null);

            // agarrar la manija
            if (grip && !lastGrip && panelOk && cooldownTimer <= 0f)
            {
                Aiming = true;
                anchor = f.manip.offset;
                aimStart = Time.time;
            }
            lastGrip = grip;

            if (Aiming && !panelOk)
            {
                Aiming = false;
                if (dots != null) dots.Hide();
                return;
            }

            if (Aiming && grip)
            {
                currentVelocity = AimVelocity(f.manip.offset, s);
                Predict(currentVelocity);
                return;
            }

            if (Aiming && !grip)
            {
                Aiming = false;
                if (Power >= minPowerToFire) Fire(f);
                if (dots != null) dots.Hide();
                return;
            }

            if (dots != null) { dots.Hide(); if (s != null && s.launchAssist >= LaunchAssist.Guia) ShowGhost(); }
        }

        /// <summary>Resortera: el tirón (ancla − mano) da la dirección y su largo la fuerza, como en el lab.</summary>
        private Vector3 AimVelocity(Vector2 hand, GameSettings s)
        {
            Vector2 pull = anchor - hand;
            float pullForMax = s != null ? Mathf.Max(0.15f, s.launchPullForMax) : 0.55f;
            Power = Mathf.Clamp01(pull.magnitude / pullForMax);

            bool surface = Surface;
            float minS = surface ? surfaceMinSpeed : underwaterMinSpeed;
            float maxS = surface ? surfaceMaxSpeed : underwaterMaxSpeed;
            float speed = Mathf.Lerp(minS, maxS, Power);

            Vector3 fwd = submarine.Forward, right = submarine.Right;
            float yaw = Mathf.Clamp(pull.x * yawGain, -maxYaw, maxYaw);
            float baseElev = surface ? surfaceBaseElevation : underwaterBaseElevation;
            float pitch = Mathf.Clamp(baseElev + pull.y * pitchGain, surface ? -10f : -80f, 80f);
            Vector3 flat = Quaternion.AngleAxis(yaw, Vector3.up) * fwd;
            Vector3 dir = flat * Mathf.Cos(pitch * Mathf.Deg2Rad) + Vector3.up * Mathf.Sin(pitch * Mathf.Deg2Rad);
            Vector3 v = dir.normalized * speed;

            // blanco al que apunta (el más alineado con el tiro)
            SelectTarget(v);
            if (s != null && s.launchAssist == LaunchAssist.Automatica && UpdateSolution() && Power >= minPowerToFire)
            {
                v = solution;
            }
            return v;
        }

        private void Predict(Vector3 v0)
        {
            Projectile tpl = TemplateFor(Ammo);
            TrajectoryMath.MediumAt medium = tpl != null ? tpl.MediumFunction() : (p => new TrajectoryMedium(Physics.gravity, 0f));
            currentPrediction = TrajectoryMath.Simulate(MuzzlePos, v0, medium, Time.fixedDeltaTime, predictionTime, points, recordEvery,
                                                        predictionMask, submarine != null ? submarine.transform : null, WaterZone.SurfaceAt);
            bool onTarget = Target != null && currentPrediction.hit &&
                            (currentPrediction.collider.GetComponentInParent<LaunchTarget>() == Target
                             || Vector3.Distance(currentPrediction.point, Target.AimPoint) < 1.2f);
            if (dots != null)
            {
                dots.Show(points, onTarget);
                dots.ShowLanding(currentPrediction.hit, currentPrediction.point, currentPrediction.normal, onTarget);
                GameSettings s = Settings;
                if (s != null && s.launchAssist >= LaunchAssist.Guia && s.launchAssist != LaunchAssist.Automatica) ShowGhost();
                else dots.HideGhost();
            }
        }

        // ------------------------------------------------------------------ blanco y solución de disparo

        private void SelectTarget(Vector3? aimVelocity)
        {
            LaunchTarget best = null;
            float bestScore = float.MaxValue;
            Vector3 from = MuzzlePos;
            Vector3 look = aimVelocity.HasValue ? aimVelocity.Value : (submarine != null ? submarine.Forward : Vector3.forward);
            Vector3 lookFlat = new Vector3(look.x, 0f, look.z);
            bool surface = Surface;
            foreach (LaunchTarget t in LaunchTarget.All)
            {
                if (!t.Ready || t.IsSurfaceTarget != surface) continue;
                Vector3 to = t.AimPoint - from;
                if (to.magnitude > Range) continue;
                Vector3 toFlat = new Vector3(to.x, 0f, to.z);
                float angle = lookFlat.sqrMagnitude > 1e-4f && toFlat.sqrMagnitude > 1e-4f ? Vector3.Angle(lookFlat, toFlat) : 0f;
                float score = angle + to.magnitude * 0.3f;
                if (score < bestScore) { bestScore = score; best = t; }
            }
            if (best != Target) { Target = best; hasSolution = false; }
            if (Target != null) Ammo = Target.Ammo;
            else Ammo = surface ? ProjectileKind.Lata : ProjectileKind.Lastre;
        }

        /// <summary>
        /// Arco ideal para el blanco elegido. En el aire, fórmula cerrada (tiro bajo y alto, se usa una rapidez un
        /// poco mayor que la mínima); bajo el agua, búsqueda numérica porque el arrastre rompe la parábola.
        /// </summary>
        private bool UpdateSolution()
        {
            if (Target == null) return false;
            Vector3 from = MuzzlePos;
            if (hasSolution && solutionTarget == Target && (from - solutionFrom).sqrMagnitude < 0.25f) return true;
            hasSolution = false;
            solutionTarget = Target;
            solutionFrom = from;
            Projectile tpl = TemplateFor(Ammo);
            if (tpl == null) return false;
            Vector3 to = Target.AimPoint;
            Vector3 low, high;
            if (Surface)
            {
                float g = Physics.gravity.magnitude * tpl.airGravityScale;
                float speed = Mathf.Clamp(TrajectoryMath.MinimumSpeed(from, to, g) * 1.12f, surfaceMinSpeed, surfaceMaxSpeed);
                int n = TrajectoryMath.FiringSolutions(from, to, speed, g, out low, out high);
                if (n == 0) return false;
                solution = high.y > 0f && n == 2 && LowArcBlocked(from, low, tpl) ? high : low;
                // PhysX integra por pasos (Euler semi-implícito): el vuelo real equivale a la parábola continua
                // con v0 + g·dt/2. Para que caiga EXACTO donde dice la fórmula se lanza con v0 = v' − g·dt/2.
                solution = TrajectoryMath.DiscreteLaunchVelocity(solution, Physics.gravity * tpl.airGravityScale, Time.fixedDeltaTime);
            }
            else
            {
                TrajectoryMath.MediumAt medium = tpl.MediumFunction();
                float dt = Time.fixedDeltaTime;
                float speed = TrajectoryMath.MinimumSpeedNumeric(from, to, underwaterMinSpeed, underwaterMaxSpeed, medium, dt);
                if (speed < 0f) return false;
                speed = Mathf.Min(underwaterMaxSpeed, speed * 1.1f);
                int n = TrajectoryMath.FiringSolutionNumeric(from, to, speed, medium, dt, out low, out high);
                if (n == 0) return false;
                solution = low;
            }
            hasSolution = true;
            return true;
        }

        private bool LowArcBlocked(Vector3 from, Vector3 v, Projectile tpl)
        {
            TrajectoryMath.Prediction p = TrajectoryMath.Simulate(from, v, tpl.MediumFunction(), Time.fixedDeltaTime, predictionTime, null, 1,
                                                                  predictionMask, submarine != null ? submarine.transform : null, WaterZone.SurfaceAt);
            return p.hit && p.collider.GetComponentInParent<LaunchTarget>() != Target && Vector3.Distance(p.point, Target.AimPoint) > 1.5f;
        }

        private void ShowGhost()
        {
            if (dots == null) return;
            if (!UpdateSolution()) { dots.HideGhost(); return; }
            Projectile tpl = TemplateFor(Ammo);
            TrajectoryMath.Simulate(MuzzlePos, solution, tpl.MediumFunction(), Time.fixedDeltaTime, predictionTime, ghostPoints, 2,
                                    predictionMask, submarine != null ? submarine.transform : null, WaterZone.SurfaceAt);
            dots.ShowGhost(ghostPoints);
        }

        // ------------------------------------------------------------------ disparo

        private void Fire(BimanualFrame f)
        {
            Projectile tpl = TemplateFor(Ammo);
            if (tpl == null) return;
            GameSettings s = Settings;
            Vector3 v = currentVelocity;
            bool assisted = s != null && s.launchAssist == LaunchAssist.Automatica;
            if (s != null && s.launchAssist == LaunchAssist.Iman && UpdateSolution())
            {
                float angle = Vector3.Angle(v, solution);
                float ratio = Mathf.Abs(v.magnitude - solution.magnitude) / Mathf.Max(0.1f, solution.magnitude);
                if (angle <= magnetAngle && ratio <= magnetSpeed) { v = solution; assisted = true; }
            }

            Vector3 from = MuzzlePos;
            Projectile p = Instantiate(tpl, from, Quaternion.LookRotation(v.normalized));
            p.name = tpl.name.Replace("Plantilla_", "");
            p.LaunchPower = Power;
            p.Assisted = assisted;

            // predicción definitiva (con la velocidad real del tiro) para los peces y la guía
            TrajectoryMath.Prediction pred = TrajectoryMath.Simulate(from, v, p.MediumFunction(), Time.fixedDeltaTime, 8f, null, 1,
                                                                     predictionMask, submarine != null ? submarine.transform : null, WaterZone.SurfaceAt);
            PredictedImpact impact = new PredictedImpact
            {
                hits = pred.hit,
                point = pred.point,
                impactTime = Time.time + pred.time,
                entersWater = pred.entersWater,
                waterEntry = pred.waterEntry,
                waterEntryTime = Time.time + pred.waterEntryTime
            };
            p.Launch(v, submarine != null ? submarine.transform : null, impact);
            float aimSeconds = Time.time - aimStart;
            bool compensated = f.compensating;
            ProjectileKind kind = p.kind;
            float power = Power;
            p.Resolved += (proj, target) => OnResolved(proj, target, kind, power, aimSeconds, compensated);

            Launches++;
            cooldownTimer = cooldown;
            AudioFx fx = AudioFx.Instance;
            if (fx != null) AudioFx.Play(fx.bubblePop, from, 1f, Surface ? 0.55f : 0.4f);
        }

        private void OnResolved(Projectile p, LaunchTarget target, ProjectileKind kind, float power, float aimSeconds, bool compensated)
        {
            bool hit = target != null;
            if (hit) Hits++;
            GameManager gm = GameManager.Instance;
            if (gm != null && gm.metrics != null)
            {
                gm.metrics.RecordLaunch(kind, hit, p != null && p.Assisted, power, aimSeconds, compensated);
            }
        }

        // ------------------------------------------------------------------ textos

        public string PromptText(bool webcam)
        {
            if (!IsOpen) return "";
            if (Target == null) return Surface ? "No quedan gatitos con hambre cerca · LANZAR para guardar" : "No quedan blancos cerca · LANZAR para guardar";
            string what = Ammo == ProjectileKind.Lata ? "la lata" : (Ammo == ProjectileKind.Lastre ? "el lastre (cae)" : "la boya (sube)");
            if (Aiming)
            {
                string pct = Mathf.RoundToInt(Power * 100f) + "%";
                return webcam ? "Fuerza " + pct + " · abre la mano para lanzar " + what
                              : "Fuerza " + pct + " · suelta el clic para lanzar " + what;
            }
            GameSettings s = Settings;
            bool needLever = s != null && s.launchNeedsLever;
            if (webcam) return (needLever ? "Mano del panel ARRIBA y " : "Apoya la mano del panel, ") + "cierra la otra y tira hacia atrás: " + Target.displayName;
            return (needLever ? "Mantén ESPACIO, " : "") + "clic y arrastra hacia atrás (como una resortera): " + Target.displayName + " · G = guardar";
        }
    }
}
