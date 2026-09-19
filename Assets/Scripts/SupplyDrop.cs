using UnityEngine;

public class SupplyDrop : MonoBehaviour
{
    public float fallSpeed = 8f;
    public float groundY = 0.6f;
    public int ammoAmount = 15;

    [Header("Tactical Camera Event")]
    public TacticalCameraEvent tacticalCameraEvent;
    public float tacticalCameraDuration = 3f;
    public bool playCameraOnStart = true;

    private bool hasLanded = false;

    void Start()
    {
        if (tacticalCameraEvent == null)
        {
            tacticalCameraEvent = FindObjectOfType<TacticalCameraEvent>();
        }

        if (playCameraOnStart && tacticalCameraEvent != null)
        {
            tacticalCameraEvent.PlayTacticalEvent(transform, tacticalCameraDuration);
        }
    }

    void Update()
    {
        if (!hasLanded)
        {
            Vector3 targetPosition = new Vector3(transform.position.x, groundY, transform.position.z);
            transform.position = Vector3.MoveTowards(transform.position, targetPosition, fallSpeed * Time.deltaTime);

            if (Vector3.Distance(transform.position, targetPosition) < 0.05f)
            {
                transform.position = targetPosition;
                hasLanded = true;
                Debug.Log("La caja de suministros aterrizó.");
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!hasLanded)
        {
            return;
        }

        WeaponRaycast weapon = other.GetComponentInChildren<WeaponRaycast>();

        if (weapon != null)
        {
            weapon.currentAmmo = Mathf.Min(weapon.currentAmmo + ammoAmount, weapon.maxAmmo);

            if (weapon.uiManager != null)
            {
                weapon.uiManager.UpdateAmmo(weapon.currentAmmo, weapon.maxAmmo);
                weapon.uiManager.ShowMessage("Munición recogida");
            }

            Debug.Log("El jugador recogió munición.");
            Destroy(gameObject);
        }
    }
}