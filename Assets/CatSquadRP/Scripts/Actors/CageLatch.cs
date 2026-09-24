using UnityEngine;

namespace CatSquad.RescateProfundo
{
    /// <summary>Cerrojo de la jaula: está "atado" a su anclaje; al tirar lo suficiente, abre la jaula.</summary>
    public class CageLatch : Grabbable
    {
        public KittenCage cage;
        public float maxPull = 1.4f;
        public float pullToOpen = 1.0f;

        private Vector3 anchor;
        private bool detached;

        protected override void Awake()
        {
            base.Awake();
            kind = GrabKind.Cerrojo;
            anchor = transform.position;
        }

        public override bool CanBeGrabbed { get { return base.CanBeGrabbed && !detached; } }

        protected override void FixedUpdate()
        {
            if (detached)
            {
                base.FixedUpdate();
                return;
            }

            Vector3 target = IsHeld ? Holder.GripPoint + holdOffset : anchor;
            target.z = anchor.z;
            Vector3 offset = Vector3.ClampMagnitude(target - anchor, maxPull);
            rb.linearVelocity = (anchor + offset - rb.position) * 12f;
            rb.angularVelocity = Vector3.zero;

            if (IsHeld && Vector3.Distance(rb.position, anchor) >= pullToOpen)
            {
                detached = true;
                if (cage != null) cage.Open();
                if (Holder != null) Holder.Release(false);
            }
        }
    }
}
