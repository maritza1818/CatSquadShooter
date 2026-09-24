using System;
using UnityEngine;

namespace CatSquad.RescateProfundo
{
    [Serializable]
    public class PumpWaterTarget
    {
        public WaterZone zone;
        public float levelFrom;
        public float levelTo;
    }

    /// <summary>
    /// Bomba de agua. Tarea BIMANUAL ASIMÉTRICA (núcleo de HABIT):
    ///  - La mano asistente MANTIENE la palanca del panel (contracción sostenida).
    ///  - La mano manipuladora GIRA el antebrazo para verter el bidón en la toma.
    /// Con la palanca sostenida, el combustible del tanque se convierte en agua:
    /// sube el nivel de las zonas enlazadas y, al completarse, abre compuertas.
    /// </summary>
    public class PumpStation : MonoBehaviour
    {
        private static readonly System.Collections.Generic.List<PumpStation> all = new System.Collections.Generic.List<PumpStation>();
        public static System.Collections.Generic.IReadOnlyList<PumpStation> All { get { return all; } }

        public string pumpId = "bomba";
        public string displayName = "Bomba";

        [Header("Puntos")]
        public Transform dockPoint;
        public Transform intake;
        public float dockRadius = 1.8f;
        public float intakeRadius = 1.4f;

        [Header("Bombeo")]
        [Tooltip("Litros (bidones) necesarios para completar la bomba")]
        public float fuelToComplete = 2f;
        [Tooltip("Combustible por segundo que la bomba consume mientras la palanca está sostenida")]
        public float pumpRate = 0.3f;
        public PumpWaterTarget[] waterTargets = new PumpWaterTarget[0];
        public FloodGate[] gates = new FloodGate[0];
        [Range(0f, 1f)] public float gatesOpenAtProgress = 1f;

        [Header("Visual")]
        public Transform lever;
        public float leverDownAngle = 35f;
        public float leverUpAngle = -35f;
        public Transform progressFill;
        public Transform tankFill;
        public Renderer statusLight;
        public Color idleColor = new Color(1f, 0.6f, 0.1f);
        public Color pumpingColor = new Color(0.2f, 1f, 0.5f);
        public Color doneColor = new Color(0.3f, 0.7f, 1f);
        public ParticleSystem pumpBubbles;

        public float Progress { get; private set; }
        public float Tank { get; private set; }
        public bool LeverHeld { get; private set; }
        public bool IsPumping { get; private set; }
        public bool Complete { get { return Progress >= 0.999f; } }
        public bool SubDockedHere
        {
            get
            {
                SubmarineController sub = GameManager.Instance != null ? GameManager.Instance.submarine : null;
                return sub != null && sub.DockedPump == this;
            }
        }

        public event Action<PumpStation> Completed;
        public event Action<PumpStation, float> FuelReceived;

        private float leverAngle;
        private AudioSource pumpAudio;
        private MaterialPropertyBlock block;
        private bool completedFired;

        private void OnEnable()
        {
            if (!all.Contains(this)) all.Add(this);
        }

        private void OnDisable()
        {
            all.Remove(this);
        }

        private void Start()
        {
            if (GameManager.Instance != null) Progress = GameManager.Instance.GetPumpFuel(pumpId);
            completedFired = Complete;
            ApplyProgress(true);
            pumpAudio = AudioFx.CreateLoop(gameObject, AudioFx.Instance != null ? AudioFx.Instance.pumpLoop : null, 0.35f);
            leverAngle = leverDownAngle;
        }

        public bool RequiresLeverForFuel
        {
            get { return GameManager.Instance == null || GameManager.Instance.Settings.requireLeverWhilePouring; }
        }

        /// <summary>¿La toma acepta combustible ahora mismo?</summary>
        public bool AcceptingFuel
        {
            get
            {
                if (Complete) return false;
                if (Tank >= 1.5f) return false;
                return !RequiresLeverForFuel || LeverHeld;
            }
        }

