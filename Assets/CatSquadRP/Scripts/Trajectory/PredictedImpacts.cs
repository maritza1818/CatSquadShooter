using System.Collections.Generic;
using UnityEngine;

namespace CatSquad.RescateProfundo
{
    public enum ProjectileKind
    {
        Lata = 0,    // lata de atún: se lanza en la superficie, por el aire
        Lastre = 1,  // pesa de hierro: bajo el agua cae (g_ef ≈ 0.87·g)
        Boya = 2     // boya de corcho: bajo el agua SUBE (g_ef negativa): parábola invertida
    }

    /// <summary>Impacto predicho de un proyectil en vuelo (lo calcula el proyectil al salir).</summary>
    public class PredictedImpact
    {
        public Projectile source;
        public ProjectileKind kind;
        public bool hits;
        public Vector3 point;
        /// <summary>Instante (Time.time) del impacto.</summary>
        public float impactTime;
        public bool entersWater;
        public Vector3 waterEntry;
        public float waterEntryTime;

        public float SecondsToImpact { get { return impactTime - Time.time; } }
        public float SecondsToWater { get { return waterEntryTime - Time.time; } }
    }

    /// <summary>
    /// Registro de impactos predichos. Es la aplicación "más simple" que menciona el lab:
    /// saber si un personaje será alcanzado y dónde caerá algo. Lo leen los peces (van con
    /// ARRIVE al punto donde caerá la comida) y la gata guía (hace FLEE del punto donde caerá un lastre).
    /// </summary>
    public static class PredictedImpacts
    {
        private static readonly List<PredictedImpact> active = new List<PredictedImpact>();

        public static IReadOnlyList<PredictedImpact> Active { get { return active; } }

        public static void Register(PredictedImpact p)
        {
            if (p != null && !active.Contains(p)) active.Add(p);
        }

        public static void Unregister(PredictedImpact p)
        {
            active.Remove(p);
        }

        public static void Unregister(Projectile source)
        {
            active.RemoveAll(p => p == null || p.source == source);
        }

        /// <summary>Impacto más cercano de un tipo a un punto (o null).</summary>
        public static PredictedImpact Nearest(ProjectileKind kind, Vector3 from, float maxDistance, bool useWaterEntry)
        {
            PredictedImpact best = null;
            float bestD = maxDistance;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                PredictedImpact p = active[i];
                if (p == null || p.source == null) { active.RemoveAt(i); continue; }
                if (p.kind != kind) continue;
                Vector3 target = useWaterEntry && p.entersWater ? p.waterEntry : p.point;
                float d = Vector3.Distance(from, target);
                if (d < bestD) { bestD = d; best = p; }
            }
            return best;
        }

        /// <summary>
        /// ¿Será alcanzado algo en <paramref name="position"/>? Devuelve el impacto peligroso más próximo en el
        /// tiempo cuyo punto de caída está a menos de <paramref name="radius"/> y ocurre antes de <paramref name="withinSeconds"/>.
        /// </summary>
        public static PredictedImpact Threat(Vector3 position, float radius, float withinSeconds)
        {
            PredictedImpact best = null;
            float bestT = withinSeconds;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                PredictedImpact p = active[i];
                if (p == null || p.source == null) { active.RemoveAt(i); continue; }
                if (p.kind != ProjectileKind.Lastre || !p.hits) continue;
                float t = p.SecondsToImpact;
                if (t < -0.2f || t > bestT) continue;
                if (Vector3.Distance(position, p.point) > radius) continue;
                bestT = t;
                best = p;
            }
            return best;
        }
    }
}
