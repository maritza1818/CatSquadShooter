using UnityEngine;

namespace CatSquad.RescateProfundo
{
    /// <summary>
    /// "Mapa" de la cabina = la cámara lateral del primer prototipo (vista 2.5D), que sigue al submarino
    /// y se dibuja en la pantalla del panel junto a la palanca (RenderTexture).
    /// Mira desde un costado de la avenida; el plano cercano recorta todo lo que está delante del corte
    /// (edificios de la vereda cercana), así que se ve el "corte" del nivel como en la vista lateral.
    /// No dibuja la capa de la cabina; sí la del gato piloto, que en primera persona no se ve.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class SideMapCamera : MonoBehaviour
    {
        public Transform target;
        [Tooltip("Igual que la cámara de la vista lateral del Avance 1")]
        public Vector3 offset = new Vector3(0f, 1.2f, -19f);
        public float smoothTime = 0.25f;
        public Vector2 minBounds = new Vector2(-1000f, -1000f);
        public Vector2 maxBounds = new Vector2(1000f, 1000f);
        [Tooltip("Z del corte: lo que está más cerca de la cámara que esto no se dibuja")]
        public float cutZ = -5.05f;
        public RenderTexture texture;

        private Camera cam;
        private Vector3 velocity;
        private static readonly float[] zoomDistances = { -19f, -32f, -12f };
        private static readonly string[] zoomNames = { "normal", "lejos", "cerca" };
        private int zoomIndex;

        /// <summary>Botón ZOOM del panel: alterna la distancia de la cámara lateral.</summary>
        public string CycleZoom()
        {
            zoomIndex = (zoomIndex + 1) % zoomDistances.Length;
            offset.z = zoomDistances[zoomIndex];
            return zoomNames[zoomIndex];
        }

        public string ZoomName { get { return zoomNames[zoomIndex]; } }

        private void Awake()
        {
            cam = GetComponent<Camera>();
            if (texture != null) cam.targetTexture = texture;
        }

        private void Start()
        {
            if (target != null) transform.position = Desired();
        }

        private Vector3 Desired()
        {
            Vector3 p = target.position + offset;
            p.x = Mathf.Clamp(p.x, minBounds.x, maxBounds.x);
            p.y = Mathf.Clamp(p.y, minBounds.y, maxBounds.y);
            p.z = offset.z;
            return p;
        }

        private void LateUpdate()
        {
            if (target == null) return;
            transform.position = Vector3.SmoothDamp(transform.position, Desired(), ref velocity, smoothTime);
            transform.rotation = Quaternion.identity;
            cam.nearClipPlane = Mathf.Max(0.3f, cutZ - transform.position.z);
            // mismo color de fondo que el agua (o el cielo) que ve el piloto
            cam.backgroundColor = RenderSettings.fogColor;
        }
    }
}
