using UnityEngine;

namespace CatSquad.RescateProfundo
{
    /// <summary>
    /// Cabina en primera persona: anima la palanca con el timón y controla la pantallita del mapa
    /// que baja del techo en un brazo mecánico (se muestra y se quita a voluntad).
    /// El mapa es una versión simplificada de la vista lateral del Avance 1, con el submarino encima.
    /// </summary>
    public class CockpitRig : MonoBehaviour
    {
        [Header("Palanca")]
        public Transform lever;
        public float leverTilt = 20f;
        public float leverLift = 0.03f;

        [Header("Mapa abatible")]
        public Transform mapArm;
        public Renderer mapScreen;
        [Tooltip("Rotación extra (grados en X local) para guardar el brazo en el techo")]
        public float stowAngle = 100f;
        public float deploySpeed = 2.5f;
        public bool startOpen = true;

        [Header("Marcadores en el mapa")]
        public Transform subMarker;
        public Transform subHeading;
        public Transform guideMarker;
        [Tooltip("Objetivo del riel (modo automático o ayuda)")]
        public Transform stationMarker;
        [Tooltip("Rectángulo del mundo (X, Y) que representa la textura del mapa")]
        public Vector2 mapMin;
        public Vector2 mapMax;

        public bool MapOpen { get; private set; }

        private Quaternion leverRest;
        private Vector3 leverRestPos;
        private Quaternion armDeployed;
        private float deployT;

        private void Start()
        {
            if (lever != null)
            {
                leverRest = lever.localRotation;
                leverRestPos = lever.localPosition;
            }
            if (mapArm != null) armDeployed = mapArm.localRotation;
            MapOpen = startOpen;
            deployT = MapOpen ? 1f : 0f;
            ApplyArm();
        }

        public void ToggleMap()
        {
            MapOpen = !MapOpen;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            BimanualFrame frame = BimanualInput.Instance != null ? BimanualInput.Instance.Current : new BimanualFrame();
            if (frame.panelToggle) ToggleMap();

            // ---- palanca: se inclina como la mano del panel
            if (lever != null)
            {
                HelmFrame h = frame.helm;
                Quaternion target = leverRest * Quaternion.Euler(h.throttle * leverTilt, 0f, -h.turn * leverTilt);
                lever.localRotation = Quaternion.Slerp(lever.localRotation, target, 1f - Mathf.Exp(-12f * dt));
                Vector3 p = leverRestPos + Vector3.up * h.vertical * leverLift;
                lever.localPosition = Vector3.Lerp(lever.localPosition, p, 1f - Mathf.Exp(-12f * dt));
            }

            // ---- brazo del mapa
            deployT = Mathf.MoveTowards(deployT, MapOpen ? 1f : 0f, deploySpeed * dt);
            ApplyArm();

            // ---- marcadores
            UpdateMarkers();
        }

        private void ApplyArm()
        {
            if (mapArm == null) return;
            float e = deployT * deployT * (3f - 2f * deployT);
            mapArm.localRotation = armDeployed * Quaternion.Euler((1f - e) * stowAngle, 0f, 0f);
            if (mapScreen != null) mapScreen.enabled = deployT > 0.05f;
        }

        private Vector3 ToScreen(Vector3 world)
        {
            float u = Mathf.InverseLerp(mapMin.x, mapMax.x, world.x) - 0.5f;
            float v = Mathf.InverseLerp(mapMin.y, mapMax.y, world.y) - 0.5f;
            return new Vector3(u, v, -0.02f);
        }

        private void UpdateMarkers()
        {
            GameManager gm = GameManager.Instance;
            if (gm == null) return;

            if (subMarker != null && gm.submarine != null)
            {
                subMarker.localPosition = ToScreen(gm.submarine.transform.position);
                if (subHeading != null)
                {
                    // La vista del mapa es lateral: la proa apunta a izquierda o derecha según hacia dónde mira
                    float fx = gm.submarine.Forward.x;
                    subHeading.localPosition = new Vector3(Mathf.Clamp(fx, -1f, 1f) * 0.8f, 0f, 0f);
                }
                float pulse = 1f + 0.25f * Mathf.Sin(Time.time * 6f);
                subMarker.localScale = Vector3.one * 0.035f * pulse;
            }

            if (guideMarker != null && gm.guide != null)
            {
                guideMarker.localPosition = ToScreen(gm.guide.transform.position);
            }

            if (stationMarker != null)
            {
                Vector3 target = Vector3.zero;
                bool show = RailPilot.Instance != null && RailPilot.Instance.TryGetTarget(out target);
                if (stationMarker.gameObject.activeSelf != show) stationMarker.gameObject.SetActive(show);
                if (show)
                {
                    stationMarker.localPosition = ToScreen(target);
                    stationMarker.localScale = Vector3.one * 0.03f * (1f + 0.35f * Mathf.Abs(Mathf.Sin(Time.time * 4f)));
                }
            }
        }
    }
}
