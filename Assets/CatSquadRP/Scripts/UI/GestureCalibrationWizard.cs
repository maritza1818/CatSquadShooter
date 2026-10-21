using UnityEngine;

namespace CatSquad.RescateProfundo
{
    /// <summary>
    /// Calibración POR GESTO (webcam, primera persona). El terapeuta la abre desde F1.
    /// Paso a paso, el niño muestra cada gesto: 3 s para acomodarse y 2 s de grabación. Se guarda la
    /// forma de SU mano como plantilla (las manos con parálisis cerebral no hacen los gestos "de libro",
    /// así que el juego aprende cómo los hace cada niño). El juego queda en pausa mientras tanto.
    /// </summary>
    public class GestureCalibrationWizard : MonoBehaviour
    {
        private enum Who { Ambas, Panel, Brazo }
        private enum Stage { Preparar, Grabar, Resultado, Fin }

        private struct Step
        {
            public string title;
            public string text;
            public HandGesture gesture;
            public string special;
            public Who who;
            public Step(string title, string text, HandGesture gesture, string special, Who who)
            {
                this.title = title; this.text = text; this.gesture = gesture; this.special = special; this.who = who;
            }
        }

        private static readonly Step[] Steps =
        {
            new Step("Manos en reposo", "Apoya AMBAS manos relajadas, con la palma hacia abajo, como sobre una mesa.", HandGesture.Reposo, null, Who.Ambas),
            new Step("Pulgares arriba", "Sin mover las manos, levanta AMBOS pulgares.", HandGesture.ReposoPulgar, null, Who.Ambas),
            new Step("Mano abierta (bajar)", "Mano del PANEL: palma abajo y ábrela bien, con los dedos estirados.", HandGesture.Abierta, null, Who.Panel),
            new Step("Mano de canto (avanzar)", "Mano del PANEL: ponla de canto, como un \"like\" pero con el pulgar guardado.", HandGesture.Vertical, null, Who.Panel),
            new Step("De canto con el pulgar (retroceder)", "Mano del PANEL: igual que antes, pero ahora con el pulgar ARRIBA.", HandGesture.VerticalPulgar, null, Who.Panel),
            new Step("Puño (pinza cerrada)", "Mano del BRAZO: cierra la mano como si agarraras algo.", HandGesture.Desconocido, "puno", Who.Brazo),
            new Step("Mano abierta (pinza abierta)", "Mano del BRAZO: abre la mano.", HandGesture.Desconocido, "abierta", Who.Brazo),
            new Step("Manos juntas (panel)", "Junta ambas manos al frente, como para aplaudir despacio.", HandGesture.Desconocido, "juntas", Who.Ambas),
            new Step("Meñique (opcional)", "Mano del PANEL: levanta solo el meñique. Si cuesta, pulsa \"Saltar\".", HandGesture.Menique, null, Who.Panel),
        };

        public float prepareSeconds = 3f;
        public float captureSeconds = 2f;

        public bool Running { get; private set; }

        private int index;
        private Stage stage;
        private float timer;
        private bool lastOk;
        private int saved;
        private float previousTimeScale = 1f;
        private GUIStyle big, mid, small;

        private static WebcamPoseInputProvider Webcam
        {
            get { return BimanualInput.Instance != null ? BimanualInput.Instance.webcam : null; }
        }

        public void Begin()
        {
            if (Webcam == null) return;
            Running = true;
            index = 0;
            saved = 0;
            previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            StartStep();
        }

        private void StartStep()
        {
            stage = Stage.Preparar;
            timer = prepareSeconds;
        }

        private void Finish()
        {
            stage = Stage.Fin;
            timer = 3f;
            if (GameManager.Instance != null) GameManager.Instance.Save();
        }

        private void Close()
        {
            if (Webcam != null) Webcam.CancelGestureCapture();
            Running = false;
            Time.timeScale = previousTimeScale <= 0f ? 1f : previousTimeScale;
        }

        private BodySide PanelSide { get { return BimanualInput.Instance != null ? BimanualInput.Instance.PanelSide : BodySide.Izquierdo; } }
        private BodySide ManipSide { get { return BimanualInput.Instance != null ? BimanualInput.Instance.ManipSide : BodySide.Derecho; } }

