using UnityEngine;

namespace CatSquad.Lab06
{
    /// <summary>
    /// LAB 06 · WAYPOINTS. El script del tutorial ("FollowWP"), pero en los karts de los gatos.
    /// Las palmeras son los waypoints: el gato va hacia la palmera actual y, cuando está a menos de
    /// <see cref="reachDistance"/> metros, pasa a la siguiente. Al llegar a la última vuelve a la primera:
    /// así la secuencia de puntos forma un CIRCUITO.
    /// (Está en el namespace CatSquad.Lab06 para que no choque con el FollowWP del paquete del tutorial.)
    /// </summary>
    public class FollowWP : MonoBehaviour
    {
        [Tooltip("Las palmeras del circuito, en orden (arrástralas aquí con el candado del Inspector cerrado)")]
        public GameObject[] waypoints;

        [Tooltip("Velocidad de avance (m/s)")]
        public float speed = 10.0f;

        [Tooltip("Velocidad de giro hacia la palmera (más alto = gira más brusco)")]
        public float rotSpeed = 10.0f;

        [Tooltip("Distancia a la que se da por alcanzada una palmera y se pasa a la siguiente")]
        public float reachDistance = 3.0f;

        int currentWP = 0;

        void Update()
        {
            if (waypoints == null || waypoints.Length == 0) return;

            // 1. ¿Ya llegué a la palmera actual? → paso a la siguiente
            if (Vector3.Distance(this.transform.position, waypoints[currentWP].transform.position) < reachDistance)
                currentWP++;

            // 2. Después de la última vuelvo a la primera: el recorrido se cierra en un circuito
            if (currentWP >= waypoints.Length)
                currentWP = 0;

            // 3. Girar suavemente hacia la palmera (en lugar de LookAt, que gira de golpe)
            Vector3 direction = waypoints[currentWP].transform.position - this.transform.position;
            direction.y = 0f;   // solo en el plano: el kart no se inclina hacia la base de la palmera
            if (direction.sqrMagnitude > 0.001f)
            {
                Quaternion lookatWP = Quaternion.LookRotation(direction);
                this.transform.rotation = Quaternion.Slerp(this.transform.rotation, lookatWP, rotSpeed * Time.deltaTime);
            }

            // 4. Avanzar siempre hacia adelante (eje Z local)
            this.transform.Translate(0, 0, speed * Time.deltaTime);
        }

        // Solo para ver en la vista Scene hacia qué palmera va (línea amarilla) y el circuito completo (línea verde)
        void OnDrawGizmosSelected()
        {
            if (waypoints == null || waypoints.Length == 0) return;
            Gizmos.color = new Color(0.3f, 1f, 0.4f);
            for (int i = 0; i < waypoints.Length; i++)
            {
                if (waypoints[i] == null || waypoints[(i + 1) % waypoints.Length] == null) continue;
                Gizmos.DrawLine(waypoints[i].transform.position + Vector3.up, waypoints[(i + 1) % waypoints.Length].transform.position + Vector3.up);
            }
            if (Application.isPlaying && waypoints[currentWP] != null)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(transform.position + Vector3.up, waypoints[currentWP].transform.position + Vector3.up);
                Gizmos.DrawWireSphere(waypoints[currentWP].transform.position, reachDistance);
            }
        }
    }
}
