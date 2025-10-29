using UnityEngine;
using System.Collections;
using UnityEditor;

namespace SnakyColors
{
    public class GeneratedItem : MonoBehaviour
    {
        [HideInInspector] public ItemData data;

        [Header("References")]
        public SpriteRenderer graphicsRenderer;
        [Header("Particles")]
        [SerializeField] private ParticleSystem collectParticle;

        public DynamicItemSpawner spawner { get; set; }
        private FruitCollectEffect collectEffect;
        private Vector3 originalScale;
        private Transform playerHead; // For collect effect target
        private Collider2D col;

        [Header("Settings")]
        public float despawnOffset = 10f;

        

        private bool isBeingPulled = false; // State flag
        private Transform playerTransform; // Cache the main player transform for area check

        private void Awake()
        {
            collectEffect = GetComponent<FruitCollectEffect>();
            col = GetComponent<Collider2D>();
            originalScale = transform.localScale;
            if (collectParticle == null) collectParticle = GetComponent<ParticleSystem>();
        }

        private void OnEnable()
        {
            transform.localScale = originalScale;
            if (graphicsRenderer != null)
            {
                graphicsRenderer.enabled = true;
                graphicsRenderer.color = Color.white;
            }
            if (col != null) col.enabled = true;
            if (collectEffect != null) collectEffect.enabled = true;
            isBeingPulled = false;
        }

        public void SetData(ItemData newItemData, Transform player)
        {
            data = newItemData;
            playerHead = player; // Head for collect effect
            playerTransform = player; // Main transform for magnet area
            Debug.Log($"SetData called on {gameObject.name}. PlayerTransform assigned: {(playerTransform != null)}", gameObject); // LOG S1
        }

        void Update()
        {
            if (playerTransform == null || data == null) return;

            if (isBeingPulled)
            {
                Vector3 direction = (playerHead.position - transform.position).normalized;
                transform.position += direction * data.magnetPullSpeed * Time.deltaTime;
                // Optional: Add a check to auto-collect if very close to prevent orbiting?
                // if ((playerHead.position - transform.position).sqrMagnitude < 0.1f * 0.1f) {
                //     if (col != null && col.enabled) { // Check if not already collected
                //         HandleCollection(); // Apply stats
                //         StartCollectSequence(); // Start visual effect and return
                //         return; // Exit Update early after auto-collect
                //     }
                // }
            }

            else
            {
                bool magnetIsActive = PowerupManager.Instance != null && PowerupManager.Instance.IsMagnetActive;

                if (data.isAttractable && magnetIsActive && IsInMagnetArea(transform.position, playerTransform))
                {
                    Debug.Log($"{gameObject.name}: Started pulling! (Locked On)", gameObject);
                    isBeingPulled = true; // LOCK ON - This won't become false again until collected/disabled
                                          // Start moving immediately
                    Vector3 direction = (playerHead.position - transform.position).normalized;
                    transform.position += direction * data.magnetPullSpeed * Time.deltaTime;
                }
                // (If not pulled, and not starting pull, do nothing related to magnet)
            } 
            if (!isBeingPulled && spawner != null && spawner.player != null)
            {
                if (transform.position.y < spawner.player.position.y - despawnOffset)
                {
                    ReturnToPool();
                }
            }
        }

