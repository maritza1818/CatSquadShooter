using UnityEngine;

public class EnemyAI : MonoBehaviour
{
    public enum EnemyState
    {
        Patrol,
        Chase,
        Shoot
    }

    [Header("References")]
    public Transform player;
    public PlayerHealth playerHealth;
    public CharacterController controller;

    [Header("Patrol")]
    public Transform[] patrolPoints;
    public float patrolSpeed = 2f;
    public float pointReachDistance = 0.4f;

    [Header("Detection")]
    public float viewDistance = 15f;
    public float viewAngle = 100f;
    public LayerMask obstacleMask = ~0;

    [Header("Chase")]
    public float chaseSpeed = 3.5f;
    public float stopDistance = 8f;

    [Header("Shooting")]
    public Transform shootPoint;
    public float shootRange = 30f;
    public int damage = 10;
    public float fireCooldown = 1.2f;
    public LayerMask hitMask = ~0;

    [Header("Effects")]
    public GameObject impactEffectPrefab;
    public GameObject bulletTrailPrefab;
    public GameObject muzzleFlashPrefab;
    public AudioSource audioSource;
    public AudioClip shootSound;

    [Header("Gravity")]
    public float gravity = -9.81f;

    private int currentPointIndex = 0;
    private float nextFireTime = 0f;
    private Vector3 velocity;
    private EnemyState currentState = EnemyState.Patrol;

    void Start()
    {
        if (controller == null)
        {
            controller = GetComponent<CharacterController>();
        }

        if (player != null && playerHealth == null)
        {
            playerHealth = player.GetComponent<PlayerHealth>();
        }
    }

    void Update()
    {
        if (player == null)
        {
            return;
        }

        ApplyGravity();

        switch (currentState)
        {
            case EnemyState.Patrol:
                Patrol();

                if (CanSeePlayer())
                {
                    currentState = EnemyState.Chase;
                }
                break;

            case EnemyState.Chase:
                ChasePlayer();

                if (CanSeePlayer() && Vector3.Distance(transform.position, player.position) <= stopDistance)
                {
                    currentState = EnemyState.Shoot;
                }

                if (!CanSeePlayer())
                {
                    currentState = EnemyState.Patrol;
                }
                break;

            case EnemyState.Shoot:
                LookAtTarget(player.position);
                ShootAtPlayer();

                if (!CanSeePlayer())
                {
                    currentState = EnemyState.Patrol;
                }
                else if (Vector3.Distance(transform.position, player.position) > stopDistance)
                {
                    currentState = EnemyState.Chase;
                }
                break;
        }
    }

    void Patrol()
    {
        if (patrolPoints == null || patrolPoints.Length == 0)
        {
            return;
        }

        Transform targetPoint = patrolPoints[currentPointIndex];

        MoveTo(targetPoint.position, patrolSpeed);

        float distance = Vector3.Distance(transform.position, targetPoint.position);

        if (distance <= pointReachDistance)
        {
            currentPointIndex++;

            if (currentPointIndex >= patrolPoints.Length)
            {
                currentPointIndex = 0;
            }
        }
    }

    void ChasePlayer()
    {
        float distanceToPlayer = Vector3.Distance(transform.position, player.position);

        if (distanceToPlayer > stopDistance)
        {
            MoveTo(player.position, chaseSpeed);
        }
        else
        {
            LookAtTarget(player.position);
        }
    }

    void ShootAtPlayer()
    {
        if (Time.time < nextFireTime)
        {
            return;
        }

        nextFireTime = Time.time + fireCooldown;

        Transform originTransform = shootPoint != null ? shootPoint : transform;

        Vector3 origin = originTransform.position;
        Vector3 targetPoint = player.position + Vector3.up * 1.2f;
        Vector3 direction = (targetPoint - origin).normalized;

        if (muzzleFlashPrefab != null && shootPoint != null)
        {
            Instantiate(muzzleFlashPrefab, shootPoint.position, shootPoint.rotation);
        }

        if (audioSource != null && shootSound != null)
        {
            audioSource.PlayOneShot(shootSound);
        }

        Vector3 endPoint = origin + direction * shootRange;

        if (Physics.Raycast(origin, direction, out RaycastHit hit, shootRange, hitMask))
        {
            endPoint = hit.point;

            Debug.Log(gameObject.name + " disparó e impactó contra: " + hit.collider.gameObject.name);

            if (impactEffectPrefab != null)
            {
                Instantiate(impactEffectPrefab, hit.point, Quaternion.LookRotation(hit.normal));
            }

            PlayerHealth health = hit.collider.GetComponentInParent<PlayerHealth>();

            if (health != null)
            {
                health.TakeDamage(damage);
            }
        }
        else
        {
            Debug.Log(gameObject.name + " disparó pero no impactó.");
        }

        if (bulletTrailPrefab != null)
        {
            GameObject trailObject = Instantiate(bulletTrailPrefab, Vector3.zero, Quaternion.identity);
            BulletTrail trail = trailObject.GetComponent<BulletTrail>();

            if (trail != null)
            {
                trail.SetPositions(origin, endPoint);
            }
        }
    }

    void MoveTo(Vector3 destination, float speed)
    {
        if (controller == null || !controller.enabled || !gameObject.activeInHierarchy)
        {
            return;
        }

        Vector3 direction = destination - transform.position;
        direction.y = 0f;

        if (direction.magnitude < 0.1f)
        {
            return;
        }

        direction.Normalize();

        controller.Move(direction * speed * Time.deltaTime);
        LookAtTarget(transform.position + direction);
    }

    void LookAtTarget(Vector3 targetPosition)
    {
        Vector3 direction = targetPosition - transform.position;
        direction.y = 0f;

        if (direction.sqrMagnitude <= 0.01f)
        {
            return;
        }

        Quaternion targetRotation = Quaternion.LookRotation(direction);

        transform.rotation = Quaternion.Lerp(
            transform.rotation,
            targetRotation,
            10f * Time.deltaTime
        );
    }

    bool CanSeePlayer()
    {
        if (player == null)
        {
            return false;
        }

        Vector3 origin = transform.position + Vector3.up * 1.2f;
        Vector3 targetPoint = player.position + Vector3.up * 1.2f;

        Vector3 directionToPlayer = targetPoint - origin;
        float distanceToPlayer = directionToPlayer.magnitude;

        if (distanceToPlayer > viewDistance)
        {
            return false;
        }

        float angle = Vector3.Angle(transform.forward, directionToPlayer);

        if (angle > viewAngle / 2f)
        {
            return false;
        }

        if (Physics.Raycast(origin, directionToPlayer.normalized, out RaycastHit hit, viewDistance, obstacleMask))
        {
            if (hit.transform == player || hit.transform.IsChildOf(player))
            {
                return true;
            }

            return false;
        }

        return false;
    }

    void ApplyGravity()
    {
        if (controller == null || !controller.enabled || !gameObject.activeInHierarchy)
        {
            return;
        }

        if (controller.isGrounded && velocity.y < 0)
        {
            velocity.y = -2f;
        }

        velocity.y += gravity * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);
    }    

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, viewDistance);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, stopDistance);

        Gizmos.color = Color.blue;
        Gizmos.DrawWireSphere(transform.position, shootRange);
    }
}