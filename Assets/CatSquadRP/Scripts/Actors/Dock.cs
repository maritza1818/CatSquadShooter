using System.Collections.Generic;
using UnityEngine;

namespace CatSquad.RescateProfundo
{
    /// <summary>
    /// Muelle en la superficie: punto de partida, destino del auto-emerger y lugar
    /// donde los gatitos rescatados se quedan sentados (trofeo visible sesión tras sesión).
    /// </summary>
    public class Dock : MonoBehaviour
    {
        public static Dock Instance { get; private set; }

        public Transform[] seats = new Transform[0];
        public Transform waterEntry;
        public float rescueRadius = 5f;

        private int seatsUsed;

        public bool SubAtDock { get; private set; }

        private void Awake()
        {
            Instance = this;
        }

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

        private void Update()
        {
            SubmarineController sub = GameManager.Instance != null ? GameManager.Instance.submarine : null;
            Transform point = GameManager.Instance != null ? GameManager.Instance.dockPoint : null;
            if (sub == null || point == null) return;

            Vector3 d = sub.transform.position - point.position;
            d.z = 0f;
            SubAtDock = d.magnitude <= rescueRadius && sub.IsSurfaced;

            if (SubAtDock && RescuedKitten.Followers.Count > 0)
            {
                List<RescuedKitten> copy = new List<RescuedKitten>(RescuedKitten.Followers);
                foreach (RescuedKitten k in copy) k.GoToDock(this);
            }
        }
    }
}
