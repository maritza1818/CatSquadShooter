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

        private Rect window = new Rect(20f, 170f, 470f, 610f);
        private GUIStyle title;

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
            window = GUILayout.Window(4711, window, DrawWindow, "Panel del terapeuta (F1 para cerrar)");
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

            GUILayout.Label("Sesión", title);
            GUILayout.Label("Duración (oxígeno): " + s.sessionMinutes.ToString("F0", ci) + " min  (recomendado 15–20)");
            s.sessionMinutes = Mathf.Round(GUILayout.HorizontalSlider(s.sessionMinutes, 1f, 30f));
            GUILayout.Label("Se aplica al iniciar la próxima sesión.");

            GUILayout.Space(6);
            GUILayout.Label("Entrada", title);
            bool webcam = GUILayout.Toggle(s.inputMode == InputMode.Webcam, " Usar webcam (puente Python/MediaPipe)");
            s.inputMode = webcam ? InputMode.Webcam : InputMode.TecladoMouse;
            if (BimanualInput.Instance != null) GUILayout.Label("Estado: " + BimanualInput.Instance.StatusText());

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

            GUI.DragWindow();
        }
    }
}
