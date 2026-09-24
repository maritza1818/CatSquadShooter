using System.Collections.Generic;
using UnityEngine;

namespace CatSquad.RescateProfundo
{
    public enum GuideMode { Quieta, Viajando, Acompanando }

    /// <summary>
    /// Capitana Pelusa, la gata guía del tutorial.
    ///
    /// Uso de ARRIVE: se desplaza de un punto del tutorial al siguiente y FRENA
    /// suavemente junto a lo que va a explicar (una guía que se pasa de largo o
    /// frena en seco se ve mal y distrae). Para rutas con esquinas usa PathFollower:
    /// Seek en los puntos intermedios + Arrive en el último.
    /// Cuando acompaña al jugador, hace Arrive a un punto del rastro del submarino.
    /// </summary>
    [RequireComponent(typeof(SteeringAgent))]
    public class GuideCat : MonoBehaviour
    {
        public string guideName = "Capitana Pelusa";

        [Header("Arrive")]
        public float travelSpeed = 4.5f;
        public float slowingRadius = 3f;
        public float followDistance = 1.8f;
        public float followHeight = 1.5f;

        [Header("Pistas")]
        public float hintAfterSeconds = 45f;
        public Transform pointerArrow;

        public GuideMode Mode { get; private set; }
        public bool TutorialActive { get; set; }
        public bool HintsEnabled { get; set; }

        private SteeringAgent agent;
        private readonly PathFollower path = new PathFollower { waypointRadius = 1.4f, slowingRadius = 3f, stopRadius = 0.1f };
        private Vector3 holdPoint;
        private float idleTimer;
        private float pointerTimer;
        private Vector3 pointerTarget;
        private float reactionCooldown;
        private readonly RaycastHit[] hits = new RaycastHit[8];

        public SteeringAgent Agent { get { return agent; } }

        private void Awake()
        {
            agent = GetComponent<SteeringAgent>();
            agent.body.maxSpeed = travelSpeed;
            holdPoint = transform.position;
            Mode = GuideMode.Quieta;
        }

        private void OnEnable()
        {
            MaterialCrate.AnyCrateOpened += OnCrateOpened;
            RescuedKitten.AnyFreed += OnKittenFreed;
            RescuedKitten.AnyRescued += OnKittenRescued;
        }

        private void OnDisable()
        {
            MaterialCrate.AnyCrateOpened -= OnCrateOpened;
            RescuedKitten.AnyFreed -= OnKittenFreed;
            RescuedKitten.AnyRescued -= OnKittenRescued;
        }

