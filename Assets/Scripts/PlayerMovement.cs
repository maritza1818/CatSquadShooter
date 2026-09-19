using UnityEngine;

public class Move : MonoBehaviour
{
    public GameObject goal;
    public float speed = 5f;
    public float distanciaDeParada = 2f;

    Vector3 direction;

    void Start()
    {
        //direction = goal.transform.position - this.transform.position;
        //this.transform.Translate(direction);
    }

    private void LateUpdate()
    {
        if (goal == null) return;

        direction = goal.transform.position - this.transform.position;

        this.transform.LookAt(goal.transform.position);

        if (direction.magnitude > distanciaDeParada)
        {
            Vector3 velocity = direction.normalized * speed * Time.deltaTime;
            this.transform.position = this.transform.position + velocity;
        }
    }
}