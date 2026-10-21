using System.Collections.Generic;
using CatSquad.RescateProfundo;
using UnityEngine;

namespace CatSquad.Muelle
{
    /// <summary>
    /// LAB 05 · GameManager del minijuego 2D "El Muelle" (misma estructura que el del lab):
    ///   OnDragStart() → al presionar: el ovillo queda quieto (DesactivateRb), se guarda el punto inicial y se muestran los puntos.
    ///   OnDrag()      → mientras se arrastra: distancia y dirección (inicio − actual) → fuerza = dirección·distancia·pushForce,
    ///                   y se actualizan los puntos de la trayectoria.
    ///   OnDragEnd()   → al soltar: ActivateRb + Push(fuerza) y se ocultan los puntos.
    /// Además (ejercicios 2–4): dos blancos con puntuación, racha, la solución de disparo de Unity Learn
    /// "Calculating Trajectories" (arco guía y tiro automático con predicción del movimiento de la balsa)
    /// y el modo "rebotes" con una escena de física paralela.
    ///
    /// Teclas: arrastrar con el mouse = lanzar · A = arco guía · T = puntos con rebotes · 1/2 o Tab = blanco
    ///         Espacio = tiro automático (solución de disparo) · R = reiniciar puntaje.
    /// </summary>
    public class MuelleGameManager : MonoBehaviour
    {
        public static MuelleGameManager Instance;

        private Camera cam;

        public Ball ball;
        public Trajectory trajectory;
        [SerializeField] float pushForce = 4f;
        [Tooltip("Arrastre máximo (unidades del mundo): limita la fuerza")]
        public float maxDragDistance = 3.5f;

        [Header("Escena")]
        public Transform spawnPoint;
        public float waterY = -2.5f;
        public MuelleTarget[] targets;
        public LineRenderer ghostLine;
        public ParticleSystem splash;

        [Header("Sonidos")]
        public AudioSource audioSource;
        public AudioClip launchClip;
        public AudioClip scoreClip;
        public AudioClip meowClip;
        public AudioClip splashClip;
        public AudioClip bounceClip;

        private bool isDragging = false;
        private Vector2 startPoint;
        private Vector2 endPoint;
        private Vector2 direction;
        private Vector2 force;
        private float distance;

        public int Score { get; private set; }
        public int Shots { get; private set; }
        public int Hits { get; private set; }
        public int Streak { get; private set; }
        public int BestStreak { get; private set; }
        public bool Assist { get; private set; }

        private int selected;
        private float respawnTimer = -1f;
        private float stillTimer;
        private float flightTime;
        private bool scoredThisShot;
        private string popup = "";
        private float popupTimer;
        private Vector2 popupPos;
        private readonly List<Vector3> ghostPoints = new List<Vector3>();
        private GUIStyle big, mid, small;

        private void Awake()
        {
            if (Instance == null) Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            cam = Camera.main;
            if (spawnPoint != null) ball.PlaceAt(spawnPoint.position);
            ball.DesactivateRb();
            if (trajectory != null) trajectory.waterY = waterY;
        }

        private void Update()
        {
            HandleKeys();

            if (ball.InFlight)
            {
                UpdateFlight();
            }
            else if (respawnTimer < 0f)
            {
                if (Input.GetMouseButtonDown(0))
                {
                    isDragging = true;
                    OnDragStart();
                }
                if (Input.GetMouseButtonUp(0) && isDragging)
                {
                    isDragging = false;
                    OnDragEnd();
                }
                if (isDragging) OnDrag();
            }

            if (respawnTimer >= 0f)
            {
                respawnTimer -= Time.deltaTime;
                if (respawnTimer < 0f) Respawn();
            }

            if (Assist && !ball.InFlight) DrawGhost(isDragging ? force.magnitude / Mathf.Max(0.001f, ball.rb.mass) : 0f);
            else if (ghostLine != null) ghostLine.enabled = false;

            if (popupTimer > 0f) popupTimer -= Time.deltaTime;
        }

        // ------------------------------------------------------------------ arrastrar (como el lab)

        private void OnDragStart()
        {
            ball.DesactivateRb();
            startPoint = cam.ScreenToWorldPoint(Input.mousePosition);
            trajectory.Show();
        }

