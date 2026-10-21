using UnityEngine;

namespace CatSquad.RescateProfundo
{
    /// <summary>Franja translúcida que marca el límite de presión actual.</summary>
    public class PressureLimitVisual : MonoBehaviour
    {
        public float minX = 0f;
        public float maxX = 120f;
        public float thickness = 0.25f;
        [Tooltip("Profundidad (Z) de la franja: fina en la vista lateral, del ancho del complejo en primera persona")]
        public float depthZ = 0.05f;
        public float zPosition = -0.9f;

        private void Start()
        {
            if (GameManager.Instance != null) GameManager.Instance.UpgradesChanged += Refresh;
            Refresh();
        }

        private void OnDestroy()
        {
            if (GameManager.Instance != null) GameManager.Instance.UpgradesChanged -= Refresh;
        }

        private void Refresh()
        {
            if (GameManager.FirstPerson) return; // en la ciudad se actualiza en Update
            float y = GameManager.Instance != null ? GameManager.Instance.DepthLimitY : -1000f;
            bool visible = y > -500f;
            gameObject.SetActive(true);
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true)) r.enabled = visible;
            transform.position = new Vector3((minX + maxX) * 0.5f, y, zPosition);
            transform.localScale = new Vector3(maxX - minX, thickness, depthZ);
        }

        private void Update()
        {
            // Ciudad: el límite depende de la superficie del agua donde está el submarino
            GameManager gm = GameManager.Instance;
            if (GameManager.FirstPerson && gm != null && gm.submarine != null)
            {
                Vector3 sp = gm.submarine.transform.position;
                WaterZone z = WaterZone.FindAt(sp);
                float y = gm.DepthLimitAt(sp);
                bool show = z != null && !float.IsInfinity(y) && y > -500f;
                foreach (Renderer r in GetComponentsInChildren<Renderer>(true)) r.enabled = show;
                if (show) transform.position = new Vector3((z.min.x + z.max.x) * 0.5f, y, zPosition);
                if (show) transform.localScale = new Vector3(z.max.x - z.min.x, transform.localScale.y, depthZ);
            }

            // pulso suave
            float a = 0.5f + 0.5f * Mathf.Sin(Time.time * 2f);
            Vector3 s = transform.localScale;
            s.y = thickness * (0.8f + 0.4f * a);
            transform.localScale = s;
        }
    }
}
