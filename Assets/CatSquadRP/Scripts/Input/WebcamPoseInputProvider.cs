using System;
using System.Collections.Generic;
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
        // Forma de la mano en 3D (etapa 4: gestos). Ver hand_shape() en el puente Python.
        public float[] nL, nR;       // normal que sale de la palma (x espejada, y hacia abajo)
        public float[] extL, extR;   // extensión de cada dedo [pulgar, índice, medio, anular, meñique]
        public float open3L = 1f, open3R = 1f;   // apertura 3D (0 puño, 1 abierta)
        public float tliftL, tliftR; // pulgar separado de la palma hacia el dorso
        public float tupL, tupR;     // punta del pulgar más arriba que su base
        public float[] hcL, hcR;     // centro de cada mano (espejo, normalizado) para "juntar las manos"
    }

    [Serializable]
    public class WebcamCalibrationData
    {
        public bool valid;
        public ReachEnvelope manipEnvelope = new ReachEnvelope(1.2f);
        public ReachEnvelope panelEnvelope = new ReachEnvelope(0.45f);

        // ---- Etapa 4: calibración POR GESTO (plantillas propias de cada niño)
        public List<GestureTemplate> gestures = new List<GestureTemplate>();
        [Tooltip("Distancia entre manos (en anchos de hombro) por debajo de la cual cuentan como 'juntas'")]
        public float handsTogether = 0.45f;
        [Tooltip("Apertura 3D de la mano del brazo: por debajo cierra la pinza, por encima la abre")]
        public float gripClose3 = 0.2f;
        public float gripOpen3 = 0.4f;

        public bool HasGestures { get { return gestures != null && gestures.Count > 0; } }
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

        // ---- gestos (etapa 4)
        private readonly GestureRecognizer recognizer = new GestureRecognizer();
        private bool togetherArmed = true;
        private float togetherTime;
        private HandGesture capture = HandGesture.Desconocido;
        private string captureSpecial;
        private BodySide[] captureSides = new BodySide[0];
        private readonly float[][] captureSum = { new float[GestureFeatures.Count], new float[GestureFeatures.Count] };
        private readonly int[] captureCount = new int[2];
        private float captureScalarSum;
        private int captureScalarCount;

        public HandGesture GestureLeft { get; private set; }
        public HandGesture GestureRight { get; private set; }
        public float GestureDistanceLeft { get { return recognizer.Distance(BodySide.Izquierdo); } }
        public float GestureDistanceRight { get { return recognizer.Distance(BodySide.Derecho); } }
        /// <summary>Distancia entre las manos en anchos de hombro (o -1 si no se ven ambas).</summary>
        public float HandsDistance { get; private set; }
        public bool IsCapturing { get { return capture != HandGesture.Desconocido || captureSpecial != null; } }

        /// <summary>Carga las plantillas guardadas (o las de por defecto).</summary>
        public void ApplyGestureCalibration()
        {
            recognizer.SetTemplates(calibration != null ? calibration.gestures : null);
        }
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
            // Con la forma 3D (etapa 4) la apertura no se confunde cuando los dedos apuntan a la cámara
            bool has3D = (panelIsLeft ? packet.nR : packet.nL) != null;
            float closeBelow = has3D ? calibration.gripClose3 : gripCloseBelow;
            float openAbove = has3D ? calibration.gripOpen3 : gripOpenAbove;
            if (has3D) manipOpen = panelIsLeft ? packet.open3R : packet.open3L;
            frame.manip.gripKnown = manipHandSeen;
            if (manipHandSeen)
            {
                bool state = panelIsLeft ? gripStateR : gripStateL;
                if (state && manipOpen > openAbove) state = false;
                else if (!state && manipOpen < closeBelow) state = true;
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

            // ---- Gestos de cada mano (etapa 4)
            GestureFeatures fL = GestureFeatures.From(packet.nL, packet.open3L, packet.tliftL, packet.tupL, packet.extL);
            GestureFeatures fR = GestureFeatures.From(packet.nR, packet.open3R, packet.tliftR, packet.tupR, packet.extR);
            if (packet.handL != 1) fL.valid = false;
            if (packet.handR != 1) fR.valid = false;
            GestureLeft = recognizer.Update(BodySide.Izquierdo, fL, dt);
            GestureRight = recognizer.Update(BodySide.Derecho, fR, dt);
            RecordCapture(fL, fR);

            HandsDistance = -1f;
            if (packet.handL == 1 && packet.handR == 1 && packet.hcL != null && packet.hcR != null && packet.hcL.Length >= 2 && packet.hcR.Length >= 2)
            {
                HandsDistance = new Vector2(packet.hcL[0] - packet.hcR[0], packet.hcL[1] - packet.hcR[1]).magnitude / shoulderWidth0;
            }
            else if (hasL && hasR)
            {
                HandsDistance = (smoothL - smoothR).magnitude / shoulderWidth0;
            }
            RecordScalarCapture();

            HandGesture gPanel = panelIsLeft ? GestureLeft : GestureRight;
            bool panelHandSeen = panelIsLeft ? packet.handL == 1 : packet.handR == 1;
            bool together = HandsDistance >= 0f && HandsDistance < calibration.handsTogether;

            // ---- Juntar las manos = abrir/cerrar el panel de la cabina
            if (together) togetherTime += dt; else togetherTime = 0f;
            if (togetherArmed && togetherTime >= 0.4f && !calibrating && !IsCapturing)
            {
                frame.panelToggle = true;
                togetherArmed = false;
            }
            if (!together && HandsDistance > calibration.handsTogether * 1.5f) togetherArmed = true;

            // ---- Timón con la mano del panel (primera persona):
            //   palma abajo relajada = estacionario: a los lados gira, subir la mano sube, ABRIR la mano baja
            //   mano de canto ("like" sin pulgar) = avanza mientras se mantiene; con el pulgar arriba retrocede
            frame.helm.tracked = panelTracked;
            frame.helm.turn = stick.x;
            frame.helm.vertical = Mathf.Max(0f, stick.y);
            frame.helm.throttle = 0f;
            frame.helm.mode = HelmMode.Estacionaria;
            if (panelHandSeen)
            {
                switch (gPanel)
                {
                    case HandGesture.Vertical:
                        frame.helm.mode = HelmMode.Avance;
                        frame.helm.throttle = 1f;
                        break;
                    case HandGesture.VerticalPulgar:
                        frame.helm.mode = HelmMode.Avance;
                        frame.helm.throttle = -1f;
                        break;
                    case HandGesture.Abierta:
                        frame.helm.mode = HelmMode.Suelta;
                        frame.helm.vertical = -1f;
                        break;
                }
            }
            else
            {
                // sin la mano detectada: la posición de la muñeca sigue sirviendo (bajar la mano = bajar)
                frame.helm.vertical = stick.y;
            }
            if (together || HandsDistance >= 0f && HandsDistance < calibration.handsTogether * 1.5f)
            {
                // juntando las manos no se maneja (las manos pasan por el centro)
                frame.helm.turn = 0f;
                frame.helm.throttle = 0f;
                frame.helm.vertical = 0f;
            }

            // ---- Panel con los pulgares: ambas manos en reposo (palma abajo) y un pulgar levantado
            bool readyL = packet.handL == 1 && (GestureLeft == HandGesture.Reposo || GestureLeft == HandGesture.ReposoPulgar);
            bool readyR = packet.handR == 1 && (GestureRight == HandGesture.Reposo || GestureRight == HandGesture.ReposoPulgar);
            frame.panelHandsReady = readyL && readyR && !together;
            frame.thumbLeft = GestureLeft == HandGesture.ReposoPulgar;
            frame.thumbRight = GestureRight == HandGesture.ReposoPulgar;
            frame.pinkyPanel = panelHandSeen && gPanel == HandGesture.Menique;

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

        // ------------------------------------------------------------------ calibración por gesto

        /// <summary>Empieza a grabar un gesto (para las manos indicadas). Terminar con EndGestureCapture.</summary>
        public void BeginGestureCapture(HandGesture gesture, params BodySide[] sides)
        {
            capture = gesture;
            captureSpecial = null;
            captureSides = sides ?? new BodySide[0];
            for (int i = 0; i < 2; i++) { Array.Clear(captureSum[i], 0, GestureFeatures.Count); captureCount[i] = 0; }
        }

        /// <summary>Grabaciones especiales: "juntas" (distancia entre manos), "puno"/"abierta" (pinza del brazo).</summary>
        public void BeginSpecialCapture(string what, BodySide side)
        {
            capture = HandGesture.Desconocido;
            captureSpecial = what;
            captureSides = new[] { side };
            captureScalarSum = 0f;
            captureScalarCount = 0;
        }

        private void RecordCapture(GestureFeatures fL, GestureFeatures fR)
        {
            if (capture == HandGesture.Desconocido) return;
            foreach (BodySide side in captureSides)
            {
                GestureFeatures f = side == BodySide.Izquierdo ? fL : fR;
                if (!f.valid) continue;
                int i = (int)side;
                for (int k = 0; k < GestureFeatures.Count; k++) captureSum[i][k] += f.v[k];
                captureCount[i]++;
            }
        }

        private void RecordScalarCapture()
        {
            if (captureSpecial == null || packet == null) return;
            if (captureSpecial == "juntas")
            {
                if (HandsDistance >= 0f) { captureScalarSum += HandsDistance; captureScalarCount++; }
                return;
            }
            BodySide side = captureSides.Length > 0 ? captureSides[0] : BodySide.Derecho;
            bool seen = side == BodySide.Izquierdo ? packet.handL == 1 : packet.handR == 1;
            float[] n = side == BodySide.Izquierdo ? packet.nL : packet.nR;
            if (!seen || n == null) return;
            captureScalarSum += side == BodySide.Izquierdo ? packet.open3L : packet.open3R;
            captureScalarCount++;
        }

        /// <summary>Muestras válidas grabadas hasta ahora (para mostrar si la cámara ve las manos).</summary>
        public int CaptureSamples
        {
            get
            {
                if (captureSpecial != null) return captureScalarCount;
                int n = int.MaxValue;
                foreach (BodySide side in captureSides) n = Mathf.Min(n, captureCount[(int)side]);
                return n == int.MaxValue ? 0 : n;
            }
        }

        /// <summary>Guarda la media como plantilla del niño. Devuelve false si casi no se vio la mano.</summary>
        public bool EndGestureCapture()
        {
            bool ok = true;
            if (captureSpecial != null)
            {
                if (captureScalarCount < 10) ok = false;
                else
                {
                    float mean = captureScalarSum / captureScalarCount;
                    if (captureSpecial == "juntas") calibration.handsTogether = Mathf.Clamp(mean * 1.6f + 0.05f, 0.2f, 1.2f);
                    else if (captureSpecial == "puno") calibration.gripClose3 = mean;
                    else if (captureSpecial == "abierta") calibration.gripOpen3 = mean;
                    if (captureSpecial != "juntas" && calibration.gripOpen3 > calibration.gripClose3 + 0.1f)
                    {
                        // umbrales con histéresis: un tercio y dos tercios entre puño y mano abierta
                        float a = calibration.gripClose3, b = calibration.gripOpen3;
                        calibration.gripClose3 = Mathf.Lerp(a, b, 0.35f);
                        calibration.gripOpen3 = Mathf.Lerp(a, b, 0.65f);
                    }
                }
            }
            else if (capture != HandGesture.Desconocido)
            {
                foreach (BodySide side in captureSides)
                {
                    int i = (int)side;
                    if (captureCount[i] < 10) { ok = false; continue; }
                    GestureTemplate t = new GestureTemplate { gesture = capture, side = side, features = new float[GestureFeatures.Count] };
                    for (int k = 0; k < GestureFeatures.Count; k++) t.features[k] = captureSum[i][k] / captureCount[i];
                    if (calibration.gestures == null) calibration.gestures = new List<GestureTemplate>();
                    calibration.gestures.RemoveAll(g => g.gesture == capture && g.side == side);
                    calibration.gestures.Add(t);
                }
                ApplyGestureCalibration();
            }
            capture = HandGesture.Desconocido;
            captureSpecial = null;
            return ok;
        }

        public void CancelGestureCapture()
        {
            capture = HandGesture.Desconocido;
            captureSpecial = null;
        }

        public void ResetGestureCalibration()
        {
            calibration.gestures = new List<GestureTemplate>();
            calibration.handsTogether = 0.45f;
            calibration.gripClose3 = 0.2f;
            calibration.gripOpen3 = 0.4f;
            ApplyGestureCalibration();
        }

        private ReachEnvelope cachedCalibEnvelope;
        private ReachEnvelope CalibrationEnvelope(float value)
        {
            if (cachedCalibEnvelope == null) cachedCalibEnvelope = new ReachEnvelope(value);
            return cachedCalibEnvelope;
        }
    }
}