        private void OnDrag()
        {
            endPoint = cam.ScreenToWorldPoint(Input.mousePosition);
            distance = Mathf.Min(Vector2.Distance(startPoint, endPoint), maxDragDistance);
            direction = (startPoint - endPoint).normalized;
            force = direction * distance * pushForce;

            // solo para depurar
            Debug.DrawLine(startPoint, endPoint);

            trajectory.UpdateDots(ball.pos, force, ball.rb.mass, ball.rb.gravityScale);
        }

        private void OnDragEnd()
        {
            trajectory.Hide();
            if (force.magnitude < 0.5f) return;   // un clic sin arrastrar no lanza
            Launch(force);
        }

        private void Launch(Vector2 impulse)
        {
            ball.ActivateRb();
            ball.Push(impulse);
            Shots++;
            flightTime = 0f;
            stillTimer = 0f;
            scoredThisShot = false;
            Play(launchClip, 0.8f);
            force = Vector2.zero;
        }

        // ------------------------------------------------------------------ vuelo, aciertos y fallos

        private void UpdateFlight()
        {
            flightTime += Time.deltaTime;
            if (ball.pos.y < waterY - ball.col.radius)
            {
                if (!scoredThisShot) Miss("¡Al agua!");
                if (splash != null) { splash.transform.position = new Vector3(ball.pos.x, waterY, 0f); splash.Play(); }
                Play(splashClip, 0.9f);
                ball.DesactivateRb();
                respawnTimer = 0.8f;
                return;
            }
            if (ball.rb.linearVelocity.magnitude < 0.08f) stillTimer += Time.deltaTime; else stillTimer = 0f;
            if ((stillTimer > 1.2f || flightTime > 12f) && respawnTimer < 0f)
            {
                if (!scoredThisShot) Miss("Casi...");
                respawnTimer = 0.6f;
            }
        }

        public void OnBallInTarget(MuelleTarget t)
        {
            if (!ball.InFlight || scoredThisShot || !t.RegisterHit()) return;
            scoredThisShot = true;
            Hits++;
            Streak++;
            BestStreak = Mathf.Max(BestStreak, Streak);
            int pts = t.points * (Streak >= 3 ? 2 : 1);
            Score += pts;
            ShowPopup("+" + pts + (Streak >= 3 ? "  ¡racha x2!" : "") + "  ¡Miau!", t.AimPoint);
            Play(scoreClip, 1f);
            Play(meowClip, 0.9f);
            respawnTimer = 1.4f;
        }

        public void OnBounce(float speed)
        {
            Play(bounceClip, Mathf.Clamp01(speed / 12f) * 0.6f);
        }

        private void Miss(string text)
        {
            Streak = 0;
            ShowPopup(text, ball.pos);
        }

        private void Respawn()
        {
            respawnTimer = -1f;
            if (spawnPoint != null) ball.PlaceAt(spawnPoint.position);
        }

        // ------------------------------------------------------------------ Unity Learn: solución de disparo

        private MuelleTarget Selected { get { return targets != null && targets.Length > 0 ? targets[Mathf.Clamp(selected, 0, targets.Length - 1)] : null; } }

        /// <summary>
        /// Velocidad para dar en el blanco con una rapidez dada (tiro bajo o alto, el más parecido al actual).
        /// Para la balsa se predice dónde estará cuando llegue el ovillo (dos iteraciones: tiempo de vuelo → nueva posición).
        /// </summary>
        public bool Solve(float speed, out Vector2 v0, out float lowDeg, out float highDeg)
        {
            v0 = Vector2.zero;
            lowDeg = highDeg = 0f;
            MuelleTarget t = Selected;
            if (t == null) return false;
            float g = Physics2D.gravity.magnitude * ball.rb.gravityScale;
            Vector2 from = ball.pos;
            Vector2 aim = t.AimPoint;
            float maxSpeed = maxDragDistance * pushForce / Mathf.Max(0.001f, ball.rb.mass);
            if (speed <= 0.1f)
            {
                float min = TrajectoryMath.MinimumSpeed(new Vector3(from.x, from.y, 0f), new Vector3(aim.x, aim.y, 0f), g);
                speed = Mathf.Min(maxSpeed, min * 1.08f);
            }
            Vector2 low = Vector2.zero, high = Vector2.zero;
            int n = 0;
            for (int it = 0; it < 3; it++)
            {
                n = TrajectoryMath.FiringSolutions2D(from, aim, speed, g, out low, out high);
                if (n == 0) return false;
                float tf = Mathf.Abs(low.x) > 1e-3f ? Mathf.Abs((aim.x - from.x) / low.x) : 0f;
                aim = t.PredictAimPoint(tf);
            }
            lowDeg = Mathf.Atan2(low.y, low.x) * Mathf.Rad2Deg;
            highDeg = Mathf.Atan2(high.y, high.x) * Mathf.Rad2Deg;
            // de los dos arcos se prefiere el que no choca con nada (p. ej. el faro tapa el tiro bajo)
            bool lowClear = ArcClear(from, low, aim, t), highClear = ArcClear(from, high, aim, t);
            if (lowClear != highClear) { v0 = lowClear ? low : high; return true; }
            float current = force.sqrMagnitude > 0.01f ? Mathf.Atan2(force.y, force.x) * Mathf.Rad2Deg : lowDeg;
            v0 = Mathf.Abs(Mathf.DeltaAngle(current, lowDeg)) <= Mathf.Abs(Mathf.DeltaAngle(current, highDeg)) ? low : high;
            return true;
        }

