using System;
using UnityEngine;

namespace CatSquad.RescateProfundo
{
    /// <summary>Burbuja de la calibración: se revienta tocándola con la pinza.</summary>
    public class ReachBubble : MonoBehaviour
    {
        public float popRadius = 0.6f;
        public float fraction;           // fracción del alcance del brazo a la que está
        public Vector2 direction;

        public event Action<ReachBubble> Popped;
        public bool IsPopped { get; private set; }

        private float age;
        private float popTimer;
        private Vector3 baseScale;
        private Vector3 basePos;

        private void Start()
        {
            baseScale = transform.localScale;
            basePos = transform.position;
            transform.localScale = Vector3.zero;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            age += dt;

            if (IsPopped)
            {
                popTimer += dt;
                float s = 1f + popTimer * 4f;
                transform.localScale = baseScale * s;
                if (popTimer > 0.18f) Destroy(gameObject);
                return;
            }

            float grow = Mathf.Clamp01(age * 3f);
            float pulse = 1f + Mathf.Sin(age * 4f) * 0.06f;
            transform.localScale = baseScale * grow * pulse;
            transform.position = basePos + Vector3.up * Mathf.Sin(age * 1.7f) * 0.08f;

            ManipulatorArm arm = GameManager.Instance != null ? GameManager.Instance.arm : null;
            if (arm != null && grow >= 1f)
            {
                Vector3 d = arm.TipPosition - transform.position;
                if (!GameManager.FirstPerson) d.z = 0f;
                if (d.magnitude <= popRadius) Pop();
            }
        }

        public void Pop()
        {
            if (IsPopped) return;
            IsPopped = true;
            AudioFx.Play(AudioFx.Instance != null ? AudioFx.Instance.bubblePop : null, transform.position, 0.9f, 0.9f + UnityEngine.Random.value * 0.3f);
            if (Popped != null) Popped(this);
        }
    }
}
