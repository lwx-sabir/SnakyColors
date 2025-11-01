using UnityEngine;
using System.Collections;

namespace SnakyColors
{
    public class PlayerStats : MonoBehaviour
    {
        public static PlayerStats Instance { get; private set; }

        [Header("Active Config Reference")]
        [SerializeField] private PlayerStatConfig activeConfig;

        [Header("Runtime Stats")]
        [SerializeField] private int currentScore = 0;
        [SerializeField] private float currentFruitMeter = 0f;
        [SerializeField] private int currentAmmo = 0;
        [SerializeField] private float currentHealth = 0f;
        [SerializeField] private float currentDashCharge = 0f;

        // --- Events ---
        public event System.Action<int> OnScoreChanged;
        public event System.Action<float> OnMeterChanged;
        public event System.Action<int> OnAmmoChanged;
        public event System.Action<float> OnHealthChanged;
        public event System.Action<float> OnDashChargeChanged;

        // --- Internals ---
        private Coroutine ammoRegenRoutine;
        private Coroutine healthRegenRoutine;
        private Coroutine dashRegenRoutine;

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

        private void OnDisable() => StopAllRegen();
        private void OnDestroy() => StopAllRegen();

        // --- Reset ---
        public void ResetStats()
        {
            if (activeConfig == null)
            {
                Debug.LogError("[PlayerStats] No active PlayerStatConfig assigned!");
                return;
            }

            currentScore = 0;
            currentFruitMeter = 0f;
            currentAmmo = activeConfig.startingAmmo;
            currentHealth = activeConfig.startingHealth;
            currentDashCharge = activeConfig.startingDashCharge;

            ClampAllStats();
            TriggerAllEvents();
            EditorLog("PlayerStats Reset Complete");

            StartAllRegen();
        }

        public void SetConfig(PlayerStatConfig newConfig, bool resetStats = false)
        {
            if (newConfig == null)
            {
                Debug.LogWarning("[PlayerStats] SetConfig() called with null config!");
                return;
            }

            activeConfig = newConfig;
            EditorLog($"PlayerStats configuration updated at runtime: {newConfig.name}");

            StopAllRegen();
            StartAllRegen();

            if (resetStats)
            {
                ResetStats();
            }
            else
            {
                ClampAllStats();
                TriggerAllEvents();
            }
        }

        // --- Regen Control ---
        private void StartAllRegen()
        {
            if (activeConfig == null) return;

            StartAmmoRegen();

            if (activeConfig.enableHealthRegen)
                StartHealthRegen();

            if (activeConfig.enableDashRegen)
                StartDashRegen();
        }

        private void StopAllRegen()
        {
            StopAmmoRegen();
            StopHealthRegen();
            StopDashRegen();
        }

        // --- Ammo Regen ---
        private void StartAmmoRegen()
        {
            if (ammoRegenRoutine == null)
                ammoRegenRoutine = StartCoroutine(AutoGenerateAmmo());
        }

        private void StopAmmoRegen()
        {
            if (ammoRegenRoutine != null)
            {
                StopCoroutine(ammoRegenRoutine);
                ammoRegenRoutine = null;
            }
        }

        private IEnumerator AutoGenerateAmmo()
        {
            var wait = new WaitForSeconds(activeConfig.ammoGenerationInterval);
            while (true)
            {
                yield return wait;
                if (currentAmmo < activeConfig.maxAmmo)
                    AddAmmo(activeConfig.ammoRegenRate);
            }
        }

        // --- Health Regen ---
        private void StartHealthRegen()
        {
            if (healthRegenRoutine == null)
                healthRegenRoutine = StartCoroutine(AutoRegenerateHealth());
        }

        private void StopHealthRegen()
        {
            if (healthRegenRoutine != null)
            {
                StopCoroutine(healthRegenRoutine);
                healthRegenRoutine = null;
            }
        }

        private IEnumerator AutoRegenerateHealth()
        {
            var wait = new WaitForSeconds(activeConfig.healthRegenInterval);
            while (true)
            {
                yield return wait;
                if (currentHealth < activeConfig.maxHealth && currentHealth > 0)
                    Heal(activeConfig.healthRegenRate);
            }
        }

        // --- Dash Regen ---
        private void StartDashRegen()
        {
            if (dashRegenRoutine == null)
                dashRegenRoutine = StartCoroutine(AutoRegenerateDash());
        }

