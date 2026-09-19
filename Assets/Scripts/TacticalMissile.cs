using UnityEngine;

public class TacticalMissile : MonoBehaviour
{
    public float fallSpeed = 15f;
    public float groundY = 0.8f;
    public float explosionRadius = 5f;
    public int damage = 50;

    public GameObject explosionEffectPrefab;

    [Header("Tactical Camera Event")]
    public TacticalCameraEvent tacticalCameraEvent;
    public float tacticalCameraDuration = 3f;
    public bool playCameraOnStart = true;

    private Vector3 targetPosition;
    private bool exploded = false;

    void Start()
    {
        targetPosition = new Vector3(transform.position.x, groundY, transform.position.z);

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
        if (exploded)
        {
            return;
        }

        transform.position = Vector3.MoveTowards(transform.position, targetPosition, fallSpeed * Time.deltaTime);

        if (Vector3.Distance(transform.position, targetPosition) < 0.05f)
        {
            Explode();
        }
    }

    void Explode()
    {
        exploded = true;

        if (explosionEffectPrefab != null)
        {
            Instantiate(explosionEffectPrefab, transform.position, Quaternion.identity);
        }

        Collider[] hits = Physics.OverlapSphere(transform.position, explosionRadius);

        foreach (Collider hit in hits)
        {
            PlayerHealth playerHealth = hit.GetComponent<PlayerHealth>();

            if (playerHealth != null)
            {
                playerHealth.TakeDamage(damage);
            }

            TargetHealth targetHealth = hit.GetComponent<TargetHealth>();

            if (targetHealth != null)
            {
                targetHealth.TakeDamage(damage);
            }
        }

        Debug.Log("Misil táctico explotó.");
        Destroy(gameObject);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.DrawWireSphere(transform.position, explosionRadius);
    }
}