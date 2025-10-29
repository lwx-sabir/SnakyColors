using UnityEngine;

namespace SnakyColors
{
    public class PlayerStats : MonoBehaviour
    {
        // --- Singleton Pattern ---
        public static PlayerStats Instance { get; private set; }

        // --- Core Player Stats ---
        [Header("Runtime Stats")]
        [SerializeField] private int currentScore = 0;
        [SerializeField] private float currentFruitMeter = 0f;
        [SerializeField] private int currentAmmo = 0;

        // --- Configuration (Optional) ---
        [Header("Configuration")]
        [SerializeField] private int startingAmmo = 10;
        [SerializeField] private float maxFruitMeter = 100f; // Example max value

        // --- Events (Optional but Recommended) ---
        // Other scripts can listen to these to update UI, etc.
        public event System.Action<int> OnScoreChanged;
        public event System.Action<float> OnMeterChanged; // Sends current meter value
        public event System.Action<int> OnAmmoChanged;

        private void Awake()
        {
            // Setup Singleton
            if (Instance == null)
            {
                Instance = this;
                // DontDestroyOnLoad(gameObject); // Optional: if player stats need to persist across scenes
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void Start()
        {
            // Initialize stats at the start of the game/level
            ResetStats();
        }

        /// <summary>
        /// Resets all player stats to their initial values.
        /// Called at the start of a new game or level.
        /// </summary>
        public void ResetStats()
        {
            currentScore = 0;
            currentFruitMeter = 0f;
            currentAmmo = startingAmmo;

            // Notify listeners about the reset
            OnScoreChanged?.Invoke(currentScore);
            OnMeterChanged?.Invoke(currentFruitMeter);
            OnAmmoChanged?.Invoke(currentAmmo);

            Debug.Log("PlayerStats Reset");
        }

        /// <summary>
        /// Adds the specified amount to the player's score.
        /// </summary>
        /// <param name="amount">Score to add (can be negative).</param>
        public void AddScore(int amount)
        {
            currentScore += amount;
            // Ensure score doesn't go below zero (optional)
            // currentScore = Mathf.Max(0, currentScore);

            OnScoreChanged?.Invoke(currentScore);
            Debug.Log($"Score updated: {currentScore}");
        }

        /// <summary>
        /// Adds the specified amount to the fruit meter, clamping it to the max value.
        /// </summary>
        /// <param name="amount">Amount to add (can be negative).</param>
        public void AddToMeter(float amount)
        {
            currentFruitMeter += amount;
            // Clamp the meter value between 0 and max
            currentFruitMeter = Mathf.Clamp(currentFruitMeter, 0f, maxFruitMeter);

            OnMeterChanged?.Invoke(currentFruitMeter);
            Debug.Log($"Meter updated: {currentFruitMeter}");
        }

        /// <summary>
        /// Attempts to consume the specified amount of ammo.
        /// </summary>
        /// <param name="amount">Amount of ammo to consume.</param>
        /// <returns>True if ammo was successfully consumed, false otherwise.</returns>
        public bool TryConsumeAmmo(int amount)
        {
            if (currentAmmo >= amount)
            {
                currentAmmo -= amount;
                OnAmmoChanged?.Invoke(currentAmmo);
                Debug.Log($"Ammo consumed. Remaining: {currentAmmo}");
                return true;
            }
            else
            {
                Debug.Log($"Not enough ammo. Required: {amount}, Have: {currentAmmo}");
                return false; // Not enough ammo
            }
        }

        /// <summary>
        /// Adds the specified amount of ammo.
        /// </summary>
        /// <param name="amount">Amount to add.</param>
        public void AddAmmo(int amount)
        {
            if (amount <= 0) return; // Ignore non-positive amounts
            currentAmmo += amount;
            OnAmmoChanged?.Invoke(currentAmmo);
            Debug.Log($"Ammo added. Current: {currentAmmo}");
        }


        // --- Getters (Optional) ---
        // Provide read-only access to stats if needed by other scripts

        public int GetCurrentScore() => currentScore;
        public float GetCurrentMeter() => currentFruitMeter;
        public float GetMaxMeter() => maxFruitMeter;
        public int GetCurrentAmmo() => currentAmmo;

    }
}