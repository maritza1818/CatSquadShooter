using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace CatSquad.RescateProfundo
{
    public enum LaunchTargetKind
    {
        Cesta = 0,  // en una azotea o balcón sobre el agua: un gatito con hambre espera una lata
        Placa = 1,  // placa de presión en el fondo: se activa con un lastre
        Gancho = 2  // gancho bajo un puente o balcón sumergido: una boya que sube se engancha
    }

    /// <summary>
    /// Blanco del lanzador. Cada blanco acepta un tipo de proyectil (el lanzador elige solo cuál usar:
    /// lata en la superficie, lastre si el blanco está más abajo, boya si está más arriba bajo el agua).
    /// Al acertar da chatarra y, pasado un rato, vuelve a estar listo: son repeticiones de la terapia.
    /// </summary>
    public class LaunchTarget : MonoBehaviour
    {
        private static readonly List<LaunchTarget> all = new List<LaunchTarget>();
        public static IReadOnlyList<LaunchTarget> All { get { return all; } }

        public static event Action<LaunchTarget, Projectile> AnyHit;

        public LaunchTargetKind kind = LaunchTargetKind.Cesta;
        public string displayName = "Cesta";
        [Tooltip("Centro del blanco (lo usa la asistencia de puntería)")]
        public Transform aimPoint;
        [Tooltip("Cesta: el gatito que espera la comida")]
        public Transform kitten;
        [Tooltip("Anillo o luz que indica que el blanco está listo")]
        public Renderer indicator;
        [Tooltip("Placa: la parte que se hunde al recibir el lastre")]
        public Transform movingPart;
        public int reward = 2;
        public float resetSeconds = 25f;

        public bool Ready { get; private set; }
        public int TimesHit { get; private set; }

        private float resetTimer;
        private float happyTimer;
        private Vector3 kittenBase;
        private Vector3 movingBase;
        private Projectile held;
        private Vector3 indicatorScale = Vector3.one;

        public ProjectileKind Ammo
        {
            get
            {
                switch (kind)
                {
                    case LaunchTargetKind.Placa: return ProjectileKind.Lastre;
                    case LaunchTargetKind.Gancho: return ProjectileKind.Boya;
                    default: return ProjectileKind.Lata;
                }
            }
        }

        public bool IsSurfaceTarget { get { return kind == LaunchTargetKind.Cesta; } }
        public Vector3 AimPoint { get { return aimPoint != null ? aimPoint.position : transform.position; } }

        private void OnEnable() { if (!all.Contains(this)) all.Add(this); }
        private void OnDisable() { all.Remove(this); }

        private void Awake()
        {
            Ready = true;
            if (kitten != null) kittenBase = kitten.localPosition;
            if (movingPart != null) movingBase = movingPart.localPosition;
            if (indicator != null) indicatorScale = indicator.transform.localScale;
        }

        public bool Accepts(Projectile p)
        {
            return Ready && p != null && p.kind == Ammo;
        }

        public void Receive(Projectile p)
        {
            Ready = false;
            TimesHit++;
            resetTimer = resetSeconds;
            happyTimer = 2.5f;
            if (GameManager.Instance != null && reward > 0) GameManager.Instance.AddMaterials(reward);
            AudioFx fx = AudioFx.Instance;
            if (fx != null)
            {
                AudioFx.Play(kind == LaunchTargetKind.Cesta ? fx.kittenMeow : fx.chime, AimPoint, 0.9f, kind == LaunchTargetKind.Cesta ? 1.15f : 1f);
                AudioFx.Play(fx.shardCollect, AimPoint, 0.6f, 1.2f);
            }
            if (AnyHit != null) AnyHit(this, p);
            StartCoroutine(Absorb(p));
        }

        private IEnumerator Absorb(Projectile p)
        {
            if (p == null) yield break;
            switch (kind)
            {
                case LaunchTargetKind.Cesta:
                    // la lata cae dentro y el gatito se la come
                    yield return new WaitForSeconds(0.5f);
                    if (p != null) p.Hold(transform);
                    yield return new WaitForSeconds(1.2f);
                    if (p != null) p.Consume();
                    break;
                case LaunchTargetKind.Gancho:
                    // la boya queda enganchada hasta que el gancho se reinicia
                    if (p != null) p.Hold(aimPoint != null ? aimPoint : transform);
                    held = p;
                    break;
                default:
                    yield return new WaitForSeconds(2.5f);
                    if (p != null) p.Despawn();
                    break;
            }
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (!Ready)
            {
                resetTimer -= dt;
                if (resetTimer <= 0f)
                {
                    Ready = true;
                    if (held != null) { held.Despawn(); held = null; }
                }
            }
            if (happyTimer > 0f) happyTimer -= dt;

            if (kitten != null)
            {
                // con hambre: mira y maúlla quieto; feliz: salta
                float hop = happyTimer > 0f ? Mathf.Abs(Mathf.Sin(Time.time * 9f)) * 0.25f : Mathf.Sin(Time.time * 2f) * 0.02f;
                kitten.localPosition = kittenBase + Vector3.up * hop;
            }
            if (movingPart != null)
            {
                Vector3 target = movingBase + (Ready ? Vector3.zero : Vector3.down * 0.12f);
                movingPart.localPosition = Vector3.Lerp(movingPart.localPosition, target, 1f - Mathf.Exp(-10f * dt));
            }
            if (indicator != null)
            {
                indicator.enabled = Ready;
                float pulse = 0.6f + 0.4f * Mathf.Sin(Time.time * 4f);
                float k = 0.9f + 0.15f * pulse;
                indicator.transform.localScale = new Vector3(indicatorScale.x * k, indicatorScale.y, indicatorScale.z * k);
            }
        }

        /// <summary>Blanco listo más cercano dentro de un radio (de un tipo de medio: superficie o bajo el agua).</summary>
        public static LaunchTarget NearestReady(Vector3 from, float range, bool surface)
        {
            LaunchTarget best = null;
            float bestD = range;
            foreach (LaunchTarget t in all)
            {
                if (!t.Ready || t.IsSurfaceTarget != surface) continue;
                float d = Vector3.Distance(from, t.AimPoint);
                if (d < bestD) { bestD = d; best = t; }
            }
            return best;
        }
    }
}
