using UnityEngine;
using TMPro;
using System.Collections;

public class UIManager : MonoBehaviour
{
    public TMP_Text healthText;
    public TMP_Text ammoText;
    public TMP_Text messageText;

    public void UpdateHealth(int currentHealth, int maxHealth)
    {
        healthText.text = "Vida: " + currentHealth + " / " + maxHealth;
    }

    public void UpdateAmmo(int currentAmmo, int maxAmmo)
    {
        ammoText.text = "Munición: " + currentAmmo + " / " + maxAmmo;
    }

    public void ShowMessage(string message)
    {
        StartCoroutine(ShowMessageRoutine(message));
    }

    IEnumerator ShowMessageRoutine(string message)
    {
        messageText.text = message;
        yield return new WaitForSeconds(1.5f);
        messageText.text = "";
    }
}