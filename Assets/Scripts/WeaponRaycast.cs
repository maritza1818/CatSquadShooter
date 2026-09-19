using UnityEngine;

public class WeaponRaycast : MonoBehaviour
{
    public Camera playerCamera;

    public float range = 50f;
    public int damage = 25;
    public float fireRate = 0.3f;

    public int maxAmmo = 30;
    public int currentAmmo;
    public UIManager uiManager;

    public GameObject impactEffectPrefab;
    public GameObject bulletTrailPrefab;
    public GameObject muzzleFlashPrefab;
    public Transform shootPoint;

    public AudioSource audioSource;
    public AudioClip shootSound;

    public LayerMask hitMask = ~0;

    [Header("Replay")]
    public GameplayReplayRecorder replayRecorder;

    private float nextTimeToFire = 0f;

    void Start()
    {
        currentAmmo = maxAmmo;

        if (uiManager != null)
        {
            uiManager.UpdateAmmo(currentAmmo, maxAmmo);
        }
    }

    void Update()
    {
        if (Input.GetMouseButtonDown(0) && Time.time >= nextTimeToFire)
        {
            if (currentAmmo > 0)
            {
                nextTimeToFire = Time.time + fireRate;
                Shoot();
            }
            else
            {
                Debug.Log("Sin munición");

                if (uiManager != null)
                {
                    uiManager.ShowMessage("Sin munición");
                }
            }
        }

        if (Input.GetKeyDown(KeyCode.R))
        {
            Reload();
        }
    }

    void Shoot()
    {
        if (playerCamera == null)
        {
            Debug.LogWarning("No hay cámara asignada para el disparo.");
            return;
        }

        currentAmmo--;

        if (uiManager != null)
        {
            uiManager.UpdateAmmo(currentAmmo, maxAmmo);
        }

        if (audioSource != null && shootSound != null)
        {
            audioSource.PlayOneShot(shootSound);
        }

        if (muzzleFlashPrefab != null && shootPoint != null)
        {
            Instantiate(muzzleFlashPrefab, shootPoint.position, shootPoint.rotation);
        }

        Debug.Log("Disparo realizado");

        Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        RaycastHit hit;

        Vector3 startPoint;

        if (shootPoint != null)
        {
            startPoint = shootPoint.position;
        }
        else
        {
            startPoint = playerCamera.transform.position;
        }

        Vector3 endPoint = ray.origin + ray.direction * range;

        Vector3 hitPoint = Vector3.zero;
        Vector3 hitNormal = Vector3.up;
        bool hasHit = false;

        if (Physics.Raycast(ray, out hit, range, hitMask))
        {
            endPoint = hit.point;
            hitPoint = hit.point;
            hitNormal = hit.normal;
            hasHit = true;

            Debug.Log("Impactó contra: " + hit.collider.gameObject.name);

            if (impactEffectPrefab != null)
            {
                Instantiate(impactEffectPrefab, hit.point, Quaternion.LookRotation(hit.normal));
            }

            TargetHealth target = hit.collider.GetComponent<TargetHealth>();

            if (target != null)
            {
                target.TakeDamage(damage);
            }
        }

        if (bulletTrailPrefab != null)
        {
            GameObject trailObject = Instantiate(bulletTrailPrefab, Vector3.zero, Quaternion.identity);
            BulletTrail trail = trailObject.GetComponent<BulletTrail>();

            if (trail != null)
            {
                trail.SetPositions(startPoint, endPoint);
            }
        }

        if (replayRecorder != null)
        {
            replayRecorder.RegisterShot(startPoint, endPoint, hitPoint, hitNormal, hasHit);
        }
    }

    void Reload()
    {
        currentAmmo = maxAmmo;

        if (uiManager != null)
        {
            uiManager.UpdateAmmo(currentAmmo, maxAmmo);
            uiManager.ShowMessage("Recargado");
        }

        Debug.Log("Arma recargada");
    }

    public void SetActiveCamera(Camera newCamera)
    {
        playerCamera = newCamera;
    }
}