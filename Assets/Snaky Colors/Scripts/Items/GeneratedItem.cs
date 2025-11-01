using UnityEngine;
using System.Collections;
// using UnityEditor; // This should not be in a runtime script.

namespace SnakyColors
{
    public class GeneratedItem : MonoBehaviour
    {
        [HideInInspector] public ItemData data;

        [Header("References")]
        public SpriteRenderer graphicsRenderer;
        [SerializeField] private SpriteRenderer shadowRenderer; // Added shadow reference
        [Header("Particles")]
        [SerializeField] private ParticleSystem collectParticle;

        // References set at runtime
        public IItemSpawner spawner { get; set; }
        private FruitCollectEffect collectEffect;
        private Transform playerHead;
        private Collider2D col;
        private Transform playerTransform;

        [Header("Settings")]
        public float despawnOffset = 10f;

        private Vector3 originalScale;
        private bool isBeingPulled = false;
        [HideInInspector] public bool isDropped = false;

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
            if (shadowRenderer != null) // Reset shadow
            {
                shadowRenderer.enabled = true;
            }
            if (col != null) col.enabled = true;
            if (collectEffect != null) collectEffect.enabled = true;
            isBeingPulled = false;
             
            playerHead = null;
            playerTransform = null;
            spawner = null;
            data = null;

            if (collectParticle != null)
            {
                collectParticle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                collectParticle.gameObject.SetActive(false);
            }
        }

        public void SetData(ItemData newItemData, Transform player)
        {
            data = newItemData;
            playerHead = player;
            playerTransform = player;
        } 

        void Update()
        {  
            if (playerTransform == null)
            {
                GameObject playerObj = GameObject.FindWithTag("Player");
                if (playerObj != null)
                {
                    playerTransform = playerObj.transform;
                    playerHead = playerObj.transform; // Assume head is main transform
                }
                else
                {
                    return; // No player found, can't update
                }
            }
            if (playerTransform == null || data == null) return;
             
            if (isBeingPulled) // 1. If locked on, just move
            {
                Vector3 direction = (playerHead.position - transform.position).normalized;
                transform.position += direction * data.magnetPullSpeed * Time.deltaTime;
            }
            else // 2. If not locked on, check if magnet is active and in range
            {
                bool magnetIsActive = PowerupManager.Instance != null && PowerupManager.Instance.IsMagnetActive;
                if (data.isAttractable && magnetIsActive && IsInMagnetArea(transform.position, playerTransform))
                {
                    isBeingPulled = true; // Lock on 
                    Vector3 direction = (playerHead.position - transform.position).normalized;
                    transform.position += direction * data.magnetPullSpeed * Time.deltaTime;
                }
            } 

            if (playerTransform != null)
            {
                if (transform.position.y < playerTransform.position.y - despawnOffset)
                {  
                    ReturnToPool();
                }
            }
        }

        void OnTriggerEnter2D(Collider2D other)
        {
            if (col == null || !col.enabled) return; // Already collected

            if (other.CompareTag("Player") || other.GetComponent<PlayerMovement>() != null)
            {
                if (GameManager.Instance == null || data == null) return;

                switch (data.category)
                {
                    case ItemCategory.Collectible:
                        HandleCollection();
                        StartCollectSequence(); // Plays effect + returns via coroutine
                        break;

                    case ItemCategory.PowerUp:
                        HandlePowerupActivation(other);
                        HandleCollection(); // Apply potential score
                        StartCollectSequence(); // Plays effect + returns via coroutine
                        break;

                    case ItemCategory.Hazard:
                        HandleHazardCollision();
                        // Return is handled by HazardReturnRoutine
                        break;

                    default:
                        Debug.LogWarning($"Unhandled ItemCategory: {data.category}", gameObject);
                        break;
                }
            }
        }

        private void HandleCollection()
        {
            if (PlayerStats.Instance != null)
            {
                if (data.category == ItemCategory.Collectible)
                {
                    if (data.collectibleType == CollectibleType.Basic)
                    {
                        PlayerStats.Instance.AddToMeter(data.value);
                    }
                    else if (data.collectibleType == CollectibleType.DashCharge)
                    {
                        PlayerStats.Instance.AddDashCharge(data.value);
                    }
                    else if (data.collectibleType == CollectibleType.Health)
                    {
                        PlayerStats.Instance.Heal(data.value);
                    }
                    else if (data.collectibleType == CollectibleType.Ammo) // This logic might be better under ItemCategory.Ammo
                    {
                        PlayerStats.Instance.AddAmmo((int)data.value);
                    }
                }

                if (int.TryParse(data.scoreText, out int scoreValue) && scoreValue != 0)
                {
                    PlayerStats.Instance.AddScore(scoreValue);
                }
            }
            else Debug.LogError("HandleCollection failed: PlayerStats.Instance is NULL!", gameObject);

            if (data.collectSound != null && AudioManager.Instance != null)
            {
                AudioManager.Instance.PlayClip(data.collectSound, Random.Range(0.92f, 1.0f));
            }

            // Play particle only for non-hazard collection
            if (data.category != ItemCategory.Hazard && collectParticle != null)
            {
                PlayCollectParticle(); // Use your robust particle play method
            }
        }

