using UnityEngine;

namespace CatSquad.RescateProfundo
{
    /// <summary>
    /// Entrada de desarrollo / demostración con teclado y mouse.
    /// (Primera persona: ver el bloque "Timón" más abajo.)
    ///   Panel (mano asistente):  WASD o flechas = palancas de desplazamiento, Espacio = palanca de bomba.
    ///   Brazo (mano manipuladora): el mouse es la mano; clic izquierdo sostenido = cerrar la pinza;
    ///                              Q / E (o clic derecho) = girar la muñeca para verter.
    ///   L sostenida = simula compensación de tronco (para probar que el objeto resbala).
    /// </summary>
    public class KeyboardMouseInputProvider : BimanualInputProvider
    {
        public float wristRotateSpeed = 220f;
        public float maxWristAngle = 100f;

        private float wristAngle;

        public override string DisplayName { get { return "Teclado + mouse"; } }
        public override bool IsAvailable { get { return true; } }

        public override void Read(ref BimanualFrame frame, InputContext context, float dt)
        {
            // ---- Panel (mano asistente)
            Vector2 stick = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            if (stick.sqrMagnitude > 1f) stick.Normalize();

            frame.panel.tracked = true;
            frame.panel.joystick = stick;
            frame.panel.offset = stick;
            frame.panel.extension = stick.magnitude;
            frame.panel.rawExtension = stick.magnitude;
            frame.panel.lever = Input.GetKey(KeyCode.Space);
            frame.panel.grip = frame.panel.lever;
            frame.panel.gripKnown = true;

            // ---- Brazo manipulador (mano hábil): el mouse proyectado al plano de trabajo del brazo
            frame.manip.tracked = false;
            Camera cam = context.camera != null ? context.camera : Camera.main;
            if (cam != null && context.armReachWorld > 0.01f)
            {
                Ray ray = cam.ScreenPointToRay(Input.mousePosition);
                Plane plane;
                Vector3 right, up;
                if (context.firstPerson)
                {
                    // plano perpendicular a la mirada, a la profundidad de trabajo del brazo
                    Vector3 f = context.armForward.sqrMagnitude > 0.01f ? context.armForward.normalized : cam.transform.forward;
                    plane = new Plane(-f, context.armBaseWorld + f * context.armPlaneDepth);
                    right = context.armRight;
                    up = context.armUp;
                }
                else
                {
                    plane = new Plane(Vector3.back, new Vector3(0f, 0f, context.gamePlaneZ));
                    right = Vector3.right;
                    up = Vector3.up;
                }

                float enter;
                if (plane.Raycast(ray, out enter))
                {
                    Vector3 hit = ray.GetPoint(enter);
                    Vector3 rel = hit - context.armBaseWorld;
                    Vector2 delta = new Vector2(Vector3.Dot(rel, right), Vector3.Dot(rel, up)) / context.armReachWorld;
                    if (delta.magnitude > 1.05f) delta = delta.normalized * 1.05f;

                    frame.manip.tracked = true;
                    frame.manip.offset = delta;
                    frame.manip.extension = delta.magnitude;
                    frame.manip.rawExtension = delta.magnitude;
                }
            }

            frame.manip.grip = Input.GetMouseButton(0);
            frame.manip.gripKnown = true;

            float targetWrist = 0f;
            if (Input.GetKey(KeyCode.E) || Input.GetMouseButton(1)) targetWrist = maxWristAngle;
            else if (Input.GetKey(KeyCode.Q)) targetWrist = -maxWristAngle;
            wristAngle = Mathf.MoveTowards(wristAngle, targetWrist, wristRotateSpeed * dt);
            frame.manip.wristAngle = wristAngle;
            frame.manip.wristKnown = true;

            // ---- Timón en primera persona (equivale a los gestos de la palanca)
            //   W = mano vertical (avanzar) · S = pulgar arriba (retroceder) · A/D = girar
            //   Espacio = levantar la mano (subir / palanca de bomba) · Shift o C = abrir la mano (bajar)
            bool forward = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow);
            bool back = Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow);
            float turn = (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? 1f : 0f)
                       - (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? 1f : 0f);
            bool rise = Input.GetKey(KeyCode.Space);
            bool sink = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) || Input.GetKey(KeyCode.C);
            frame.helm.tracked = true;
            frame.helm.turn = turn;
            frame.helm.throttle = forward ? 1f : (back ? -1f : 0f);
            frame.helm.vertical = rise ? 1f : (sink ? -1f : 0f);
            frame.helm.mode = sink ? HelmMode.Suelta : (forward || back ? HelmMode.Avance : HelmMode.Estacionaria);

            // ---- Panel de cabina (en el juego real: juntar las manos y usar los pulgares)
            frame.panelToggle = Input.GetKeyDown(KeyCode.M) || Input.GetKeyDown(KeyCode.Tab);
            // Z = pulgar izquierdo, X = pulgar derecho (Z+X a la vez = ambos pulgares = confirmar). Enter confirma directo.
            frame.thumbLeft = Input.GetKey(KeyCode.Z);
            frame.thumbRight = Input.GetKey(KeyCode.X);
            frame.panelHandsReady = true;
            frame.panelConfirm = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);

            // ---- Tronco (solo para pruebas: con teclado no hay forma real de medirlo)
            frame.trunkCompensation = Input.GetKey(KeyCode.L) ? 1.6f : 0f;
            frame.compensating = frame.trunkCompensation >= 1f;
            frame.fromWebcam = false;
        }
    }
}
