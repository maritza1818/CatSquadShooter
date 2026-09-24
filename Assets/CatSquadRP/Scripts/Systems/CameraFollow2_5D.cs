using UnityEngine;

namespace CatSquad.RescateProfundo
{
    /// <summary>Cámara 2.5D que sigue al submarino con anticipación según su velocidad.</summary>
    public class CameraFollow2_5D : MonoBehaviour
    {
        public Transform target;
        public Vector3 offset = new Vector3(0f, 1.2f, -19f);
        public float smoothTime = 0.35f;
        public float lookAhead = 1.4f;
        public float tiltDegrees = 4f;
        public Vector2 minBounds = new Vector2(-1000f, -1000f);
        public Vector2 maxBounds = new Vector2(1000f, 1000f);

        private Vector3 velocity;
        private Vector3 ahead;

        private void Start()
        {
            if (target != null) transform.position = target.position + offset;
        }

        private void LateUpdate()
        {
            if (target == null) return;
            SubmarineController sub = target.GetComponent<SubmarineController>();
            Vector3 v = sub != null ? sub.Velocity : Vector3.zero;
            ahead = Vector3.Lerp(ahead, new Vector3(v.x, v.y * 0.5f, 0f) * (lookAhead / 4f), 1f - Mathf.Exp(-2f * Time.deltaTime));

            Vector3 desired = target.position + offset + ahead;
            desired.x = Mathf.Clamp(desired.x, minBounds.x, maxBounds.x);
            desired.y = Mathf.Clamp(desired.y, minBounds.y, maxBounds.y);
            transform.position = Vector3.SmoothDamp(transform.position, desired, ref velocity, smoothTime);
            transform.rotation = Quaternion.Euler(tiltDegrees, 0f, 0f);
        }
    }
}
