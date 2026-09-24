using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace CatSquad.RescateProfundo
{
    /// <summary>
    /// Tutorial guiado por la Capitana Pelusa (primera sesión).
    /// Cada paso: la guía LLEGA (Arrive) junto a lo que va a explicar, lo explica
    /// y espera a que el jugador lo haga. Los pasos reaccionan a lo que el jugador
    /// ya hizo (si se adelanta, el paso se da por cumplido).
    /// </summary>
    public class TutorialDirector : MonoBehaviour
    {
        [Header("Referencias")]
        public SessionManager session;
        public WaterZone hallZone;
        public WaterZone storageZone;
        public MaterialCrate tutorialCrate;
        public Grabbable[] tutorialDebris = new Grabbable[0];
        public FuelCanister[] tutorialFuel = new FuelCanister[0];
        public PumpStation tutorialPump;

        [Header("Puntos de la guía")]
        public Transform guideStart;
        public Transform guideDock;
        public Transform[] diveRoute = new Transform[0];
        public Transform crateSpot;
        public Transform debrisSpot;
        public Transform pumpSpot;
        public Transform[] shaftRoute = new Transform[0];

        public bool Running { get; private set; }
        public string CurrentStep { get; private set; }

        private GuideCat guide;
        private SubmarineController sub;
        private ManipulatorArm arm;
        private Coroutine routine;

        public void Run()
        {
            GameManager gm = GameManager.Instance;
            guide = gm != null ? gm.guide : null;
            sub = gm != null ? gm.submarine : null;
            arm = gm != null ? gm.arm : null;
            if (guide == null || sub == null) return;
            routine = StartCoroutine(Routine());
        }

        public void Abort()
        {
            if (routine != null) StopCoroutine(routine);
            routine = null;
            Running = false;
            if (guide != null) guide.TutorialActive = false;
        }

        private bool KeyboardMode
        {
            get { return BimanualInput.Instance == null || !BimanualInput.Instance.UsingWebcam; }
        }

        private IEnumerator WaitUntilOr(Func<bool> condition, float timeout)
        {
            float t = 0f;
            while (!condition() && t < timeout)
            {
                t += Time.deltaTime;
                yield return null;
            }
        }

        private IEnumerator GuideGo(Vector3[] points)
        {
            guide.TravelTo(points);
            yield return WaitUntilOr(() => guide.HasArrived, 20f);
        }

        private static Vector3 Beside(Transform t, float dx, float dy)
        {
            return t != null ? t.position + new Vector3(dx, dy, 0f) : Vector3.zero;
        }

        private IEnumerator Routine()
        {
            Running = true;
            guide.TutorialActive = true;
            guide.HintsEnabled = false;

            // ---------------------------------------------------------------- 0. Presentación
            CurrentStep = "Presentación";
            if (guideStart != null) guide.TeleportTo(guideStart.position);
            if (guideDock != null) yield return GuideGo(new[] { guideDock.position });
            guide.Say("¡Hola, capitán! Soy la Capitana Pelusa. Bajo nosotros hay un complejo inundado lleno de chatarra... ¡y quizás gatitos que necesitan ayuda!");
            yield return new WaitForSeconds(5f);

            // ---------------------------------------------------------------- 1. Moverse (mano asistente)
            CurrentStep = "Moverse";
            guide.Say(KeyboardMode
                ? "Tu PANEL de mando mueve el submarino (W A S D o flechas). ¡Sígueme hacia abajo!"
                : "Tu brazo del PANEL mueve el submarino: lleva la mano hacia donde quieras ir. ¡Sígueme hacia abajo!");
            List<Vector3> dive = new List<Vector3>();
            foreach (Transform t in diveRoute) if (t != null) dive.Add(t.position + new Vector3(0f, 0.8f, 0f));
            if (session != null && session.calibrationPoint != null) dive.Add(session.calibrationPoint.position + new Vector3(-2.4f, 1.4f, 0f));
            guide.TravelTo(dive);
            yield return WaitUntilOr(() => sub.CurrentZone == hallZone
                                           && session != null && session.calibrationPoint != null
                                           && Vector3.Distance(sub.transform.position, session.calibrationPoint.position) < 3.5f, 240f);

            // ---------------------------------------------------------------- 2. Calibración
            CurrentStep = "Calibración";
            bool calibrated = false;
            if (session != null) session.BeginCalibration(() => calibrated = true);
            else calibrated = true;
            while (!calibrated) yield return null;
            if (session != null) session.StartPlaying();

            // ---------------------------------------------------------------- 3. Alcanzar y sujetar (mano manipuladora)
            if (tutorialCrate != null && !tutorialCrate.Opened)
            {
                CurrentStep = "Chatarra";
                yield return GuideGo(new[] { Beside(crateSpot, 0f, 1.2f) });
                guide.PointAt(tutorialCrate.transform.position, 6f);
                guide.Say(KeyboardMode
                    ? "¿Ves ese brillo tras la rejilla? El submarino no cabe, pero tu BRAZO sí. Acércate, apunta con el mouse y MANTÉN el clic izquierdo para cerrar la pinza."
                    : "¿Ves ese brillo tras la rejilla? El submarino no cabe, pero tu BRAZO sí. Acércate, estira la mano hasta el brillo y CIERRA la mano.");
                yield return WaitUntilOr(() => tutorialCrate == null || tutorialCrate.Opened, 300f);
                guide.Say("¡Eso es! Los fragmentos vuelan solitos a tu submarino. Mientras más estires el brazo, más fragmentos salen.");
                yield return new WaitForSeconds(4.5f);
            }

            // ---------------------------------------------------------------- 4-8. Combustible y bomba
            if (tutorialPump != null && !tutorialPump.Complete)
            {
                CurrentStep = "Escombros";
                yield return GuideGo(new[] { Beside(debrisSpot, 0f, 0f) });
                if (!AnyDebrisMoved() && !AnyFuelHeld())
                {
                    guide.PointAt(debrisSpot != null ? debrisSpot.position + Vector3.down * 3f : guide.transform.position, 5f);
                    guide.Say("Abajo hay combustible, pero los escombros lo tapan. Sujeta un escombro y APÁRTALO a un lado.");
                    yield return WaitUntilOr(() => AnyDebrisMoved() || AnyFuelHeld(), 300f);
                    guide.Say(KeyboardMode ? "¡Muy bien! Suelta el clic para dejarlo." : "¡Muy bien! Abre la mano para soltarlo.");
                    yield return new WaitForSeconds(2.5f);
                }

                CurrentStep = "Bidón";
                if (!AnyFuelHeld())
                {
                    guide.Say("Ahora sujeta el bidón ROJO de combustible.");
                    yield return WaitUntilOr(AnyFuelHeld, 300f);
                }

                CurrentStep = "Acoplar";
                yield return GuideGo(new[] { Beside(pumpSpot, 0f, 0f) });
                guide.Say("¡Llévalo a la bomba! Acércate despacio al lado de la bomba para acoplarte.");
                yield return WaitUntilOr(() => tutorialPump.SubDockedHere || tutorialPump.Progress > 0.01f, 300f);

                CurrentStep = "Verter";
                guide.Say(KeyboardMode
                    ? "¡Acoplado! MANTÉN ESPACIO para sostener la palanca y, a la vez, mantén E para girar la pinza y verter en el embudo."
                    : "¡Acoplado! SUBE tu mano del panel y mantenla arriba (es la palanca) mientras giras la muñeca del brazo para verter en el embudo.");
                bool warnedValve = false;
                while (tutorialPump.Progress < 0.45f && !tutorialPump.Complete)
                {
                    if (!warnedValve && AnyFuelWantsValve())
                    {
                        warnedValve = true;
                        guide.Say(KeyboardMode ? "¡Casi! La válvula solo se abre si MANTIENES ESPACIO (la palanca) mientras viertes."
                                               : "¡Casi! La válvula solo se abre si MANTIENES arriba tu mano del panel mientras viertes.");
                    }
                    yield return null;
                }

                if (!tutorialPump.Complete)
                {
                    guide.Say(KeyboardMode
                        ? "¡El agua del pozo sube! Para desacoplarte, mantén A o D. Trae el otro bidón para llenarlo del todo."
                        : "¡El agua del pozo sube! Para desacoplarte, lleva tu mano del panel a un lado. Trae el otro bidón para llenarlo del todo.");
                    yield return WaitUntilOr(() => tutorialPump.Complete, 600f);
                }
            }

            // ---------------------------------------------------------------- 9. Subir por el pozo
            CurrentStep = "Pozo";
            guide.Say("¡Pozo lleno! El agua ya llega al almacén de arriba. ¡Sígueme!");
            List<Vector3> up = new List<Vector3>();
            foreach (Transform t in shaftRoute) if (t != null) up.Add(t.position);
            if (up.Count > 0) guide.TravelTo(up);
            yield return WaitUntilOr(() => sub.CurrentZone == storageZone, 300f);

            // ---------------------------------------------------------------- 10. Juego libre
            CurrentStep = "Libre";
            guide.Say("Aquí hay mucha chatarra y combustible. La bomba del fondo abre la compuerta hacia la fosa. ¡Cuando se acabe el oxígeno volveremos solos a casa!");
            if (GameManager.Instance != null)
            {
                GameManager.Instance.Data.tutorialDone = true;
                GameManager.Instance.Save();
            }
            yield return new WaitForSeconds(4f);

            guide.TutorialActive = false;
            guide.HintsEnabled = true;
            guide.FollowPlayer();
            Running = false;
            routine = null;
        }

        private bool AnyDebrisMoved()
        {
            foreach (Grabbable g in tutorialDebris)
            {
                if (g != null && g.DistanceFromSpawn > 1.5f) return true;
            }
            return false;
        }

        private bool AnyFuelHeld()
        {
            foreach (FuelCanister f in tutorialFuel)
            {
                if (f != null && f.IsHeld) return true;
            }
            return arm != null && arm.Held is FuelCanister;
        }

        private bool AnyFuelWantsValve()
        {
            foreach (FuelCanister f in tutorialFuel)
            {
                if (f != null && f.WantsToPourButValveClosed) return true;
            }
            return false;
        }
    }
}
