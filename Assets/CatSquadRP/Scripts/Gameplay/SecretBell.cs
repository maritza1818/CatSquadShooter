using System;
using System.Collections.Generic;
using UnityEngine;

namespace CatSquad.RescateProfundo
{
    /// <summary>
    /// Cascabel dorado secreto (primera persona). Está lejos de los rieles del modo automático,
    /// así que solo lo encuentra quien MANEJA por su cuenta: premia explorar con la mano del panel.
    /// Se recoge pasando con el submarino a través de él; suelta fragmentos que vuelan a la cabina (Seek).
    /// </summary>
    [RequireComponent(typeof(SphereCollider))]
    public class SecretBell : MonoBehaviour
    {
        private static readonly List<SecretBell> all = new List<SecretBell>();
        public static IReadOnlyList<SecretBell> All { get { return all; } }
        public static event Action<SecretBell> AnyFound;

        public string secretId = "cascabel";
        public Transform visual;
        public Light glow;
        public Material shardMaterial;
        public int shards = 6;
        public float spinSpeed = 70f;

        public bool Found { get; private set; }

        private Vector3 basePosition;
        private float collectTimer;

        private void OnEnable() { if (!all.Contains(this)) all.Add(this); }
        private void OnDisable() { all.Remove(this); }

        private void Start()
        {
            GetComponent<SphereCollider>().isTrigger = true;
            basePosition = visual != null ? visual.localPosition : Vector3.zero;
            if (GameManager.Instance != null && GameManager.Instance.IsSecretFound(secretId))
            {
                Found = true;
                gameObject.SetActive(false);
            }
        }

        private void Update()
        {
            if (visual != null)
            {
                visual.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.World);
                if (!Found) visual.localPosition = basePosition + Vector3.up * Mathf.Sin(Time.time * 1.8f) * 0.15f;
            }
            if (glow != null && !Found) glow.intensity = 1.6f + 0.6f * Mathf.Sin(Time.time * 4f);

            if (Found)
            {
                collectTimer += Time.deltaTime;
                if (visual != null) visual.localScale = Vector3.one * Mathf.Max(0.001f, 1f - collectTimer / 0.5f);
                if (glow != null) glow.intensity = Mathf.Max(0f, 3f * (1f - collectTimer / 0.5f));
                if (collectTimer > 0.6f) gameObject.SetActive(false);
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (Found || other.GetComponentInParent<SubmarineController>() == null) return;
            Collect();
        }

        private void Collect()
        {
            Found = true;
            GameManager gm = GameManager.Instance;
            if (gm != null) gm.MarkSecretFound(secretId);

            Transform target = null;
            if (gm != null && gm.submarine != null) target = gm.submarine.cargoPort != null ? gm.submarine.cargoPort : gm.submarine.transform;
            for (int i = 0; i < shards; i++)
            {
                float a = (i / (float)shards) * Mathf.PI * 2f;
                Vector3 burst = new Vector3(Mathf.Cos(a), 0.6f + Mathf.Sin(a) * 0.5f, Mathf.Sin(a)) * 3.5f;
                MaterialShard.Spawn(null, shardMaterial, transform.position, burst, target);
            }
            AudioFx.Play(AudioFx.Instance != null ? AudioFx.Instance.chime : null, transform.position);
            if (GameHUD.Instance != null && gm != null)
            {
                GameHUD.Instance.Toast("¡Cascabel secreto! (" + gm.SecretsFound + " de " + TotalInScene + ")", 4f);
            }
            if (AnyFound != null) AnyFound(this);
        }

        private static int total;

        /// <summary>Cascabeles de la escena (encontrados o no).</summary>
        public static int TotalInScene { get { return total; } }

        private void Awake() { total++; }
        private void OnDestroy() { total = Mathf.Max(0, total - 1); }
    }
}