        private void Start()
        {
            if (GameManager.Instance != null && GameManager.Instance.submarine != null)
            {
                GameManager.Instance.submarine.PressureWarning += OnPressure;
            }
            foreach (PumpStation p in PumpStation.All) p.Completed += OnPumpCompleted;
            if (pointerArrow != null) pointerArrow.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ API

        public void Say(string text)
        {
            if (GameHUD.Instance != null) GameHUD.Instance.ShowDialog(guideName, text);
        }

        public void Silence()
        {
            if (GameHUD.Instance != null) GameHUD.Instance.HideDialog();
        }

        public void TravelTo(IEnumerable<Vector3> points)
        {
            path.SetPath(points);
            path.slowingRadius = slowingRadius;
            Mode = GuideMode.Viajando;
        }

        public void TravelTo(Vector3 point)
        {
            TravelTo(new[] { point });
        }

        public void TravelTo(Transform[] points)
        {
            List<Vector3> list = new List<Vector3>();
            foreach (Transform t in points) if (t != null) list.Add(t.position);
            TravelTo(list);
        }

        public bool HasArrived
        {
            get { return Mode != GuideMode.Viajando || path.Arrived(agent.Position, agent.Velocity, 0.5f); }
        }

        public void StayHere()
        {
            holdPoint = agent.Position;
            Mode = GuideMode.Quieta;
        }

        public void FollowPlayer()
        {
            Mode = GuideMode.Acompanando;
        }

        public void PointAt(Vector3 target, float seconds)
        {
            pointerTarget = target;
            pointerTimer = seconds;
        }

        public void ResetIdle()
        {
            idleTimer = 0f;
        }

        public void TeleportTo(Vector3 p)
        {
            agent.Teleport(p);
            holdPoint = p;
        }

        // ------------------------------------------------------------------ comportamiento

        private void Update()
        {
            float dt = Time.deltaTime;
            SubmarineController sub = GameManager.Instance != null ? GameManager.Instance.submarine : null;
            if (reactionCooldown > 0f) reactionCooldown -= dt;

            switch (Mode)
            {
                case GuideMode.Viajando:
                    agent.body.maxSpeed = travelSpeed;
                    agent.FollowPath(path, dt);
                    if (path.Arrived(agent.Position, agent.Velocity, 0.3f))
                    {
                        holdPoint = path.FinalTarget;
                        Mode = GuideMode.Quieta;
                    }
                    break;

                case GuideMode.Quieta:
                    agent.ArriveAt(holdPoint, 1.5f, 0.05f, dt);
                    if (sub != null && agent.Velocity.magnitude < 0.3f)
                    {
                        agent.LookTowardsX(sub.transform.position.x - agent.Position.x);
                    }
                    break;

                case GuideMode.Acompanando:
                    if (sub == null) { agent.Brake(dt); break; }
                    agent.body.maxSpeed = travelSpeed * 1.3f;
                    agent.ArriveAt(FollowTarget(sub), slowingRadius, 0.1f, dt);
                    break;
            }

            UpdateHints(dt, sub);
            UpdatePointer(dt);
        }

        private Vector3 FollowTarget(SubmarineController sub)
        {
            Vector3 onTrail = sub.GetPointBehind(followDistance);
            Vector3 raised = onTrail + Vector3.up * followHeight;
            Vector3 origin = sub.transform.position;
            origin.z = 0f;
            Vector3 dir = raised - origin;
            int count = Physics.RaycastNonAlloc(origin, dir.normalized, hits, dir.magnitude + 0.6f, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                if (hits[i].collider.GetComponentInParent<SubmarineController>() != null) continue;
                if (hits[i].collider.GetComponentInParent<Grabbable>() != null) continue;
                if (hits[i].collider.GetComponentInParent<Grate>() != null) continue;
                return onTrail + Vector3.up * 0.4f;
            }
            raised.z = agent.body.planeZ;
            return raised;
        }

        // ------------------------------------------------------------------ pistas y reacciones positivas

        private void UpdateHints(float dt, SubmarineController sub)
        {
            if (!HintsEnabled || TutorialActive || sub == null) { idleTimer = 0f; return; }
            idleTimer += dt;
            if (idleTimer < hintAfterSeconds) return;
            idleTimer = 0f;

            Vector3 target;
            string text;
            if (FindHintTarget(sub.transform.position, out target, out text))
            {
                Say(text);
                PointAt(target, 6f);
            }
        }

        private bool FindHintTarget(Vector3 from, out Vector3 target, out string text)
        {
            target = from;
            text = null;
            float best = float.MaxValue;

            foreach (MaterialCrate c in MaterialCrate.Crates)
            {
                if (c.Opened || !c.gameObject.activeInHierarchy) continue;
                float d = Vector3.Distance(from, c.transform.position);
                if (d < best && d < 25f) { best = d; target = c.transform.position; text = "¡Veo chatarra brillante por allá! Estira bien el brazo para sacar más fragmentos."; }
            }
            foreach (PumpStation p in PumpStation.All)
            {
                if (p.Complete) continue;
                float d = Vector3.Distance(from, p.transform.position);
                if (d < best * 0.6f) { best = d; target = p.transform.position; text = "Esa bomba todavía necesita combustible. ¡Busca un bidón rojo!"; }
            }
            return text != null;
        }

        private void UpdatePointer(float dt)
        {
            if (pointerArrow == null) return;
            pointerTimer -= dt;
            bool show = pointerTimer > 0f;
            if (pointerArrow.gameObject.activeSelf != show) pointerArrow.gameObject.SetActive(show);
            if (!show) return;
            Vector3 dir = pointerTarget - agent.Position;
            dir.z = 0f;
            if (dir.sqrMagnitude < 0.01f) return;
            dir.Normalize();
            pointerArrow.position = agent.Position + dir * 1.2f + Vector3.back * 0.5f;
            pointerArrow.rotation = Quaternion.FromToRotation(Vector3.up, dir);
        }

        private void React(string text)
        {
            if (TutorialActive || reactionCooldown > 0f) return;
            reactionCooldown = 8f;
            idleTimer = 0f;
            Say(text);
        }

        private void OnCrateOpened(MaterialCrate crate, int shards)
        {
            idleTimer = 0f;
            if (shards >= 5) React("¡Guau, qué estirada! ¡Cinco fragmentos de una vez!");
            else if (shards >= 4) React("¡Muy bien estirado! Mientras más lejos llegues, más fragmentos salen.");
        }

        private void OnKittenFreed(RescuedKitten k)
        {
            idleTimer = 0f;
            reactionCooldown = 0f;
            React("¡Liberaste a " + k.kittenName + "! Te seguirá hasta el muelle. ¡Llévalo a la superficie!");
        }

        private void OnKittenRescued(RescuedKitten k)
        {
            reactionCooldown = 0f;
            React("¡" + k.kittenName + " está a salvo en el muelle! Gracias, capitán.");
        }

        private void OnPumpCompleted(PumpStation p)
        {
            idleTimer = 0f;
            reactionCooldown = 0f;
            React("¡La " + p.displayName.ToLower() + " terminó su trabajo!");
        }

        private void OnPressure()
        {
            reactionCooldown = 0f;
            React("¡Uf! El casco no aguanta más presión. Con la mejora de Profundidad podremos bajar más.");
        }
    }
}
