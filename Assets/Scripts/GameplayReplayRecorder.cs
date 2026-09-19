using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class GameplayReplayRecorder : MonoBehaviour
{
    [System.Serializable]
    public struct CameraFrame
    {
        public Vector3 position;
        public Quaternion rotation;
        public float time;

        public CameraFrame(Vector3 position, Quaternion rotation, float time)
        {
            this.position = position;
            this.rotation = rotation;
            this.time = time;
        }
    }

    [System.Serializable]
    public struct ShotFrame
    {
        public Vector3 startPoint;
        public Vector3 endPoint;
        public Vector3 hitPoint;
        public Vector3 hitNormal;
        public bool hasHit;
        public float time;

        public ShotFrame(Vector3 startPoint, Vector3 endPoint, Vector3 hitPoint, Vector3 hitNormal, bool hasHit, float time)
        {
            this.startPoint = startPoint;
            this.endPoint = endPoint;
            this.hitPoint = hitPoint;
            this.hitNormal = hitNormal;
            this.hasHit = hasHit;
            this.time = time;
        }
    }

    [Header("Replay Camera")]
    public Camera replayCamera;

    [Header("Cameras To Record")]
    public Camera[] camerasToRecord;

    [Header("Replay Recording")]
    public float secondsToRecord = 5f;
    public float recordInterval = 0.03f;

    [Header("Replay Playback")]
    public float playbackSpeed = 1f;
    public bool disableWeaponDuringReplay = true;
    public KeyCode testReplayKey = KeyCode.K;

    [Header("Replay Effects")]
    public GameObject bulletTrailPrefab;
    public GameObject impactEffectPrefab;

    [Header("References")]
    public WeaponRaycast weaponRaycast;

    private List<CameraFrame> recordedFrames = new List<CameraFrame>();
    private List<ShotFrame> recordedShots = new List<ShotFrame>();

    private float recordTimer = 0f;
    private float recordingTime = 0f;

    private bool isRecording = true;
    private bool isReplaying = false;

    private Camera previousCamera;

    void Update()
    {
        if (Input.GetKeyDown(testReplayKey))
        {
            Debug.Log("Replay manual activado con tecla de prueba.");
            PlayReplay();
        }

        if (isReplaying || !isRecording)
        {
            return;
        }

        recordingTime += Time.deltaTime;
        recordTimer += Time.deltaTime;

        if (recordTimer >= recordInterval)
        {
            recordTimer = 0f;
            RecordCurrentCamera();
            RemoveOldData();
        }
    }

    void RecordCurrentCamera()
    {
        Camera currentCamera = GetCurrentActiveCamera();

        if (currentCamera == null)
        {
            return;
        }

        recordedFrames.Add(new CameraFrame(
            currentCamera.transform.position,
            currentCamera.transform.rotation,
            recordingTime
        ));
    }

    public void RegisterShot(Vector3 startPoint, Vector3 endPoint, Vector3 hitPoint, Vector3 hitNormal, bool hasHit)
    {
        if (!isRecording || isReplaying)
        {
            return;
        }

        recordedShots.Add(new ShotFrame(
            startPoint,
            endPoint,
            hitPoint,
            hitNormal,
            hasHit,
            recordingTime
        ));

        RemoveOldData();
    }

    void RemoveOldData()
    {
        float minTime = recordingTime - secondsToRecord;

        while (recordedFrames.Count > 0 && recordedFrames[0].time < minTime)
        {
            recordedFrames.RemoveAt(0);
        }

        while (recordedShots.Count > 0 && recordedShots[0].time < minTime)
        {
            recordedShots.RemoveAt(0);
        }
    }

    public void PlayReplay(Transform focusTarget)
    {
        PlayReplay();
    }

    public void PlayReplay()
    {
        if (isReplaying)
        {
            return;
        }

        if (replayCamera == null)
        {
            Debug.LogWarning("No hay ReplayCamera asignada.");
            return;
        }

        if (recordedFrames.Count < 5)
        {
            Debug.LogWarning("No hay suficientes frames grabados para reproducir replay. Frames: " + recordedFrames.Count);
            return;
        }

        StartCoroutine(ReplayRoutine());
    }

    IEnumerator ReplayRoutine()
    {
        isReplaying = true;
        isRecording = false;

        previousCamera = GetCurrentActiveCamera();

        if (previousCamera != null)
        {
            previousCamera.gameObject.SetActive(false);
        }

        replayCamera.gameObject.SetActive(true);

        if (weaponRaycast != null && disableWeaponDuringReplay)
        {
            weaponRaycast.enabled = false;
        }

        List<CameraFrame> framesToPlay = new List<CameraFrame>(recordedFrames);
        List<ShotFrame> shotsToPlay = new List<ShotFrame>(recordedShots);

        float startTime = framesToPlay[0].time;
        int shotIndex = 0;

        replayCamera.transform.position = framesToPlay[0].position;
        replayCamera.transform.rotation = framesToPlay[0].rotation;

        for (int i = 0; i < framesToPlay.Count; i++)
        {
            CameraFrame frame = framesToPlay[i];

            replayCamera.transform.position = frame.position;
            replayCamera.transform.rotation = frame.rotation;

            float replayTime = frame.time - startTime;

            while (shotIndex < shotsToPlay.Count && shotsToPlay[shotIndex].time - startTime <= replayTime)
            {
                PlayShotEffect(shotsToPlay[shotIndex]);
                shotIndex++;
            }

            yield return new WaitForSeconds(recordInterval / playbackSpeed);
        }

        replayCamera.gameObject.SetActive(false);

        if (previousCamera != null)
        {
            previousCamera.gameObject.SetActive(true);

            if (weaponRaycast != null)
            {
                weaponRaycast.SetActiveCamera(previousCamera);
            }
        }

        if (weaponRaycast != null && disableWeaponDuringReplay)
        {
            weaponRaycast.enabled = true;
        }

        recordedFrames.Clear();
        recordedShots.Clear();

        isRecording = true;
        isReplaying = false;

        Debug.Log("Replay terminado.");
    }

    void PlayShotEffect(ShotFrame shot)
    {
        if (bulletTrailPrefab != null)
        {
            GameObject trailObject = Instantiate(bulletTrailPrefab, Vector3.zero, Quaternion.identity);
            BulletTrail trail = trailObject.GetComponent<BulletTrail>();

            if (trail != null)
            {
                trail.SetPositions(shot.startPoint, shot.endPoint);
            }
        }

        if (shot.hasHit && impactEffectPrefab != null)
        {
            Instantiate(impactEffectPrefab, shot.hitPoint, Quaternion.LookRotation(shot.hitNormal));
        }
    }

    Camera GetCurrentActiveCamera()
    {
        if (camerasToRecord == null)
        {
            return null;
        }

        foreach (Camera cam in camerasToRecord)
        {
            if (cam != null && cam.gameObject.activeInHierarchy)
            {
                return cam;
            }
        }

        return null;
    }
}