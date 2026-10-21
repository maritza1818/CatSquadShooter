using UnityEngine;

namespace CatSquad.RescateProfundo
{
    public enum BodySide { Izquierdo = 0, Derecho = 1 }

    public enum InputMode
    {
        TecladoMouse = 0,   // desarrollo y demostración
        Webcam = 1          // puente Python/MediaPipe; si no llega señal, cae a teclado
    }

    /// <summary>Estado de UNA mano ya interpretado en coordenadas de juego (x derecha, y arriba).</summary>
    [System.Serializable]
    public struct HandFrame
    {
        public bool tracked;

        /// <summary>Dirección * extensión normalizada. |offset| ≈ 1 significa "al 100 % de su alcance calibrado".</summary>
        public Vector2 offset;

        /// <summary>Extensión normalizada al envolvente calibrado del niño (0 = mano en reposo, 1 = su máximo).</summary>
        public float extension;

        /// <summary>Extensión sin normalizar (en anchos de hombro para webcam; igual a extension en mouse).</summary>
        public float rawExtension;

        /// <summary>Prensión (mano cerrada / clic sostenido).</summary>
        public bool grip;

        /// <summary>false si el sensor no puede saber si la mano está cerrada (se usa "mantener quieto" para agarrar).</summary>
        public bool gripKnown;

        /// <summary>Rotación de antebrazo en grados respecto a la posición neutra (para verter).</summary>
        public float wristAngle;

        /// <summary>false si no hay estimación de rotación de muñeca (se vierte manteniendo el bidón sobre la bomba).</summary>
        public bool wristKnown;

        /// <summary>Palancas de desplazamiento del panel (-1..1).</summary>
        public Vector2 joystick;

        /// <summary>Palanca de la bomba sostenida.</summary>
        public bool lever;

        /// <summary>Rapidez de la mano en unidades normalizadas por segundo.</summary>
        public float speed;
    }

    public enum HelmMode
    {
        Suelta = 0,        // mano abierta: no sostiene la palanca (el submarino baja)
        Estacionaria = 1,  // mano relajada/semicerrada con la palma hacia abajo: girar, subir; sin avanzar
        Avance = 2         // mano en vertical (como un "like" sin pulgar): avanza; pulgar arriba = retroceder
    }

    /// <summary>Timón de la cabina en primera persona (lo produce la mano del PANEL).</summary>
    [System.Serializable]
    public struct HelmFrame
    {
        public bool tracked;
        public HelmMode mode;
        /// <summary>-1 izquierda .. 1 derecha</summary>
        public float turn;
        /// <summary>-1 retroceder .. 1 avanzar</summary>
        public float throttle;
        /// <summary>-1 bajar .. 1 subir</summary>
        public float vertical;
    }

    /// <summary>Lectura bimanual completa de un frame.</summary>
    [System.Serializable]
    public struct BimanualFrame
    {
        /// <summary>Mano asistente: panel de mando (desplazamiento, palancas).</summary>
        public HandFrame panel;

        /// <summary>Mano manipuladora: brazo robótico (alcanzar, sujetar, verter, soltar).</summary>
        public HandFrame manip;

        /// <summary>Timón (solo primera persona).</summary>
        public HelmFrame helm;

        /// <summary>
        /// Panel de cabina con los PULGARES. Los proveedores (teclado o webcam) solo informan el estado
        /// sostenido de cada pulgar y si ambas manos están en la pose de reposo (relajadas, palma abajo);
        /// BimanualInput los interpreta y genera los eventos:
        ///   · un pulgar solo arriba  → mover la selección (-1 izquierda, 1 derecha)
        ///   · ambos pulgares arriba  → confirmar
        /// </summary>
        public int panelNavigate;
        public bool panelConfirm;
        public bool panelToggle;
        public bool thumbLeft;
        public bool thumbRight;
        /// <summary>Ambas manos relajadas con la palma abajo (en teclado siempre true).</summary>
        public bool panelHandsReady;
        /// <summary>Ambos pulgares arriba sostenidos (para mostrar el progreso de la confirmación).</summary>
        public float bothThumbs01;
        /// <summary>Meñique arriba con la mano del panel (atajo opcional).</summary>
        public bool pinkyPanel;

        /// <summary>Compensación de tronco normalizada (1 = en el umbral).</summary>
        public float trunkCompensation;
        public bool compensating;

        public bool fromWebcam;
        public float time;
    }

    /// <summary>Datos de contexto que necesitan los proveedores (p. ej. el mouse necesita saber dónde está el brazo).</summary>
    public struct InputContext
    {
        public Camera camera;
        public Vector3 armBaseWorld;
        public float armReachWorld;
        public float gamePlaneZ;

        // Primera persona: el brazo trabaja en un plano perpendicular a la mirada
        public bool firstPerson;
        public Vector3 armRight;
        public Vector3 armUp;
        public Vector3 armForward;
        public float armPlaneDepth;
    }
}