        private void Update()
        {
            if (!Running) return;
            WebcamPoseInputProvider cam = Webcam;
            if (cam == null) { Close(); return; }
            float dt = Time.unscaledDeltaTime;
            timer -= dt;
            Step s = Steps[Mathf.Min(index, Steps.Length - 1)];

            switch (stage)
            {
                case Stage.Preparar:
                    if (timer <= 0f)
                    {
                        stage = Stage.Grabar;
                        timer = captureSeconds;
                        if (s.special != null) cam.BeginSpecialCapture(s.special, s.who == Who.Brazo ? ManipSide : PanelSide);
                        else if (s.who == Who.Ambas) cam.BeginGestureCapture(s.gesture, BodySide.Izquierdo, BodySide.Derecho);
                        else cam.BeginGestureCapture(s.gesture, s.who == Who.Panel ? PanelSide : ManipSide);
                    }
                    break;
                case Stage.Grabar:
                    if (timer <= 0f)
                    {
                        lastOk = cam.EndGestureCapture();
                        if (lastOk) saved++;
                        stage = Stage.Resultado;
                        timer = 1.4f;
                    }
                    break;
                case Stage.Resultado:
                    if (timer <= 0f)
                    {
                        if (!lastOk) { StartStep(); break; }       // no se vio la mano: repetir el paso
                        index++;
                        if (index >= Steps.Length) Finish(); else StartStep();
                    }
                    break;
                case Stage.Fin:
                    if (timer <= 0f) Close();
                    break;
            }
        }

        private void OnGUI()
        {
            if (!Running) return;
            float scale = Mathf.Max(1f, Screen.height / 1080f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float w = Screen.width / scale, h = Screen.height / scale;
            if (big == null)
            {
                big = new GUIStyle(GUI.skin.label) { fontSize = 44, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = true };
                mid = new GUIStyle(GUI.skin.label) { fontSize = 30, alignment = TextAnchor.MiddleCenter, wordWrap = true };
                small = new GUIStyle(GUI.skin.label) { fontSize = 22, alignment = TextAnchor.MiddleCenter, wordWrap = true };
            }

            Rect box = new Rect(w * 0.5f - 520f, h * 0.5f - 300f, 1040f, 600f);
            GUI.Box(box, "");
            GUI.Box(box, "");
            GUILayout.BeginArea(new Rect(box.x + 30f, box.y + 20f, box.width - 60f, box.height - 40f));
            WebcamPoseInputProvider cam = Webcam;

            if (stage == Stage.Fin)
            {
                GUILayout.Label("¡Listo!", big, GUILayout.Height(120f));
                GUILayout.Label(saved + " gestos guardados para este niño.", mid, GUILayout.Height(80f));
                GUILayout.Label("Se pueden repetir cuando se quiera desde F1.", small);
            }
            else
            {
                Step s = Steps[index];
                string hand = s.who == Who.Ambas ? "AMBAS MANOS"
                    : (s.who == Who.Panel ? "mano del PANEL (" + Side(PanelSide) + ")" : "mano del BRAZO (" + Side(ManipSide) + ")");
                GUILayout.Label("Gesto " + (index + 1) + " de " + Steps.Length + "  ·  " + hand, small, GUILayout.Height(40f));
                GUILayout.Label(s.title, big, GUILayout.Height(90f));
                GUILayout.Label(s.text, mid, GUILayout.Height(110f));

                string status;
                if (stage == Stage.Preparar) status = "Prepárate... " + Mathf.CeilToInt(Mathf.Max(0f, timer));
                else if (stage == Stage.Grabar) status = "¡Quieto así! Grabando... (" + (cam != null ? cam.CaptureSamples : 0) + " muestras)";
                else status = lastOk ? "¡Guardado!" : "No se vio bien la mano. Repitamos.";
                GUILayout.Label(status, big, GUILayout.Height(90f));

                if (cam != null)
                {
                    GUILayout.Label("Ahora veo → izquierda: " + GestureRecognizer.Name(cam.GestureLeft) +
                                    "   ·   derecha: " + GestureRecognizer.Name(cam.GestureRight) +
                                    (cam.HandsDistance >= 0f ? "   ·   manos a " + cam.HandsDistance.ToString("F2") : ""), small, GUILayout.Height(40f));
                }

                GUILayout.FlexibleSpace();
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Repetir", GUILayout.Height(50f))) { cam.CancelGestureCapture(); StartStep(); }
                if (GUILayout.Button("Saltar", GUILayout.Height(50f)))
                {
                    cam.CancelGestureCapture();
                    index++;
                    if (index >= Steps.Length) Finish(); else StartStep();
                }
                if (GUILayout.Button("Cancelar", GUILayout.Height(50f))) Close();
                GUILayout.EndHorizontal();
            }
            GUILayout.EndArea();
        }

        private static string Side(BodySide s) { return s == BodySide.Izquierdo ? "izquierda" : "derecha"; }
    }
}
