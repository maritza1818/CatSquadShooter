using System;
using System.Collections.Generic;
using UnityEngine;

namespace CatSquad.RescateProfundo
{
    /// <summary>
    /// Esclusa entre dos zonas de la ciudad con distinto nivel de agua (p. ej. el mar a -5 m y un
    /// barrio drenado a -17 m). Es el "ascensor de agua" para pasar de un nivel a otro:
    ///   1. El submarino entra a la cámara por la compuerta abierta y se acerca a la boya de mando.
    ///   2. Se ancla solo (Arrive) junto a la boya.
    ///   3. Mientras SOSTIENE la palanca (mano del panel: contracción sostenida), el agua de la cámara
    ///      sube o baja hasta igualar al otro lado; el submarino sube o baja con ella.
    ///   4. Al igualarse se abre la otra compuerta. Para salir: girar o avanzar.
    /// Una compuerta está abierta solo si la cámara está al mismo nivel que ese lado.
    /// </summary>
    public class LockStation : MonoBehaviour
    {
        private static readonly List<LockStation> all = new List<LockStation>();
        public static IReadOnlyList<LockStation> All { get { return all; } }
        public static event Action<LockStation> AnyEqualized;

        public string displayName = "Esclusa";
        public WaterZone chamber;
        public WaterZone west;
        public WaterZone east;
        public FloodGate westGate;
        public FloodGate eastGate;

        [Header("Boya de mando (flota en la cámara)")]
        public Transform buoy;
        public Transform lever;
        public Renderer statusLight;
        [Tooltip("Z de la boya: pegada a la pared del fondo de la cámara")]
        public float buoyZ = 3.3f;

        [Header("Movimiento del agua")]
        [Tooltip("Metros por segundo mientras se sostiene la palanca")]
        public float speed = 1.4f;
        public float tolerance = 0.15f;
        [Tooltip("Profundidad del submarino anclado bajo la superficie de la cámara")]
        public float dockDepth = 1.7f;

        public float Level { get; private set; }
        public bool LeverHeld { get; private set; }
        public bool IsMoving { get; private set; }
        public bool SubDockedHere
        {
            get
            {
                SubmarineController sub = GameManager.Instance != null ? GameManager.Instance.submarine : null;
                return sub != null && sub.State == SubState.Acoplado && sub.AnchorOwner == this;
            }
        }

        public bool WestOpen { get { return west != null && Mathf.Abs(Level - west.Level) < tolerance; } }
        public bool EastOpen { get { return east != null && Mathf.Abs(Level - east.Level) < tolerance; } }
        /// <summary>Ambas compuertas abiertas: no hay nada que igualar.</summary>
        public bool Balanced { get { return WestOpen && EastOpen; } }

        private float targetLevel;
        private bool hasTarget;
        private bool wasDocked;
        private bool suppressUntilExit;
        private float leverAngle;
        private MaterialPropertyBlock block;
        private AudioSource loop;

        private void OnEnable() { if (!all.Contains(this)) all.Add(this); }
        private void OnDisable() { all.Remove(this); }

        private bool initialized;

        private void Start()
        {
            loop = AudioFx.CreateLoop(gameObject, AudioFx.Instance != null ? AudioFx.Instance.pumpLoop : null, 0.3f);
        }

        /// <summary>
        /// En el primer Update (cuando las bombas ya fijaron el nivel guardado de cada barrio), la cámara
        /// toma el nivel del lado más alto: con todo inundado, ambas compuertas quedan abiertas.
        /// </summary>
        private void Initialize()
        {
            initialized = true;
            Level = Mathf.Max(west != null ? west.Level : -5f, east != null ? east.Level : -5f);
            if (chamber != null) chamber.SetPumpedLevel(Level, true);
            if (westGate != null) westGate.SetOpen(WestOpen, true);
            if (eastGate != null) eastGate.SetOpen(EastOpen, true);
        }

        public Vector3 CenterAt(float depthBelowSurface)
        {
            Vector3 c = chamber != null
                ? new Vector3((chamber.min.x + chamber.max.x) * 0.5f, Level - depthBelowSurface, 0f)
                : transform.position;
            return c;
        }

        /// <summary>Pose de anclaje: en el centro de la cámara, bajo la superficie, mirando a la boya.</summary>
        public Vector3 DockPosition { get { return CenterAt(dockDepth); } }

        public bool ContainsSub(SubmarineController sub)
        {
            if (sub == null || chamber == null) return false;
            Vector3 p = sub.transform.position;
            return chamber.Contains(p) && Mathf.Abs(p.z) < 5.5f;
        }

        /// <summary>El otro lado: al que hay que igualar la cámara para abrir su compuerta.</summary>
        private bool ComputeTarget(SubmarineController sub, out float target)
        {
            target = Level;
            if (west == null || east == null) return false;
            bool w = WestOpen, e = EastOpen;
            if (w && e) return false;
            if (w) { target = east.Level; return true; }
            if (e) { target = west.Level; return true; }
            // Ninguna abierta (un barrio cambió de nivel con el submarino dentro): ir al más cercano
            target = Mathf.Abs(Level - west.Level) < Mathf.Abs(Level - east.Level) ? west.Level : east.Level;
            return true;
        }

