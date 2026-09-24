using UnityEngine;

namespace CatSquad.RescateProfundo
{
    /// <summary>
    /// Envolvente de alcance: cuánto puede extender el niño la mano en cada dirección
    /// (8 sectores de 45°). Se mide en la calibración y crece sola durante el juego
    /// cuando el niño supera su marca ("se ajusta solo conforme el envolvente crece").
    /// </summary>
    [System.Serializable]
    public class ReachEnvelope
    {
        public const int Sectors = 8;
        public float[] values = new float[Sectors];

        public ReachEnvelope() { }

        public ReachEnvelope(float initial)
        {
            Reset(initial);
        }

        public void Reset(float value)
        {
            if (values == null || values.Length != Sectors) values = new float[Sectors];
            for (int i = 0; i < Sectors; i++) values[i] = value;
        }

        public bool IsValid
        {
            get
            {
                if (values == null || values.Length != Sectors) return false;
                for (int i = 0; i < Sectors; i++) if (values[i] <= 0.0001f) return false;
                return true;
            }
        }

        private static float SectorPosition(Vector2 dir)
        {
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg; // -180..180, 0 = derecha
            if (angle < 0f) angle += 360f;
            return angle / (360f / Sectors);
        }

        /// <summary>Alcance máximo interpolado en la dirección dada.</summary>
        public float Get(Vector2 dir)
        {
            if (!IsValid) return 1f;
            if (dir.sqrMagnitude < 1e-6f) return Average();
            float s = SectorPosition(dir);
            int a = Mathf.FloorToInt(s) % Sectors;
            int b = (a + 1) % Sectors;
            float t = s - Mathf.Floor(s);
            return Mathf.Lerp(values[a], values[b], t);
        }

        /// <summary>Registra una observación. Si supera el valor actual, el sector crece (suavemente).</summary>
        public bool Observe(Vector2 delta, float growthRate)
        {
            if (!IsValid) return false;
            float magnitude = delta.magnitude;
            if (magnitude < 1e-4f) return false;
            int sector = Mathf.RoundToInt(SectorPosition(delta)) % Sectors;
            if (magnitude > values[sector])
            {
                values[sector] = Mathf.Lerp(values[sector], magnitude, Mathf.Clamp01(growthRate));
                return true;
            }
            return false;
        }

        /// <summary>Durante la calibración: registra el máximo exacto.</summary>
        public void RecordMax(Vector2 delta)
        {
            if (values == null || values.Length != Sectors) values = new float[Sectors];
            float magnitude = delta.magnitude;
            if (magnitude < 1e-4f) return;
            int sector = Mathf.RoundToInt(SectorPosition(delta)) % Sectors;
            values[sector] = Mathf.Max(values[sector], magnitude);
        }

        /// <summary>Rellena sectores sin datos con el promedio de sus vecinos y aplica un mínimo.</summary>
        public void FillGaps(float minimum)
        {
            float max = 0f;
            for (int i = 0; i < Sectors; i++) max = Mathf.Max(max, values[i]);
            for (int i = 0; i < Sectors; i++)
            {
                if (values[i] < minimum)
                {
                    float left = values[(i + Sectors - 1) % Sectors];
                    float right = values[(i + 1) % Sectors];
                    float neighbour = Mathf.Max(left, right);
                    values[i] = Mathf.Max(minimum, neighbour > 0f ? neighbour * 0.85f : max * 0.7f);
                }
            }
        }

        public float Average()
        {
            if (values == null || values.Length == 0) return 1f;
            float sum = 0f;
            for (int i = 0; i < values.Length; i++) sum += values[i];
            return sum / values.Length;
        }

        public ReachEnvelope Clone()
        {
            ReachEnvelope e = new ReachEnvelope();
            e.values = (float[])values.Clone();
            return e;
        }
    }
}
