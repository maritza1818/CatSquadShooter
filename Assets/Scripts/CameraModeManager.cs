using UnityEngine;
using System.Collections;

public class CameraModeManager : MonoBehaviour
{
    [Header("Cameras")]
    public Camera firstPersonCamera;
    public Camera thirdPersonCamera;
    public Camera tacticalCamera;

    [Header("Weapon")]
    public WeaponRaycast weaponRaycast;

    [Header("Transition")]
    public float transitionSpeed = 8f;

    private Camera activeCamera;
    private Camera previousCamera;

    private bool isTransitioning = false;
    private Vector3 targetPosition;
    private Quaternion targetRotation;

    private Coroutine temporaryCameraRoutine;

    void Start()
    {
        SetCameraInstant(firstPersonCamera);
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            SwitchCamera(firstPersonCamera);
        }

        if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            SwitchCamera(thirdPersonCamera);
        }

        if (Input.GetKeyDown(KeyCode.Alpha3))
        {
            SwitchCamera(tacticalCamera);
        }

        if (isTransitioning && activeCamera != null)
        {
            activeCamera.transform.position = Vector3.Lerp(
                activeCamera.transform.position,
                targetPosition,
                transitionSpeed * Time.deltaTime
            );

            activeCamera.transform.rotation = Quaternion.Lerp(
                activeCamera.transform.rotation,
                targetRotation,
                transitionSpeed * Time.deltaTime
            );

            float positionDistance = Vector3.Distance(activeCamera.transform.position, targetPosition);

            if (positionDistance < 0.05f)
            {
                activeCamera.transform.position = targetPosition;
                activeCamera.transform.rotation = targetRotation;
                isTransitioning = false;
            }
        }
    }

    void SetCameraInstant(Camera cameraToActivate)
    {
        if (cameraToActivate == null)
        {
            return;
        }

        firstPersonCamera.gameObject.SetActive(false);
        thirdPersonCamera.gameObject.SetActive(false);
        tacticalCamera.gameObject.SetActive(false);

        cameraToActivate.gameObject.SetActive(true);
        activeCamera = cameraToActivate;

        UpdateWeaponCamera(cameraToActivate);
    }

    public void SwitchCamera(Camera cameraToActivate)
    {
        if (cameraToActivate == null || cameraToActivate == activeCamera)
        {
            return;
        }

        if (activeCamera == null)
        {
            SetCameraInstant(cameraToActivate);
            return;
        }

        previousCamera = activeCamera;

        Vector3 previousPosition = activeCamera.transform.position;
        Quaternion previousRotation = activeCamera.transform.rotation;

        firstPersonCamera.gameObject.SetActive(false);
        thirdPersonCamera.gameObject.SetActive(false);
        tacticalCamera.gameObject.SetActive(false);

        cameraToActivate.gameObject.SetActive(true);

        targetPosition = cameraToActivate.transform.position;
        targetRotation = cameraToActivate.transform.rotation;

        cameraToActivate.transform.position = previousPosition;
        cameraToActivate.transform.rotation = previousRotation;

        activeCamera = cameraToActivate;
        isTransitioning = true;

        UpdateWeaponCamera(cameraToActivate);
    }

    public void ActivateTacticalCameraTemporarily(float duration)
    {
        if (temporaryCameraRoutine != null)
        {
            StopCoroutine(temporaryCameraRoutine);
        }

        temporaryCameraRoutine = StartCoroutine(TacticalCameraRoutine(duration));
    }

    IEnumerator TacticalCameraRoutine(float duration)
    {
        Camera cameraBeforeEvent = activeCamera;

        SwitchCamera(tacticalCamera);

        yield return new WaitForSeconds(duration);

        if (cameraBeforeEvent != null)
        {
            SwitchCamera(cameraBeforeEvent);
        }
    }

    void UpdateWeaponCamera(Camera newCamera)
    {
        if (weaponRaycast != null)
        {
            weaponRaycast.SetActiveCamera(newCamera);
        }
    }
}