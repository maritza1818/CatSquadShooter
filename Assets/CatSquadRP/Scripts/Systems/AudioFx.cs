using UnityEngine;

namespace CatSquad.RescateProfundo
{
    /// <summary>Efectos de sonido sencillos (los clips los asigna el constructor de escena).</summary>
    public class AudioFx : MonoBehaviour
    {
        public static AudioFx Instance { get; private set; }

        public AudioClip bubblePop;
        public AudioClip shardCollect;
        public AudioClip crateOpen;
        public AudioClip grab;
        public AudioClip release;
        public AudioClip pumpLoop;
        public AudioClip pourLoop;
        public AudioClip gateOpen;
        public AudioClip kittenMeow;
        public AudioClip chime;
        public AudioClip ambientLoop;

        [Range(0f, 1f)] public float masterVolume = 0.8f;

        private AudioSource oneShots;
        private AudioSource ambient;

        private void Awake()
        {
            Instance = this;
            oneShots = gameObject.AddComponent<AudioSource>();
            oneShots.playOnAwake = false;
            oneShots.spatialBlend = 0f;

            if (ambientLoop != null)
            {
                ambient = gameObject.AddComponent<AudioSource>();
                ambient.clip = ambientLoop;
                ambient.loop = true;
                ambient.volume = 0.35f * masterVolume;
                ambient.Play();
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public static void Play(AudioClip clip, Vector3 position, float volume = 1f, float pitch = 1f)
        {
            if (clip == null || Instance == null || Instance.oneShots == null) return;
            Instance.oneShots.pitch = pitch;
            Instance.oneShots.PlayOneShot(clip, volume * Instance.masterVolume);
        }

        /// <summary>Crea una fuente en bucle (bomba, vertido) que el llamador enciende/apaga.</summary>
        public static AudioSource CreateLoop(GameObject owner, AudioClip clip, float volume)
        {
            if (clip == null) return null;
            AudioSource s = owner.AddComponent<AudioSource>();
            s.clip = clip;
            s.loop = true;
            s.playOnAwake = false;
            s.volume = volume * (Instance != null ? Instance.masterVolume : 0.8f);
            s.spatialBlend = 0f;
            return s;
        }
    }
}
