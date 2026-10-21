using System.Collections.Generic;
using UnityEngine;

namespace CatSquad.RescateProfundo
{
    /// <summary>
    /// Dibuja la trayectoria predicha como en el lab: una fila de puntos que se achican a lo largo del arco
    /// (dotMaxScale → dotMinScale), un anillo donde va a caer y, opcional, el arco "fantasma" de la asistencia.
    /// </summary>
    public class TrajectoryDots : MonoBehaviour
    {
        public int dotCount = 30;
        public float dotMaxScale = 0.3f;
        public float dotMinScale = 0.08f;
        public Material dotMaterial;
        public Color dotColor = new Color(1f, 1f, 1f, 0.9f);
        public Color onTargetColor = new Color(0.45f, 1f, 0.55f, 1f);
        public Transform landingMarker;
        public LineRenderer ghostLine;
        public int layer = 0;

        private readonly List<Transform> dots = new List<Transform>();
        private readonly List<Renderer> renderers = new List<Renderer>();
        private MaterialPropertyBlock block;
        private bool built;

        private void Build()
        {
            if (built) return;
            built = true;
            block = new MaterialPropertyBlock();
            if (dotMaterial == null)
            {
                Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
                if (sh == null) sh = Shader.Find("Unlit/Color");
                dotMaterial = new Material(sh);
            }
            for (int i = 0; i < dotCount; i++)
            {
                GameObject d = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                d.name = "Punto" + i;
                d.layer = layer;
                Collider c = d.GetComponent<Collider>();
                if (c != null) Destroy(c);
                d.transform.SetParent(transform, false);
                Renderer r = d.GetComponent<Renderer>();
                r.sharedMaterial = dotMaterial;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                d.SetActive(false);
                dots.Add(d.transform);
                renderers.Add(r);
            }
            if (landingMarker != null) landingMarker.gameObject.SetActive(false);
            if (ghostLine != null) ghostLine.enabled = false;
        }

        /// <summary>Coloca los puntos repartidos a lo largo de los puntos predichos.</summary>
        public void Show(IList<Vector3> points, bool onTarget)
        {
            Build();
            int n = points != null ? points.Count : 0;
            int shown = Mathf.Min(dotCount, n);
            Color c = onTarget ? onTargetColor : dotColor;
            for (int i = 0; i < dots.Count; i++)
            {
                bool on = i < shown;
                if (dots[i].gameObject.activeSelf != on) dots[i].gameObject.SetActive(on);
                if (!on) continue;
                // índice repartido sobre todo el arco
                float u = shown > 1 ? i / (float)(shown - 1) : 0f;
                int idx = Mathf.Clamp(Mathf.RoundToInt(u * (n - 1)), 0, n - 1);
                dots[i].position = points[idx];
                float s = Mathf.Lerp(dotMaxScale, dotMinScale, u);
                dots[i].localScale = Vector3.one * s;
                block.SetColor("_BaseColor", c);
                block.SetColor("_Color", c);
                renderers[i].SetPropertyBlock(block);
            }
        }

        public void ShowLanding(bool visible, Vector3 point, Vector3 normal, bool onTarget)
        {
            Build();
            if (landingMarker == null) return;
            landingMarker.gameObject.SetActive(visible);
            if (!visible) return;
            landingMarker.position = point + normal * 0.03f;
            landingMarker.rotation = Quaternion.FromToRotation(Vector3.up, normal.sqrMagnitude > 0.01f ? normal : Vector3.up);
            Renderer r = landingMarker.GetComponentInChildren<Renderer>();
            if (r != null)
            {
                Color c = onTarget ? onTargetColor : new Color(1f, 0.75f, 0.3f, 1f);
                block.SetColor("_BaseColor", c);
                block.SetColor("_Color", c);
                r.SetPropertyBlock(block);
            }
        }

        public void ShowGhost(IList<Vector3> points)
        {
            Build();
            if (ghostLine == null) return;
            if (points == null || points.Count < 2) { ghostLine.enabled = false; return; }
            ghostLine.enabled = true;
            ghostLine.positionCount = points.Count;
            for (int i = 0; i < points.Count; i++) ghostLine.SetPosition(i, points[i]);
        }

        public void HideGhost()
        {
            if (ghostLine != null) ghostLine.enabled = false;
        }

        public void Hide()
        {
            Build();
            foreach (Transform d in dots) if (d.gameObject.activeSelf) d.gameObject.SetActive(false);
            if (landingMarker != null) landingMarker.gameObject.SetActive(false);
            HideGhost();
        }
    }
}
