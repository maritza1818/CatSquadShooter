using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace CatSquad.RescateProfundo
{
    /// <summary>
    /// Calibración integrada en la ficción: "¡revienta las burbujas!".
    /// Mide el envolvente de alcance real del niño (webcam) antes de jugar.
    /// Las burbujas aparecen en oleadas cada vez más lejos; no hay que reventarlas
    /// todas: lo que se mide es hasta dónde llegó, no si "aprobó".
    /// </summary>
    public class CalibrationSequence : MonoBehaviour
    {
        public ReachBubble bubblePrefab;
        public Material bubbleMaterial;
        public float[] waveFractions = { 0.5f, 0.75f, 1f };
        public float[] directionsDeg = { 0f, 45f, 90f, 135f, 180f, -45f, -135f };
        public float waveTimeout = 11f;
        public float restSeconds = 2.2f;
        public float bubbleSize = 0.7f;

        public bool Running { get; private set; }
        public int Popped { get; private set; }
        public int Spawned { get; private set; }
        public int CurrentWave { get; private set; }

        private readonly List<ReachBubble> live = new List<ReachBubble>();

        public void Run(Action onDone)
        {
            if (Running) return;
            StartCoroutine(Routine(onDone));
        }

        private IEnumerator Routine(Action onDone)
        {
            Running = true;
            Popped = 0;
            Spawned = 0;
            GameManager gm = GameManager.Instance;
            SubmarineController sub = gm != null ? gm.submarine : null;
            ManipulatorArm arm = gm != null ? gm.arm : null;
            GuideCat guide = gm != null ? gm.guide : null;
            BimanualInput input = BimanualInput.Instance;

            if (sub != null) sub.Freeze();
            if (arm != null && arm.Held != null) arm.Release(false);

            if (guide != null) guide.Say("Primero, relaja los brazos y deja las manos en reposo un momento...");
            if (input != null) input.BeginCalibration();
            yield return new WaitForSeconds(restSeconds);

            if (guide != null) guide.Say("¡Ahora revienta las burbujas con tu brazo robótico! Estira todo lo que puedas.");

            for (int w = 0; w < waveFractions.Length; w++)
            {
                CurrentWave = w + 1;
                SpawnWave(waveFractions[w], arm);
                float t = 0f;
                while (t < waveTimeout && AnyAlive())
                {
                    t += Time.deltaTime;
                    yield return null;
                }
                ClearWave();
                yield return new WaitForSeconds(0.4f);
            }

            if (input != null) input.EndCalibration();
            if (gm != null) gm.Save();

            if (guide != null)
            {
                guide.Say(Popped >= Spawned * 0.7f
                    ? "¡Fantástico! Ya sé hasta dónde llega tu brazo."
                    : "¡Muy bien! Ya sé hasta dónde llega tu brazo hoy.");
            }
            yield return new WaitForSeconds(1.5f);

            if (sub != null) sub.Unfreeze();
            Running = false;
            if (onDone != null) onDone();
        }

        private bool AnyAlive()
        {
            for (int i = 0; i < live.Count; i++)
            {
                if (live[i] != null && !live[i].IsPopped) return true;
            }
            return false;
        }

        private void ClearWave()
        {
            foreach (ReachBubble b in live)
            {
                if (b != null && !b.IsPopped) Destroy(b.gameObject);
            }
            live.Clear();
        }

        private void SpawnWave(float fraction, ManipulatorArm arm)
        {
            if (arm == null) return;
            Vector3 origin = arm.BasePosition;
            foreach (float deg in directionsDeg)
            {
                Vector2 dir = new Vector2(Mathf.Cos(deg * Mathf.Deg2Rad), Mathf.Sin(deg * Mathf.Deg2Rad));
                float distance = fraction * arm.reach * 0.95f;
                Vector3 pos = origin + (Vector3)(dir * distance);

                // No poner burbujas dentro de paredes ni fuera del agua
                if (IsBlocked(origin, dir, distance + 0.4f)) continue;
                if (!WaterZone.PointIsUnderwater(pos)) continue;

                ReachBubble b = CreateBubble(pos);
                b.fraction = fraction;
                b.direction = dir;
                b.Popped += OnPopped;
                live.Add(b);
                Spawned++;
            }
        }

        private static bool IsBlocked(Vector3 origin, Vector2 dir, float distance)
        {
            RaycastHit[] hits = Physics.RaycastAll(origin, dir, distance, ~0, QueryTriggerInteraction.Ignore);
            foreach (RaycastHit h in hits)
            {
                if (h.collider.GetComponentInParent<SubmarineController>() != null) continue;
                if (h.collider.GetComponentInParent<Grate>() != null) continue;
                if (h.collider.GetComponentInParent<Grabbable>() != null) continue;
                return true;
            }
            return false;
        }

        private ReachBubble CreateBubble(Vector3 pos)
        {
            pos.z = -0.2f;
            if (bubblePrefab != null) return Instantiate(bubblePrefab, pos, Quaternion.identity);
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Burbuja";
            Collider c = go.GetComponent<Collider>();
            if (c != null) Destroy(c);
            go.transform.position = pos;
            go.transform.localScale = Vector3.one * bubbleSize;
            if (bubbleMaterial != null) go.GetComponent<Renderer>().sharedMaterial = bubbleMaterial;
            return go.AddComponent<ReachBubble>();
        }

        private void OnPopped(ReachBubble b)
        {
            Popped++;
            if (GameManager.Instance != null && GameManager.Instance.metrics != null) GameManager.Instance.metrics.RecordBubble();
        }
    }
}
