using System;
using UnityEngine;

namespace CatSquad.RescateProfundo
{
    /// <summary>
    /// Fragmento de material que vuela a la compuerta de carga del submarino usando SEEK.
    /// Sale disparado en una dirección aleatoria y el Seek lo va curvando hacia el
    /// submarino (se nota el "sobrepaso" característico de Seek, que aquí no importa:
    /// el objetivo es ATRAPARLO, no detenerse). Al tocar la compuerta se absorbe.
    /// </summary>
    public class MaterialShard : MonoBehaviour
    {
        public SteeringBody body = new SteeringBody { maxSpeed = 13f, maxForce = 30f, mass = 1f };
        public Transform target;
        public float absorbRadius = 0.55f;
        public float maxLifetime = 6f;
        public float spin = 360f;

        public static event Action<MaterialShard> AnyAbsorbed;

        private float age;
        private bool absorbed;

        public static MaterialShard Spawn(MaterialShard prefab, Material material, Vector3 position, Vector3 initialVelocity, Transform target)
        {
            MaterialShard shard;
            if (prefab != null)
            {
                shard = Instantiate(prefab, position, UnityEngine.Random.rotation);
            }
            else
            {
                GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "Fragmento";
                Collider c = go.GetComponent<Collider>();
                if (c != null) Destroy(c);
                go.transform.position = position;
                go.transform.rotation = UnityEngine.Random.rotation;
                go.transform.localScale = Vector3.one * 0.22f;
                if (material != null) go.GetComponent<Renderer>().sharedMaterial = material;

                TrailRenderer trail = go.AddComponent<TrailRenderer>();
                trail.time = 0.25f;
                trail.startWidth = 0.16f;
                trail.endWidth = 0f;
                if (material != null) trail.sharedMaterial = material;
                shard = go.AddComponent<MaterialShard>();
            }

            shard.target = target;
            shard.body.position = position;
            shard.body.velocity = initialVelocity;
            shard.body.planeZ = -0.3f;
            shard.body.lockToPlane = !GameManager.FirstPerson;
            return shard;
        }

        private void Update()
        {
            if (absorbed) return;
            float dt = Time.deltaTime;
            age += dt;

            if (target == null)
            {
                Destroy(gameObject);
                return;
            }

            Vector3 goal = target.position;
            if (body.lockToPlane) goal.z = body.planeZ;

            // ---- SEEK (la fuerza máxima crece con el tiempo para que nunca orbite para siempre)
            body.maxForce = 30f + age * 25f;
            body.Integrate(body.Seek(goal), dt);
            transform.position = body.position;
            transform.Rotate(Vector3.one, spin * dt, Space.Self);

            if (Vector3.Distance(body.position, goal) <= absorbRadius || age >= maxLifetime)
            {
                Absorb();
            }
        }

        private void Absorb()
        {
            absorbed = true;
            if (GameManager.Instance != null) GameManager.Instance.AddMaterials(1);
            if (AnyAbsorbed != null) AnyAbsorbed(this);
            AudioFx.Play(AudioFx.Instance != null ? AudioFx.Instance.shardCollect : null, transform.position, 0.5f, 0.9f + UnityEngine.Random.value * 0.4f);
            Destroy(gameObject);
        }
    }
}
