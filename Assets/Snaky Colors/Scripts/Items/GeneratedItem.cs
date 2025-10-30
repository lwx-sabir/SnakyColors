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
                    isBeingPulled = true;  
                    Vector3 direction = (playerHead.position - transform.position).normalized;
                    transform.position += direction * data.magnetPullSpeed * Time.deltaTime;
                } 
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
            if (col == null || !col.enabled)
            { 
                return;
            }

            if (other.CompareTag("Player") || other.GetComponent<PlayerMovement>() != null)
            { 
                if (GameManager.Instance == null) {return; } 
                if (data == null) { return; }
                
                switch (data.category)
                {
                    case ItemCategory.Collectible:
                    case ItemCategory.Ammo:
                        Debug.Log($"Entering Collectible/Ammo case...", gameObject);
                        HandleCollection();
                        StartCollectSequence();
                        break; 

                    case ItemCategory.PowerUp:
                        Debug.Log($"Entering PowerUp case...", gameObject); 
                        HandlePowerupActivation(other);
                        HandleCollection(); 
                        StartCollectSequence();
                        break;

                    case ItemCategory.Hazard:
                        Debug.Log($"Entering Hazard case...", gameObject);
                        HandleHazardCollision();  
                        break; 

                    default:
                        Debug.LogWarning($"Unhandled ItemCategory: {data.category}", gameObject); 
                        break;
                }
            }
            else
            {
                Debug.Log($"Collision ignored: Collided with non-player object '{other.name}'.", gameObject);
            }
        }

        private void HandleCollection()
        {
            if (PlayerStats.Instance != null)
            {
                if (data.category == ItemCategory.Collectible)
                {
                    PlayerStats.Instance.AddToMeter(data.value);
                }
                else if (data.category == ItemCategory.Ammo)
                {
                    PlayerStats.Instance.AddAmmo((int)data.value);
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
            else if (AudioManager.Instance == null) Debug.LogWarning("AudioManager.Instance is NULL, cannot play sound.", gameObject);


            if (collectParticle != null)
            {
                PlayCollectParticle();
            }
            else Debug.LogWarning("Collect Particle reference is missing.", gameObject); 
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
                ReturnToPool();
            }
        }

        private IEnumerator CollectAndReturnToPool()
        {
            if (collectEffect != null)
            {
                collectEffect.PlayCollectAnimation(data.scoreText, data.itemColor);
                yield return new WaitForSeconds(collectEffect.pullDuration);
            }
            ReturnToPool();
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
            else if (data.powerupEffect == PowerupType.None) Debug.Log("Powerup effect is 'None', skipping activation.", gameObject);

        }

        private void HandleHazardCollision()
        {
            Debug.Log("Hazard Hit!");

            if (graphicsRenderer != null) graphicsRenderer.enabled = false;
            if (col != null) col.enabled = false;

            PlayCollectParticle();
             
            StartCoroutine(ReturnToPoolAfterDelay(collectParticle.main.duration));

            if (data.collectSound != null && AudioManager.Instance != null)
            {
                AudioManager.Instance.PlayClip(data.collectSound, Random.Range(0.88f, 1f));
            }
        }

        private void PlayCollectParticle()
        {
            if (collectParticle == null) return;

            collectParticle.gameObject.SetActive(true);

            var main = collectParticle.main;
            main.stopAction = ParticleSystemStopAction.Callback;

            collectParticle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            collectParticle.Clear(true);
            collectParticle.Simulate(0f, true, true); 
            collectParticle.Play(true);
        } 

        public void ReturnToPool()
        {
            if (!gameObject.activeSelf) return; 
             
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

        private IEnumerator ReturnToPoolAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);
            ReturnToPool();
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