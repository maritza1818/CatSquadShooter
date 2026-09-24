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

            frame.time = Time.time;
            Current = frame;
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
