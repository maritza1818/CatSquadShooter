using System;
using UnityEngine;

namespace CatSquad.RescateProfundo
{
    /// <summary>
    /// Proyectil con Rigidbody (lata, lastre o boya). La gravedad NO es la de Unity: cada objeto aplica la
    /// suya según el medio en que está (el lab permite una gravedad distinta por objeto):
    ///   · aire : g·escala (sin arrastre)                         → parábola exacta
    ///   · agua : g_ef = g·(1 − ρ_agua/ρ_objeto) y arrastre k·v   → lastre cae en arco, boya sube en arco
    /// El paso es el mismo que usa TrajectoryMath.Step para predecir, así la predicción coincide con el vuelo.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class Projectile : MonoBehaviour
    {
        public ProjectileKind kind = ProjectileKind.Lata;
        [Tooltip("Densidad del objeto (kg/m³). Agua = 1000: menos densa sube, más densa se hunde")]
        public float density = 1300f;
        [Tooltip("En el aire la gravedad se multiplica (el lab: los juegos suelen usar más de 9.81)")]
        public float airGravityScale = 1.5f;
        public float waterGravityScale = 1f;
        public float airDrag = 0f;
        [Tooltip("Arrastre lineal bajo el agua (1/s)")]
        public float waterDrag = 0.8f;
        public float lifetime = 12f;

        public event Action<Projectile, LaunchTarget> Resolved;

        public PredictedImpact Prediction { get; private set; }
        public float LaunchTime { get; private set; }
        public float LaunchPower { get; set; }
        public bool Assisted { get; set; }
        public bool IsResolved { get { return resolved; } }
        public bool Caught { get; private set; }
        /// <summary>Lata en el agua lista para que un pez se la coma.</summary>
        public bool IsFoodInWater { get { return kind == ProjectileKind.Lata && resolved && !Caught && WaterZone.PointIsUnderwater(transform.position); } }
        public Rigidbody Body { get { return rb; } }

        private Rigidbody rb;
        private bool launched;
        private bool resolved;
        private float slowTimer;
        private bool touched;

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            rb.useGravity = false;
            rb.isKinematic = true;
        }

        // ------------------------------------------------------------------ medio

        public static TrajectoryMedium MediumFor(Vector3 p, float density, float airScale, float waterScale, float airDrag, float waterDrag)
        {
            if (WaterZone.PointIsUnderwater(p))
            {
                return new TrajectoryMedium(TrajectoryMath.EffectiveGravity(Physics.gravity * waterScale, density), waterDrag);
            }
            return new TrajectoryMedium(Physics.gravity * airScale, airDrag);
        }

        public TrajectoryMedium MediumAt(Vector3 p)
        {
            return MediumFor(p, density, airGravityScale, waterGravityScale, airDrag, waterDrag);
        }

        /// <summary>Delegado para TrajectoryMath.Simulate con los parámetros de este proyectil.</summary>
        public TrajectoryMath.MediumAt MediumFunction()
        {
            float d = density, a = airGravityScale, w = waterGravityScale, ad = airDrag, wd = waterDrag;
            return p => MediumFor(p, d, a, w, ad, wd);
        }

        // ------------------------------------------------------------------ vuelo

        public void Launch(Vector3 velocity, Transform ignoreRoot, PredictedImpact prediction)
        {
            transform.SetParent(null, true);
            gameObject.SetActive(true);
            if (ignoreRoot != null)
            {
                Collider[] mine = GetComponentsInChildren<Collider>();
                foreach (Collider other in ignoreRoot.GetComponentsInChildren<Collider>())
                    foreach (Collider c in mine) Physics.IgnoreCollision(c, other, true);
            }
            rb.isKinematic = false;
            rb.useGravity = false;
            rb.linearDamping = 0f;
            rb.angularDamping = 0.5f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.linearVelocity = velocity;
            rb.angularVelocity = UnityEngine.Random.insideUnitSphere * 4f;
            LaunchTime = Time.time;
            launched = true;
            Prediction = prediction;
            if (prediction != null)
            {
                prediction.source = this;
                prediction.kind = kind;
                PredictedImpacts.Register(prediction);
            }
        }

        private void FixedUpdate()
        {
            if (!launched || rb.isKinematic) return;
            TrajectoryMedium m = MediumAt(rb.position);
            // a = g_medio − k·v  (mismo paso que TrajectoryMath.Step)
            rb.AddForce(m.gravity - m.drag * rb.linearVelocity, ForceMode.Acceleration);

            if (resolved) return;
            // la lata que cae al agua ya no dio en la cesta (pero los peces se la comen)
            if (kind == ProjectileKind.Lata && WaterZone.PointIsUnderwater(rb.position)) { Resolve(null); return; }
            if (touched && rb.linearVelocity.magnitude < 0.6f)
            {
                slowTimer += Time.fixedDeltaTime;
                if (slowTimer > 0.4f) Resolve(null);
            }
            else slowTimer = 0f;
        }

        private void Update()
        {
            if (!launched) return;
            if (Time.time - LaunchTime > lifetime) { Despawn(); return; }
            // la lata ya está en el agua: los peces persiguen la lata real
            if (resolved && !Caught && kind == ProjectileKind.Lata && Prediction != null && WaterZone.PointIsUnderwater(transform.position))
            {
                Prediction.entersWater = true;
                Prediction.waterEntry = transform.position;
                Prediction.waterEntryTime = Time.time;
            }
        }

        private void OnTriggerEnter(Collider other) { TryTarget(other); }

        private void OnCollisionEnter(Collision c)
        {
            touched = true;
            TryTarget(c.collider);
        }

        private void TryTarget(Collider other)
        {
            if (resolved || !launched) return;
            LaunchTarget t = other.GetComponentInParent<LaunchTarget>();
            if (t == null || !t.Accepts(this)) return;
            Caught = true;
            t.Receive(this);
            Resolve(t);
        }

        private void Resolve(LaunchTarget target)
        {
            if (resolved) return;
            resolved = true;
            // una lata que no entró en la cesta sigue siendo COMIDA para los peces: su predicción sigue activa
            bool food = kind == ProjectileKind.Lata && target == null;
            if (Prediction != null && !food) PredictedImpacts.Unregister(Prediction);
            if (Resolved != null) Resolved(this, target);
        }

        /// <summary>Un pez se comió la lata (o la cesta la consumió).</summary>
        public void Consume()
        {
            Resolve(null);
            Despawn();
        }

        /// <summary>La cesta / el gancho lo sujetan (deja de moverse).</summary>
        public void Hold(Transform parent)
        {
            launched = false;
            rb.isKinematic = true;
            transform.SetParent(parent, true);
        }

        public void Despawn()
        {
            if (!resolved) Resolve(null);
            PredictedImpacts.Unregister(this);
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            PredictedImpacts.Unregister(this);
        }
    }
}