        void OnTriggerEnter2D(Collider2D other)
        {
            Debug.Log($"Trigger Enter: {gameObject.name} collided with {other.name}", gameObject); // LOG 1
            if (col == null || !col.enabled)
            {
                Debug.LogWarning("Trigger ignored: Collider disabled or null.", gameObject); // LOG 2
                return;
            }

            if (other.CompareTag("Player") || other.GetComponent<PlayerMovement>() != null)
            {
                Debug.Log("Collision confirmed with Player.", gameObject); // LOG 3
                if (GameManager.Instance == null) { Debug.LogError("GameManager.Instance is NULL.", gameObject); return; } // LOG 4
                if (data == null) { Debug.LogError("ItemData (data) is NULL.", gameObject); return; } // LOG 5
                Debug.Log($"Processing Item: {data.itemName}, Category: {data.category}, PowerupEffect: {data.powerupEffect}, Value: {data.value}, ScoreText: {data.scoreText}", gameObject); // LOG 6

                switch (data.category)
                {
                    case ItemCategory.Collectible:
                    case ItemCategory.Ammo:
                        Debug.Log($"Entering Collectible/Ammo case...", gameObject); // LOG 7a
                        HandleCollection();
                        StartCollectSequence();
                        break; // <-- BREAK ADDED

                    case ItemCategory.PowerUp:
                        Debug.Log($"Entering PowerUp case...", gameObject); // LOG 7b
                        HandlePowerupActivation(other);
                        HandleCollection(); // Apply potential score/value
                        StartCollectSequence();
                        break; // <-- BREAK ADDED

                    case ItemCategory.Hazard:
                        Debug.Log($"Entering Hazard case...", gameObject); // LOG 7c
                        HandleHazardCollision();
                        ReturnToPool();
                        break; // <-- BREAK ADDED

                    default:
                        Debug.LogWarning($"Unhandled ItemCategory: {data.category}", gameObject); // LOG 7d
                        break;
                }
            }
            else
            {
                Debug.Log($"Collision ignored: Collided with non-player object '{other.name}'.", gameObject); // LOG 8
            }
        }

        private void HandleCollection()
        {
            Debug.Log("Executing HandleCollection...", gameObject);
            if (PlayerStats.Instance != null)
            {
                if (data.category == ItemCategory.Collectible)
                {
                    PlayerStats.Instance.AddToMeter(data.value);
                    Debug.Log($"Added {data.value} to meter.", gameObject);
                }
                else if (data.category == ItemCategory.Ammo)
                {
                    PlayerStats.Instance.AddAmmo((int)data.value);
                    Debug.Log($"Added {(int)data.value} ammo.", gameObject); 
                }

                if (int.TryParse(data.scoreText, out int scoreValue) && scoreValue != 0)
                {
                    PlayerStats.Instance.AddScore(scoreValue);
                    Debug.Log($"Added {scoreValue} score.", gameObject);
                }
                // ... (rest of score parsing logs 10d, 10e) ...
                else if (scoreValue == 0) Debug.Log("Score value is 0, not adding score.", gameObject);
                else Debug.LogWarning($"Could not parse scoreText '{data.scoreText}' into an integer.", gameObject);
            }
            else Debug.LogError("HandleCollection failed: PlayerStats.Instance is NULL!", gameObject);


            if (data.collectSound != null && AudioManager.Instance != null)
            {
                Debug.Log("Playing collect sound.", gameObject);
                AudioManager.Instance.PlayClip(data.collectSound, Random.Range(0.92f, 1.0f));
            }
            else if (AudioManager.Instance == null) Debug.LogWarning("AudioManager.Instance is NULL, cannot play sound.", gameObject); // LOG 12b


            if (collectParticle != null)
            {
                Debug.Log("Playing collect particle.", gameObject); // LOG 13a
                collectParticle.gameObject.SetActive(true);
                var main = collectParticle.main; main.startDelay = 0f;
                collectParticle.Simulate(0f, true, true); collectParticle.Play(true);
            }
            else Debug.LogWarning("Collect Particle reference is missing.", gameObject); // LOG 13b

        }

        private void HandlePowerupActivation(Collider2D playerCollider)
        {
            Debug.Log("Executing HandlePowerupActivation...", gameObject); // LOG 14
            if (PowerupManager.Instance != null && data.powerupEffect != PowerupType.None)
            {
                Debug.Log($"Attempting to activate powerup: {data.powerupEffect}", gameObject); // LOG 15
                if (data.powerupEffect == PowerupType.WeaponUpgrade)
                {
                    Debug.LogWarning("WeaponUpgrade powerup collected but 'weaponToEquip' needs setup in ItemData!", data);
                }
                else
                {
                    PowerupManager.Instance.ActivatePowerup(data.powerupEffect, data.duration);
                }
            }
            else if (PowerupManager.Instance == null) Debug.LogError("HandlePowerupActivation failed: PowerupManager.Instance is NULL!", gameObject); // LOG 16
            else if (data.powerupEffect == PowerupType.None) Debug.Log("Powerup effect is 'None', skipping activation.", gameObject); // LOG 17

        }

