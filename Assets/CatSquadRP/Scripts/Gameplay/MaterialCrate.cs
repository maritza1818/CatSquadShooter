using System;
using UnityEngine;

namespace CatSquad.RescateProfundo
{
    /// <summary>
    /// Caja de chatarra brillante. Al sujetarla se abre y suelta fragmentos que
    /// vuelan solos al submarino con SEEK.
    /// "Recolección de dosis variable": la cantidad de fragmentos depende de cuánto
    /// extendió el niño el brazo RESPECTO A SU PROPIO ALCANCE calibrado.
    /// </summary>
    public class MaterialCrate : Grabbable
    {
        [Header("Fragmentos")]
        public MaterialShard shardPrefab;
        public Material shardMaterial;
        [Tooltip("Umbrales de extensión normalizada → cantidad de fragmentos (1 + nº de umbrales superados)")]
        public float[] extensionThresholds = { 0.45f, 0.6f, 0.75f, 0.9f };
        public float burstSpeed = 4f;
        public bool isTutorialCrate;
        [Tooltip("Corriente que mantiene al submarino a 70-90 % del alcance del brazo")]
        public GameObject currentField;

        public bool Opened { get; private set; }
        public int LastShardCount { get; private set; }

        public static event Action<MaterialCrate, int> AnyCrateOpened;

        private static readonly System.Collections.Generic.List<MaterialCrate> all = new System.Collections.Generic.List<MaterialCrate>();
        public static System.Collections.Generic.IReadOnlyList<MaterialCrate> Crates { get { return all; } }

        protected override void OnEnable() { base.OnEnable(); if (!all.Contains(this)) all.Add(this); }
        protected override void OnDisable() { base.OnDisable(); all.Remove(this); }

        public override bool ConsumeOnGrab { get { return true; } }
        public override bool CanBeGrabbed { get { return base.CanBeGrabbed && !Opened; } }

        private float shrinkTimer;
        private Vector3 scaleAtOpen = Vector3.one;

        protected override void Awake()
        {
            base.Awake();
            rb.isKinematic = true; // la caja no se mueve: se abre donde está
        }

        protected override void FixedUpdate()
        {
            // Sin física de flotación: está fija en su nicho.
        }

        public int ShardsForExtension(float extension)
        {
            int count = 1;
            for (int i = 0; i < extensionThresholds.Length; i++)
            {
                if (extension >= extensionThresholds[i]) count++;
            }
            return count;
        }

        protected override void OnGrabbed(ManipulatorArm arm, float extension)
        {
            if (Opened) return;
            Opened = true;
            scaleAtOpen = transform.localScale;
            if (currentField != null) currentField.SetActive(false);
            int count = ShardsForExtension(extension);
            LastShardCount = count;

            Transform target = null;
            if (GameManager.Instance != null && GameManager.Instance.submarine != null)
            {
                target = GameManager.Instance.submarine.cargoPort != null
                    ? GameManager.Instance.submarine.cargoPort
                    : GameManager.Instance.submarine.transform;
            }

            for (int i = 0; i < count; i++)
            {
                float angle = (i / (float)count) * Mathf.PI * 2f + UnityEngine.Random.value;
                float bz = GameManager.FirstPerson ? UnityEngine.Random.Range(-1f, 1f) : 0f;
                Vector3 burst = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle) + 0.5f, bz) * burstSpeed;
                MaterialShard.Spawn(shardPrefab, shardMaterial, transform.position + Vector3.up * 0.3f, burst, target);
            }

            if (AnyCrateOpened != null) AnyCrateOpened(this, count);
            AudioFx.Play(AudioFx.Instance != null ? AudioFx.Instance.crateOpen : null, transform.position);
        }

        private void Update()
        {
            if (!Opened) return;
            shrinkTimer += Time.deltaTime;
            float s = Mathf.Clamp01(1f - shrinkTimer / 0.6f);
            transform.localScale = scaleAtOpen * Mathf.Max(0.001f, s);
            if (shrinkTimer > 0.6f) gameObject.SetActive(false);
        }
    }
}
