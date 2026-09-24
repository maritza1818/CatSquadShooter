using System;
using UnityEngine;

namespace CatSquad.RescateProfundo
{
    /// <summary>
    /// Jaula burbuja con un gatito varado. Para liberarlo hay que sujetar el cerrojo
    /// y TIRAR de él (alcance + prensión sostenida + traslado).
    /// Ningún gatito sufre daño: si la sesión termina antes, sigue a salvo en su jaula.
    /// </summary>
    public class KittenCage : MonoBehaviour
    {
        public RescuedKitten kitten;
        public CageLatch latch;
        public Transform door;
        public Vector3 doorOpenEuler = new Vector3(0f, 0f, 110f);
        public Transform bubbleShell;
        public GameObject currentField;

        public bool IsOpen { get; private set; }
        public static event Action<KittenCage> AnyOpened;

        private float doorT;

        private void Start()
        {
            if (kitten != null && GameManager.Instance != null && GameManager.Instance.IsKittenRescued(kitten.kittenId))
            {
                // Ya fue rescatado en una sesión anterior: la jaula queda abierta y vacía.
                IsOpen = true;
                doorT = 1f;
                ApplyDoor();
                if (latch != null) latch.gameObject.SetActive(false);
                if (bubbleShell != null) bubbleShell.gameObject.SetActive(false);
                if (currentField != null) currentField.SetActive(false);
            }
            if (latch != null) latch.cage = this;
        }

        public void Open()
        {
            if (IsOpen) return;
            IsOpen = true;
            if (kitten != null) kitten.Free();
            if (currentField != null) currentField.SetActive(false);
            if (bubbleShell != null) bubbleShell.gameObject.SetActive(false);
            AudioFx.Play(AudioFx.Instance != null ? AudioFx.Instance.kittenMeow : null, transform.position);
            if (AnyOpened != null) AnyOpened(this);
        }

        private void Update()
        {
            if (!IsOpen || doorT >= 1f) return;
            doorT = Mathf.Clamp01(doorT + Time.deltaTime * 1.5f);
            ApplyDoor();
        }

        private void ApplyDoor()
        {
            if (door == null) return;
            door.localRotation = Quaternion.Euler(doorOpenEuler * (doorT * doorT * (3f - 2f * doorT)));
        }
    }
}
