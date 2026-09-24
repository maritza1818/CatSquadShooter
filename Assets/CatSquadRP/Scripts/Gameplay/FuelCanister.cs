using System;
using UnityEngine;

namespace CatSquad.RescateProfundo
{
    /// <summary>
    /// Bidón de combustible: se sujeta, se TRANSPORTA (idealmente cruzando la línea media)
    /// y se VIERTE girando el antebrazo sobre la toma de una bomba.
    /// </summary>
    public class FuelCanister : Grabbable
    {
        [Header("Combustible")]
        public float fuel = 1f;
        public float pourRate = 0.35f;
        [Tooltip("Grados de rotación de muñeca para empezar a verter")]
        public float pourAngle = 55f;
        [Tooltip("Sin sensor de muñeca: segundos sobre la toma para verter")]
        public float dwellPourDelay = 1f;
        public Transform spout;
        public Material streamMaterial;
        public Transform fuelLevelVisual;

        public bool IsPouring { get; private set; }
        public bool WantsToPourButValveClosed { get; private set; }
        public PumpStation NearbyPump { get; private set; }

        public static event Action<FuelCanister> AnyEmptied;

        private LineRenderer stream;
        private float dwellTimer;
        private bool emptied;
        private float shrink;
        private Vector3 initialScale;
        private AudioSource pourAudio;

        protected override void Awake()
        {
            base.Awake();
            initialScale = transform.localScale;
            stream = gameObject.AddComponent<LineRenderer>();
            stream.positionCount = 2;
            stream.startWidth = 0.12f;
            stream.endWidth = 0.05f;
            stream.useWorldSpace = true;
            stream.enabled = false;
            if (streamMaterial != null) stream.sharedMaterial = streamMaterial;
        }

        private void Start()
        {
            if (streamMaterial != null) stream.sharedMaterial = streamMaterial;
            pourAudio = AudioFx.CreateLoop(gameObject, AudioFx.Instance != null ? AudioFx.Instance.pourLoop : null, 0.4f);
        }

        private void Update()
        {
            if (emptied)
            {
                shrink += Time.deltaTime / 0.5f;
                transform.localScale = initialScale * Mathf.Max(0.001f, 1f - shrink);
                if (shrink >= 1f) gameObject.SetActive(false);
                return;
            }

            IsPouring = false;
            WantsToPourButValveClosed = false;
            NearbyPump = null;

            if (IsHeld)
            {
                NearbyPump = FindPump();
                BimanualFrame frame = BimanualInput.Instance != null ? BimanualInput.Instance.Current : new BimanualFrame();
                bool tilted;
                if (frame.manip.wristKnown)
                {
                    tilted = Mathf.Abs(Holder.WristAngle) >= pourAngle;
                    dwellTimer = 0f;
                }
                else
                {
                    if (NearbyPump != null) dwellTimer += Time.deltaTime; else dwellTimer = 0f;
                    tilted = dwellTimer >= dwellPourDelay;
                }

                if (tilted && NearbyPump != null)
                {
                    if (NearbyPump.AcceptingFuel)
                    {
                        float accepted = NearbyPump.ReceiveFuel(Mathf.Min(fuel, pourRate * Time.deltaTime));
                        fuel -= accepted;
                        IsPouring = accepted > 0f;
                    }
                    else if (!NearbyPump.Complete)
                    {
                        WantsToPourButValveClosed = true;
                    }
                }

                if (fuel <= 0.001f)
                {
                    fuel = 0f;
                    emptied = true;
                    if (Holder != null) Holder.Release(false);
                    if (AnyEmptied != null) AnyEmptied(this);
                }
            }

            UpdateStream();

            if (fuelLevelVisual != null)
            {
                Vector3 s = fuelLevelVisual.localScale;
                s.y = Mathf.Max(0.001f, fuel);
                fuelLevelVisual.localScale = s;
            }

            if (pourAudio != null)
            {
                if (IsPouring && !pourAudio.isPlaying) pourAudio.Play();
                else if (!IsPouring && pourAudio.isPlaying) pourAudio.Stop();
            }
        }

        private PumpStation FindPump()
        {
            Vector3 from = spout != null ? spout.position : transform.position;
            PumpStation best = null;
            float bestDistance = float.MaxValue;
            foreach (PumpStation p in PumpStation.All)
            {
                if (p == null || p.intake == null) continue;
                Vector3 d = p.intake.position - from;
                d.z = 0f;
                float dist = d.magnitude;
                if (dist <= p.intakeRadius && dist < bestDistance)
                {
                    best = p;
                    bestDistance = dist;
                }
            }
            return best;
        }

        private void UpdateStream()
        {
            if (stream == null) return;
            bool show = IsPouring && NearbyPump != null;
            stream.enabled = show;
            if (!show) return;
            Vector3 a = spout != null ? spout.position : transform.position;
            Vector3 b = NearbyPump.intake.position;
            a.z = b.z = -0.4f;
            stream.SetPosition(0, a);
            stream.SetPosition(1, b);
        }
    }
}
