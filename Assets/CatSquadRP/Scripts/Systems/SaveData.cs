using System.Collections.Generic;

namespace CatSquad.RescateProfundo
{
    public enum UpgradeKind { Autonomia = 0, Iluminacion = 1, Profundidad = 2 }

    /// <summary>Lateral25D = versión del Avance 1. PrimeraPersona = cabina 3D con mapa abatible.</summary>
    public enum ViewMode { Lateral25D = 0, PrimeraPersona = 1 }

    /// <summary>
    /// Cómo se desplaza el submarino en primera persona.
    /// Manual: la palanca (con ayuda del riel si queda quieta). Automático: el riel lleva al submarino.
    /// </summary>
    public enum DriveMode { Manual = 0, Automatico = 1 }

    /// <summary>Configuración que ajusta el terapeuta (panel F1).</summary>
    [System.Serializable]
    public class GameSettings
    {
        public float sessionMinutes = 15f;
        public InputMode inputMode = InputMode.TecladoMouse;
        public BodySide affectedSide = BodySide.Izquierdo;
        public bool invertRoles = false;
        public bool requireLeverWhilePouring = true;
        public float compensationSensitivity = 1f;
        public bool compensationEnabled = true;
        public bool showControlsHelp = true;
        public bool skipTutorial = false;
        public DriveMode driveMode = DriveMode.Manual;
        [UnityEngine.Tooltip("Segundos sin tocar la palanca en modo manual antes de que el riel ayude")]
        public float autoAssistAfterSeconds = 8f;
        [UnityEngine.Tooltip("Velocidad del submarino por el riel (m/s); la manual es ~4.2")]
        public float railSpeed = 1.6f;
        public bool invertPanelThumbs = false;
        [UnityEngine.Tooltip("Panel de cabina: además de 'ambos pulgares', confirmar al quedarse quieto en un botón")]
        public bool panelDwellConfirm = false;
        public float panelDwellSeconds = 2.5f;
        [UnityEngine.Tooltip("El niño puede cambiar a modo automático desde el panel de la cabina")]
        public bool allowChildAutoToggle = true;
        [UnityEngine.Tooltip("Webcam: levantar solo el meñique de la mano del panel pide una pista a la guía")]
        public bool pinkyHint = false;
        [UnityEngine.Tooltip("Lanzador (Lab 05): ayuda de puntería")]
        public LaunchAssist launchAssist = LaunchAssist.Guia;
        [UnityEngine.Tooltip("Lanzador: exige sostener la palanca con la mano del panel mientras se tira")]
        public bool launchNeedsLever = false;
        [UnityEngine.Tooltip("Lanzador: tirón (fracción del alcance calibrado) para la fuerza máxima")]
        public float launchPullForMax = 0.55f;
    }

    [System.Serializable]
    public class PumpState
    {
        public string id;
        public float fuel;
    }

    /// <summary>Progreso persistente. Se guarda como JSON en Application.persistentDataPath.</summary>
    [System.Serializable]
    public class SaveData
    {
        public int version = 1;
        public int materials = 0;
        public int totalMaterialsCollected = 0;
        public int[] upgrades = new int[3];
        public bool tutorialDone = false;
        public bool phase2Announced = false;
        public int sessionsPlayed = 0;
        public List<PumpState> pumps = new List<PumpState>();
        public List<string> rescuedKittens = new List<string>();
        public List<string> secretsFound = new List<string>();
        [UnityEngine.Tooltip("Ciudad: zona (barrio) de la última balsa; la sesión siguiente empieza ahí")]
        public string lastDistrict = "";
        public GameSettings settings = new GameSettings();
        public WebcamCalibrationData webcamCalibration = new WebcamCalibrationData();
    }
}