        public bool TryDock(SubmarineController sub)
        {
            if (sub == null || !sub.CanDock || !ContainsSub(sub)) return false;
            sub.AnchorAt(DockPosition, 0f, this);
            return sub.AnchorOwner == this;
        }

        private void Update()
        {
            if (!initialized) Initialize();
            float dt = Time.deltaTime;
            GameManager gm = GameManager.Instance;
            SubmarineController sub = gm != null ? gm.submarine : null;
            BimanualFrame frame = BimanualInput.Instance != null ? BimanualInput.Instance.Current : new BimanualFrame();

            // ---- acople automático: dentro de la cámara, despacio y con algo que igualar.
            // Después de soltarse, no vuelve a acoplar hasta que el submarino salga de la cámara.
            if (suppressUntilExit && (sub == null || !ContainsSub(sub))) suppressUntilExit = false;
            if (sub != null && sub.CanDock && sub.InputEnabled && !suppressUntilExit && !Balanced && ContainsSub(sub) && sub.Velocity.magnitude < 2.2f)
            {
                bool railAllows = RailPilot.Instance == null || RailPilot.Instance.AllowsLock(this);
                if (railAllows) TryDock(sub);
            }

            bool docked = SubDockedHere;
            if (wasDocked && !docked) suppressUntilExit = true;
            wasDocked = docked;
            if (docked) sub.SetAnchorTarget(DockPosition, this);

            // ---- palanca sostenida (misma que la de las bombas)
            bool leverGesture = frame.panel.lever || frame.panel.joystick.y > 0.55f || (frame.helm.tracked && frame.helm.vertical > 0.5f);
            LeverHeld = docked && sub.InputEnabled && (frame.panel.tracked || frame.helm.tracked) && leverGesture;

            // ---- mover el agua de la cámara hacia el nivel del otro lado
            IsMoving = false;
            hasTarget = ComputeTarget(sub, out targetLevel);
            if (LeverHeld && hasTarget)
            {
                float before = Level;
                Level = Mathf.MoveTowards(Level, targetLevel, speed * dt);
                IsMoving = !Mathf.Approximately(before, Level);
                if (Mathf.Abs(Level - targetLevel) < 0.01f)
                {
                    Level = targetLevel;
                    AudioFx.Play(AudioFx.Instance != null ? AudioFx.Instance.chime : null, transform.position);
                    if (AnyEqualized != null) AnyEqualized(this);
                }
            }
            if (chamber != null) chamber.SetPumpedLevel(Level, true);

            // ---- compuertas: abiertas solo con los niveles iguales
            if (westGate != null) westGate.SetOpen(WestOpen, false);
            if (eastGate != null) eastGate.SetOpen(EastOpen, false);

            UpdateVisuals(dt);
        }

        /// <summary>Texto para el HUD mientras el submarino está anclado aquí.</summary>
        public string PromptText(bool webcam)
        {
            float target;
            if (!ComputeTarget(null, out target)) return displayName + ": las dos compuertas están abiertas. Para salir, gira o avanza";
            string dir = target < Level ? "BAJAR" : "SUBIR";
            if (IsMoving) return displayName + ": ¡el agua se mueve! " + Mathf.Abs(Level - target).ToString("F0") + " m";
            return displayName + (webcam ? ": mantén ARRIBA la mano del panel para " : ": mantén ESPACIO (la palanca) para ") + dir + " el agua";
        }

        private void UpdateVisuals(float dt)
        {
            if (buoy != null && chamber != null)
            {
                float bob = Mathf.Sin(Time.time * 1.6f) * 0.05f;
                Vector3 c = CenterAt(0f);
                buoy.position = new Vector3(c.x, Level + bob, buoyZ);
            }
            if (lever != null)
            {
                float targetAngle = LeverHeld ? -35f : 35f;
                leverAngle = Mathf.Lerp(leverAngle, targetAngle, 1f - Mathf.Exp(-10f * dt));
                lever.localRotation = Quaternion.Euler(leverAngle, 0f, 0f);
            }
            if (statusLight != null)
            {
                if (block == null) block = new MaterialPropertyBlock();
                Color c = Balanced ? new Color(0.3f, 0.7f, 1f) : (IsMoving ? new Color(0.2f, 1f, 0.5f) : new Color(1f, 0.6f, 0.1f));
                float blink = IsMoving ? 0.6f + 0.4f * Mathf.Sin(Time.time * 10f) : 1f;
                block.SetColor("_BaseColor", c);
                block.SetColor("_EmissionColor", c * 2.5f * blink);
                statusLight.SetPropertyBlock(block);
            }
            if (loop != null)
            {
                if (IsMoving && !loop.isPlaying) loop.Play();
                else if (!IsMoving && loop.isPlaying) loop.Stop();
            }
        }

        private void OnDrawGizmos()
        {
            if (chamber == null) return;
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.6f);
            Gizmos.DrawWireCube(CenterAt(0f), new Vector3(chamber.max.x - chamber.min.x, 0.1f, 10f));
        }
    }
}
