using UnityEngine;

namespace CatSquad.RescateProfundo
{
    /// <summary>Oscurece el ambiente con la profundidad: la mejora de Iluminación importa en la fosa.</summary>
    public class DepthAmbience : MonoBehaviour
    {
        public Transform target;
        public Camera cam;
        public Light sun;
        public float surfaceY = -5f;
        public float deepY = -80f;

        public Color shallowFog = new Color(0.16f, 0.42f, 0.55f);
        public Color deepFog = new Color(0.01f, 0.03f, 0.08f);
        public Color skyColor = new Color(0.55f, 0.8f, 0.95f);
        public float shallowDensity = 0.012f;
        public float deepDensity = 0.03f;
        public Color shallowAmbient = new Color(0.45f, 0.6f, 0.7f);
        public Color deepAmbient = new Color(0.04f, 0.06f, 0.1f);
        public float shallowSun = 1.1f;
        public float deepSun = 0.05f;

        private void Start()
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            if (cam != null) cam.clearFlags = CameraClearFlags.SolidColor;
        }

        private void LateUpdate()
        {
            if (target == null) return;
            float y = target.position.y;
            float t = Mathf.InverseLerp(surfaceY, deepY, y);
            t = t * t * (3f - 2f * t);

            Color fog = Color.Lerp(shallowFog, deepFog, t);
            RenderSettings.fogColor = fog;
            RenderSettings.fogDensity = Mathf.Lerp(shallowDensity, deepDensity, t);
            RenderSettings.ambientLight = Color.Lerp(shallowAmbient, deepAmbient, t);
            if (sun != null) sun.intensity = Mathf.Lerp(shallowSun, deepSun, t);

            if (cam != null)
            {
                // Sobre la superficie se ve el cielo; bajo el agua, el color del agua.
                float camY = cam.transform.position.y;
                cam.backgroundColor = camY > surfaceY + 1.5f ? skyColor : fog;
            }
        }
    }
}
