using System;
using UnityEngine;

namespace CatSquad.RescateProfundo
{
    /// <summary>Compuerta que se desliza hacia arriba: la abre una bomba (vista lateral) o una esclusa (ciudad).</summary>
    public class FloodGate : MonoBehaviour
    {
        public string displayName = "Compuerta";
        public Transform door;
        public float openHeight = 8f;
        public float openDuration = 2.5f;
        public Collider blocker;

        public bool IsOpen { get; private set; }
        public event Action<FloodGate> Opened;

        private Vector3 closedLocal;
        private float t;
        private bool animating;

        private void Awake()
        {
            if (door == null) door = transform;
            closedLocal = door.localPosition;
            if (blocker == null) blocker = door.GetComponent<Collider>();
        }

        /// <summary>
        /// Abre o cierra la compuerta. Las compuertas de las esclusas se abren y cierran muchas veces;
        /// la de la vista lateral solo se abre una vez.
        /// </summary>
        public void SetOpen(bool open, bool instant)
        {
            bool changed = open != IsOpen;
            if (!changed && !instant) return;
            IsOpen = open;
            if (instant)
            {
                t = open ? 1f : 0f;
                animating = false;
                ApplyPosition();
                if (blocker != null) blocker.enabled = !open;
                return;
            }
            animating = true;
            AudioFx.Play(AudioFx.Instance != null ? AudioFx.Instance.gateOpen : null, transform.position);
            if (open && Opened != null) Opened(this);
        }

        private void Update()
        {
            if (!animating) return;
            float goal = IsOpen ? 1f : 0f;
            t = Mathf.MoveTowards(t, goal, Time.deltaTime / Mathf.Max(0.1f, openDuration));
            ApplyPosition();
            if (blocker != null)
            {
                if (IsOpen && t > 0.7f) blocker.enabled = false;
                else if (!IsOpen && t < 0.6f) blocker.enabled = true;
            }
            if (Mathf.Approximately(t, goal)) animating = false;
        }

        private void ApplyPosition()
        {
            float eased = t * t * (3f - 2f * t);
            door.localPosition = closedLocal + Vector3.up * openHeight * eased;
        }
    }
}
