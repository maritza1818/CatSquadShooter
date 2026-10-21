using System.Collections.Generic;
using CatSquad.RescateProfundo;
using UnityEngine;

namespace CatSquad.Muelle
{
    /// <summary>
    /// LAB 05 · Trajectory: la fila de puntos que predice el vuelo del ovillo.
    ///   PrepareDots()  → crea dotsNumber puntos hijos de dotsParent, del más grande (dotMaxScale) al más chico (dotMinScale).
    ///   UpdateDots()   → coloca el punto i en el instante t = i·dotSpacing con la fórmula del lab
    ///                    p(t) = p0 + v0·t + ½·g·t²  (v0 = fuerza / masa porque el empujón es un impulso).
    ///   Show() / Hide()→ muestra u oculta los puntos mientras se arrastra.
    /// Modo "rebotes" (tecla T): en vez de la fórmula, simula el vuelo en una escena de física paralela
    /// (PhysicsScene2D) y así los puntos siguen también los rebotes en los muros.
    /// </summary>
    public class Trajectory : MonoBehaviour
    {
        [SerializeField] int dotsNumber = 30;
        [SerializeField] GameObject dotsParent;
        [SerializeField] GameObject dotPrefab;
        [SerializeField] float dotSpacing = 0.06f;
        [SerializeField] [Range(0.01f, 0.3f)] float dotMinScale = 0.08f;
        [SerializeField] [Range(0.3f, 1f)] float dotMaxScale = 0.35f;
        [Tooltip("Los puntos que caen al agua se ocultan")]
        public float waterY = -2.5f;
        public TrajectorySimulator2D simulator;

        public bool UseSimulation { get; set; }
        public Vector2 LastLanding { get; private set; }

        private Transform[] dotsList;
        private Vector2 pos;
        private float timeStamp;
        private readonly List<Vector2> simPoints = new List<Vector2>();

        private void Start()
        {
            Hide();
            PrepareDots();
        }

        private void PrepareDots()
        {
            dotsList = new Transform[dotsNumber];
            float scale = dotMaxScale;
            float scaleFactor = dotsNumber > 1 ? (dotMaxScale - dotMinScale) / (dotsNumber - 1) : 0f;
            for (int i = 0; i < dotsNumber; i++)
            {
                dotsList[i] = Instantiate(dotPrefab, null).transform;
                dotsList[i].parent = dotsParent.transform;
                dotsList[i].localScale = Vector3.one * scale;
                if (scale > dotMinScale) scale -= scaleFactor;
            }
        }

        /// <summary>Puntos con la FÓRMULA del lab (sin choques).</summary>
        public void UpdateDots(Vector3 ballPos, Vector2 forceApplied, float mass, float gravityScale)
        {
            if (dotsList == null) return;
            if (UseSimulation && simulator != null)
            {
                UpdateDotsSimulated(ballPos, forceApplied / Mathf.Max(0.001f, mass));
                return;
            }
            Vector2 v0 = forceApplied / Mathf.Max(0.001f, mass);
            Vector2 g = Physics2D.gravity * gravityScale;
            timeStamp = dotSpacing;
            bool underwater = false;
            for (int i = 0; i < dotsNumber; i++)
            {
                pos = TrajectoryMath.PositionAt((Vector2)ballPos, v0, g, timeStamp);
                if (!underwater && pos.y < waterY) { underwater = true; LastLanding = pos; }
                dotsList[i].position = pos;
                dotsList[i].gameObject.SetActive(!underwater);
                timeStamp += dotSpacing;
            }
            if (!underwater) LastLanding = pos;
        }

        /// <summary>Puntos con la SIMULACIÓN (Physics2D en una escena paralela): incluye los rebotes.</summary>
        private void UpdateDotsSimulated(Vector3 ballPos, Vector2 v0)
        {
            int steps = Mathf.RoundToInt(dotSpacing / Time.fixedDeltaTime);
            simulator.Simulate(ballPos, v0, dotsNumber, Mathf.Max(1, steps), simPoints);
            for (int i = 0; i < dotsNumber; i++)
            {
                bool on = i < simPoints.Count && simPoints[i].y > waterY;
                dotsList[i].gameObject.SetActive(on);
                if (i < simPoints.Count) dotsList[i].position = simPoints[i];
            }
            if (simPoints.Count > 0) LastLanding = simPoints[simPoints.Count - 1];
        }

        public void Show()
        {
            dotsParent.SetActive(true);
        }

        public void Hide()
        {
            dotsParent.SetActive(false);
        }
    }
}
