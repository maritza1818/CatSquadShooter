using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace CatSquad.RescateProfundo
{
    [Serializable]
    public class RepRecord
    {
        public float time;
        public string side;
        public string objectKind;
        public bool success;
        public float extension;      // normalizada al alcance calibrado (1 = su máximo)
        public float rawExtension;   // sin normalizar (anchos de hombro con webcam)
        public float duration;       // segundos desde que empezó a extender hasta agarrar
        public int velocityPeaks;    // unidades de movimiento: menos picos = trayectoria más suave
        public bool compensated;     // hubo compensación de tronco durante el intento
    }

    [Serializable]
    public class SessionReport
    {
        public string sessionId;
        public string startedAt;
        public string endedAt;
        public string inputMode;
        public string panelSide;
        public string manipSide;
        public bool invertedRoles;
        public float configuredMinutes;
        public float durationSeconds;

        public int repetitions;          // agarres exitosos
        public int attempts;             // intentos de alcance (exitosos o no)
        public int slips;                // objetos que resbalaron por compensación
        public int bubblesPopped;        // calibración
        public float reachMeanManip;
        public float reachMaxManip;
        public float reachMeanPanel;
        public float reachMaxPanel;
        public float meanRepSeconds;
        public float meanVelocityPeaks;
        public float compensationRatio;  // intentos con compensación / intentos
        public float manipActiveSeconds;
        public float panelActiveSeconds;
        public float manipUsageRatio;    // manip / (manip + panel)
        public float leverHoldSeconds;
        public float longestLeverHold;
        public float fuelPoured;
        public int materialsCollected;
        public int cratesOpened;
        public int kittensFreed;
        public int kittensRescued;
        // conducción (primera persona)
        public string driveMode;
        public float manualDriveSeconds;   // moviendo el submarino con la palanca
        public float railDriveSeconds;     // llevado por el riel (automático o ayuda)
        public int assistActivations;      // veces que la ayuda del riel tomó el control
        public int secretsFound;           // cascabeles secretos (solo manejando)
        public int panelNavigations;       // pulgar solo: mover la selección del panel de la cabina
        public int panelConfirmations;     // ambos pulgares (o espera): confirmar un botón del panel

        // Lab 05: lanzador (resortera bimanual)
        public int launches;               // tiros (latas, lastres, boyas)
        public int launchHits;             // tiros que dieron en su blanco
        public int launchAssisted;         // tiros corregidos por la asistencia (imán o automática)
        public float launchMeanPower;      // tirón medio (0..1 de la fuerza máxima)

        public List<RepRecord> reps = new List<RepRecord>();
    }

    /// <summary>
    /// Instrumento de medición: registra lo que hizo el niño durante la sesión y lo exporta
    /// en JSON (detalle) y CSV (una fila por repetición + un resumen acumulado por sesión)
    /// en Application.persistentDataPath/CatSquadSesiones.
    /// </summary>
    public class SessionMetrics : MonoBehaviour
    {
        [Header("Detección de intentos de alcance")]
        public float attemptStartExtension = 0.3f;
        public float attemptEndExtension = 0.2f;
        public float peakMinSpeed = 0.35f;

        public SessionReport Report { get; private set; }
        public bool Recording { get; private set; }
        public string LastExportFolder { get; private set; }

        private float startTime;
        private bool inAttempt;
        private float attemptStart;
        private float attemptMaxExt;
        private float attemptMaxRaw;
        private int attemptPeaks;
        private bool attemptCompensated;
        private bool attemptSucceeded;
        private float smoothSpeed;
        private float prevSpeed;
        private bool rising;
        private float panelExtSum;
        private int panelExtSamples;
        private float leverRun;
        private float fuelAtStart;
        private int materialsAtStart;
        private int assistsAtStart;

        private void OnEnable()
        {
            MaterialCrate.AnyCrateOpened += OnCrate;
            RescuedKitten.AnyFreed += OnKittenFreed;
            RescuedKitten.AnyRescued += OnKittenRescued;
            SecretBell.AnyFound += OnSecret;
        }

        private void OnDisable()
        {
            MaterialCrate.AnyCrateOpened -= OnCrate;
            RescuedKitten.AnyFreed -= OnKittenFreed;
            RescuedKitten.AnyRescued -= OnKittenRescued;
            SecretBell.AnyFound -= OnSecret;
        }

        private void Start()
        {
            GameManager gm = GameManager.Instance;
            if (gm != null && gm.arm != null)
            {
                gm.arm.OnGrab += OnGrab;
                gm.arm.OnRelease += OnRelease;
            }
            foreach (PumpStation p in PumpStation.All) p.FuelReceived += OnFuel;
        }

        public void BeginSession()
        {
            Report = new SessionReport();
            Report.sessionId = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            Report.startedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            GameManager gm = GameManager.Instance;
            BimanualInput input = BimanualInput.Instance;
            if (gm != null)
            {
                Report.configuredMinutes = gm.Settings.sessionMinutes;
                Report.invertedRoles = gm.Settings.invertRoles;
                materialsAtStart = gm.Data.totalMaterialsCollected;
                Report.driveMode = GameManager.FirstPerson ? gm.Settings.driveMode.ToString() : "Lateral";
            }
            assistsAtStart = RailPilot.Instance != null ? RailPilot.Instance.AssistActivations : 0;
            if (input != null)
            {
                Report.panelSide = input.PanelSide.ToString();
                Report.manipSide = input.ManipSide.ToString();
                Report.inputMode = input.mode.ToString();
            }
            startTime = Time.time;
            Recording = true;
        }

        public void RecordBubble()
        {
            if (Report != null) Report.bubblesPopped++;
        }

        private void Update()
        {
            if (!Recording || BimanualInput.Instance == null) return;
            float dt = Time.deltaTime;
            BimanualFrame f = BimanualInput.Instance.Current;

            // ---- actividad de cada lado
            if (f.manip.tracked && (f.manip.speed > 0.15f || f.manip.grip)) Report.manipActiveSeconds += dt;
            bool panelActive = f.panel.tracked && (f.panel.joystick.magnitude > 0.2f || f.panel.lever);
            if (panelActive)
            {
                Report.panelActiveSeconds += dt;
                panelExtSum += f.panel.extension;
                panelExtSamples++;
                Report.reachMaxPanel = Mathf.Max(Report.reachMaxPanel, f.panel.extension);
            }

            // ---- conducción: palanca vs. riel
            SubmarineController subm = GameManager.Instance != null ? GameManager.Instance.submarine : null;
            if (GameManager.FirstPerson && subm != null && subm.State == SubState.Libre)
            {
                if (RailPilot.Instance != null && RailPilot.Instance.IsDriving) Report.railDriveSeconds += dt;
                else if (f.helm.tracked && (Mathf.Abs(f.helm.turn) > 0.2f || Mathf.Abs(f.helm.throttle) > 0.2f || Mathf.Abs(f.helm.vertical) > 0.2f))
                    Report.manualDriveSeconds += dt;
            }

            // ---- palanca sostenida (contracción sostenida del lado asistente)
            bool leverOnPump = GameManager.Instance != null && GameManager.Instance.submarine != null
                               && GameManager.Instance.submarine.DockedPump != null && GameManager.Instance.submarine.DockedPump.LeverHeld;
            // la palanca de una esclusa también es contracción sostenida del lado asistente
            if (!leverOnPump && GameManager.Instance != null && GameManager.Instance.submarine != null)
            {
                LockStation lk = GameManager.Instance.submarine.AnchorOwner as LockStation;
                leverOnPump = lk != null && lk.LeverHeld;
            }
            if (leverOnPump)
            {
                leverRun += dt;
                Report.leverHoldSeconds += dt;
                Report.longestLeverHold = Mathf.Max(Report.longestLeverHold, leverRun);
            }
            else leverRun = 0f;

            if (!f.manip.tracked) return;

            // ---- intentos de alcance + suavidad (conteo de picos de velocidad)
            smoothSpeed = Mathf.Lerp(smoothSpeed, f.manip.speed, 1f - Mathf.Exp(-20f * dt));
            float ext = f.manip.extension;

            if (!inAttempt && ext >= attemptStartExtension)
            {
                inAttempt = true;
                attemptStart = Time.time;
                attemptMaxExt = ext;
                attemptMaxRaw = f.manip.rawExtension;
                attemptPeaks = 0;
                attemptCompensated = false;
                attemptSucceeded = false;
                rising = false;
            }

            if (inAttempt)
            {
                attemptMaxExt = Mathf.Max(attemptMaxExt, ext);
                attemptMaxRaw = Mathf.Max(attemptMaxRaw, f.manip.rawExtension);
                if (f.compensating) attemptCompensated = true;

                if (smoothSpeed > prevSpeed + 0.01f) rising = true;
                else if (rising && smoothSpeed < prevSpeed - 0.01f && prevSpeed > peakMinSpeed)
                {
                    attemptPeaks++;
                    rising = false;
                }

                if (ext < attemptEndExtension)
                {
                    Report.attempts++;
                    if (attemptCompensated) compensatedAttempts++;
                    if (!attemptSucceeded)
                    {
                        AddRep(false, "ninguno", attemptMaxExt, attemptMaxRaw, Time.time - attemptStart);
                    }
                    inAttempt = false;
                }
            }
            prevSpeed = smoothSpeed;
        }

        private int compensatedAttempts;

        /// <summary>Lab 05: un tiro del lanzador. Se guarda como repetición (agarrar · tirar · soltar).</summary>
        public void RecordLaunch(ProjectileKind kind, bool hit, bool assisted, float power, float aimSeconds, bool compensated)
        {
            if (!Recording) return;
            Report.launchMeanPower = (Report.launchMeanPower * Report.launches + power) / (Report.launches + 1);
            Report.launches++;
            if (hit) Report.launchHits++;
            if (assisted) Report.launchAssisted++;
            Report.reps.Add(new RepRecord
            {
                time = Time.time - startTime,
                side = Report.manipSide,
                objectKind = "lanzamiento_" + kind.ToString().ToLowerInvariant(),
                success = hit,
                extension = power,
                rawExtension = power,
                duration = aimSeconds,
                velocityPeaks = 0,
                compensated = compensated
            });
        }

        private void AddRep(bool success, string kind, float ext, float raw, float duration)
        {
            RepRecord r = new RepRecord
            {
                time = Time.time - startTime,
                side = Report.manipSide,
                objectKind = kind,
                success = success,
                extension = ext,
                rawExtension = raw,
                duration = duration,
                velocityPeaks = attemptPeaks,
                compensated = attemptCompensated
            };
            Report.reps.Add(r);
        }

        private void OnGrab(Grabbable g, float extension)
        {
            if (!Recording) return;
            BimanualFrame f = BimanualInput.Instance != null ? BimanualInput.Instance.Current : new BimanualFrame();
            Report.repetitions++;
            float duration = inAttempt ? Time.time - attemptStart : 0f;
            if (!inAttempt) { attemptPeaks = 0; attemptCompensated = f.compensating; }
            AddRep(true, g.kind.ToString(), extension, f.manip.rawExtension, duration);
            attemptSucceeded = true;
        }

        private void OnRelease(Grabbable g, bool slipped)
        {
            if (Recording && slipped) Report.slips++;
        }

        private void OnFuel(PumpStation p, float amount)
        {
            if (Recording) Report.fuelPoured += amount;
        }

        private void OnCrate(MaterialCrate c, int shards)
        {
            if (Recording) Report.cratesOpened++;
        }

        private void OnKittenFreed(RescuedKitten k)
        {
            if (Recording) Report.kittensFreed++;
        }

        private void OnKittenRescued(RescuedKitten k)
        {
            if (Recording) Report.kittensRescued++;
        }

        /// <summary>El panel de la cabina informa cada gesto de pulgares (coordinación de ambas manos).</summary>
        public void RecordPanel(bool confirmation)
        {
            if (!Recording) return;
            if (confirmation) Report.panelConfirmations++;
            else Report.panelNavigations++;
        }

        private void OnSecret(SecretBell b)
        {
            if (Recording) Report.secretsFound++;
        }

        /// <summary>Cierra la sesión, calcula los agregados y exporta los archivos.</summary>
        public SessionReport EndSession()
        {
            if (Report == null) BeginSession();
            Recording = false;
            Report.endedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            Report.durationSeconds = Time.time - startTime;

            float extSum = 0f, durSum = 0f, peakSum = 0f;
            int ok = 0;
            foreach (RepRecord r in Report.reps)
            {
                if (!r.success || (r.objectKind != null && r.objectKind.StartsWith("lanzamiento"))) continue;
                ok++;
                extSum += r.extension;
                durSum += r.duration;
                peakSum += r.velocityPeaks;
                Report.reachMaxManip = Mathf.Max(Report.reachMaxManip, r.extension);
            }
            Report.reachMeanManip = ok > 0 ? extSum / ok : 0f;
            Report.meanRepSeconds = ok > 0 ? durSum / ok : 0f;
            Report.meanVelocityPeaks = ok > 0 ? peakSum / ok : 0f;
            Report.reachMeanPanel = panelExtSamples > 0 ? panelExtSum / panelExtSamples : 0f;
            Report.compensationRatio = Report.attempts > 0 ? compensatedAttempts / (float)Report.attempts : 0f;
            float total = Report.manipActiveSeconds + Report.panelActiveSeconds;
            Report.manipUsageRatio = total > 0f ? Report.manipActiveSeconds / total : 0f;
            if (GameManager.Instance != null)
            {
                Report.materialsCollected = GameManager.Instance.Data.totalMaterialsCollected - materialsAtStart;
            }
            if (RailPilot.Instance != null) Report.assistActivations = RailPilot.Instance.AssistActivations - assistsAtStart;

            Export();
            return Report;
        }

        private void Export()
        {
            try
            {
                string folder = Path.Combine(Application.persistentDataPath, "CatSquadSesiones");
                Directory.CreateDirectory(folder);
                LastExportFolder = folder;
                string baseName = "sesion_" + Report.sessionId;

                File.WriteAllText(Path.Combine(folder, baseName + ".json"), JsonUtility.ToJson(Report, true));

                CultureInfo ci = CultureInfo.InvariantCulture;
                StringBuilder csv = new StringBuilder();
                csv.AppendLine("tiempo_s,lado,objeto,exito,extension_norm,extension_cruda,duracion_s,picos_velocidad,compensacion");
                foreach (RepRecord r in Report.reps)
                {
                    csv.AppendLine(string.Join(",", new[]
                    {
                        r.time.ToString("F2", ci), r.side, r.objectKind, r.success ? "1" : "0",
                        r.extension.ToString("F3", ci), r.rawExtension.ToString("F3", ci),
                        r.duration.ToString("F2", ci), r.velocityPeaks.ToString(ci), r.compensated ? "1" : "0"
                    }));
                }
                File.WriteAllText(Path.Combine(folder, baseName + "_repeticiones.csv"), csv.ToString());

                string summaryPath = Path.Combine(folder, "resumen_sesiones.csv");
                const string header = "sesion,inicio,duracion_s,modo,lado_panel,lado_brazo,repeticiones,intentos,alcance_medio,alcance_max," +
                                      "tiempo_medio_rep_s,picos_medios,prop_compensacion,prop_uso_brazo,palanca_s,palanca_max_s,resbalones,materiales,gatitos," +
                                      "conduccion,manejo_manual_s,riel_s,ayudas_riel,secretos,panel_mover,panel_confirmar," +
                                      "lanzamientos,aciertos,asistidos,tiron_medio";
                if (File.Exists(summaryPath))
                {
                    // Un resumen de una versión anterior (con menos columnas) se guarda aparte
                    string first = "";
                    using (StreamReader reader = new StreamReader(summaryPath)) first = reader.ReadLine() ?? "";
                    if (first != header)
                    {
                        string old = Path.Combine(folder, "resumen_sesiones_version_anterior.csv");
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(summaryPath, old);
                    }
                }
                bool newFile = !File.Exists(summaryPath);
                StringBuilder s = new StringBuilder();
                if (newFile)
                {
                    s.AppendLine(header);
                }
                s.AppendLine(string.Join(",", new[]
                {
                    Report.sessionId, Report.startedAt, Report.durationSeconds.ToString("F0", ci), Report.inputMode,
                    Report.panelSide, Report.manipSide, Report.repetitions.ToString(ci), Report.attempts.ToString(ci),
                    Report.reachMeanManip.ToString("F3", ci), Report.reachMaxManip.ToString("F3", ci),
                    Report.meanRepSeconds.ToString("F2", ci), Report.meanVelocityPeaks.ToString("F2", ci),
                    Report.compensationRatio.ToString("F3", ci), Report.manipUsageRatio.ToString("F3", ci),
                    Report.leverHoldSeconds.ToString("F1", ci), Report.longestLeverHold.ToString("F1", ci),
                    Report.slips.ToString(ci), Report.materialsCollected.ToString(ci), Report.kittensRescued.ToString(ci),
                    Report.driveMode ?? "", Report.manualDriveSeconds.ToString("F0", ci), Report.railDriveSeconds.ToString("F0", ci),
                    Report.assistActivations.ToString(ci), Report.secretsFound.ToString(ci),
                    Report.panelNavigations.ToString(ci), Report.panelConfirmations.ToString(ci),
                    Report.launches.ToString(ci), Report.launchHits.ToString(ci), Report.launchAssisted.ToString(ci),
                    Report.launchMeanPower.ToString("F2", ci)
                }));
                File.AppendAllText(summaryPath, s.ToString());

                Debug.Log("[CatSquad] Métricas exportadas en: " + folder);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[CatSquad] No se pudieron exportar las métricas: " + e.Message);
            }
        }
    }
}
