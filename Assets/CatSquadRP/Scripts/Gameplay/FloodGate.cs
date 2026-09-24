using System;
using UnityEngine;

namespace CatSquad.RescateProfundo
{
    /// <summary>Compuerta que se abre deslizándose hacia arriba cuando una bomba termina.</summary>
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

        public void SetOpen(bool open, bool instant)
        {
            if (!open || IsOpen) return;
            IsOpen = true;
            if (instant)
            {
                t = 1f;
                ApplyPosition();
                if (blocker != null) blocker.enabled = false;
                return;
            }
            animating = true;
            AudioFx.Play(AudioFx.Instance != null ? AudioFx.Instance.gateOpen : null, transform.position);
            if (Opened != null) Opened(this);
        }

        private void Update()
        {
            if (!animating) return;
            t = Mathf.Clamp01(t + Time.deltaTime / Mathf.Max(0.1f, openDuration));
            ApplyPosition();
            if (t > 0.7f && blocker != null) blocker.enabled = false;
            if (t >= 1f) animating = false;
        }

        private void ApplyPosition()
        {
            float eased = t * t * (3f - 2f * t);
            door.localPosition = closedLocal + Vector3.up * openHeight * eased;
        }
    }
}
