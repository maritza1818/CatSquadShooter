using UnityEngine;

namespace CatSquad.RescateProfundo
{
    /// <summary>
    /// Punto único de entrada del juego. Decide qué proveedor usar (webcam o teclado/mouse),
    /// asigna los roles de cada brazo según la configuración del terapeuta y publica
    /// el BimanualFrame del frame actual para que lo lean las mecánicas.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class BimanualInput : MonoBehaviour
    {
        public static BimanualInput Instance { get; private set; }

        [Header("Configuración (la sobrescribe GameManager con los ajustes guardados)")]
        public InputMode mode = InputMode.TecladoMouse;
        [Tooltip("Lado afectado del niño. Por defecto opera el PANEL (mano asistente).")]
        public BodySide affectedSide = BodySide.Izquierdo;
        [Tooltip("Progresión clínica: el lado afectado pasa a operar el BRAZO manipulador.")]
        public bool invertRoles = false;

        [Header("Proveedores")]
        public KeyboardMouseInputProvider keyboard;
        public WebcamPoseInputProvider webcam;
        public Camera gameCamera;

        public BimanualFrame Current { get; private set; }
        public bool UsingWebcam { get; private set; }
        public BimanualInputProvider ActiveProvider { get { return UsingWebcam ? (BimanualInputProvider)webcam : keyboard; } }

        public BodySide PanelSide { get { return invertRoles ? Opposite(affectedSide) : affectedSide; } }
        public BodySide ManipSide { get { return Opposite(PanelSide); } }

        private InputContext context;
        private Vector2 lastKeyboardManip;

        public static BodySide Opposite(BodySide side)
        {
            return side == BodySide.Izquierdo ? BodySide.Derecho : BodySide.Izquierdo;
        }

        private void Awake()
        {
            Instance = this;
            if (keyboard == null) keyboard = GetComponent<KeyboardMouseInputProvider>();
            if (keyboard == null) keyboard = gameObject.AddComponent<KeyboardMouseInputProvider>();
            if (webcam == null) webcam = GetComponent<WebcamPoseInputProvider>();
            context.armReachWorld = 3f;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>El brazo manipulador informa cada frame dónde está su base y cuánto alcanza.</summary>
        public void SetArmContext(Vector3 armBaseWorld, float reachWorld, float planeZ)
        {
            context.armBaseWorld = armBaseWorld;
            context.armReachWorld = reachWorld;
            context.gamePlaneZ = planeZ;
            context.firstPerson = false;
        }

        /// <summary>Versión primera persona: el brazo trabaja en un plano frente a la cabina.</summary>
        public void SetArmContextFirstPerson(Vector3 armBaseWorld, float reachWorld, Vector3 right, Vector3 up, Vector3 forward, float planeDepth)
        {
            context.armBaseWorld = armBaseWorld;
            context.armReachWorld = reachWorld;
            context.firstPerson = true;
            context.armRight = right;
            context.armUp = up;
            context.armForward = forward;
            context.armPlaneDepth = planeDepth;
        }

        private void Update()
        {
            context.camera = gameCamera != null ? gameCamera : Camera.main;

            if (webcam != null)
            {
                webcam.panelSide = PanelSide;
            }

            BimanualFrame frame = new BimanualFrame();
            float dt = Mathf.Max(Time.unscaledDeltaTime, 0.0001f);

            UsingWebcam = mode == InputMode.Webcam && webcam != null && webcam.enabled && webcam.IsAvailable;

            if (UsingWebcam)
            {
                webcam.Read(ref frame, context, dt);
            }
            else if (keyboard != null)
            {
                keyboard.Read(ref frame, context, dt);
                frame.manip.speed = (frame.manip.offset - lastKeyboardManip).magnitude / dt;
                lastKeyboardManip = frame.manip.offset;
            }

            InterpretThumbs(ref frame, dt);
            frame.time = Time.time;
            Current = frame;
        }

        // ------------------------------------------------------------------ pulgares del panel

        [Header("Panel de cabina (pulgares)")]
        [Tooltip("Tiempo para esperar al otro pulgar antes de tomarlo como 'mover' (s)")]
        public float thumbChordWindow = 0.25f;
        [Tooltip("Tiempo con ambos pulgares arriba para confirmar (s)")]
        public float bothThumbsHold = 0.35f;
        [Tooltip("Intercambia pulgar izquierdo y derecho (lo fija el terapeuta)")]
        public bool invertThumbs;

        private float firstThumbTimer = -1f;
        private int firstThumbSide;
        private bool gestureUsed;
        private float bothTimer;
        private bool chordStarted;

        /// <summary>
        /// Convierte el estado sostenido de los pulgares en eventos (igual para teclado y webcam):
        /// un pulgar que sube solo mueve la selección una vez; si el otro sube enseguida es un acorde
        /// y, sostenidos ambos un momento, confirma. Hay que bajar ambos para el siguiente gesto.
        /// </summary>
        private void InterpretThumbs(ref BimanualFrame frame, float dt)
        {
            bool left = frame.thumbLeft, right = frame.thumbRight;
            if (invertThumbs) { bool tmp = left; left = right; right = tmp; frame.thumbLeft = left; frame.thumbRight = right; }
            if (!frame.panelHandsReady) { left = false; right = false; }

            if (!left && !right)
            {
                // toque corto de un solo pulgar (se soltó antes de la ventana del acorde): también mueve
                if (!gestureUsed && !chordStarted && firstThumbTimer >= 0f) frame.panelNavigate = firstThumbSide;
                firstThumbTimer = -1f;
                gestureUsed = false;
                chordStarted = false;
                bothTimer = 0f;
                frame.bothThumbs01 = 0f;
                return;
            }
            if (gestureUsed) { frame.bothThumbs01 = left && right ? 1f : 0f; return; }

            if (left && right)
            {
                chordStarted = true;
                bothTimer += dt;
                frame.bothThumbs01 = Mathf.Clamp01(bothTimer / Mathf.Max(0.05f, bothThumbsHold));
                if (bothTimer >= bothThumbsHold)
                {
                    frame.panelConfirm = true;
                    gestureUsed = true;
                }
                return;
            }

            bothTimer = 0f;
            // un acorde que se soltó antes de tiempo no cuenta como "mover": hay que bajar ambos pulgares
            if (chordStarted) { gestureUsed = true; frame.bothThumbs01 = 0f; return; }
            int side = left ? -1 : 1;
            if (firstThumbTimer < 0f) { firstThumbTimer = 0f; firstThumbSide = side; }
            firstThumbTimer += dt;
            if (firstThumbTimer >= thumbChordWindow)
            {
                frame.panelNavigate = firstThumbSide;
                gestureUsed = true;
            }
        }

        public void BeginCalibration()
        {
            if (webcam != null) webcam.BeginCalibration();
            if (keyboard != null) keyboard.BeginCalibration();
        }

        public void EndCalibration()
        {
            if (webcam != null) webcam.EndCalibration();
            if (keyboard != null) keyboard.EndCalibration();
        }

        public string StatusText()
        {
            if (mode == InputMode.Webcam)
            {
                if (webcam == null) return "Webcam: no configurada (usando teclado)";
                if (UsingWebcam) return "Webcam activa (" + webcam.PacketsPerSecond + " fps)";
                if (webcam.IsReceiving) return "Webcam: sin cuerpo detectado (usando teclado)";
                return "Webcam: sin señal del puente Python (usando teclado)";
            }
            return "Teclado + mouse";
        }
    }
}
