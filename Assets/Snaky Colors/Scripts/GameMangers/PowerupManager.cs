using UnityEngine;
using System.Collections;
using System.Collections.Generic; // For potential future powerup tracking

namespace SnakyColors
{
    public class PowerupManager : MonoBehaviour
    {
        // --- Singleton ---
        public static PowerupManager Instance { get; private set; }

        // --- Magnet State ---
        public bool IsMagnetActive { get; private set; } = false;
        private Coroutine magnetCoroutine;

        // --- (Add states/coroutines for other powerups like Rush, Shield here later) ---
        // public bool IsRushActive { get; private set; } = false;
        // private Coroutine rushCoroutine;
        // public bool IsShieldActive { get; private set; } = false;
        // private Coroutine shieldCoroutine;

        private void Awake()
        {
            // Setup Singleton
            if (Instance == null)
            {
                Instance = this;
                // Consider DontDestroyOnLoad if this manager needs to persist across scenes
                // DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject); // Destroy duplicate instances
            }
        }

        /// <summary>
        /// Activates a specific powerup for a given duration. Called by GeneratedItem on pickup.
        /// </summary>
        /// <param name="type">The type of powerup to activate.</param>
        /// <param name="duration">How long the powerup should last.</param>
        public void ActivatePowerup(PowerupType type, float duration)
        {
            // Ignore activation if duration is invalid or powerup is None
            if (duration <= 0 || type == PowerupType.None)
            {
                if (duration <= 0) Debug.LogWarning($"Attempted to activate {type} with zero or negative duration.");
                return;
            }

            Debug.Log($"Attempting to activate {type} for {duration}s.");

            // Handle specific powerup activation
            switch (type)
            {
                case PowerupType.Magnet:
                    ActivateMagnet(duration);
                    break;
                case PowerupType.Rush:
                    // ActivateRush(duration); // Implement later
                    Debug.Log("Rush powerup activated (logic needs implementation).");
                    break;
                case PowerupType.Shield:
                    // ActivateShield(duration); // Implement later
                    Debug.Log("Shield powerup activated (logic needs implementation).");
                    break;
                // Add cases for other specific powerup types here
                // case PowerupType.ScoreMultiplier:
                //    ActivateScoreMultiplier(duration);
                //    break;

                case PowerupType.WeaponUpgrade:
                    // Weapon upgrades are handled directly by PlayerShooting.EquipWeapon()
                    // No state needs to be tracked in this manager for that.
                    Debug.Log("WeaponUpgrade type received - PlayerShooting handles this directly.");
                    break;

                default:
                    Debug.LogWarning($"Powerup type {type} activation not specifically handled by PowerupManager.");
                    break;
            }
        }

        /// <summary>
        /// Specific activation logic for the Magnet.
        /// </summary>
        private void ActivateMagnet(float duration)
        {
            // Stop any previous magnet timer to reset duration
            if (magnetCoroutine != null)
            {
                StopCoroutine(magnetCoroutine);
                Debug.Log("Stopping previous magnet coroutine.");
            }

            IsMagnetActive = true;
            Debug.Log($"Magnet Activated for {duration} seconds!");
            magnetCoroutine = StartCoroutine(DeactivateAfterDelay(PowerupType.Magnet, duration));

            // TODO: Play activation sound/visual effect on the player?
            // e.g., PlayerGraphicsManager.Instance?.ShowMagnetEffect(true); // Assuming PlayerGraphicsManager is a Singleton
        }

        // --- (Implement ActivateRush, ActivateShield methods similarly later) ---
        /*
        private void ActivateRush(float duration) { ... }
        private void ActivateShield(float duration) { ... }
        */

        /// <summary>
        /// Coroutine to deactivate a powerup after its duration expires.
        /// </summary>
        private IEnumerator DeactivateAfterDelay(PowerupType type, float duration)
        {
            yield return new WaitForSeconds(duration);

            // Ensure the instance wasn't destroyed while waiting (e.g., scene change)
            if (this == null) yield break;

            Debug.Log($"Deactivating {type} after {duration}s delay.");

            // Handle specific powerup deactivation
            switch (type)
            {
                case PowerupType.Magnet:
                    IsMagnetActive = false;
                    Debug.Log("Magnet Deactivated.");
                    magnetCoroutine = null; // Clear the coroutine handle
                    // TODO: Stop activation sound/visual effect on the player?
                    // e.g., PlayerGraphicsManager.Instance?.ShowMagnetEffect(false);
                    break;
                case PowerupType.Rush:
                    // DeactivateRush(); // Implement later
                    // e.g., PlayerMovement.Instance?.SetSpeedMultiplier(1f); // Reset speed
                    break;
                case PowerupType.Shield:
                    // DeactivateShield(); // Implement later
                    // e.g., PlayerHealth.Instance?.SetInvulnerable(false); // If you add health
                    break;
                    // Add cases for other powerup types
            }
        }

        /// <summary>
        /// Optional: Method to forcefully deactivate all tracked powerups immediately.
        /// Useful for game over or level transitions.
        /// </summary>
        public void DeactivateAllPowerups()
        {
            Debug.Log("Force deactivating all powerups.");

            // Stop coroutines and reset flags
            if (magnetCoroutine != null) StopCoroutine(magnetCoroutine);
            IsMagnetActive = false;
            magnetCoroutine = null;

            // --- Add similar logic for Rush, Shield, etc. when implemented ---
            // if (rushCoroutine != null) StopCoroutine(rushCoroutine);
            // IsRushActive = false;
            // rushCoroutine = null;
            // PlayerMovement.Instance?.SetSpeedMultiplier(1f); // Example reset

            // if (shieldCoroutine != null) StopCoroutine(shieldCoroutine);
            // IsShieldActive = false;
            // shieldCoroutine = null;
            // PlayerHealth.Instance?.SetInvulnerable(false); // Example reset

            // Stop any visual effects
            // PlayerGraphicsManager.Instance?.HideAllPowerupEffects();
        }
    }
}