        /// <summary>¿El arco llega al blanco sin tocar otro collider? (Physics2D.LinecastAll entre puntos de la fórmula)</summary>
        private bool ArcClear(Vector2 from, Vector2 v0, Vector2 aim, MuelleTarget target)
        {
            Vector2 g = Physics2D.gravity * ball.rb.gravityScale;
            float tf = Mathf.Abs(v0.x) > 1e-3f ? Mathf.Abs((aim.x - from.x) / v0.x) : 2f;
            Vector2 prev = from;
            const int n = 30;
            for (int i = 1; i <= n; i++)
            {
                Vector2 p = TrajectoryMath.PositionAt(from, v0, g, tf * i / n * 0.97f);
                foreach (RaycastHit2D h in Physics2D.LinecastAll(prev, p))
                {
                    if (h.collider == null || h.collider.isTrigger) continue;
                    if (h.collider.GetComponentInParent<Ball>() != null) continue;
                    if (h.collider.GetComponentInParent<MuelleTarget>() == target) continue;
                    return false;
                }
                prev = p;
            }
            return true;
        }

        private void DrawGhost(float currentSpeed)
        {
            if (ghostLine == null) return;
            Vector2 v0;
            float lo, hi;
            if (!Solve(currentSpeed, out v0, out lo, out hi)) { ghostLine.enabled = false; return; }
            Vector2 g = Physics2D.gravity * ball.rb.gravityScale;
            ghostPoints.Clear();
            for (int i = 0; i < 60; i++)
            {
                Vector2 p = TrajectoryMath.PositionAt((Vector2)ball.pos, v0, g, i * 0.05f);
                ghostPoints.Add(p);
                if (p.y < waterY) break;
            }
            ghostLine.enabled = true;
            ghostLine.positionCount = ghostPoints.Count;
            for (int i = 0; i < ghostPoints.Count; i++) ghostLine.SetPosition(i, ghostPoints[i]);
        }

        private void AutoShot()
        {
            if (ball.InFlight || respawnTimer >= 0f) return;
            Vector2 v0;
            float lo, hi;
            if (!Solve(0f, out v0, out lo, out hi)) { ShowPopup("Fuera de alcance", ball.pos); return; }
            // corrección por el paso de la física: v0 = v' − g·dt/2 (así cae exacto donde dice la fórmula)
            v0 = TrajectoryMath.DiscreteLaunchVelocity(v0, Physics2D.gravity * ball.rb.gravityScale, Time.fixedDeltaTime);
            Launch(v0 * ball.rb.mass);
        }

        // ------------------------------------------------------------------ teclas y textos