        public float ReceiveFuel(float amount)
        {
            if (!AcceptingFuel || amount <= 0f) return 0f;
            float accepted = Mathf.Min(amount, 1.5f - Tank);
            Tank += accepted;
            if (FuelReceived != null) FuelReceived(this, accepted);
            return accepted;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            SubmarineController sub = GameManager.Instance != null ? GameManager.Instance.submarine : null;
            BimanualFrame frame = BimanualInput.Instance != null ? BimanualInput.Instance.Current : new BimanualFrame();

            // ---- acople automático cuando el submarino se acerca despacio
            if (sub != null && dockPoint != null && sub.CanDock && sub.InputEnabled && !Complete)
            {
                Vector3 d = sub.transform.position - dockPoint.position;
                d.z = 0f;
                if (d.magnitude < dockRadius && sub.Velocity.magnitude < 2.2f)
                {
                    sub.DockTo(this);
                }
            }

            // ---- palanca (mano asistente sostenida)
            bool docked = sub != null && sub.DockedPump == this && sub.InputEnabled;
            LeverHeld = docked && frame.panel.tracked && (frame.panel.lever || frame.panel.joystick.y > 0.55f);

            // ---- bombeo
            IsPumping = false;
            if (LeverHeld && Tank > 0f && !Complete)
            {
                float efficiency = GameManager.Instance != null ? GameManager.Instance.PumpEfficiency : 1f;
                float use = Mathf.Min(Tank, pumpRate * dt);
                Tank -= use;
                Progress = Mathf.Clamp01(Progress + use * efficiency / Mathf.Max(0.01f, fuelToComplete));
                IsPumping = true;
                ApplyProgress(false);
                if (GameManager.Instance != null) GameManager.Instance.SetPumpFuel(pumpId, Progress);
            }

            if (Complete && !completedFired)
            {
                completedFired = true;
                Tank = 0f;
                if (sub != null && sub.DockedPump == this) sub.Undock();
                if (Completed != null) Completed(this);
                AudioFx.Play(AudioFx.Instance != null ? AudioFx.Instance.chime : null, transform.position);
                if (GameManager.Instance != null) GameManager.Instance.Save();
            }

            UpdateVisuals(dt);
        }

        private void ApplyProgress(bool instant)
        {
            if (waterTargets != null)
            {
                foreach (PumpWaterTarget t in waterTargets)
                {
                    if (t == null || t.zone == null) continue;
                    t.zone.SetPumpedLevel(Mathf.Lerp(t.levelFrom, t.levelTo, Progress), instant);
                }
            }

            if (gates != null && Progress >= gatesOpenAtProgress - 0.0001f)
            {
                foreach (FloodGate g in gates)
                {
                    if (g != null) g.SetOpen(true, instant);
                }
            }
        }

        private void UpdateVisuals(float dt)
        {
            float targetLever = LeverHeld ? leverUpAngle : leverDownAngle;
            leverAngle = Mathf.Lerp(leverAngle, targetLever, 1f - Mathf.Exp(-10f * dt));
            if (lever != null) lever.localRotation = Quaternion.Euler(0f, 0f, leverAngle);

            if (progressFill != null)
            {
                Vector3 s = progressFill.localScale;
                s.y = Mathf.Max(0.001f, Progress);
                progressFill.localScale = s;
            }
            if (tankFill != null)
            {
                Vector3 s = tankFill.localScale;
                s.y = Mathf.Max(0.001f, Mathf.Clamp01(Tank / 1.5f));
                tankFill.localScale = s;
            }

            if (statusLight != null)
            {
                if (block == null) block = new MaterialPropertyBlock();
                Color c = Complete ? doneColor : (IsPumping ? pumpingColor : idleColor);
                float blink = IsPumping ? 0.6f + 0.4f * Mathf.Sin(Time.time * 10f) : 1f;
                block.SetColor("_BaseColor", c);
                block.SetColor("_EmissionColor", c * 2.5f * blink);
                statusLight.SetPropertyBlock(block);
            }

            if (pumpBubbles != null)
            {
                ParticleSystem.EmissionModule e = pumpBubbles.emission;
                e.rateOverTime = IsPumping ? 25f : 0f;
            }

            if (pumpAudio != null)
            {
                if (IsPumping && !pumpAudio.isPlaying) pumpAudio.Play();
                else if (!IsPumping && pumpAudio.isPlaying) pumpAudio.Stop();
            }
        }

        private void OnDrawGizmos()
        {
            if (dockPoint != null)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawWireSphere(dockPoint.position, dockRadius);
            }
            if (intake != null)
            {
                Gizmos.color = new Color(1f, 0.5f, 0f);
                Gizmos.DrawWireSphere(intake.position, intakeRadius);
            }
        }
    }
}
