using System.Collections.Generic;

namespace CatSquad.RescateProfundo
{
    public enum UpgradeKind { Autonomia = 0, Iluminacion = 1, Profundidad = 2 }

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
        public GameSettings settings = new GameSettings();
        public WebcamCalibrationData webcamCalibration = new WebcamCalibrationData();
    }
}
