using UnityEngine;

public class PlayerBoxPusher : MonoBehaviour
{
    public Camera playerCamera;
    public float pushRange = 3f;

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.E))
        {
            TryPushBox();
        }
    }

    void TryPushBox()
    {
        Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, pushRange))
        {
            PushableBox box = hit.collider.GetComponent<PushableBox>();

            if (box != null)
            {
                Vector3 pushDirection = hit.collider.transform.position - transform.position;
                box.TryPush(pushDirection);
            }
        }
    }
}