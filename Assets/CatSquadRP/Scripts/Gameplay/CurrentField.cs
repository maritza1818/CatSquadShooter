using UnityEngine;

namespace CatSquad.RescateProfundo
{
    /// <summary>
    /// "Corriente" alrededor de un objetivo: una esfera que empuja al SUBMARINO pero que
    /// la PINZA atraviesa (se comporta como una rejilla). Garantiza que cada objetivo quede
    /// entre el 70 % y el 90 % del alcance del brazo, sin importar desde dónde se acerque el niño.
    /// Como el alcance del brazo corresponde al alcance calibrado del niño, esa distancia es
    /// también el 70–90 % de SU alcance real (y crece con él).
    /// </summary>
    [RequireComponent(typeof(SphereCollider))]
    public class CurrentField : MonoBehaviour
    {
        public Material ringMaterial;
        public int ringSegments = 56;
        public float ringWidth = 0.07f;
        public float spinSpeed = 25f;

        private LineRenderer ring;
        private SphereCollider sphere;
        private float angle;

        private void Awake()
        {
            sphere = GetComponent<SphereCollider>();
            if (GetComponent<Grate>() == null) gameObject.AddComponent<Grate>();
            BuildRing();
        }

        private void Start()
        {
            // Los objetos que se transportan con la pinza sí pueden cruzar la corriente
            foreach (Grabbable g in Grabbable.All)
            {
                if (g == null) continue;
                foreach (Collider c in g.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(sphere, c, true);
            }
        }

        private void BuildRing()
        {
            ring = gameObject.AddComponent<LineRenderer>();
            ring.useWorldSpace = false;
            ring.loop = true;
            ring.positionCount = ringSegments;
            ring.startWidth = ringWidth;
            ring.endWidth = ringWidth;
            ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (ringMaterial != null) ring.sharedMaterial = ringMaterial;
            UpdateRing();
        }

        private void UpdateRing()
        {
            float r = sphere.radius;
            for (int i = 0; i < ringSegments; i++)
            {
                float a = angle * Mathf.Deg2Rad + i * Mathf.PI * 2f / ringSegments;
                // pequeña ondulación para que parezca agua en movimiento
                float wobble = 1f + 0.03f * Mathf.Sin(a * 6f + Time.time * 2f);
                ring.SetPosition(i, new Vector3(Mathf.Cos(a) * r * wobble, Mathf.Sin(a) * r * wobble, -0.5f));
            }
        }

        private void Update()
        {
            angle += spinSpeed * Time.deltaTime;
            UpdateRing();
        }
    }
}
