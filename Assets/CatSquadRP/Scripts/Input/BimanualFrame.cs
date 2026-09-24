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

    /// <summary>Lectura bimanual completa de un frame.</summary>
    [System.Serializable]
    public struct BimanualFrame
    {
        /// <summary>Mano asistente: panel de mando (desplazamiento, palancas).</summary>
        public HandFrame panel;

        /// <summary>Mano manipuladora: brazo robótico (alcanzar, sujetar, verter, soltar).</summary>
        public HandFrame manip;

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
    }
}
