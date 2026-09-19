using UnityEngine;
using System.Collections;

public class TacticalCameraEvent : MonoBehaviour
{
    public CameraModeManager cameraModeManager;
    public SimpleTacticalCamera tacticalCameraController;

    public void PlayTacticalEvent(Transform focusTarget, float duration)
    {
        if (tacticalCameraController != null)
        {
            tacticalCameraController.FocusOnEventTarget(focusTarget);
        }

        if (cameraModeManager != null)
        {
            cameraModeManager.ActivateTacticalCameraTemporarily(duration);
        }

        StartCoroutine(ClearFocusAfterTime(duration));
    }

    IEnumerator ClearFocusAfterTime(float time)
    {
        yield return new WaitForSeconds(time);

        if (tacticalCameraController != null)
        {
            tacticalCameraController.ClearEventTarget();
        }
    }
}