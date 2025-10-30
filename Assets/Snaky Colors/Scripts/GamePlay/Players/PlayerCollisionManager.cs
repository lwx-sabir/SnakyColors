using UnityEngine;
using System.Collections; // For Coroutines

namespace SnakyColors
{
    [RequireComponent(typeof(Collider2D))] // Ensure a Collider2D exists
    public class PlayerCollisionManager : MonoBehaviour
    {
        // --- Optional References ---
        // Assign in Inspector if needed for visual/audio feedback on player
        [Header("Optional Component References")]
        [SerializeField] private Animator playerAnimator;
        [SerializeField] private SpriteRenderer playerSpriteRenderer; // For invincibility flash

        // --- Configuration ---
        [Header("Invincibility")]
        [SerializeField] private float invincibilityDuration = 1.0f; // Seconds of invincibility after hit
        [SerializeField] private float flashInterval = 0.1f; // How fast to flash during invincibility

        private bool isInvincible = false;
        private Coroutine invincibilityCoroutine;
        private Collider2D playerCollider; // Cache own collider
        private AudioClip basicHitClip;
        private void Awake()
        {
            playerCollider = GetComponent<Collider2D>();
            basicHitClip = GetComponent<PlayerVisuals>().basicHitClip; 
            if (playerSpriteRenderer == null) playerSpriteRenderer = GetComponent<SpriteRenderer>();
        }

        /// <summary>
        /// Main entry point for detecting trigger collisions.
        /// </summary>
        private void OnTriggerEnter2D(Collider2D other)
        {
            // Ignore collisions if currently invincible
            if (isInvincible) return;

            // --- Identify what was hit ---
            // Check first if it's a GeneratedItem
            if (other.TryGetComponent<GeneratedItem>(out var item) && item.data != null)
            {
                // Handle based on the item's category
                switch (item.data.category)
                {
                    case ItemCategory.Hazard:
                        // Player hit a hazard item
                        HandleHazardCollision(item);
                        break;

                    case ItemCategory.Collectible:
                    case ItemCategory.Ammo:
                        // Player hit a collectible or ammo
                        // The item itself handles applying stats/sounds via its HandleCollection
                        HandleCollectibleCollision(item); // Player-specific reaction (optional)
                        break;

                    case ItemCategory.PowerUp:
                        // Player hit a powerup
                        // The item itself handles activation via its HandlePowerupActivation
                        // and stats via HandleCollection
                        HandlePowerupCollision(item); // Player-specific reaction (optional)
                        break;

                    default:
                        // Optional log for unhandled item categories
                        // Debug.LogWarning($"Player collided with unhandled ItemCategory: {item.data.category}", other.gameObject);
                        break;
                }
            }
            // Check if it's an Enemy Projectile (Example structure)
            // else if (other.TryGetComponent<EnemyProjectile>(out var enemyProjectile))
            // {
            //     TakeDamage(enemyProjectile.Damage); // Projectile tells player to take damage
            //     enemyProjectile.HandleHit(); // Tell projectile it hit something
            // }
            else
            {
                // Handle collisions with other non-item objects (walls, etc.) if needed
                HandleOtherCollision(other);
            }
        }

        /// <summary>
        /// Public method for external sources (like enemy projectiles) to damage the player.
        /// </summary>
        /// <param name="damageAmount">Amount of damage/meter loss to apply.</param>
        public void TakeDamage(float damageAmount)
        {
            // Ignore damage if invincible
            if (isInvincible) return;

            Debug.Log($"Player taking {damageAmount} damage/meter loss.");

            // 1. Apply Damage/Meter Loss via PlayerStats
            if (PlayerStats.Instance != null)
            {
                PlayerStats.Instance.AddToMeter(-Mathf.Abs(damageAmount)); // Ensure it subtracts
            }
            else
            {
                Debug.LogError("TakeDamage failed: PlayerStats.Instance is NULL!");
            }

            // 2. Play Player Hit Animation (if Animator reference exists)
            playerAnimator?.SetTrigger("Hit"); // Use null-conditional operator

            // 3. Play Player Hit Sound (if AudioManager exists)
            // TODO: Define and assign a specific player hit sound
            // AudioManager.Instance?.Play(/* PlayerHitSound */);

            // 4. Start Invincibility
            StartInvincibility();
        }

        /// <summary>
        /// Called specifically when the player collides with a Hazard-category GeneratedItem.
        /// </summary>
        private void HandleHazardCollision(GeneratedItem hazardItem)
        {
            if (basicHitClip != null)
            {
                if (AudioManager.Instance != null)
                {
                    AudioManager.Instance.PlayClip(basicHitClip, Random.Range(0.88f, 1f));
                }
            }
            TakeDamage(hazardItem.data.value); 
        }

        /// <summary>
        /// Optional: Handles player-specific reactions to collecting items (e.g., player animation).
        /// </summary>
        private void HandleCollectibleCollision(GeneratedItem collectibleItem)
        {
            // Add any player-specific *visual* or *audio* feedback here if needed.
            // Stat changes are handled by the item itself.
        }

        /// <summary>
        /// Optional: Handles player-specific reactions to collecting powerups.
        /// </summary>
        private void HandlePowerupCollision(GeneratedItem powerupItem)
        {
            // Add any player-specific *visual* or *audio* feedback here if needed.
            // Stat changes and powerup activation are handled by the item itself.
        }

        /// <summary>
        /// Handles collisions with objects that aren't GeneratedItems or known projectiles.
        /// </summary>
        private void HandleOtherCollision(Collider2D other)
        {
            // Add logic for walls, boundaries, etc. if required
            // Debug.Log($"Player collided with other object: {other.name}", other.gameObject);
        }

        /// <summary>
        /// Initiates the player's invincibility period.
        /// </summary>
        private void StartInvincibility()
        {
            if (invincibilityCoroutine != null)
            {
                StopCoroutine(invincibilityCoroutine); // Stop previous if any
            }
            invincibilityCoroutine = StartCoroutine(InvincibilityCoroutine());
        }

        /// <summary>
        /// Coroutine for handling invincibility duration and visual flashing.
        /// </summary>
        private IEnumerator InvincibilityCoroutine()
        {
            isInvincible = true;
            Debug.Log("Player Invincible");

            float endTime = Time.time + invincibilityDuration;
            bool visible = true;

            // Flash effect
            while (Time.time < endTime)
            {
                if (playerSpriteRenderer != null)
                {
                    // Toggle visibility using alpha or enabled state
                    // Using enabled is simpler but harsher visually
                    playerSpriteRenderer.enabled = visible;
                    // Or fade alpha:
                    // playerSpriteRenderer.color = new Color(1f, 1f, 1f, visible ? 1f : 0.5f);
                }
                visible = !visible;
                yield return new WaitForSeconds(flashInterval);
            }

            // Ensure player is visible and invincibility ends
            if (playerSpriteRenderer != null)
            {
                playerSpriteRenderer.enabled = true;
                // playerSpriteRenderer.color = Color.white; // Reset color if using alpha fade
            }
            isInvincible = false;
            invincibilityCoroutine = null; // Clear coroutine handle
            Debug.Log("Player No Longer Invincible");
        }
    }
}