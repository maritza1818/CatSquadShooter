using System;
using System.Collections.Generic;
using UnityEngine;

namespace CatSquad.RescateProfundo
{
    /// <summary>Gestos de mano que usa el juego en primera persona (webcam).</summary>
    public enum HandGesture
    {
        Desconocido = 0,
        Reposo,          // mano relajada, palma abajo (timón quieto; base para los pulgares del panel)
        ReposoPulgar,    // palma abajo con el pulgar levantado (mover la selección del panel)
        Abierta,         // palma abajo y la mano bien abierta (bajar)
        Vertical,        // mano de canto, como un "like" sin pulgar (avanzar)
        VerticalPulgar,  // mano de canto con el pulgar arriba (retroceder)
        Menique          // solo el meñique arriba (atajo opcional)
    }

    /// <summary>Plantilla grabada en la calibración por gesto (media de ~2 s de muestras).</summary>
    [Serializable]
    public class GestureTemplate
    {
        public HandGesture gesture;
        public BodySide side;
        public float[] features = new float[GestureFeatures.Count];
    }

    /// <summary>
    /// Rasgos de una mano que llegan del puente Python (Python solo mide; aquí se interpretan):
    /// [normal de la palma x, y, z, apertura 3D, pulgar separado de la palma, pulgar hacia arriba, meñique].
    /// </summary>
    public struct GestureFeatures
    {
        public const int Count = 7;
        public static readonly float[] Weights = { 1.2f, 1.2f, 1.2f, 1.5f, 1.4f, 1.2f, 1.0f };

        public float[] v;
        public bool valid;

        public static GestureFeatures From(float[] normal, float open3, float tlift, float tup, float[] ext)
        {
            GestureFeatures f = new GestureFeatures { v = new float[Count] };
            if (normal == null || normal.Length < 3) return f;
            f.v[0] = normal[0];
            f.v[1] = normal[1];
            f.v[2] = normal[2];
            f.v[3] = open3;
            f.v[4] = Mathf.Clamp(tlift, -1f, 1.5f);
            f.v[5] = Mathf.Clamp(tup, -1f, 1.5f);
            float pinky = 0f;
            if (ext != null && ext.Length >= 5) pinky = Mathf.Clamp((ext[4] - (ext[1] + ext[2] + ext[3]) / 3f) / 0.8f, -1f, 1f);
            f.v[6] = pinky;
            f.valid = true;
            return f;
        }
    }

    /// <summary>
    /// Reconocedor por plantillas: elige el gesto calibrado más parecido (distancia ponderada) para cada
    /// mano. Si el niño todavía no calibró, usa plantillas por defecto razonables. Como las manos de cada
    /// niño son distintas (sobre todo el lado afectado), la calibración graba SU versión de cada gesto.
    /// Un gesto nuevo solo se acepta cuando se mantiene un momento (evita parpadeos).
    /// </summary>
    public class GestureRecognizer
    {
        public float acceptDistance = 0.8f;
        public float holdToSwitch = 0.12f;

        private readonly List<GestureTemplate> templates = new List<GestureTemplate>();
        private readonly HandGesture[] current = new HandGesture[2];
        private readonly HandGesture[] candidate = new HandGesture[2];
        private readonly float[] candidateTime = new float[2];
        private readonly float[] lastDistance = new float[2];

        public HandGesture Get(BodySide side) { return current[(int)side]; }
        public float Distance(BodySide side) { return lastDistance[(int)side]; }

        public GestureRecognizer()
        {
            SetTemplates(null);
        }

        /// <summary>Plantillas calibradas; las que falten se completan con las de por defecto.</summary>
        public void SetTemplates(List<GestureTemplate> calibrated)
        {
            templates.Clear();
            if (calibrated != null) templates.AddRange(calibrated);
            foreach (BodySide side in new[] { BodySide.Izquierdo, BodySide.Derecho })
            {
                foreach (GestureTemplate d in Defaults(side))
                {
                    bool has = false;
                    foreach (GestureTemplate t in templates) if (t.gesture == d.gesture && t.side == side) { has = true; break; }
                    if (!has) templates.Add(d);
                }
            }
        }