        private void HandleHazardCollision()
        {
            Debug.Log("Executing HandleHazardCollision...", gameObject); // LOG 18
            Debug.Log("Hazard Hit!");
            if (PlayerStats.Instance != null)
            {
                PlayerStats.Instance.AddToMeter(data.value);
                Debug.Log($"Applied hazard value {data.value} to meter.", gameObject); // LOG 19
            }
            else Debug.LogError("HandleHazardCollision failed: PlayerStats.Instance is NULL!", gameObject); // LOG 20

        }

        private void StartCollectSequence()
        {
            Debug.Log("Executing StartCollectSequence...", gameObject); // LOG 21
            if (col != null) col.enabled = false;

            if (collectEffect != null)
            {
                collectEffect.playerHead = playerHead;
                StartCoroutine(CollectAndReturnToPool());
            }
            else
            {
                Debug.Log("No collect effect found, returning to pool immediately.", gameObject); // LOG 22
                ReturnToPool();
            }
        }

        private IEnumerator CollectAndReturnToPool()
        {
            Debug.Log("Executing CollectAndReturnToPool coroutine...", gameObject); // LOG 23
            if (collectEffect != null)
            {
                Debug.Log("Playing collect animation...", gameObject); // LOG 24
                collectEffect.PlayCollectAnimation(data.scoreText, data.itemColor);
                yield return new WaitForSeconds(collectEffect.pullDuration);
            }
            Debug.Log("Coroutine finished, returning to pool.", gameObject); // LOG 25
            ReturnToPool();
        }

        public void ReturnToPool()
        {
            if (!gameObject.activeSelf) return; // Prevent double calls

            Debug.Log("Executing ReturnToPool...", gameObject); // LOG 26
            if (spawner != null && data != null)
            {
                spawner.OnItemDespawned(this.gameObject, data);
            }
            transform.localScale = originalScale;
            if (col != null) col.enabled = false;
            if (graphicsRenderer != null) graphicsRenderer.enabled = true;
            if (collectParticle != null)
            {
                collectParticle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                collectParticle.gameObject.SetActive(false);
            }
            isBeingPulled = false;
            gameObject.SetActive(false);
            Debug.Log("Item returned to pool and deactivated.", gameObject); // LOG 27
        }
         
        bool IsInMagnetArea(Vector3 itemPos, Transform player)
        {
            Vector3 toItem = itemPos - player.position;

            // Project the item position onto the player's local axes
            float forwardDist = Vector3.Dot(toItem, player.up);      // along the player's "forward" (up in 2D)
            float sidewaysDist = Vector3.Dot(toItem, player.right);  // perpendicular

            // In front of player only
            if (forwardDist < 0) return false;
            if (forwardDist > data.magnetRange) return false;

            // Triangle width scales with distance
            float halfWidthAtY = (forwardDist / data.magnetRange) * (data.magnetBaseWidth / 2f);
            if (Mathf.Abs(sidewaysDist) > halfWidthAtY) return false;

            return true;
        }


        // Replace your OnDrawGizmosSelected function with this one
        void OnDrawGizmosSelected()
        {
            if (playerTransform == null) return;

            Gizmos.color = Color.cyan;

            Vector3 tip = playerTransform.position;
            Vector3 forward = playerTransform.up;   // Player’s facing direction
            Vector3 right = playerTransform.right;  // Perpendicular

            int steps = 10; // number of segments to draw triangle edges
            for (int i = 1; i <= steps; i++)
            {
                float dist = (i / (float)steps) * data.magnetRange;
                float halfWidth = (dist / data.magnetRange) * (data.magnetBaseWidth / 2f);

                Vector3 baseCenter = tip + forward * dist;
                Vector3 left = baseCenter - right * halfWidth;
                Vector3 rightPt = baseCenter + right * halfWidth;

                // Draw edges
                Gizmos.DrawLine(left, rightPt);
                Gizmos.DrawLine(tip, left);
                Gizmos.DrawLine(tip, rightPt);
            }
        }


    }
}