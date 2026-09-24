using UnityEngine;

namespace CatSquad.RescateProfundo
{
    /// <summary>
    /// Entrada de desarrollo / demostración con teclado y mouse.
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

            // ---- Brazo manipulador (mano hábil): el mouse proyectado al plano del juego
            frame.manip.tracked = false;
            Camera cam = context.camera != null ? context.camera : Camera.main;
            if (cam != null && context.armReachWorld > 0.01f)
            {
                Ray ray = cam.ScreenPointToRay(Input.mousePosition);
                Plane plane = new Plane(Vector3.back, new Vector3(0f, 0f, context.gamePlaneZ));
                float enter;
                if (plane.Raycast(ray, out enter))
                {
                    Vector3 hit = ray.GetPoint(enter);
                    Vector2 delta = (Vector2)(hit - context.armBaseWorld) / context.armReachWorld;
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

            // ---- Tronco (solo para pruebas: con teclado no hay forma real de medirlo)
            frame.trunkCompensation = Input.GetKey(KeyCode.L) ? 1.6f : 0f;
            frame.compensating = frame.trunkCompensation >= 1f;
            frame.fromWebcam = false;
        }
    }
}
