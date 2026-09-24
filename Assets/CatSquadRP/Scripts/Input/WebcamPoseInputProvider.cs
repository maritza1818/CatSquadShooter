using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace CatSquad.RescateProfundo
{
    /// <summary>Paquete JSON que envía VisionBridge/catsquad_pose_bridge.py por UDP.</summary>
    [Serializable]
    public class PosePacket
    {
        public float t;
        public int pose;          // 1 si se detectó el cuerpo
        // [x, y, visibilidad] en coordenadas de ESPEJO normalizadas (x a la derecha como en un espejo, y hacia abajo)
        public float[] ls, rs;    // hombros izq/der (anatómicos)
        public float[] le, re;    // codos
        public float[] lw, rw;    // muñecas
        public float[] lh, rh;    // caderas
        public int handL, handR;  // 1 si se detectó esa mano
        public float openL, openR;   // 0 = puño cerrado, 1 = mano abierta
        public float rollL, rollR;   // rotación del antebrazo estimada (grados)
    }

    [Serializable]
    public class WebcamCalibrationData
    {
        public bool valid;
        public ReachEnvelope manipEnvelope = new ReachEnvelope(1.2f);
        public ReachEnvelope panelEnvelope = new ReachEnvelope(0.45f);
    }

    /// <summary>
    /// Recibe la estimación de pose por UDP y la INTERPRETA (capa "Interpretación → Posición y movimiento"
    /// de la arquitectura): alcance normalizado, prensión, rotación de muñeca y compensación de tronco.
    /// Toda la lógica clínica configurable vive aquí, en Unity; Python solo percibe.
    /// </summary>
    public class WebcamPoseInputProvider : BimanualInputProvider
    {
        [Header("Red")]
        public int port = 5065;
        [Tooltip("Segundos sin paquetes para considerar la webcam desconectada")]
        public float timeout = 0.6f;

        [Header("Roles (los asigna BimanualInput según la configuración del terapeuta)")]
        public BodySide panelSide = BodySide.Izquierdo;

        [Header("Interpretación")]
        [Tooltip("Suavizado exponencial de posiciones (0 = nada)")]
        [Range(0f, 0.95f)] public float positionSmoothing = 0.55f;
        public float gripCloseBelow = 0.35f;
        public float gripOpenAbove = 0.55f;
        [Tooltip("Desplazamiento del centro de hombros (en anchos de hombro) que cuenta como compensación")]
        public float trunkShiftThreshold = 0.22f;
        [Tooltip("Inclinación de la línea de hombros (grados) que cuenta como compensación")]
        public float trunkTiltThreshold = 12f;
        [Tooltip("Multiplicador de sensibilidad (lo ajusta el terapeuta)")]
        public float compensationSensitivity = 1f;
        public float panelDeadZone = 0.18f;
        public float leverThreshold = 0.55f;
        [Tooltip("Qué tan rápido crece el envolvente cuando el niño supera su marca")]
        public float envelopeGrowth = 0.08f;

        public WebcamCalibrationData calibration = new WebcamCalibrationData();

        // ---- estado de red
        private UdpClient client;
        private Thread thread;
        private volatile bool running;
        private readonly object packetLock = new object();
        private string latestJson;
        private float lastPacketTime = -999f;
        private int packetsThisSecond;
        private float packetCounterTime;
        public int PacketsPerSecond { get; private set; }

        private PosePacket packet;

        // ---- estado de interpretación
        private Vector2 homeL, homeR;
        private bool hasHome;
        private Vector2 shoulderMid0;
        private float shoulderWidth0 = 0.2f;
        private float shoulderTilt0;
        private float rollNeutralL, rollNeutralR;
        private bool calibrating;
        private float calibrationStartTime;
        private int baselineSamples;
        private Vector2 accHomeL, accHomeR, accMid;
        private float accWidth, accTilt, accRollL, accRollR;
        private ReachEnvelope calibManip;
        private ReachEnvelope calibPanel;

        private Vector2 smoothL, smoothR;
        private bool smoothInit;
        private bool gripStateL, gripStateR;
        private Vector2 lastManipOffset;

        public override string DisplayName { get { return "Webcam (MediaPipe)"; } }

        public override bool IsAvailable
        {
            get { return Time.unscaledTime - lastPacketTime < timeout && packet != null && packet.pose == 1; }
        }

        public bool IsReceiving { get { return Time.unscaledTime - lastPacketTime < timeout; } }

        private void OnEnable()
        {
            StartListening();
        }

        private void OnDisable()
        {
            StopListening();
        }

        private void StartListening()
        {
            if (running) return;
            try
            {
                client = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
                running = true;
                thread = new Thread(ReceiveLoop) { IsBackground = true, Name = "CatSquadPoseUDP" };
                thread.Start();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Webcam] No se pudo abrir el puerto UDP " + port + ": " + e.Message);
                running = false;
            }
        }

        private void StopListening()
        {
            running = false;
            try { if (client != null) client.Close(); } catch (Exception) { }
            client = null;
            thread = null;
        }

        private void ReceiveLoop()
        {
            IPEndPoint any = new IPEndPoint(IPAddress.Any, 0);
            while (running)
            {
                try
                {
                    byte[] data = client.Receive(ref any);
                    string json = System.Text.Encoding.UTF8.GetString(data);
                    lock (packetLock) { latestJson = json; }
                }
                catch (Exception)
                {
                    if (!running) break;
                }
            }
        }

        private void Update()
        {
            string json = null;
            lock (packetLock)
            {
                json = latestJson;
                latestJson = null;
            }

            if (json != null)
            {
                try
                {
                    packet = JsonUtility.FromJson<PosePacket>(json);
                    lastPacketTime = Time.unscaledTime;
                    packetsThisSecond++;
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[Webcam] Paquete inválido: " + e.Message);
                }
            }

            if (Time.unscaledTime - packetCounterTime >= 1f)
            {
                PacketsPerSecond = packetsThisSecond;
                packetsThisSecond = 0;
                packetCounterTime = Time.unscaledTime;
            }
        }

        // ------------------------------------------------------------------ calibración

        public override void BeginCalibration()
        {
            calibrating = true;
            calibrationStartTime = Time.unscaledTime;
            baselineSamples = 0;
            accHomeL = accHomeR = accMid = Vector2.zero;
            accWidth = accTilt = accRollL = accRollR = 0f;
            calibManip = new ReachEnvelope(0f);
            calibPanel = new ReachEnvelope(0f);
        }

        public override void EndCalibration()
        {
            if (!calibrating) return;
            calibrating = false;

            if (calibManip != null && calibManip.Average() > 0.05f)
            {
                calibManip.FillGaps(0.35f);
                calibration.manipEnvelope = calibManip;
            }
            if (calibPanel != null && calibPanel.Average() > 0.05f)
            {
                calibPanel.FillGaps(0.3f);
                calibration.panelEnvelope = calibPanel;
            }
            calibration.valid = true;
        }

        public bool IsCalibrating { get { return calibrating; } }

        // ------------------------------------------------------------------ lectura

        private static bool Valid(float[] p, float minVisibility = 0.4f)
        {
            return p != null && p.Length >= 3 && p[2] >= minVisibility;
        }

        private static Vector2 V(float[] p)
        {
            // espejo normalizado (y hacia abajo) -> juego (y hacia arriba)
            return new Vector2(p[0], -p[1]);
        }

        public override void Read(ref BimanualFrame frame, InputContext context, float dt)
        {
            frame.fromWebcam = true;
            if (packet == null || packet.pose != 1 || !Valid(packet.ls) || !Valid(packet.rs))
            {
                frame.panel.tracked = false;
                frame.manip.tracked = false;
                return;
            }

            Vector2 ls = V(packet.ls), rs = V(packet.rs);
            float sw = Mathf.Max(0.02f, Vector2.Distance(ls, rs));
            Vector2 mid = (ls + rs) * 0.5f;
            float tilt = Mathf.Atan2(ls.y - rs.y, ls.x - rs.x) * Mathf.Rad2Deg;

            bool hasL = Valid(packet.lw, 0.3f);
            bool hasR = Valid(packet.rw, 0.3f);
            Vector2 wl = hasL ? V(packet.lw) : smoothL;
            Vector2 wr = hasR ? V(packet.rw) : smoothR;

            if (!smoothInit)
            {
                smoothL = wl; smoothR = wr; smoothInit = true;
            }
            float k = 1f - positionSmoothing;
            smoothL = Vector2.Lerp(smoothL, wl, k);
            smoothR = Vector2.Lerp(smoothR, wr, k);

            // ---- línea base (primeros 1.5 s de la calibración: manos en reposo)
            if (calibrating && Time.unscaledTime - calibrationStartTime < 1.5f)
            {
                baselineSamples++;
                accHomeL += smoothL; accHomeR += smoothR; accMid += mid;
                accWidth += sw; accTilt += tilt;
                accRollL += packet.rollL; accRollR += packet.rollR;

                homeL = accHomeL / baselineSamples;
                homeR = accHomeR / baselineSamples;
                shoulderMid0 = accMid / baselineSamples;
                shoulderWidth0 = Mathf.Max(0.02f, accWidth / baselineSamples);
                shoulderTilt0 = accTilt / baselineSamples;
                rollNeutralL = accRollL / baselineSamples;
                rollNeutralR = accRollR / baselineSamples;
                hasHome = true;
            }

            if (!hasHome)
            {
                // Sin calibración aún: se asume reposo = debajo del hombro
                homeL = ls + new Vector2(0f, -1.2f * sw);
                homeR = rs + new Vector2(0f, -1.2f * sw);
                shoulderMid0 = mid;
                shoulderWidth0 = sw;
                shoulderTilt0 = tilt;
                hasHome = true;
            }

            bool panelIsLeft = panelSide == BodySide.Izquierdo;

            // ---- Deltas desde la posición de reposo, en anchos de hombro
            Vector2 dL = (smoothL - homeL) / shoulderWidth0;
            Vector2 dR = (smoothR - homeR) / shoulderWidth0;
            Vector2 dPanel = panelIsLeft ? dL : dR;
            Vector2 dManip = panelIsLeft ? dR : dL;
            bool panelTracked = panelIsLeft ? hasL : hasR;
            bool manipTracked = panelIsLeft ? hasR : hasL;

            // ---- Mano manipuladora
            if (calibrating && calibManip != null) calibManip.RecordMax(dManip);
            ReachEnvelope manipEnv = calibrating ? CalibrationEnvelope(1.4f) : calibration.manipEnvelope;
            if (!calibrating) calibration.manipEnvelope.Observe(dManip, envelopeGrowth * dt * 10f);

            float manipRaw = dManip.magnitude;
            float manipExt = manipRaw / Mathf.Max(0.1f, manipEnv.Get(dManip));
            Vector2 manipOffset = manipRaw > 1e-4f ? dManip / manipRaw * Mathf.Min(manipExt, 1.15f) : Vector2.zero;

            frame.manip.tracked = manipTracked;
            frame.manip.rawExtension = manipRaw;
            frame.manip.extension = manipExt;
            frame.manip.offset = manipOffset;
            frame.manip.speed = dt > 0f ? (manipOffset - lastManipOffset).magnitude / dt : 0f;
            lastManipOffset = manipOffset;

            bool manipHandSeen = panelIsLeft ? packet.handR == 1 : packet.handL == 1;
            float manipOpen = panelIsLeft ? packet.openR : packet.openL;
            frame.manip.gripKnown = manipHandSeen;
            if (manipHandSeen)
            {
                bool state = panelIsLeft ? gripStateR : gripStateL;
                if (state && manipOpen > gripOpenAbove) state = false;
                else if (!state && manipOpen < gripCloseBelow) state = true;
                if (panelIsLeft) gripStateR = state; else gripStateL = state;
                frame.manip.grip = state;

                float roll = panelIsLeft ? packet.rollR - rollNeutralR : packet.rollL - rollNeutralL;
                frame.manip.wristAngle = Mathf.DeltaAngle(0f, roll);
                frame.manip.wristKnown = true;
            }
            else
            {
                frame.manip.grip = false;
                frame.manip.wristKnown = false;
                frame.manip.wristAngle = 0f;
            }

            // ---- Mano asistente (panel): desplazamiento grueso y palanca sostenida
            if (calibrating && calibPanel != null) calibPanel.RecordMax(dPanel);
            ReachEnvelope panelEnv = calibration.panelEnvelope;
            if (!calibrating) panelEnv.Observe(dPanel, envelopeGrowth * dt * 10f);

            float panelRaw = dPanel.magnitude;
            float panelExt = panelRaw / Mathf.Max(0.1f, panelEnv.Get(dPanel));
            Vector2 stick = panelRaw > 1e-4f ? dPanel / panelRaw * Mathf.Clamp01(panelExt) : Vector2.zero;
            if (stick.magnitude < panelDeadZone) stick = Vector2.zero;
            else stick = stick.normalized * Mathf.InverseLerp(panelDeadZone, 1f, stick.magnitude);

            frame.panel.tracked = panelTracked;
            frame.panel.rawExtension = panelRaw;
            frame.panel.extension = panelExt;
            frame.panel.offset = dPanel;
            frame.panel.joystick = stick;
            frame.panel.lever = stick.y >= leverThreshold;
            frame.panel.gripKnown = false;

            // ---- Compensación de tronco
            float shift = (mid - shoulderMid0).magnitude / shoulderWidth0;
            float tiltDelta = Mathf.Abs(Mathf.DeltaAngle(shoulderTilt0, tilt));
            float comp = Mathf.Max(shift / Mathf.Max(0.01f, trunkShiftThreshold),
                                   tiltDelta / Mathf.Max(0.1f, trunkTiltThreshold));
            if (Valid(packet.lh, 0.5f) && Valid(packet.rh, 0.5f))
            {
                Vector2 hipMid = (V(packet.lh) + V(packet.rh)) * 0.5f;
                Vector2 spine = mid - hipMid;
                float lean = Mathf.Abs(Vector2.SignedAngle(Vector2.up, spine));
                comp = Mathf.Max(comp, lean / Mathf.Max(0.1f, trunkTiltThreshold * 1.5f));
            }
            comp *= compensationSensitivity;
            frame.trunkCompensation = calibrating ? 0f : comp;
            frame.compensating = !calibrating && comp >= 1f;
        }

        private ReachEnvelope cachedCalibEnvelope;
        private ReachEnvelope CalibrationEnvelope(float value)
        {
            if (cachedCalibEnvelope == null) cachedCalibEnvelope = new ReachEnvelope(value);
            return cachedCalibEnvelope;
        }
    }
}
