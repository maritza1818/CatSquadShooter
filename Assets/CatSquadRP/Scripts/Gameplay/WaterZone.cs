using System.Collections.Generic;
using UnityEngine;

namespace CatSquad.RescateProfundo
{
    /// <summary>
    /// Compartimento con nivel de agua propio (rectángulo en el plano XY).
    /// El submarino solo puede estar por debajo de la superficie: si el agua baja, flota;
    /// si una bomba la sube, puede alcanzar zonas superiores.
    /// Dos zonas conectadas funcionan como vasos comunicantes: cuando el agua de la zona
    /// conectada supera la altura de la conexión, esta zona se llena hasta el mismo nivel.
    /// </summary>
    public class WaterZone : MonoBehaviour
    {
        private static readonly List<WaterZone> all = new List<WaterZone>();
        public static IReadOnlyList<WaterZone> All { get { return all; } }

        public string zoneId = "zona";
        public string displayName = "Zona";

        [Header("Límites (mundo)")]
        public Vector2 min;
        public Vector2 max;

        [Header("Nivel de agua (Y mundial)")]
        [Tooltip("Nivel cuando nadie ha bombeado (seco = por debajo de min.y)")]
        public float baseLevel;
        public float maxLevel = 1000f;
        public float riseSpeed = 1.2f;

        [Header("Vasos comunicantes (opcional)")]
        public WaterZone connectedTo;
        [Tooltip("Altura de la abertura que conecta con la otra zona")]
        public float connectionY;

        [Header("Visual")]
        public Transform waterBody;
        public Transform surface;
        public float visualZFront = -1.15f;
        public float visualZBack = 2.9f;

        public float Level { get; private set; }
        public float TargetLevel { get; private set; }
        public bool IsRising { get { return Level < TargetLevel - 0.02f; } }
        public bool IsFull { get { return Level >= max.y - 0.05f; } }

        private float pumpedLevel = float.NegativeInfinity;

        private void OnEnable()
        {
            if (!all.Contains(this)) all.Add(this);
        }

        private void OnDisable()
        {
            all.Remove(this);
        }

        private void Awake()
        {
            Level = baseLevel;
            TargetLevel = baseLevel;
        }

        private void Start()
        {
            RecalculateTarget();
            Level = TargetLevel; // al cargar la escena el agua aparece ya en su sitio
            UpdateVisual();
        }

        /// <summary>Una bomba fija el nivel mínimo al que debe estar esta zona.</summary>
        public void SetPumpedLevel(float y, bool instant)
        {
            pumpedLevel = y;
            RecalculateTarget();
            if (instant) Level = TargetLevel;
        }

        private void RecalculateTarget()
        {
            float target = Mathf.Max(baseLevel, pumpedLevel);
            if (connectedTo != null && connectedTo.Level > connectionY)
            {
                target = Mathf.Max(target, connectedTo.Level);
            }
            TargetLevel = Mathf.Min(target, maxLevel);
        }

        private void Update()
        {
            RecalculateTarget();
            if (!Mathf.Approximately(Level, TargetLevel))
            {
                Level = Mathf.MoveTowards(Level, TargetLevel, riseSpeed * Time.deltaTime);
            }
            UpdateVisual();
        }

        public bool Contains(Vector3 p)
        {
            return p.x >= min.x && p.x <= max.x && p.y >= min.y && p.y <= max.y;
        }

        public bool IsUnderwater(Vector3 p)
        {
            return p.y < Level;
        }

        private void UpdateVisual()
        {
            float top = Mathf.Min(Level, max.y + 0.6f);
            float height = top - min.y;
            float depth = visualZBack - visualZFront;
            float zCenter = (visualZFront + visualZBack) * 0.5f;

            if (waterBody != null)
            {
                bool visible = height > 0.01f;
                if (waterBody.gameObject.activeSelf != visible) waterBody.gameObject.SetActive(visible);
                if (visible)
                {
                    waterBody.position = new Vector3((min.x + max.x) * 0.5f, min.y + height * 0.5f, zCenter);
                    waterBody.localScale = new Vector3(max.x - min.x, height, depth);
                }
            }

            if (surface != null)
            {
                bool visible = Level > min.y && Level < max.y - 0.05f;
                if (surface.gameObject.activeSelf != visible) surface.gameObject.SetActive(visible);
                if (visible)
                {
                    float wave = Mathf.Sin(Time.time * 1.7f + min.x) * 0.04f;
                    surface.position = new Vector3((min.x + max.x) * 0.5f, Level + wave, zCenter);
                    surface.localScale = new Vector3(max.x - min.x, 0.12f, depth);
                }
            }
        }

        /// <summary>Zona que contiene el punto (si hay varias, la más pequeña).</summary>
        public static WaterZone FindAt(Vector3 p)
        {
            WaterZone best = null;
            float bestArea = float.MaxValue;
            for (int i = 0; i < all.Count; i++)
            {
                WaterZone z = all[i];
                if (!z.Contains(p)) continue;
                float area = (z.max.x - z.min.x) * (z.max.y - z.min.y);
                if (area < bestArea) { best = z; bestArea = area; }
            }
            return best;
        }

        /// <summary>Altura de la superficie en ese punto (+infinito si no hay zona: se asume agua).</summary>
        public static float SurfaceAt(Vector3 p)
        {
            WaterZone z = FindAt(p);
            return z != null ? z.Level : float.PositiveInfinity;
        }

        public static bool PointIsUnderwater(Vector3 p)
        {
            return p.y < SurfaceAt(p);
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.5f);
            Vector3 c = new Vector3((min.x + max.x) * 0.5f, (min.y + max.y) * 0.5f, 0f);
            Gizmos.DrawWireCube(c, new Vector3(max.x - min.x, max.y - min.y, 0.1f));
            float lvl = Application.isPlaying ? Level : baseLevel;
            if (lvl < max.y)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawLine(new Vector3(min.x, lvl, 0f), new Vector3(max.x, lvl, 0f));
            }
        }
    }
}