        private void HandleKeys()
        {
            if (Input.GetKeyDown(KeyCode.A)) Assist = !Assist;
            if (Input.GetKeyDown(KeyCode.T) && trajectory != null) trajectory.UseSimulation = !trajectory.UseSimulation;
            if (targets != null && targets.Length > 0)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1)) selected = 0;
                if (Input.GetKeyDown(KeyCode.Alpha2) && targets.Length > 1) selected = 1;
                if (Input.GetKeyDown(KeyCode.Tab)) selected = (selected + 1) % targets.Length;
            }
            if (Input.GetKeyDown(KeyCode.Space)) AutoShot();
            if (Input.GetKeyDown(KeyCode.R)) { Score = Shots = Hits = Streak = BestStreak = 0; }
        }

        private void ShowPopup(string text, Vector2 worldPos)
        {
            popup = text;
            popupTimer = 1.6f;
            popupPos = worldPos;
        }

        private void Play(AudioClip clip, float volume)
        {
            if (audioSource != null && clip != null) audioSource.PlayOneShot(clip, volume);
        }

        private void OnGUI()
        {
            float scale = Screen.height / 1080f;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float w = Screen.width / scale;
            if (big == null)
            {
                big = new GUIStyle(GUI.skin.label) { fontSize = 46, fontStyle = FontStyle.Bold };
                mid = new GUIStyle(GUI.skin.label) { fontSize = 28 };
                small = new GUIStyle(GUI.skin.label) { fontSize = 22 };
                big.normal.textColor = mid.normal.textColor = small.normal.textColor = Color.white;
            }

            GUI.Box(new Rect(20, 20, 560, 150), "");
            GUI.Label(new Rect(40, 26, 520, 60), "Puntos: " + Score, big);
            GUI.Label(new Rect(40, 86, 520, 40), "Aciertos " + Hits + " de " + Shots + "   ·   Racha " + Streak + " (mejor " + BestStreak + ")", mid);
            GUI.Label(new Rect(40, 126, 520, 40), "Racha de 3 o más: ¡puntos dobles!", small);

            MuelleTarget t = Selected;
            GUI.Box(new Rect(w - 640, 20, 620, 190), "");
            GUI.Label(new Rect(w - 620, 28, 600, 34), "[A] Arco guía: " + (Assist ? "SÍ" : "NO") + "     [T] Rebotes: " + (trajectory != null && trajectory.UseSimulation ? "SÍ" : "NO"), mid);
            GUI.Label(new Rect(w - 620, 64, 600, 34), "[1/2] Blanco: " + (t != null ? t.displayName + " (" + t.points + " pt)" : "-") + "     [Espacio] Tiro automático", mid);
            Vector2 v0;
            float lo, hi;
            float speedNow = isDragging ? force.magnitude / Mathf.Max(0.001f, ball.rb.mass) : 0f;
            if (Solve(speedNow, out v0, out lo, out hi))
                GUI.Label(new Rect(w - 620, 104, 600, 34), "Solución con v0 = " + v0.magnitude.ToString("F1") + " m/s:  θ bajo " + lo.ToString("F0") + "°  ·  θ alto " + hi.ToString("F0") + "°", small);
            else
                GUI.Label(new Rect(w - 620, 104, 600, 34), speedNow > 0.1f ? "Con esa fuerza no llega: tira más" : "Fuera de alcance", small);
            GUI.Label(new Rect(w - 620, 140, 600, 60), "p(t) = p0 + v0·t + ½·g·t²", mid);

            if (isDragging)
            {
                float speed = force.magnitude / Mathf.Max(0.001f, ball.rb.mass);
                float angle = Mathf.Atan2(force.y, force.x) * Mathf.Rad2Deg;
                GUI.Label(new Rect(40, 190, 700, 40), "v0 = " + speed.ToString("F1") + " m/s   ·   θ = " + angle.ToString("F0") + "°   ·   fuerza " + Mathf.RoundToInt(distance / maxDragDistance * 100f) + "%", mid);
            }
            else if (!ball.InFlight && respawnTimer < 0f)
            {
                GUI.Label(new Rect(40, 1080 - 70, 1400, 50), "Presiona, arrastra hacia ATRÁS (como una resortera) y suelta. Los puntos muestran por dónde irá el ovillo.", mid);
            }

            if (popupTimer > 0f && cam != null)
            {
                Vector3 sp = cam.WorldToScreenPoint(popupPos);
                float y = (Screen.height - sp.y) / scale - 80f - (1.6f - popupTimer) * 40f;
                GUI.color = new Color(1f, 0.85f, 0.3f, Mathf.Clamp01(popupTimer));
                GUI.Label(new Rect(sp.x / scale - 200f, y, 600, 60), popup, big);
                GUI.color = Color.white;
            }
        }
    }
}
