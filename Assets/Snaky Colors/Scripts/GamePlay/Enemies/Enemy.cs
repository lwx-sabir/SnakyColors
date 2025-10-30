using UnityEngine;
using DG.Tweening; // If using DOTween for effects like hit flash
using System.Collections;
using System.Collections.Generic;

namespace SnakyColors
{
    [RequireComponent(typeof(Collider2D))]
    [RequireComponent(typeof(GeneratedItem))]
    public abstract class Enemy : MonoBehaviour
    {
        [Header("Base Stats")]
        [SerializeField] protected float maxHealth = 50f;
        [SerializeField] protected float currentHealth;
        [SerializeField] protected int scoreValue = 10;
        // Optional: Add EnemyType enum if needed for specific logic elsewhere
        // [SerializeField] protected EnemyType enemyType = EnemyType.Basic;

        [Header("Movement (Optional Base)")]
        [SerializeField] protected float moveSpeed = 2f;

        [Header("Feedback (Optional Base)")]
        [SerializeField] protected GameObject hitEffectPrefab; // Assign particle prefab
        [SerializeField] protected GameObject deathEffectPrefab; // Assign particle prefab
        [SerializeField] protected AudioClip hitSound;
        [SerializeField] protected AudioClip deathSound;

        [Header("Item Drops")]
        [SerializeField] protected EnemyDropTable dropTable; // Assign Drop Table asset

        [Header("Core Components")]  
        [SerializeField] protected Collider2D mainCollider;

        protected GeneratedItem generatedItem; 
        protected bool isDead = false; // Flag to prevent multiple deaths

        protected virtual void Awake()
        { 
            mainCollider = GetComponent<Collider2D>(); 
            generatedItem = GetComponent<GeneratedItem>();

            // Ensure collider is a trigger
            if (mainCollider != null && !mainCollider.isTrigger)
            {
                mainCollider.isTrigger = true;
            }
            if (generatedItem == null)
            {
                Debug.LogError($"Enemy {gameObject.name} is missing the required GeneratedItem component!", this);
            }
        }

        protected virtual void OnEnable()
        {
            // Reset state for pooling
            currentHealth = maxHealth;
            isDead = false; // Reset death flag 
            if (mainCollider != null) mainCollider.enabled = true; // Ensure collider is active
        }

        protected virtual void Update()
        {
            // Only move if not dead
            if (!isDead)
            {
                Move();
            }
            // Despawn logic is handled by GeneratedItem.cs
        }

        /// <summary>
        /// Base movement logic (simple downward movement). Override for custom patterns.
        /// </summary>
        protected virtual void Move()
        {
            transform.Translate(Vector3.down * moveSpeed * Time.deltaTime, Space.World);
        }

        /// <summary>
        /// Called by Projectile or other damage sources.
        /// </summary>
        public virtual void TakeDamage(float damageAmount)
        {
            if (isDead || currentHealth <= 0) return; // Already dead or dying

            currentHealth -= damageAmount;

            // Play hit feedback
            if (hitEffectPrefab != null)
            {
                Instantiate(hitEffectPrefab, transform.position, Quaternion.identity); // TODO: Pool hit effects
            }
            if (hitSound != null && AudioManager.Instance != null)
            {
                AudioManager.Instance.PlayClip(hitSound, Random.Range(0.9f, 1.1f));
            }
            // Optional: Add visual hit flash
            // if (mainSprite != null) { mainSprite.transform.DOPunchScale(Vector3.one * 0.1f, 0.15f, 3, 0.5f); }

            // Check for death
            if (currentHealth <= 0)
            {
                Die();
            }
        }

        /// <summary>
        /// Initiates the death sequence. Handles score, sound, drops, and starts visual effects.
        /// Does NOT handle pooling directly - relies on DeathSequenceCoroutine completion.
        /// </summary>
        protected virtual void Die()
        {
            if (isDead) return; // Prevent multiple calls
            isDead = true;

            // Disable collider immediately
            if (mainCollider != null) mainCollider.enabled = false;

            // Add score & play death sound
            if (PlayerStats.Instance != null && scoreValue > 0) PlayerStats.Instance.AddScore(scoreValue);
            if (deathSound != null && AudioManager.Instance != null) AudioManager.Instance.PlayClip(deathSound);

            // Handle item drops
            HandleDrops();

            // Start the visual death sequence (which handles pooling at the end)
            StartCoroutine(DeathSequenceCoroutine());
        }

