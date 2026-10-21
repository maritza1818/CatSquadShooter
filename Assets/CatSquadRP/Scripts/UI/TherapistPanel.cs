using System.Globalization;
using UnityEngine;

namespace CatSquad.RescateProfundo
{
    /// <summary>
    /// Panel del terapeuta (F1). Configura la sesión sin tocar el editor:
    /// duración (oxígeno), lado afectado, inversión de roles, modo de entrada,
    /// exigencia bimanual al verter y sensibilidad de compensación.
    /// </summary>
    public class TherapistPanel : MonoBehaviour
    {
        public KeyCode toggleKey = KeyCode.F1;
        public bool visible;

        private Rect window = new Rect(20f, 60f, 470f, 900f);
        private GUIStyle title;
        private Vector2 scroll;

        private void Update()
        {
            if (Input.GetKeyDown(toggleKey))
            {
                visible = !visible;
                if (!visible) Apply();
            }
        }

        private void Apply()
        {
            GameManager gm = GameManager.Instance;
            if (gm == null) return;
            gm.ApplySettings();
            gm.Save();
        }

        private void OnGUI()
        {
            if (!visible || GameManager.Instance == null) return;
            float scale = Mathf.Max(1f, Screen.height / 1080f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            // En pantallas bajas (o la vista Game del editor) la ventana se achica y se desplaza con la rueda
            float maxH = Mathf.Max(300f, Screen.height / scale - window.y - 20f);
            window.height = Mathf.Min(900f, maxH);
            window = GUILayout.Window(4711, window, DrawWindow, "Panel del terapeuta (F1 para cerrar)",
                                      GUILayout.Width(window.width), GUILayout.Height(window.height));
        }

        private void DrawWindow(int id)
        {
            GameManager gm = GameManager.Instance;
            GameSettings s = gm.Settings;
            CultureInfo ci = CultureInfo.InvariantCulture;
            if (title == null)
            {
                title = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
            }

            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.Label("Sesión", title);
            GUILayout.Label("Duración (oxígeno): " + s.sessionMinutes.ToString("F0", ci) + " min  (recomendado 15–20)");
            s.sessionMinutes = Mathf.Round(GUILayout.HorizontalSlider(s.sessionMinutes, 1f, 30f));
            GUILayout.Label("Se aplica al iniciar la próxima sesión.");

            GUILayout.Space(6);
            GUILayout.Label("Entrada", title);
            bool webcam = GUILayout.Toggle(s.inputMode == InputMode.Webcam, " Usar webcam (puente Python/MediaPipe)");
            s.inputMode = webcam ? InputMode.Webcam : InputMode.TecladoMouse;
            if (BimanualInput.Instance != null) GUILayout.Label("Estado: " + BimanualInput.Instance.StatusText());

            // Etapa 4: gestos de la webcam (primera persona)
            WebcamPoseInputProvider cam = BimanualInput.Instance != null ? BimanualInput.Instance.webcam : null;
            if (GameManager.FirstPerson && cam != null)
            {
                GUILayout.Label("Gestos: " + (cam.calibration.HasGestures ? cam.calibration.gestures.Count + " plantillas del niño" : "plantillas por defecto (conviene calibrar)"));
                if (cam.IsReceiving)
                {
                    GUILayout.Label("Izq.: " + GestureRecognizer.Name(cam.GestureLeft) + " (" + cam.GestureDistanceLeft.ToString("F2", ci) + ")   ·   Der.: "
                                    + GestureRecognizer.Name(cam.GestureRight) + " (" + cam.GestureDistanceRight.ToString("F2", ci) + ")");
                    BimanualFrame fr = BimanualInput.Instance.Current;
                    GUILayout.Label("Timón: " + fr.helm.mode + "  avance " + fr.helm.throttle.ToString("F1", ci) + "  giro " + fr.helm.turn.ToString("F1", ci)
                                    + "  vertical " + fr.helm.vertical.ToString("F1", ci)
                                    + (cam.HandsDistance >= 0f ? "  ·  manos a " + cam.HandsDistance.ToString("F2", ci) : ""));
                }
                GUILayout.BeginHorizontal();
                GUI.enabled = cam.IsReceiving;
                if (GUILayout.Button("Calibrar gestos (webcam)"))
                {
                    GestureCalibrationWizard w = GetComponent<GestureCalibrationWizard>();
                    if (w == null) w = gameObject.AddComponent<GestureCalibrationWizard>();
                    Apply();
                    visible = false;
                    w.Begin();
                }
                GUI.enabled = true;
                if (GUILayout.Button("Borrar gestos")) { cam.ResetGestureCalibration(); gm.Save(); }
                GUILayout.EndHorizontal();
                s.pinkyHint = GUILayout.Toggle(s.pinkyHint, " Meñique arriba (mano del panel) = pedir una pista");
            }

            GUILayout.Space(6);
            GUILayout.Label("Roles bimanuales", title);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Lado afectado:");
            if (GUILayout.Toggle(s.affectedSide == BodySide.Izquierdo, " Izquierdo")) s.affectedSide = BodySide.Izquierdo;
            if (GUILayout.Toggle(s.affectedSide == BodySide.Derecho, " Derecho")) s.affectedSide = BodySide.Derecho;
            GUILayout.EndHorizontal();
            s.invertRoles = GUILayout.Toggle(s.invertRoles, " Invertir roles (el lado afectado opera el BRAZO)");
            if (BimanualInput.Instance != null)
            {
                GUILayout.Label("Panel: " + BimanualInput.Instance.PanelSide + "   ·   Brazo: " + BimanualInput.Instance.ManipSide);
            }
            s.requireLeverWhilePouring = GUILayout.Toggle(s.requireLeverWhilePouring, " Verter exige sostener la palanca (bimanual simultáneo)");

            if (GameManager.FirstPerson)
            {
                GUILayout.Space(6);
                GUILayout.Label("Conducción (primera persona)", title);
                GUILayout.BeginHorizontal();
                if (GUILayout.Toggle(s.driveMode == DriveMode.Manual, " Manual (palanca)")) s.driveMode = DriveMode.Manual;
                if (GUILayout.Toggle(s.driveMode == DriveMode.Automatico, " Automático (rieles)")) s.driveMode = DriveMode.Automatico;
                GUILayout.EndHorizontal();
                GUILayout.Label(s.autoAssistAfterSeconds > 0.5f
                    ? "Ayuda del riel tras " + s.autoAssistAfterSeconds.ToString("F0", ci) + " s con la palanca quieta (solo en manual)"
                    : "Ayuda del riel: desactivada");
                s.autoAssistAfterSeconds = Mathf.Round(GUILayout.HorizontalSlider(s.autoAssistAfterSeconds, 0f, 30f));
                GUILayout.Label("Velocidad por el riel: " + s.railSpeed.ToString("F1", ci) + " m/s  (manual ≈ 4.2)");
                s.railSpeed = Mathf.Round(GUILayout.HorizontalSlider(s.railSpeed, 0.8f, 3f) * 10f) / 10f;
                GUILayout.Label("Los cascabeles secretos solo se encuentran manejando. Tecla R: cambiar de modo.");

                GUILayout.Space(6);
                GUILayout.Label("Panel de cabina (pulgares)", title);
                s.invertPanelThumbs = GUILayout.Toggle(s.invertPanelThumbs, " Invertir pulgares (el izquierdo mueve a la derecha)");
                s.allowChildAutoToggle = GUILayout.Toggle(s.allowChildAutoToggle, " El niño puede activar el modo automático desde el panel");
                s.panelDwellConfirm = GUILayout.Toggle(s.panelDwellConfirm, " Confirmar también quedándose quieto en un botón");
                if (s.panelDwellConfirm)
                {
                    GUILayout.Label("Espera para confirmar: " + s.panelDwellSeconds.ToString("F1", ci) + " s");
                    s.panelDwellSeconds = Mathf.Round(GUILayout.HorizontalSlider(s.panelDwellSeconds, 1f, 5f) * 2f) / 2f;
                }
                GUILayout.Space(6);
                GUILayout.Label("Lanzador (Lab 05: trayectoria)", title);
                GUILayout.BeginHorizontal();
                GUILayout.Label("Ayuda:");
                if (GUILayout.Toggle(s.launchAssist == LaunchAssist.Ninguna, " Ninguna")) s.launchAssist = LaunchAssist.Ninguna;
                if (GUILayout.Toggle(s.launchAssist == LaunchAssist.Guia, " Arco guía")) s.launchAssist = LaunchAssist.Guia;
                if (GUILayout.Toggle(s.launchAssist == LaunchAssist.Iman, " Imán")) s.launchAssist = LaunchAssist.Iman;
                if (GUILayout.Toggle(s.launchAssist == LaunchAssist.Automatica, " Automática")) s.launchAssist = LaunchAssist.Automatica;
                GUILayout.EndHorizontal();
                s.launchNeedsLever = GUILayout.Toggle(s.launchNeedsLever, " Exigir la palanca (mano del panel arriba) mientras tira");
                GUILayout.Label("Tirón para la fuerza máxima: " + (s.launchPullForMax * 100f).ToString("F0", ci) + "% del alcance calibrado");
                s.launchPullForMax = Mathf.Round(GUILayout.HorizontalSlider(s.launchPullForMax, 0.2f, 1f) * 20f) / 20f;
                ProjectileLauncher pl = ProjectileLauncher.Instance;
                if (pl != null) GUILayout.Label("Hoy: " + pl.Hits + " aciertos de " + pl.Launches + " tiros  ·  G abre/cierra el lanzador");

                if (gm.submarine != null && RailPilot.Instance != null && RailPilot.Instance.Active)
                {
                    GUILayout.Label("Riel: " + RailPilot.Instance.PromptText);
                }
            }

            GUILayout.Space(6);
            GUILayout.Label("Compensación de tronco", title);
            s.compensationEnabled = GUILayout.Toggle(s.compensationEnabled, " Activa (el objeto resbala si inclina el tronco)");
            GUILayout.Label("Sensibilidad: " + s.compensationSensitivity.ToString("F2", ci));
            s.compensationSensitivity = GUILayout.HorizontalSlider(s.compensationSensitivity, 0.5f, 2f);

            GUILayout.Space(6);
            GUILayout.Label("Otros", title);
            s.skipTutorial = GUILayout.Toggle(s.skipTutorial, " Saltar tutorial");
            s.showControlsHelp = GUILayout.Toggle(s.showControlsHelp, " Mostrar ayuda de controles");

            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Aplicar")) Apply();
            if (GUILayout.Button("Terminar sesión"))
            {
                Apply();
                if (gm.session != null) gm.session.EndSession();
                visible = false;
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("+50 chatarra (prueba)")) gm.AddMaterials(50);
            if (GUILayout.Button("Reiniciar progreso"))
            {
                gm.ResetProgress();
                if (gm.session != null) gm.session.RestartScene();
            }
            GUILayout.EndHorizontal();
            if (GUILayout.Button("Abrir carpeta de métricas"))
            {
                string folder = System.IO.Path.Combine(Application.persistentDataPath, "CatSquadSesiones");
                System.IO.Directory.CreateDirectory(folder);
                Application.OpenURL("file://" + folder);
            }

            GUILayout.EndScrollView();
            GUI.DragWindow();
        }
    }
}
