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

        [Header("Ciudad inundada (primera persona)")]
        public bool cityMode;
        [Tooltip("Barrio que drena la bomba del tutorial")]
        public WaterZone firstDistrict;
        public LockStation firstLock;

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
            if (RailPilot.Instance != null) RailPilot.Instance.ClearFocus();
        }

        // En modo automático el riel va al objetivo de cada paso del tutorial
        private static void RailFocus(Component target)
        {
            if (RailPilot.Instance != null && target != null) RailPilot.Instance.SetFocus(target);
        }

        private static void RailFocusPoint(Vector3 p)
        {
            if (RailPilot.Instance != null) RailPilot.Instance.SetFocusPoint(p);
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
            guide.Say(cityMode
                ? "¡Hola, capitán! Soy la Capitana Pelusa. Gatópolis, la ciudad de los gatos, quedó bajo el mar. ¡Vamos a liberar sus barrios del agua y a rescatar a los gatitos!"
                : "¡Hola, capitán! Soy la Capitana Pelusa. Bajo nosotros hay un complejo inundado lleno de chatarra... ¡y quizás gatitos que necesitan ayuda!");
            yield return new WaitForSeconds(5f);

            // ---------------------------------------------------------------- 1. Moverse (mano asistente)
            CurrentStep = "Moverse";
            if (GameManager.FirstPerson && RailPilot.Instance != null && RailPilot.Instance.AutoMode)
            {
                guide.Say("¡Estás en la cabina! Hoy el submarino va solo por el RIEL de luz: tú te encargas del brazo. ¡Sígueme hacia abajo!");
            }
            else if (GameManager.FirstPerson)
            {
                guide.Say(KeyboardMode
                    ? "¡Estás en la cabina! La PALANCA mueve el submarino: W avanza, A y D giran, Shift baja y Espacio sube. ¡Sígueme hacia abajo!"
                    : "¡Estás en la cabina! Tu mano del PANEL es la palanca: ponla de CANTO para avanzar, a los lados giras, ábrela para bajar y súbela para subir. ¡Sígueme!");
            }
            else
            {
                guide.Say(KeyboardMode
                    ? "Tu PANEL de mando mueve el submarino (W A S D o flechas). ¡Sígueme hacia abajo!"
                    : "Tu brazo del PANEL mueve el submarino: lleva la mano hacia donde quieras ir. ¡Sígueme hacia abajo!");
            }
            List<Vector3> dive = new List<Vector3>();
            foreach (Transform t in diveRoute) if (t != null) dive.Add(t.position + new Vector3(0f, 0.8f, 0f));
            if (session != null && session.calibrationPoint != null) dive.Add(session.calibrationPoint.position + new Vector3(-2.4f, 1.4f, 0f));
            guide.TravelTo(dive);
            if (session != null && session.calibrationPoint != null) RailFocusPoint(session.calibrationPoint.position);
            yield return WaitUntilOr(() => sub.CurrentZone == hallZone
                                           && session != null && session.calibrationPoint != null
                                           && FlatDistance(sub.transform.position, session.calibrationPoint.position) < 3.5f, 240f);

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
                RailFocus(tutorialCrate);
                yield return GuideGo(new[] { Beside(crateSpot, 0f, 1.2f) });
                guide.PointAt(tutorialCrate.transform.position, 6f);
                if (GameManager.FirstPerson)
                {
                    guide.Say(KeyboardMode
                        ? "¿Ves ese brillo? Acércate despacio y el submarino se anclará solo. Luego lleva la pinza con el mouse y MANTÉN el clic izquierdo."
                        : "¿Ves ese brillo? Acércate despacio y el submarino se anclará solo. Luego estira el brazo hasta el brillo y CIERRA la mano.");
                }
                else
                {
                    guide.Say(KeyboardMode
                        ? "¿Ves ese brillo tras la rejilla? El submarino no cabe, pero tu BRAZO sí. Acércate, apunta con el mouse y MANTÉN el clic izquierdo para cerrar la pinza."
                        : "¿Ves ese brillo tras la rejilla? El submarino no cabe, pero tu BRAZO sí. Acércate, estira la mano hasta el brillo y CIERRA la mano.");
                }
                yield return WaitUntilOr(() => tutorialCrate == null || tutorialCrate.Opened, 300f);
                guide.Say("¡Eso es! Los fragmentos vuelan solitos a tu submarino. Mientras más estires el brazo, más fragmentos salen.");
                yield return new WaitForSeconds(4.5f);
            }

            // ---------------------------------------------------------------- 4-8. Combustible y bomba
            if (tutorialPump != null && !tutorialPump.Complete)
            {
                CurrentStep = "Escombros";
                foreach (Grabbable d in tutorialDebris)
                {
                    if (d != null && d.DistanceFromSpawn < 1.5f) { RailFocus(d); break; }
                }
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
                foreach (FuelCanister f in tutorialFuel)
                {
                    if (f != null && f.gameObject.activeInHierarchy) { RailFocus(f); break; }
                }
                if (!AnyFuelHeld())
                {
                    guide.Say("Ahora sujeta el bidón ROJO de combustible.");
                    yield return WaitUntilOr(AnyFuelHeld, 300f);
                }

                CurrentStep = "Acoplar";
                RailFocus(tutorialPump);
                yield return GuideGo(new[] { Beside(pumpSpot, 0f, 0f) });
                guide.Say(cityMode
                    ? "¡Llévalo a la bomba de drenaje! Esa bomba saca el agua del barrio del otro lado del dique. Acércate despacio para acoplarte."
                    : "¡Llévalo a la bomba! Acércate despacio al lado de la bomba para acoplarte.");
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

                if (!tutorialPump.Complete && cityMode)
                {
                    guide.Say(KeyboardMode
                        ? "¡El barrio empieza a vaciarse! Para soltarte, gira con A o D. Trae el otro bidón para drenarlo del todo."
                        : "¡El barrio empieza a vaciarse! Para soltarte, gira la mano del panel. Trae el otro bidón para drenarlo del todo.");
                    yield return WaitUntilOr(() => tutorialPump.Complete, 600f);
                }
                else if (!tutorialPump.Complete)
                {
                    guide.Say(KeyboardMode
                        ? (GameManager.FirstPerson ? "¡El agua del pozo sube! Para soltarte, gira con A o D. Trae el otro bidón para llenarlo del todo."
                                                   : "¡El agua del pozo sube! Para desacoplarte, mantén A o D. Trae el otro bidón para llenarlo del todo.")
                        : "¡El agua del pozo sube! Para desacoplarte, lleva tu mano del panel a un lado. Trae el otro bidón para llenarlo del todo.");
                    yield return WaitUntilOr(() => tutorialPump.Complete, 600f);
                }
            }

            // ---------------------------------------------------------------- 9 (ciudad). Esclusa hacia el barrio drenado
            if (cityMode)
            {
                CurrentStep = "Esclusa";
                guide.Say("¡El barrio se liberó del agua! Ahora está más abajo que el mar. Para bajar usamos la ESCLUSA, un ascensor de agua. ¡Sígueme!");
                if (firstLock != null)
                {
                    RailFocus(firstLock);
                    yield return GuideGo(new[] { firstLock.CenterAt(1.5f) + new Vector3(-1.5f, 0f, 2f) });
                    guide.Say(KeyboardMode
                        ? "Entra a la esclusa y acércate a la boya: el submarino se anclará. Luego MANTÉN ESPACIO (la palanca) para bajar el agua."
                        : "Entra a la esclusa y acércate a la boya: el submarino se anclará. Luego MANTÉN ARRIBA la mano del panel para bajar el agua.");
                    yield return WaitUntilOr(() => firstLock.SubDockedHere || sub.CurrentZone == firstDistrict, 300f);
                    yield return WaitUntilOr(() => firstLock.EastOpen || sub.CurrentZone == firstDistrict, 300f);
                    guide.Say(GameManager.FirstPerson && KeyboardMode
                        ? "¡Compuerta abierta! Avanza con W para salir al barrio."
                        : "¡Compuerta abierta! Sal al barrio.");
                }
                if (firstDistrict != null) yield return WaitUntilOr(() => sub.CurrentZone == firstDistrict, 300f);

                CurrentStep = "Libre";
                if (RailPilot.Instance != null) RailPilot.Instance.ClearFocus();
                guide.Say("¡Bienvenido al Nivel 1! Las calles están llenas de chatarra y hay gatitos en las jaulas. La balsa de rescate los espera en la superficie. Más allá hay otra esclusa... ¡y otro barrio por liberar!");
                yield return new WaitForSeconds(5.5f);
                CurrentStep = "Panel";
                guide.Say(KeyboardMode
                    ? "Último truco: el PANEL junto a la palanca. Z y X son tus pulgares para elegir un botón, y Z+X juntos (ambos pulgares) lo activan. ¡Prueba PISTA!"
                    : "Último truco: el PANEL junto a la palanca. Con las manos relajadas, sube un pulgar para elegir un botón y AMBOS pulgares para activarlo. ¡Prueba PISTA!");
                bool pressed = false;
                System.Action<string> onPress = t => pressed = true;
                CockpitPanel.AnyPressed += onPress;
                yield return WaitUntilOr(() => pressed, 40f);
                CockpitPanel.AnyPressed -= onPress;
                if (pressed) guide.Say("¡Perfecto! Con el panel también cambias el modo, las luces, el zoom del mapa, abres el LANZADOR o vuelves a la balsa.");
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
                yield break;
            }

            // ---------------------------------------------------------------- 9. Subir por el pozo
            CurrentStep = "Pozo";
            if (shaftRoute.Length > 0 && shaftRoute[shaftRoute.Length - 1] != null) RailFocusPoint(shaftRoute[shaftRoute.Length - 1].position);
            guide.Say("¡Pozo lleno! El agua ya llega al almacén de arriba. ¡Sígueme!");
            List<Vector3> up = new List<Vector3>();
            foreach (Transform t in shaftRoute) if (t != null) up.Add(t.position);
            if (up.Count > 0) guide.TravelTo(up);
            yield return WaitUntilOr(() => sub.CurrentZone == storageZone, 300f);

            // ---------------------------------------------------------------- 10. Juego libre
            CurrentStep = "Libre";
            if (RailPilot.Instance != null) RailPilot.Instance.ClearFocus();
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

        /// <summary>Distancia en el plano del mapa (X, Y): en primera persona la profundidad no cuenta.</summary>
        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.z = 0f;
            b.z = 0f;
            return Vector3.Distance(a, b);
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
