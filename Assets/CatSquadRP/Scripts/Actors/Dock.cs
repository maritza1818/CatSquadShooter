using System.Collections.Generic;
using UnityEngine;

namespace CatSquad.RescateProfundo
{
    /// <summary>
    /// Lugar seguro donde los gatitos rescatados bajan del submarino.
    ///  - El MUELLE / BASE de la superficie (el único en la vista lateral): punto de partida y trofeo.
    ///  - Las BALSAS DE RESCATE de la ciudad (primera persona): una por barrio, flotan en su nivel de
    ///    agua (bajan cuando el barrio se drena) y también son el punto de partida de la sesión siguiente.
    /// </summary>
    public class Dock : MonoBehaviour
    {
        private static readonly List<Dock> all = new List<Dock>();
        public static IReadOnlyList<Dock> All { get { return all; } }

        /// <summary>El muelle principal (base).</summary>
        public static Dock Instance { get; private set; }

        public bool isBase = true;
        public string displayName = "Muelle";
        public Transform[] seats = new Transform[0];
        public Transform waterEntry;
        public float rescueRadius = 5f;
        [Tooltip("Balsa: flota en el nivel de esta zona")]
        public WaterZone floatZone;
        [Tooltip("Altura de la balsa sobre el agua")]
        public float floatOffset = 0.05f;
        [Tooltip("Punto donde se detiene el submarino (si no hay, el propio objeto)")]
        public Transform subPoint;

        private int seatsUsed;

        public bool SubAtDock { get; private set; }

        public string ZoneId
        {
            get
            {
                if (floatZone != null) return floatZone.zoneId;
                WaterZone z = WaterZone.FindAt(SubPoint);
                return z != null ? z.zoneId : "";
            }
        }

        /// <summary>Dónde se detiene el submarino para dejar a los gatitos.</summary>
        public Vector3 SubPoint
        {
            get
            {
                if (subPoint != null) return subPoint.position;
                if (isBase && GameManager.Instance != null && GameManager.Instance.dockPoint != null) return GameManager.Instance.dockPoint.position;
                return transform.position;
            }
        }

        private void Awake()
        {
            if (isBase) Instance = this;
        }

        private void OnEnable() { if (!all.Contains(this)) all.Add(this); }
        private void OnDisable() { all.Remove(this); }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public Vector3 ReserveSeat()
        {
            if (seats == null || seats.Length == 0) return transform.position + Vector3.up;
            Transform s = seats[Mathf.Min(seatsUsed, seats.Length - 1)];
            Vector3 p = s.position + Vector3.right * 0.35f * Mathf.Max(0, seatsUsed - seats.Length + 1);
            seatsUsed++;
            return p;
        }

        /// <summary>Balsa más cercana en la zona indicada (o cualquiera si no hay).</summary>
        public static Dock FindForZone(WaterZone zone, Vector3 near)
        {
            Dock best = null;
            float bestD = float.MaxValue;
            foreach (Dock d in all)
            {
                if (d == null) continue;
                bool sameZone = zone != null && (d.floatZone == zone || (d.floatZone == null && WaterZone.FindAt(d.SubPoint) == zone));
                float dist = Vector3.Distance(near, d.SubPoint) + (sameZone ? 0f : 10000f);
                if (dist < bestD) { bestD = dist; best = d; }
            }
            return best;
        }

        public static Dock FindById(string zoneId)
        {
            foreach (Dock d in all) if (d != null && d.ZoneId == zoneId) return d;
            return null;
        }

        private void Update()
        {
            // Balsa: sube y baja con el agua del barrio
            if (floatZone != null)
            {
                Vector3 p = transform.position;
                p.y = floatZone.Level + floatOffset + Mathf.Sin(Time.time * 1.3f + p.x) * 0.04f;
                transform.position = p;
            }

            SubmarineController sub = GameManager.Instance != null ? GameManager.Instance.submarine : null;
            if (sub == null) return;

            Vector3 d = sub.transform.position - SubPoint;
            if (!GameManager.FirstPerson) d.z = 0f;
            SubAtDock = d.magnitude <= rescueRadius && sub.IsSurfaced;

            if (SubAtDock && RescuedKitten.Followers.Count > 0)
            {
                List<RescuedKitten> copy = new List<RescuedKitten>(RescuedKitten.Followers);
                foreach (RescuedKitten k in copy) k.GoToDock(this);
            }
        }
    }
}
