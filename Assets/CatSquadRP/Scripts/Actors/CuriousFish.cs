using UnityEngine;

namespace CatSquad.RescateProfundo
{
    /// <summary>
    /// Pez curioso (ambiente). Deambula cerca de su casa y, si ve el foco del submarino,
    /// hace SEEK hacia la luz (como los peces reales atraídos por la luz). Si queda
    /// demasiado cerca del casco, hace FLEE (Seek invertido) para no chocar.
    /// La mejora de Iluminación amplía el alcance del foco: atrae peces desde más lejos.
    /// </summary>
    [RequireComponent(typeof(SteeringAgent))]
    public class CuriousFish : MonoBehaviour
    {
        public float wanderRadius = 4f;
        public float wanderSpeed = 1.2f;
        public float seekSpeed = 3.2f;
        public float fleeDistance = 1.7f;
        public float lightAttraction = 1f;
        [Tooltip("Lab 05: distancia a la que el pez se entera de que cae comida")]
        public float foodSenseRadius = 16f;

        private SteeringAgent agent;
        private Vector3 home;
        private Vector3 wanderTarget;
        private float retargetTimer;
        private Vector3 personalOffset;

        private void Awake()
        {
            agent = GetComponent<SteeringAgent>();
            home = transform.position;
            personalOffset = new Vector3(Random.Range(-0.8f, 0.8f), Random.Range(-0.6f, 0.6f), 0f);
            PickWanderTarget();
        }

        private void PickWanderTarget()
        {
            Vector2 r = Random.insideUnitCircle * wanderRadius;
            float rz = agent != null && !agent.body.lockToPlane ? Random.Range(-wanderRadius, wanderRadius) * 0.8f : 0f;
            wanderTarget = home + new Vector3(r.x, r.y * 0.5f, rz);
            retargetTimer = Random.Range(2.5f, 5f);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            SubmarineController sub = GameManager.Instance != null ? GameManager.Instance.submarine : null;

            if (sub != null)
            {
                Vector3 toSub = sub.transform.position - agent.Position;
                if (!GameManager.FirstPerson) toSub.z = 0f;

                // 1) Demasiado cerca del casco: FLEE
                if (toSub.magnitude < fleeDistance)
                {
                    agent.body.maxSpeed = seekSpeed * 1.3f;
                    agent.debugMode = SteeringMode.Flee;
                    agent.Step(agent.body.Flee(sub.transform.position), dt);
                    return;
                }

                // 2) LAB 05 · PREDICCIÓN: cae una lata. El pez no persigue la lata en el aire: va con ARRIVE
                //    al punto donde la lata ENTRARÁ al agua (calculado con la fórmula al lanzarla) y la espera ahí.
                PredictedImpact food = PredictedImpacts.Nearest(ProjectileKind.Lata, agent.Position, foodSenseRadius, true);
                if (food != null && food.entersWater && food.source != null && !food.source.Caught)
                {
                    bool inWater = food.source.IsFoodInWater;
                    Vector3 meal = inWater ? food.source.transform.position
                                           : food.waterEntry + Vector3.down * 0.9f + personalOffset * 0.6f;
                    if (agent.body.lockToPlane) meal.z = agent.body.planeZ;
                    agent.body.maxSpeed = seekSpeed * 1.25f;
                    agent.ArriveAt(meal, 2f, 0.1f, dt);
                    if (inWater && Vector3.Distance(agent.Position, food.source.transform.position) < 0.9f)
                    {
                        food.source.Consume();
                        AudioFx.Play(AudioFx.Instance != null ? AudioFx.Instance.bubblePop : null, agent.Position, 0.5f, 1.6f);
                    }
                    return;
                }

                // 3) Ve la luz del foco: SEEK hacia un punto delante del foco
                Light lamp = sub.headlight;
                if (lamp != null && lamp.enabled && WaterZone.PointIsUnderwater(agent.Position))
                {
                    Vector3 lampPos = lamp.transform.position;
                    Vector3 toFish = agent.Position - lampPos;
                    if (!GameManager.FirstPerson) toFish.z = 0f;
                    Vector3 forward = lamp.transform.forward;
                    if (!GameManager.FirstPerson) forward.z = 0f;
                    bool inRange = toFish.magnitude < lamp.range * lightAttraction;
                    bool inCone = forward.sqrMagnitude > 0.01f && Vector3.Angle(forward, toFish) < lamp.spotAngle * 0.6f;
                    if (inRange && inCone)
                    {
                        Vector3 lure = lampPos + forward.normalized * 2.6f + personalOffset;
                        if (agent.body.lockToPlane) lure.z = agent.body.planeZ;
                        agent.body.maxSpeed = seekSpeed;
                        agent.SeekTo(lure, dt);
                        return;
                    }
                }
            }

            // 4) Deambular alrededor de casa (Arrive suave a puntos al azar)
            retargetTimer -= dt;
            if (retargetTimer <= 0f || Vector3.Distance(agent.Position, wanderTarget) < 0.4f) PickWanderTarget();
            agent.body.maxSpeed = wanderSpeed;
            agent.ArriveAt(wanderTarget, 1.5f, 0.2f, dt);
            agent.debugMode = SteeringMode.Wander;
        }
    }
}