        /// <summary>
        /// Base visual death sequence. Override in derived classes for custom effects (like Burst).
        /// Handles playing death particle effect and returning the object to the pool via GeneratedItem.
        /// </summary>
        protected virtual IEnumerator DeathSequenceCoroutine()
        {
            // 1. Optional Anticipation (e.g., brief pause or animation)
            // yield return new WaitForSeconds(0.1f);

            // 2. Play Death Effect & Hide Main Sprite
            if (deathEffectPrefab != null) Instantiate(deathEffectPrefab, transform.position, Quaternion.identity); // TODO: Pool effect

            // 3. Wait briefly for effects to play (optional)
            yield return new WaitForSeconds(0.1f); // Adjust as needed

            // 4. Return to Pool via GeneratedItem
            if (generatedItem != null)
            {
                generatedItem.ReturnToPool();
            }
            else
            {
                Debug.LogError($"Cannot ReturnToPool for {gameObject.name}: GeneratedItem ref missing!", this);
                gameObject.SetActive(false); // Fallback if GeneratedItem is missing
            }
        }


        /// <summary>
        /// Determines if an item should drop based on the drop table and spawns it using ItemPooler.
        /// </summary>
        protected virtual void HandleDrops()
        {
            // Ensure necessary components/references exist
            if (dropTable == null || ItemPooler.Instance == null)
            {
                Debug.LogError("HandleDrops failed: DropTable or ItemPooler Instance is missing!");
                return;
            }

            List<ItemData> itemsToDrop = dropTable.GetDrops();
            if (itemsToDrop == null || itemsToDrop.Count == 0) return; // Nothing dropped

            bool isSingleDrop = itemsToDrop.Count == 1;

            foreach (ItemData itemToDrop in itemsToDrop)
            {
                if (itemToDrop?.prefab == null) continue; // Skip invalid item data

                GameObject obj = ItemPooler.Instance.GetPooledObject(itemToDrop);
                if (obj == null) continue; // Skip if pool failed to provide an object

                // --- Initial Setup ---
                obj.transform.position = transform.position; // Start at enemy position
                obj.transform.rotation = Quaternion.Euler(0, 0, Random.Range(-15f, 15f)); // Slight random rotation
                obj.transform.localScale = Vector3.one * 0.1f; // Start small for pop animation

                // --- Set Data on GeneratedItem ---
                if (obj.TryGetComponent<GeneratedItem>(out var genItem))
                {
                    // Get Player Reference (prioritize GameManager, fallback to Tag)
                    Transform playerRef = null;
                    if (GameManager.Instance != null && GameManager.Instance.currentMode is FoodHunterGameMood hunterMode && hunterMode.playerInstance != null)
                    {
                        playerRef = hunterMode.playerInstance.transform;
                    }
                    if (playerRef == null)
                    {
                        GameObject playerObj = GameObject.FindWithTag("Player"); // Fallback lookup
                        if (playerObj != null) playerRef = playerObj.transform;
                    }

                    if (playerRef != null) genItem.SetData(itemToDrop, playerRef);
                    else Debug.LogError($"Could not find Player Transform! Cannot SetData on dropped {itemToDrop.itemName}", this);
                }

                // --- Activate Object ---
                obj.SetActive(true);

                // --- Ensure Collider is Enabled ---
                if (obj.TryGetComponent<Collider2D>(out var col) && !col.enabled)
                    col.enabled = true;

                // --- Juicy Drop Animation ---
                Vector3 targetScale = itemToDrop.prefab.transform.localScale; // Get target scale from prefab
                float duration = isSingleDrop ? 0.4f : Random.Range(0.3f, 0.6f);
                float force = isSingleDrop ? 1.0f : Random.Range(0.8f, 1.5f);
                Vector2 randomDir = Random.insideUnitCircle.normalized;
                Vector3 targetPos = transform.position + (Vector3)randomDir * force; // Scatter position

                // Kill any pre-existing tweens on this pooled object's transform
                DOTween.Kill(obj.transform);

                // Create animation sequence
                Sequence dropSequence = DOTween.Sequence();
                dropSequence.Append(obj.transform.DOScale(targetScale * 1.2f, duration * 0.4f).SetEase(Ease.OutBack)); // Pop scale up
                dropSequence.Join(obj.transform.DOMove(targetPos, duration).SetEase(Ease.OutQuad)); // Scatter movement
                dropSequence.Join(obj.transform.DORotate(new Vector3(0, 0, Random.Range(-180f, 180f)), duration, RotateMode.FastBeyond360).SetEase(Ease.OutQuad)); // Spin
                dropSequence.Append(obj.transform.DOScale(targetScale, duration * 0.6f).SetEase(Ease.OutBounce)); // Settle scale
            }
        }



        /// <summary>
        /// Base trigger detection. Projectile script now calls TakeDamage directly.
        /// </summary>
        protected virtual void OnTriggerEnter2D(Collider2D other)
        {
            // Can add specific interactions here if needed,
            // but projectile hits are handled by the projectile itself calling TakeDamage.
        }
    }
}