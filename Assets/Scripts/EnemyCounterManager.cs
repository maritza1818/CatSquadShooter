using UnityEngine;

public class EnemyCounterManager : MonoBehaviour
{
    public GameplayReplayRecorder replayRecorder;

    private int enemiesAlive;
    private bool replayActivated = false;

    void Start()
    {
        EnemyAI[] enemies = FindObjectsOfType<EnemyAI>();
        enemiesAlive = enemies.Length;

        Debug.Log("Enemigos con IA detectados: " + enemiesAlive);
    }

    public void EnemyKilled(Transform deadEnemy)
    {
        if (replayActivated)
        {
            return;
        }

        enemiesAlive--;

        Debug.Log("Enemigos restantes: " + enemiesAlive);

        if (enemiesAlive <= 0)
        {
            replayActivated = true;

            if (replayRecorder != null)
            {
                replayRecorder.PlayReplay(deadEnemy);
            }
            else
            {
                Debug.LogWarning("No se asignó GameplayReplayRecorder en EnemyCounterManager.");
            }

            Debug.Log("Último enemigo eliminado. Activando repetición.");
        }
    }
}