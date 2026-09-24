using System;
using System.IO;
using UnityEngine;

namespace CatSquad.RescateProfundo
{
    /// <summary>
    /// Progreso persistente, mejoras del submarino y referencias globales de la escena.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [Header("Referencias de escena (las asigna el constructor de escena)")]
        public SubmarineController submarine;
        public ManipulatorArm arm;
        public GuideCat guide;
        public BimanualInput input;
        public SessionManager session;
        public SessionMetrics metrics;
        public GameHUD hud;
        public Transform dockPoint;

        [Header("Mejoras")]
        public int[] autonomyCosts = { 8, 20 };
        public int[] lightCosts = { 10, 24 };
        public int[] depthCosts = { 15, 35 };
        [Tooltip("Límite de profundidad (Y mundial) por nivel de mejora de Profundidad")]
        public float[] depthLimitsY = { -59f, -75f, -1000f };
        public float[] speedMultipliers = { 1f, 1.15f, 1.3f };
        public float[] pumpEfficiency = { 1f, 1.3f, 1.6f };
        public float[] headlightRanges = { 9f, 14f, 20f };
        public float[] headlightIntensities = { 4f, 6f, 8f };

        [Header("Depuración")]
        [Tooltip("No guarda ni carga progreso (útil para presentar siempre desde cero)")]
        public bool disablePersistence = false;

        public SaveData Data { get; private set; }
        public GameSettings Settings { get { return Data.settings; } }

        public event Action<int> MaterialsChanged;
        public event Action UpgradesChanged;
        public event Action SettingsChanged;

        private string SavePath
        {
            get { return Path.Combine(Application.persistentDataPath, "catsquad_rescate_profundo.json"); }
        }

        private void Awake()
        {
            Instance = this;
            Load();
        }

        private void Start()
        {
            ApplySettings();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void OnApplicationQuit()
        {
            Save();
        }

        // ------------------------------------------------------------------ guardar / cargar

        public void Load()
        {
            Data = null;
            if (!disablePersistence)
            {
                try
                {
                    if (File.Exists(SavePath))
                    {
                        Data = JsonUtility.FromJson<SaveData>(File.ReadAllText(SavePath));
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[CatSquad] No se pudo leer el guardado: " + e.Message);
                }
            }

            if (Data == null) Data = new SaveData();
            if (Data.upgrades == null || Data.upgrades.Length != 3) Data.upgrades = new int[3];
            if (Data.settings == null) Data.settings = new GameSettings();
            if (Data.pumps == null) Data.pumps = new System.Collections.Generic.List<PumpState>();
            if (Data.rescuedKittens == null) Data.rescuedKittens = new System.Collections.Generic.List<string>();
            if (Data.webcamCalibration == null) Data.webcamCalibration = new WebcamCalibrationData();
        }

        public void Save()
        {
            if (disablePersistence || Data == null) return;
            try
            {
                if (input != null && input.webcam != null)
                {
                    Data.webcamCalibration = input.webcam.calibration;
                }
                File.WriteAllText(SavePath, JsonUtility.ToJson(Data, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[CatSquad] No se pudo guardar: " + e.Message);
            }
        }

        public void ResetProgress()
        {
            GameSettings keep = Data.settings;
            Data = new SaveData();
            Data.settings = keep;
            Save();
        }

        public void ApplySettings()
        {
            if (input != null)
            {
                input.mode = Settings.inputMode;
                input.affectedSide = Settings.affectedSide;
                input.invertRoles = Settings.invertRoles;
                if (input.webcam != null)
                {
                    input.webcam.compensationSensitivity = Settings.compensationSensitivity;
                    if (Data.webcamCalibration != null && Data.webcamCalibration.valid)
                    {
                        input.webcam.calibration = Data.webcamCalibration;
                    }
                }
            }
            if (SettingsChanged != null) SettingsChanged();
        }

        // ------------------------------------------------------------------ materiales

        public int Materials { get { return Data.materials; } }

        public void AddMaterials(int amount)
        {
            Data.materials += amount;
            Data.totalMaterialsCollected += amount;
            if (MaterialsChanged != null) MaterialsChanged(Data.materials);
        }

        // ------------------------------------------------------------------ mejoras

        public int GetUpgrade(UpgradeKind kind)
        {
            return Data.upgrades[(int)kind];
        }

        public int[] CostsFor(UpgradeKind kind)
        {
            switch (kind)
            {
                case UpgradeKind.Autonomia: return autonomyCosts;
                case UpgradeKind.Iluminacion: return lightCosts;
                default: return depthCosts;
            }
        }

        public int MaxLevel(UpgradeKind kind)
        {
            return CostsFor(kind).Length;
        }

        /// <summary>Costo del siguiente nivel, o -1 si ya está al máximo.</summary>
        public int NextCost(UpgradeKind kind)
        {
            int level = GetUpgrade(kind);
            int[] costs = CostsFor(kind);
            return level < costs.Length ? costs[level] : -1;
        }

        public bool TryBuyUpgrade(UpgradeKind kind)
        {
            int cost = NextCost(kind);
            if (cost < 0 || Data.materials < cost) return false;
            Data.materials -= cost;
            Data.upgrades[(int)kind]++;
            Save();
            if (MaterialsChanged != null) MaterialsChanged(Data.materials);
            if (UpgradesChanged != null) UpgradesChanged();
            return true;
        }

        private static float Pick(float[] values, int level)
        {
            if (values == null || values.Length == 0) return 1f;
            return values[Mathf.Clamp(level, 0, values.Length - 1)];
        }

        public float SpeedMultiplier { get { return Pick(speedMultipliers, GetUpgrade(UpgradeKind.Autonomia)); } }
        public float PumpEfficiency { get { return Pick(pumpEfficiency, GetUpgrade(UpgradeKind.Autonomia)); } }
        public float HeadlightRange { get { return Pick(headlightRanges, GetUpgrade(UpgradeKind.Iluminacion)); } }
        public float HeadlightIntensity { get { return Pick(headlightIntensities, GetUpgrade(UpgradeKind.Iluminacion)); } }
        public float DepthLimitY { get { return Pick(depthLimitsY, GetUpgrade(UpgradeKind.Profundidad)); } }

        // ------------------------------------------------------------------ bombas y gatitos

        public float GetPumpFuel(string id)
        {
            foreach (PumpState p in Data.pumps) if (p.id == id) return p.fuel;
            return 0f;
        }

        public void SetPumpFuel(string id, float fuel)
        {
            foreach (PumpState p in Data.pumps)
            {
                if (p.id == id) { p.fuel = fuel; return; }
            }
            Data.pumps.Add(new PumpState { id = id, fuel = fuel });
        }

        public bool IsKittenRescued(string id)
        {
            return Data.rescuedKittens.Contains(id);
        }

        public void MarkKittenRescued(string id)
        {
            if (!Data.rescuedKittens.Contains(id)) Data.rescuedKittens.Add(id);
            Save();
        }

        public int KittensRescued { get { return Data.rescuedKittens.Count; } }
    }
}
