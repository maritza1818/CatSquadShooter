using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CatSquad.RescateProfundo
{
    public enum SessionPhase { Inicio, Calibrando, Jugando, Emergiendo, Resumen }

    /// <summary>
    /// Ciclo de una sesión terapéutica:
    ///   Inicio → (tutorial la primera vez) → Calibración → Jugando (el oxígeno baja) →
    ///   Emergiendo (piloto automático con Arrive) → Resumen + Taller → nueva sesión.
    /// El oxígeno ES la duración de sesión configurada por el terapeuta. No hay derrota:
    /// al acabarse, el submarino emerge y conserva todo lo recolectado.
    /// </summary>
    public class SessionManager : MonoBehaviour
    {
        public TutorialDirector tutorial;
        public CalibrationSequence calibration;
        [Tooltip("Punto bajo el agua donde se hace la calibración en sesiones normales")]
        public Transform calibrationPoint;
        [Tooltip("Ruta de bajada desde el muelle hasta el punto de calibración")]
        public Transform[] diveRoute = new Transform[0];

        public SessionPhase Phase { get; private set; }
        public float OxygenTotal { get; private set; }
        public float OxygenLeft { get; private set; }
        public float Oxygen01 { get { return OxygenTotal > 0f ? Mathf.Clamp01(OxygenLeft / OxygenTotal) : 1f; } }
        public SessionReport LastReport { get; private set; }

        private bool warned50, warned20;

        private void Start()
        {
            GameManager gm = GameManager.Instance;
            OxygenTotal = Mathf.Max(0.5f, gm != null ? gm.Settings.sessionMinutes : 15f) * 60f;
            OxygenLeft = OxygenTotal;
            Phase = SessionPhase.Inicio;

            if (gm != null)
            {
                gm.Data.sessionsPlayed++;
                if (gm.metrics != null) gm.metrics.BeginSession();
            }

            bool needTutorial = gm != null && !gm.Data.tutorialDone && !gm.Settings.skipTutorial && tutorial != null;
            if (needTutorial) tutorial.Run();
            else StartCoroutine(QuickStart());
        }

        /// <summary>Sesión normal: bajar solos al punto de calibración, calentar y jugar.</summary>
        private IEnumerator QuickStart()
        {
            GameManager gm = GameManager.Instance;
            SubmarineController sub = gm != null ? gm.submarine : null;
            GuideCat guide = gm != null ? gm.guide : null;
            yield return null;

            if (guide != null)
            {
                guide.Say("¡Bienvenido de vuelta, capitán! Bajemos a calentar esos brazos.");
                List<Vector3> guidePath = new List<Vector3>();
                foreach (Transform t in diveRoute) if (t != null) guidePath.Add(t.position + new Vector3(-1.5f, 1.8f, 0f));
                if (calibrationPoint != null) guidePath.Add(calibrationPoint.position + new Vector3(-2.2f, 1.6f, 0f));
                if (guidePath.Count > 0) guide.TravelTo(guidePath);
            }

            if (sub != null && calibrationPoint != null)
            {
                bool arrived = false;
                List<Vector3> route = new List<Vector3>();
                foreach (Transform t in diveRoute) if (t != null) route.Add(t.position);
                route.Add(calibrationPoint.position);
                sub.StartAutopilot(route, () => arrived = true);
                float timeout = 25f;
                while (!arrived && timeout > 0f) { timeout -= Time.deltaTime; yield return null; }
                sub.Unfreeze();
            }

            BeginCalibration(() =>
            {
                StartPlaying();
                if (guide != null)
                {
                    AnnouncePhaseTwoIfNeeded(guide);
                    guide.FollowPlayer();
                    guide.HintsEnabled = true;
                }
            });
        }

        public void AnnouncePhaseTwoIfNeeded(GuideCat guide)
        {
            GameManager gm = GameManager.Instance;
            if (gm == null || guide == null) return;
            if (gm.GetUpgrade(UpgradeKind.Profundidad) >= 1 && !gm.Data.phase2Announced)
            {
                gm.Data.phase2Announced = true;
                guide.Say("¡Con el casco reforzado podemos bajar a la fosa! Escucho maullidos allá abajo... ¡hay gatitos esperando ayuda!");
            }
            else
            {
                guide.Say("¡A explorar! Junta chatarra y combustible. Yo te acompaño.");
            }
        }

        public void BeginCalibration(System.Action onDone)
        {
            Phase = SessionPhase.Calibrando;
            if (calibration != null) calibration.Run(onDone);
            else if (onDone != null) onDone();
        }

        public void StartPlaying()
        {
            if (Phase == SessionPhase.Jugando) return;
            Phase = SessionPhase.Jugando;
            SubmarineController sub = GameManager.Instance != null ? GameManager.Instance.submarine : null;
            if (sub != null)
            {
                sub.InputEnabled = true;
                sub.Unfreeze();
            }
        }

        private void Update()
        {
            if (Phase != SessionPhase.Jugando) return;

            OxygenLeft -= Time.deltaTime;

            GameHUD hud = GameHUD.Instance;
            if (!warned50 && Oxygen01 <= 0.5f) { warned50 = true; if (hud != null) hud.Toast("Queda la mitad del oxígeno"); }
            if (!warned20 && Oxygen01 <= 0.2f) { warned20 = true; if (hud != null) hud.Toast("Queda poco oxígeno: pronto volveremos a casa"); }

            if (OxygenLeft <= 0f)
            {
                OxygenLeft = 0f;
                EndSession();
            }
        }

        /// <summary>Termina la sesión (oxígeno agotado o el terapeuta la cierra).</summary>
        public void EndSession()
        {
            if (Phase == SessionPhase.Emergiendo || Phase == SessionPhase.Resumen) return;
            Phase = SessionPhase.Emergiendo;
            GameManager gm = GameManager.Instance;
            if (tutorial != null) tutorial.Abort();
            if (calibration != null && calibration.Running) calibration.StopAllCoroutines();

            if (gm != null && gm.arm != null)
            {
                if (gm.arm.Held != null) gm.arm.Release(false);
                gm.arm.ArmEnabled = false;
            }
            if (gm != null && gm.guide != null)
            {
                gm.guide.HintsEnabled = false;
                gm.guide.TutorialActive = false;
                gm.guide.FollowPlayer();
                gm.guide.Say("¡El tanque de oxígeno se vació por hoy! Volvemos a la superficie con todo lo que juntaste.");
            }

            if (gm != null && gm.submarine != null)
            {
                gm.submarine.InputEnabled = false;
                gm.submarine.StartAutopilotHome(OnSurfaced);
            }
            else
            {
                OnSurfaced();
            }
        }

        private void OnSurfaced()
        {
            StartCoroutine(SurfacedRoutine());
        }

        private IEnumerator SurfacedRoutine()
        {
            // Pequeña pausa para que los gatitos que venían en fila salten al muelle
            yield return new WaitForSeconds(RescuedKitten.Followers.Count > 0 ? 3f : 1f);
            Phase = SessionPhase.Resumen;
            GameManager gm = GameManager.Instance;
            if (gm != null && gm.metrics != null) LastReport = gm.metrics.EndSession();
            if (gm != null) gm.Save();
            if (gm != null && gm.guide != null) gm.guide.Say("¡Buen trabajo hoy! Mira todo lo que lograste.");
            if (GameHUD.Instance != null) GameHUD.Instance.ShowSummary(LastReport);
        }

        public void RestartScene()
        {
            if (GameManager.Instance != null) GameManager.Instance.Save();
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex >= 0
                ? SceneManager.GetActiveScene().name
                : SceneManager.GetActiveScene().path);
        }
    }
}
