using UnityEngine;
using UnityEngine.UI;


namespace SnakyColors
{
    public class AudioManager : MonoBehaviour
    {
        // The Singleton Instance for global access
        public static AudioManager Instance;

        [SerializeField] private AudioSource sfxSource;
        [SerializeField] private AudioSource bgmSource;
        [SerializeField] private Slider masterSlider;
        [SerializeField] private Slider sfxSlider;
        [SerializeField] private Slider bgmSlider;
        [SerializeField] private Toggle vibrationToggle;

        [Header("Audio Clips")]
        public AudioClip ButtonSound;
        public AudioClip ButtonSound2;
        public AudioClip ObstacleDestroySound;
        public AudioClip ArenaCompleteSound;
        public AudioClip ShopSound;
        public AudioClip DeniedSound;
        public AudioClip ExplosionSound;
        public AudioClip StarCollectSound;
        public AudioClip MenuOpen1Sound; 
        public AudioClip Whoosh1;

        public AudioClip BGMClip;


        // Volume variables (used internally and for saving)
        private float masterVolume = 0.8f;
        private float sfxVolume = 0.8f;
        private float bgmVolume = 0.8f;

        void Awake()
        { 
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
                return;
            } 
        }

        private void Start()
        {
            Play(SoundType.Whoosh1, 0f);
            // Temporarily unsubscribe before setting initial values
            masterSlider.onValueChanged.RemoveAllListeners();
            sfxSlider.onValueChanged.RemoveAllListeners();
            bgmSlider.onValueChanged.RemoveAllListeners();
            vibrationToggle.onValueChanged.RemoveAllListeners();

            // Load saved settings FIRST
            LoadSettings();

            // Set UI elements without triggering unwanted events
            masterSlider.SetValueWithoutNotify(masterVolume);
            sfxSlider.SetValueWithoutNotify(sfxVolume);
            bgmSlider.SetValueWithoutNotify(bgmVolume);
            vibrationToggle.SetIsOnWithoutNotify(PlayerPrefs.GetInt("Vibration", 1) == 1);

            // THEN re-subscribe listeners
            masterSlider.onValueChanged.AddListener(SetMasterVolume);
            sfxSlider.onValueChanged.AddListener(SetSFXVolume);
            bgmSlider.onValueChanged.AddListener(SetBGMVolume);
            vibrationToggle.onValueChanged.AddListener(SetVibration);

            // Apply immediately to sources
            ApplyVolumes();
        }

        private void LoadSettings()
        {
            masterVolume = PlayerPrefs.GetFloat("MasterVolume", masterVolume);
            sfxVolume = PlayerPrefs.GetFloat("SFXVolume", sfxVolume);
            bgmVolume = PlayerPrefs.GetFloat("BGMVolume", bgmVolume);
        } 


        private void OnDestroy()
        { 
            masterSlider.onValueChanged.RemoveListener(SetMasterVolume);
            sfxSlider.onValueChanged.RemoveListener(SetSFXVolume);
            bgmSlider.onValueChanged.RemoveListener(SetBGMVolume);
            vibrationToggle.onValueChanged.RemoveListener(SetVibration);
        }

        // Applies the current volume settings to the AudioSources
        private void ApplyVolumes()
        {
            if (sfxSource != null)
            {
                sfxSource.volume = masterVolume * sfxVolume;
            }
            if (bgmSource != null)
            {
                bgmSource.volume = masterVolume * bgmVolume;
            }
        }

        // Public setter methods for UI sliders or other scripts
        public void SetMasterVolume(float value)
        {
            masterVolume = value;
            PlayerPrefs.SetFloat("MasterVolume", value);
            PlayerPrefs.Save();
            ApplyVolumes();
            Debug.Log("Master: " + value);
        }

        public void SetSFXVolume(float value)
        {
            sfxVolume = value;
            PlayerPrefs.SetFloat("SFXVolume", value);
            PlayerPrefs.Save();
            ApplyVolumes();
            Debug.Log("SFX: " + value);
        }

        public void SetBGMVolume(float value)
        {
            bgmVolume = value;
            PlayerPrefs.SetFloat("BGMVolume", value);
            PlayerPrefs.Save();
            ApplyVolumes();
            Debug.Log("BGM: " + value);
        }

        public void SetVibration(bool value)
        {
            PlayerPrefs.SetInt("Vibration", value ? 1 : 0);
            PlayerPrefs.Save();
            Debug.Log("Vibration: " + value); 
        }

        public void SetPaused(bool paused)
        {
            if (paused) bgmSource.Pause();
            else bgmSource.UnPause();
        }


        /// <summary>
        /// Plays the requested sound effect or BGM clip.
        /// </summary>
        /// <param name="type">The type of sound to play.</param>
        /// <param name="volumeScale">An optional scale for this specific sound's volume.</param>
        public void Play(SoundType type, float volumeScale = 1f)
        {
            AudioClip clip = type switch
            {
                SoundType.Button => ButtonSound,
                SoundType.Button2 => ButtonSound2,
                SoundType.ObstacleDestroy => ObstacleDestroySound,
                SoundType.ArenaComplete => ArenaCompleteSound,
                SoundType.Shop => ShopSound,
                SoundType.Denied => DeniedSound,
                SoundType.Explosion => ExplosionSound,
                SoundType.StarCollect => StarCollectSound,
                SoundType.MenuOpen1 => MenuOpen1Sound,
                SoundType.Whoosh1 => Whoosh1,
                SoundType.BGM => BGMClip,
                _ => null
            };

            if (clip == null)
            {
                // Helpful debug for unassigned clips
                Debug.LogWarning($"AudioClip for {type} is not assigned in the AudioManager Inspector!");
                return;
            }

            if (type == SoundType.BGM)
            {
                // Only restart BGM if the requested clip is different from the current one
                if (bgmSource.clip != clip || !bgmSource.isPlaying)
                {
                    bgmSource.clip = clip;
                    bgmSource.volume = masterVolume * bgmVolume * volumeScale;
                    bgmSource.Play();
                }
            }
            else // Sound Effects (SFX)
            {
                // Use PlayOneShot to allow multiple SFX to overlap without interruption
                sfxSource.PlayOneShot(clip, masterVolume * sfxVolume * volumeScale);
            }
        }

        public void PlayClip(AudioClip clip, float volumeScale = 1f, float minPitch = 0.95f, float maxPitch = 1.05f)
        {
            if (sfxSource == null)
            {
                Debug.LogError("SFX AudioSource is not assigned in AudioManager!");
                return;
            }

            if (clip == null)
            {
                Debug.LogWarning("Attempted to play a null AudioClip via PlayClip method.");
                return;
            }

            // Randomize pitch for natural variation
            sfxSource.pitch = Random.Range(minPitch, maxPitch);

            // Play the clip with volume scaling
            sfxSource.PlayOneShot(clip, masterVolume * sfxVolume * volumeScale);
        }


        public void StopBGM()
        {
            if (bgmSource.isPlaying)
            {
                bgmSource.Stop();
            }
        } 
        public void MuteAll(bool mute)
        {
            AudioListener.volume = mute ? 0f : 1f;
        }
    }
} 

