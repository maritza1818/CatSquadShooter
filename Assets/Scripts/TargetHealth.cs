using UnityEngine;
using System.Collections;

public class TargetHealth : MonoBehaviour
{
    public int maxHealth = 100;
    public int currentHealth;

    [Header("Death Settings")]
    public float destroyDelay = 6f;
    public bool hideOnDeath = false;

    private bool isDead = false;

    void Start()
    {
        currentHealth = maxHealth;
    }

    public void TakeDamage(int damage)
    {
        if (isDead)
        {
            return;
        }

        currentHealth -= damage;

        Debug.Log(gameObject.name + " recibió daño. Vida actual: " + currentHealth);

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    void Die()
    {
        if (isDead)
        {
            return;
        }

        isDead = true;

        Debug.Log(gameObject.name + " fue eliminado.");

        Collider[] colliders = GetComponentsInChildren<Collider>();

        foreach (Collider col in colliders)
        {
            col.enabled = false;
        }

        EnemyAI enemyAI = GetComponent<EnemyAI>();

        if (enemyAI != null)
        {
            enemyAI.enabled = false;
        }

        CharacterController enemyController = GetComponent<CharacterController>();

        if (enemyController != null)
        {
            enemyController.enabled = false;
        }

        if (hideOnDeath)
        {
            Renderer[] renderers = GetComponentsInChildren<Renderer>();

            foreach (Renderer rend in renderers)
            {
                rend.enabled = false;
            }
        }

        StartCoroutine(DestroyAfterDelay());
    }

    IEnumerator DestroyAfterDelay()
    {
        yield return new WaitForSeconds(destroyDelay);
        Destroy(gameObject);
    }
}