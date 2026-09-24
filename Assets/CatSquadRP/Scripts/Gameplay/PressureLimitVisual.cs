using UnityEngine;

namespace CatSquad.RescateProfundo
{
    /// <summary>Franja translúcida que marca el límite de presión actual.</summary>
    public class PressureLimitVisual : MonoBehaviour
    {
        public float minX = 0f;
        public float maxX = 120f;
        public float thickness = 0.25f;

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
            float y = GameManager.Instance != null ? GameManager.Instance.DepthLimitY : -1000f;
            bool visible = y > -500f;
            gameObject.SetActive(true);
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true)) r.enabled = visible;
            transform.position = new Vector3((minX + maxX) * 0.5f, y, -0.9f);
            transform.localScale = new Vector3(maxX - minX, thickness, 0.05f);
        }

        private void Update()
        {
            // pulso suave
            float a = 0.5f + 0.5f * Mathf.Sin(Time.time * 2f);
            Vector3 s = transform.localScale;
            s.y = thickness * (0.8f + 0.4f * a);
            transform.localScale = s;
        }
    }
}