        private void StopDashRegen()
        {
            if (dashRegenRoutine != null)
            {
                StopCoroutine(dashRegenRoutine);
                dashRegenRoutine = null;
            }
        }

        private IEnumerator AutoRegenerateDash()
        {
            var wait = new WaitForSeconds(activeConfig.dashRegenInterval);
            while (true)
            {
                yield return wait;
                if (currentDashCharge < activeConfig.maxDashCharge)
                    AddDashCharge(activeConfig.dashRegenRate);
            }
        }

        // --- Utility ---
        private void ClampAllStats()
        {
            if (activeConfig == null) return;

            currentHealth = Mathf.Clamp(currentHealth, 0f, activeConfig.maxHealth);
            currentDashCharge = Mathf.Clamp(currentDashCharge, 0f, activeConfig.maxDashCharge);
            currentAmmo = Mathf.Clamp(currentAmmo, 0, activeConfig.maxAmmo);
            currentFruitMeter = Mathf.Clamp(currentFruitMeter, 0f, activeConfig.maxFruitMeter);
        }

        private void TriggerAllEvents()
        {
            OnScoreChanged?.Invoke(currentScore);
            OnMeterChanged?.Invoke(currentFruitMeter);
            OnAmmoChanged?.Invoke(currentAmmo);
            OnHealthChanged?.Invoke(currentHealth);
            OnDashChargeChanged?.Invoke(currentDashCharge);
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void EditorLog(string message) => Debug.Log(message);

        // --- Score ---
        public void AddScore(int amount)
        {
            if (amount == 0) return;
            currentScore += amount;
            OnScoreChanged?.Invoke(currentScore);
        }

        public void AddToMeter(float amount)
        {
            if (amount == 0f) return;
            currentFruitMeter = Mathf.Clamp(currentFruitMeter + amount, 0f, activeConfig.maxFruitMeter);
            OnMeterChanged?.Invoke(currentFruitMeter);
        }

        // --- Ammo ---
        public bool TryConsumeAmmo(int amount)
        {
            if (amount <= 0) return false;
            if (currentAmmo >= amount)
            {
                currentAmmo -= amount;
                OnAmmoChanged?.Invoke(currentAmmo);
                return true;
            }
            return false;
        }

        public void AddAmmo(int amount)
        {
            if (amount <= 0) return;
            currentAmmo = Mathf.Min(currentAmmo + amount, activeConfig.maxAmmo);
            OnAmmoChanged?.Invoke(currentAmmo);
        }

        // --- Health ---
        public void TakeDamage(float amount)
        {
            if (amount <= 0) return;
            currentHealth = Mathf.Max(0f, currentHealth - amount);
            OnHealthChanged?.Invoke(currentHealth);
            if (currentHealth <= 0)
                HandleDeath();
        }

        public void Heal(float amount)
        {
            if (amount <= 0) return;
            currentHealth = Mathf.Min(currentHealth + amount, activeConfig.maxHealth);
            OnHealthChanged?.Invoke(currentHealth);
        }

        private void HandleDeath()
        {
            EditorLog("Player has died. Trigger Game Over here.");
            // GameManager.Instance?.GameOver();
        }

        // --- Dash ---
        public bool TryConsumeDashCharge(float amount)
        {
            if (amount <= 0) return false;
            if (currentDashCharge >= amount)
            {
                currentDashCharge -= amount;
                OnDashChargeChanged?.Invoke(currentDashCharge);
                return true;
            }
            return false;
        }

        public void AddDashCharge(float amount)
        {
            if (amount <= 0) return;
            currentDashCharge = Mathf.Min(currentDashCharge + amount, activeConfig.maxDashCharge);
            OnDashChargeChanged?.Invoke(currentDashCharge);
        }

        // --- Getters ---
        public int GetCurrentScore() => currentScore;
        public float GetCurrentMeter() => currentFruitMeter;
        public float GetMaxMeter() => activeConfig.maxFruitMeter;
        public int GetCurrentAmmo() => currentAmmo;
        public int GetMaxAmmo() => activeConfig.maxAmmo;
        public float GetCurrentHealth() => currentHealth;
        public float GetMaxHealth() => activeConfig.maxHealth;
        public float GetCurrentDashCharge() => currentDashCharge;
        public float GetMaxDashCharge() => activeConfig.maxDashCharge; 
        public PlayerStatConfig GetActiveConfig() => activeConfig;
    }
}