        public static List<GestureTemplate> Defaults(BodySide side)
        {
            // Mano de canto: la palma mira al centro del cuerpo (x espejada: izquierda +1, derecha -1)
            float sx = side == BodySide.Izquierdo ? 1f : -1f;
            List<GestureTemplate> list = new List<GestureTemplate>
            {
                T(HandGesture.Reposo,         side, 0f, 1f, 0f, 0.25f, 0.0f, 0.0f, 0f),
                T(HandGesture.ReposoPulgar,   side, 0f, 1f, 0f, 0.25f, 0.6f, 0.5f, 0f),
                T(HandGesture.Abierta,        side, 0f, 1f, 0f, 0.9f,  0.1f, 0.1f, 0f),
                T(HandGesture.Vertical,       side, sx, 0f, 0f, 0.15f, 0.0f, 0.2f, 0f),
                T(HandGesture.VerticalPulgar, side, sx, 0f, 0f, 0.15f, 0.1f, 0.55f, 0f),
                T(HandGesture.Menique,        side, 0f, 1f, 0f, 0.3f,  0.0f, 0.0f, 0.7f),
            };
            return list;
        }

        private static GestureTemplate T(HandGesture g, BodySide side, params float[] f)
        {
            return new GestureTemplate { gesture = g, side = side, features = f };
        }

        public static float WeightedDistance(float[] a, float[] b)
        {
            float sum = 0f;
            int n = Mathf.Min(GestureFeatures.Count, Mathf.Min(a.Length, b.Length));
            for (int i = 0; i < n; i++)
            {
                float d = (a[i] - b[i]) * GestureFeatures.Weights[i];
                sum += d * d;
            }
            return Mathf.Sqrt(sum);
        }

        /// <summary>Clasifica sin memoria (útil para pruebas y para el panel de lectura en vivo).</summary>
        public HandGesture ClassifyRaw(BodySide side, GestureFeatures f, out float bestDistance)
        {
            bestDistance = float.MaxValue;
            HandGesture best = HandGesture.Desconocido;
            if (!f.valid) return best;
            foreach (GestureTemplate t in templates)
            {
                if (t.side != side || t.features == null) continue;
                float d = WeightedDistance(f.v, t.features);
                if (d < bestDistance) { bestDistance = d; best = t.gesture; }
            }
            if (bestDistance > acceptDistance) best = HandGesture.Desconocido;
            return best;
        }

        /// <summary>Clasifica con estabilidad: el cambio de gesto necesita sostenerse holdToSwitch segundos.</summary>
        public HandGesture Update(BodySide side, GestureFeatures f, float dt)
        {
            int i = (int)side;
            float d;
            HandGesture g = ClassifyRaw(side, f, out d);
            lastDistance[i] = d;
            if (g == current[i]) { candidateTime[i] = 0f; return current[i]; }
            if (g != candidate[i]) { candidate[i] = g; candidateTime[i] = 0f; }
            candidateTime[i] += dt;
            if (candidateTime[i] >= holdToSwitch || !f.valid)
            {
                current[i] = g;
                candidateTime[i] = 0f;
            }
            return current[i];
        }

        public static string Name(HandGesture g)
        {
            switch (g)
            {
                case HandGesture.Reposo: return "Reposo (palma abajo)";
                case HandGesture.ReposoPulgar: return "Pulgar levantado";
                case HandGesture.Abierta: return "Mano abierta";
                case HandGesture.Vertical: return "Mano de canto";
                case HandGesture.VerticalPulgar: return "De canto + pulgar";
                case HandGesture.Menique: return "Meñique arriba";
                default: return "—";
            }
        }
    }
}