        private void HandlePowerupActivation(Collider2D playerCollider)
        {
            if (PowerupManager.Instance != null && data.powerupEffect != PowerupType.None)
            {
                if (data.powerupEffect == PowerupType.WeaponUpgrade)
                {
                    Debug.LogWarning("WeaponUpgrade powerup collected but 'weaponToEquip' needs setup in ItemData!", data); 
                }
                else
                {
                    PowerupManager.Instance.ActivatePowerup(data.powerupEffect, data.duration);
                }
            }
            else if (PowerupManager.Instance == null) Debug.LogError("HandlePowerupActivation failed: PowerupManager.Instance is NULL!", gameObject);
        }

        private void HandleHazardCollision()
        {
            Debug.Log("Hazard Hit!");

            // Hide graphics immediately
            if (graphicsRenderer != null) graphicsRenderer.enabled = false;
            if (shadowRenderer != null) shadowRenderer.enabled = false;
            if (col != null) col.enabled = false;
             
            if (PlayerStats.Instance != null && data.value != 0)
            {
                PlayerStats.Instance.AddToMeter(data.value); 
            }

            // Play hazard-specific sound
            if (data.collectSound != null && AudioManager.Instance != null)
            {
                AudioManager.Instance.PlayClip(data.collectSound, Random.Range(0.88f, 1f));
            }

            PlayCollectParticle();

            float particleDuration = 1f; // Default
            if (collectParticle != null)
            {
                particleDuration = collectParticle.main.duration + collectParticle.main.startLifetime.constantMax;
            }
            StartCoroutine(ReturnToPoolAfterDelay(particleDuration * 1.1f)); // Wait 10% longer
        }


        private void StartCollectSequence()
        {
            if (col != null) col.enabled = false;

            if (collectEffect != null)
            {
                collectEffect.playerHead = playerHead;
                StartCoroutine(CollectAndReturnToPool());
            }
            else
            {
                ReturnToPool(); // Return immediately if no effect
            }
        }

        private IEnumerator CollectAndReturnToPool()
        {
            if (collectEffect != null)
            {
                float animationDuration = collectEffect.PlayCollectAnimation(
                    data.scoreText,
                    data.itemColor,
                    data.collectibleType,
                    data.icon
                );

                yield return new WaitForSeconds(animationDuration);
            }
            ReturnToPool();
        }

        private IEnumerator ReturnToPoolAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);
            ReturnToPool();
        }

        private void PlayCollectParticle()
        {
            if (collectParticle == null) return;

            collectParticle.gameObject.SetActive(true);

            var main = collectParticle.main;
            // main.stopAction = ParticleSystemStopAction.Callback; // Callback is complex, disable/delay is safer

            collectParticle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            collectParticle.Clear(true);
            collectParticle.Simulate(0f, true, true);
            collectParticle.Play(true);
        }

        public void ReturnToPool()
        {
            if (!gameObject.activeSelf) return; // Prevent double calls

            if (spawner != null && data != null)
            {
                spawner.OnItemDespawned(this.gameObject, data);
            }
            else if (isDropped)
            { 
                ResetItemState();
            }
            else
            { 
                Debug.LogWarning($"{gameObject.name} returned to pool but has no spawner or drop flag.");
                ResetItemState();
            }

            gameObject.SetActive(false);
        }

        private void ResetItemState()
        {
            transform.localScale = originalScale;
            if (graphicsRenderer != null) graphicsRenderer.enabled = true;
            if (shadowRenderer != null) shadowRenderer.enabled = true;
            if (col != null) col.enabled = true;
            if (collectParticle != null)
            {
                collectParticle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                collectParticle.gameObject.SetActive(false);
            }

            // Reset other flags
            isBeingPulled = false;
            isDropped = false;
            data = null;
            spawner = null;
        }


        bool IsInMagnetArea(Vector3 itemPos, Transform player)
        {
            if (player == null || data == null) return false;

            Vector3 toItem = itemPos - player.position;

            float forwardDist = Vector3.Dot(toItem, player.up);
            if (forwardDist < 0) return false; // Behind
            if (forwardDist > data.magnetRange) return false; // Too far

            float sidewaysDist = Vector3.Dot(toItem, player.right);
            float halfWidthAtY = (forwardDist / data.magnetRange) * (data.magnetBaseWidth / 2f);

            if (Mathf.Abs(sidewaysDist) > halfWidthAtY) return false; // Outside triangle

            return true;
        }

        void OnDrawGizmosSelected()
        {
            if (playerTransform == null) return;

            Gizmos.color = Color.cyan;

            Vector3 tip = playerTransform.position;
            Vector3 forward = playerTransform.up;   // Player’s facing direction
            Vector3 right = playerTransform.right;  // Perpendicular

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