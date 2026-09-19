using UnityEngine;

public class PlayerHealth : MonoBehaviour
{
    public int maxHealth = 100;
    public int currentHealth;

    public Transform respawnPoint;
    public UIManager uiManager;

    private CharacterController controller;

    void Start()
    {
        currentHealth = maxHealth;
        controller = GetComponent<CharacterController>();

        if (uiManager != null)
        {
            uiManager.UpdateHealth(currentHealth, maxHealth);
        }
    }

    public void TakeDamage(int damage)
    {
        currentHealth -= damage;

        if (currentHealth < 0)
        {
            currentHealth = 0;
        }

        Debug.Log("Jugador recibió daño. Vida actual: " + currentHealth);

        if (uiManager != null)
        {
            uiManager.UpdateHealth(currentHealth, maxHealth);
        }

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    void Die()
    {
        Debug.Log("Jugador eliminado. Reapareciendo...");

        if (uiManager != null)
        {
            uiManager.ShowMessage("Has sido eliminado");
        }

        currentHealth = maxHealth;

        controller.enabled = false;
        transform.position = respawnPoint.position;
        controller.enabled = true;

        if (uiManager != null)
        {
            uiManager.UpdateHealth(currentHealth, maxHealth);
            uiManager.ShowMessage("Reapareciste");
        }

        Debug.Log("Jugador reapareció con vida completa.");
    }
}