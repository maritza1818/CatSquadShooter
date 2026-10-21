using System;
using UnityEngine;

namespace CatSquad.RescateProfundo
{
    public enum GrabKind { Material, Combustible, Escombro, Cerrojo, Otro }

    /// <summary>
    /// Objeto que el brazo manipulador puede sujetar, trasladar y soltar.
    /// Mientras está libre, se hunde despacio bajo el agua (o cae rápido en el aire).
    /// Mientras está sujeto, sigue a la pinza con velocidad (así sigue chocando con las paredes).
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Grabbable : MonoBehaviour
    {
        private static readonly System.Collections.Generic.List<Grabbable> all = new System.Collections.Generic.List<Grabbable>();
        public static System.Collections.Generic.IReadOnlyList<Grabbable> All { get { return all; } }

        public GrabKind kind = GrabKind.Otro;
        public string displayName = "Objeto";

        [Header("Transporte")]
        [Tooltip("Qué tan firmemente sigue a la pinza (1/s)")]
        public float followStrength = 14f;
        public float maxCarrySpeed = 9f;
        [Tooltip("Desplazamiento desde la pinza al centro del objeto")]
        public Vector3 holdOffset = new Vector3(0f, -0.35f, 0f);

        [Header("Flotabilidad")]
        public float sinkAcceleration = 0.9f;
        public float waterDrag = 2.2f;
        public float airGravity = 12f;

        public bool IsHeld { get { return holder != null; } }
        public ManipulatorArm Holder { get { return holder; } }
        public Vector3 SpawnPosition { get; private set; }
        public Vector3 GrabStartPosition { get; private set; }
        public float DistanceFromSpawn { get { return Vector3.Distance(transform.position, SpawnPosition); } }
        public Rigidbody Body { get { return rb; } }

        public event Action<Grabbable> Grabbed;
        public event Action<Grabbable, bool> Released;

        protected Rigidbody rb;
        protected ManipulatorArm holder;
        private Collider[] ownColliders;
        private Quaternion targetRotation = Quaternion.identity;

        protected virtual void OnEnable()
        {
            if (!all.Contains(this)) all.Add(this);
        }

        protected virtual void OnDisable()
        {
            all.Remove(this);
        }

        protected virtual void Awake()
        {
            rb = GetComponent<Rigidbody>();
            rb.useGravity = false;
            rb.constraints = GameManager.FirstPerson
                ? RigidbodyConstraints.None
                : RigidbodyConstraints.FreezePositionZ | RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationY;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            ownColliders = GetComponentsInChildren<Collider>();
            SpawnPosition = transform.position;
        }

        public virtual bool CanBeGrabbed { get { return enabled && gameObject.activeInHierarchy && holder == null; } }

        /// <summary>Si es true, el objeto se "usa" al agarrarlo (p. ej. la caja de chatarra se abre).</summary>
        public virtual bool ConsumeOnGrab { get { return false; } }

        public void NotifyGrabbed(ManipulatorArm arm, float extension)
        {
            holder = arm;
            GrabStartPosition = transform.position;
            SetIgnoreSubmarine(true);
            rb.linearDamping = 0f;
            OnGrabbed(arm, extension);
            if (Grabbed != null) Grabbed(this);
        }

        public void NotifyReleased(bool slipped)
        {
            ManipulatorArm arm = holder;
            holder = null;
            SetIgnoreSubmarine(false);
            if (slipped)
            {
                rb.linearVelocity = new Vector3(UnityEngine.Random.Range(-0.6f, 0.6f), -1.6f, 0f);
                rb.angularVelocity = new Vector3(0f, 0f, UnityEngine.Random.Range(-3f, 3f));
            }
            OnReleased(arm, slipped);
            if (Released != null) Released(this, slipped);
        }

        protected virtual void OnGrabbed(ManipulatorArm arm, float extension) { }
        protected virtual void OnReleased(ManipulatorArm arm, bool slipped) { }

        /// <summary>El brazo indica cada frame el ángulo de muñeca para que el objeto rote con la pinza.</summary>
        public void SetHeldAngle(float degrees)
        {
            targetRotation = Quaternion.Euler(0f, 0f, degrees);
        }

        /// <summary>Primera persona: orientación completa que debe tener el objeto sujeto.</summary>
        public void SetHeldRotation(Quaternion rotation)
        {
            targetRotation = rotation;
        }

        private void SetIgnoreSubmarine(bool ignore)
        {
            if (GameManager.Instance == null || GameManager.Instance.submarine == null) return;
            Collider[] subColliders = GameManager.Instance.submarine.GetComponentsInChildren<Collider>();
            foreach (Collider a in ownColliders)
            {
                if (a == null) continue;
                foreach (Collider b in subColliders)
                {
                    if (b != null) Physics.IgnoreCollision(a, b, ignore);
                }
            }
        }

        protected virtual void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;

            if (holder != null)
            {
                Vector3 target = holder.GripPoint + holdOffset;
                if (!GameManager.FirstPerson) target.z = 0f;
                Vector3 desired = (target - rb.position) * followStrength;
                rb.linearVelocity = Vector3.ClampMagnitude(desired, maxCarrySpeed);

                Quaternion delta = targetRotation * Quaternion.Inverse(rb.rotation);
                float angle;
                Vector3 axis;
                delta.ToAngleAxis(out angle, out axis);
                if (angle > 180f) angle -= 360f;
                rb.angularVelocity = Mathf.Abs(angle) > 0.5f ? axis * (angle * Mathf.Deg2Rad * 8f) : Vector3.zero;
                return;
            }

            // Libre: flotabilidad simple
            bool underwater = WaterZone.PointIsUnderwater(rb.position);
            Vector3 v = rb.linearVelocity;
            if (underwater)
            {
                v += Vector3.down * sinkAcceleration * dt;
                v *= 1f / (1f + waterDrag * dt);
                rb.angularVelocity *= 1f / (1f + waterDrag * dt);
            }
            else
            {
                v += Vector3.down * airGravity * dt;
            }
            rb.linearVelocity = v;
        }
    }
}
