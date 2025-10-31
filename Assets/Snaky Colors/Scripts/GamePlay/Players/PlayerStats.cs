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
        [SerializeField] private float currentHealth = 0f;
        [SerializeField] private float currentDashCharge = 0f;

        // --- Configuration ---
        [Header("Configuration")]
        [SerializeField] private int startingAmmo = 10;
        [SerializeField] private float maxFruitMeter = 1000f;
        [SerializeField] private float startingHealth = 100f; // Use this as Max Health
        [SerializeField] private float startingDashCharge = 100f; // Use this as Max Dash
        [SerializeField] private int maxAmmo = 50; // Added Max Ammo for UI

        // --- Events ---
        public event System.Action<int> OnScoreChanged;
        public event System.Action<float> OnMeterChanged;
        public event System.Action<int> OnAmmoChanged;
        public event System.Action<float> OnHealthChanged;
        public event System.Action<float> OnDashChargeChanged;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
                ResetStats();
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void Start()
        {
        }

        public void ResetStats()
        {
            currentScore = 0;
            currentFruitMeter = 0f;
            currentAmmo = startingAmmo;
            currentHealth = startingHealth;
            currentDashCharge = startingDashCharge;

            OnScoreChanged?.Invoke(currentScore);
            OnMeterChanged?.Invoke(currentFruitMeter);
            OnAmmoChanged?.Invoke(currentAmmo);
            OnHealthChanged?.Invoke(currentHealth);
            OnDashChargeChanged?.Invoke(currentDashCharge);

            Debug.Log("PlayerStats Reset");
            Debug.Log($"ResetStats: startingDashCharge={startingDashCharge}, currentDashCharge={currentDashCharge}");

        }

        // --- Score Methods ---
        public void AddScore(int amount)
        {
            if (amount == 0) return;
            currentScore += amount;
            OnScoreChanged?.Invoke(currentScore);
            Debug.Log($"Score updated: {currentScore}");
        }
         
        public void AddToMeter(float amount)
        {
            if (amount == 0f) return;
            currentFruitMeter = Mathf.Clamp(currentFruitMeter + amount, 0f, maxFruitMeter);
            OnMeterChanged?.Invoke(currentFruitMeter);
            Debug.Log($"Meter updated: {currentFruitMeter}");
        }
         
        public bool TryConsumeAmmo(int amount)
        {
            if (amount <= 0) return false; // Can't consume 0 or less
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
                return false;
            }
        }

        public void AddAmmo(int amount)
        {
            if (amount <= 0) return;
            currentAmmo = Mathf.Min(currentAmmo + amount, maxAmmo); // Clamp to max ammo
            OnAmmoChanged?.Invoke(currentAmmo);
            Debug.Log($"Ammo added. Current: {currentAmmo}");
        }
         
        public void TakeDamage(float amount)
        {
            if (amount <= 0) return;
            currentHealth = Mathf.Max(0f, currentHealth - amount); 
            OnHealthChanged?.Invoke(currentHealth);
            Debug.Log($"Took {amount} damage. Health: {currentHealth}");

            if (currentHealth <= 0)
            {
                HandleDeath(); 
            }
        }

        public void Heal(float amount)
        {
            if (amount <= 0) return;
            currentHealth = Mathf.Min(currentHealth + amount, startingHealth); // Clamp to max health
            OnHealthChanged?.Invoke(currentHealth);
            Debug.Log($"Healed {amount}. Health: {currentHealth}");
        }

        private void HandleDeath()
        {
            Debug.Log("Player has died. Triggering Game Over.");
            // Example: Tell the GameManager to handle the game over sequence
            // GameManager.Instance?.GameOver();
        } 
         
        public bool TryConsumeDashCharge(int amount)
        {
            if (amount <= 0) return false;
            if (currentDashCharge >= amount)
            {
                currentDashCharge -= amount;
                OnDashChargeChanged?.Invoke(currentDashCharge);
                Debug.Log($"Dash charge consumed. Remaining: {currentDashCharge}");
                return true;
            }
            else
            {
                Debug.Log($"Not enough dash charge. Required: {amount}, Have: {currentDashCharge}");
                return false;
            }
        }

        public void AddDashCharge(float amount)
        {
            if (amount <= 0) return;
            currentDashCharge = Mathf.Min(currentDashCharge + amount, startingDashCharge);
            OnDashChargeChanged?.Invoke(currentDashCharge);
            Debug.Log($"Dash charge added. Current: {currentDashCharge}");
        }

        // --- Getters ---
        public int GetCurrentScore() => currentScore;
        public float GetCurrentMeter() => currentFruitMeter;
        public float GetMaxMeter() => maxFruitMeter;
        public int GetCurrentAmmo() => currentAmmo;
        public int GetMaxAmmo() => maxAmmo; 
        public float GetCurrentHealth() => currentHealth;
        public float GetMaxHealth() => startingHealth; 
        public float GetCurrentDashCharge() => currentDashCharge;
        public float GetMaxDashCharge() => startingDashCharge;
    }
}