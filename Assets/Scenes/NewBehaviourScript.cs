using UnityEngine;

public class NewBehaviourScript : MonoBehaviour
{
    public GameObject goal;
    public float speed = 5f;
    public float distanciaDeParada = 2f;
    public float velocidadGiro = 4f;

    Vector3 direction;

    private void LateUpdate()
    {
        if (goal == null) return;

        direction = goal.transform.position - this.transform.position;
        direction.y = 0f;

        if (direction != Vector3.zero)
        {
            Quaternion rotacionDeseada = Quaternion.LookRotation(direction);
            this.transform.rotation = Quaternion.Slerp(this.transform.rotation,
                                                       rotacionDeseada,
                                                       Time.deltaTime * velocidadGiro);
        }

        if (direction.magnitude > distanciaDeParada)
        {
            Vector3 velocity = direction.normalized * speed * Time.deltaTime;
            this.transform.position = this.transform.position + velocity;
        }
    }
}