using UnityEngine;
using DG.Tweening;
using System.Collections.Generic;
namespace SnakyColors
{
    [RequireComponent(typeof(Collider2D))]
    [RequireComponent(typeof(GeneratedItem))]
    public abstract class Enemy : MonoBehaviour
    {
        // ... (Your other fields: maxHealth, moveSpeed, etc.) ...
        [Header("Base Stats")]
        [SerializeField] protected float maxHealth = 50f;
        [SerializeField] protected float currentHealth;
        [SerializeField] protected int scoreValue = 10;

        [Header("Movement (Optional Base)")]
        [SerializeField] protected float moveSpeed = 2f;

        [Header("Feedback (Optional Base)")]
        [SerializeField] protected GameObject hitEffectPrefab;
        [SerializeField] protected GameObject deathEffectPrefab;
        [SerializeField] protected AudioClip hitSound;
        [SerializeField] protected AudioClip deathSound;

        [Header("Core Components")]
        [SerializeField] protected SpriteRenderer mainSprite;
        [SerializeField] protected Collider2D mainCollider;

        [Header("Burst Effect")]
        [SerializeField] protected Transform[] pieces;
        [SerializeField] protected float burstDuration = 0.7f;
        [SerializeField] protected float burstForce = 1.5f;

        protected GeneratedItem generatedItem;
         
        private List<Vector3> originalLocalPositions;
        private List<Quaternion> originalLocalRotations;
        private List<Vector3> originalLocalScales;
        private bool hasStoredPiecePositions = false; 

        protected virtual void Awake()
        {
            mainCollider = GetComponent<Collider2D>();
            mainSprite = GetComponent<SpriteRenderer>();
            generatedItem = GetComponent<GeneratedItem>();

            if (mainCollider != null && !mainCollider.isTrigger)
            {
                Debug.LogWarning($"Collider on {gameObject.name} was not set to Trigger. Forcing it.", this);
                mainCollider.isTrigger = true;
            } 
            StorePieceTransforms();
        }

        /// <summary>
        /// Saves the "prefab" state of all pieces so we can reset them.
        /// </summary>
        private void StorePieceTransforms()
        {
            // Only run this once
            if (hasStoredPiecePositions) return;

            originalLocalPositions = new List<Vector3>();
            originalLocalRotations = new List<Quaternion>();
            originalLocalScales = new List<Vector3>();

            if (pieces != null && pieces.Length > 0)
            {
                foreach (var piece in pieces)
                {
                    if (piece != null)
                    {
                        originalLocalPositions.Add(piece.localPosition);
                        originalLocalRotations.Add(piece.localRotation);
                        originalLocalScales.Add(piece.localScale);
                    }
                }
                hasStoredPiecePositions = true;
            }
        }

        protected virtual void OnEnable()
        {
            currentHealth = maxHealth;
            if (mainSprite != null) mainSprite.enabled = true;
            if (mainCollider != null) mainCollider.enabled = true;

            // --- MODIFIED: Reset all pieces ---
            // This ensures they are back in their original prefab positions and hidden.
            if (pieces != null && hasStoredPiecePositions)
            {
                for (int i = 0; i < pieces.Length; i++)
                {
                    if (pieces[i] != null)
                    {
                        pieces[i].SetParent(this.transform);
                        pieces[i].localPosition = originalLocalPositions[i];
                        pieces[i].localRotation = originalLocalRotations[i];
                        pieces[i].localScale = originalLocalScales[i];
                        pieces[i].gameObject.SetActive(false);
                    }
                }
            }
            else if (pieces != null && !hasStoredPiecePositions)
            {
                // Fallback in case Awake hasn't run
                StorePieceTransforms();
                OnEnable(); // Re-run OnEnable
            }
        }

        // ... (Update and Move methods are unchanged) ...
        protected virtual void Update()
        {
            Move();
        }
        protected virtual void Move()
        {
            transform.Translate(Vector3.down * moveSpeed * Time.deltaTime, Space.World);
        }

        public virtual void TakeDamage(float damageAmount)
        {
            // ... (TakeDamage logic is unchanged) ...
            currentHealth -= damageAmount;

            if (hitEffectPrefab != null) Instantiate(hitEffectPrefab, transform.position, Quaternion.identity);
            if (hitSound != null && AudioManager.Instance != null)
            {
                AudioManager.Instance.PlayClip(hitSound, Random.Range(0.9f, 1.1f));
            }

            if (currentHealth <= 0)
            {
                Die();
            }
        }

        /// <summary>
        /// Handles the enemy's death logic. Now with an anticipation "punch".
        /// </summary>
        protected virtual void Die()
        {
            Debug.Log($"{gameObject.name} died.", this);
              
            if (mainCollider != null) mainCollider.enabled = false;
             
            if (PlayerStats.Instance != null && scoreValue > 0)
            {
                PlayerStats.Instance.AddScore(scoreValue);
            }
            if (deathSound != null && AudioManager.Instance != null)
            {
                AudioManager.Instance.PlayClip(deathSound);
            } 
            Sequence s = DOTween.Sequence();
             
            if (mainSprite != null)
            {
                s.Append(mainSprite.transform.DOPunchScale(Vector3.one * 0.1f, 0.2f, 5, 1));
            }
             
            s.OnComplete(() =>
            {
                // Spawn particle effect
                if (deathEffectPrefab != null)
                {
                    Instantiate(deathEffectPrefab, transform.position, Quaternion.identity);
                }

                // Hide main sprite
                if (mainSprite != null)
                {
                    mainSprite.enabled = false;
                }

                // Call the burst!
                Burst();

                // Return to pool
                if (generatedItem != null)
                {
                    generatedItem.ReturnToPool();
                }
            });
        }

        /// <summary>
        /// This is your burst logic, now with more juice!
        /// </summary>
        protected virtual void Burst()
        {
            if (pieces == null || pieces.Length == 0 || !hasStoredPiecePositions)
                return;

            for (int i = 0; i < pieces.Length; i++)
            {
                var piece = pieces[i];
                if (piece == null) continue;

                piece.localPosition = originalLocalPositions[i];
                piece.localRotation = originalLocalRotations[i];
                piece.localScale = originalLocalScales[i];
                piece.gameObject.SetActive(true);

                Vector3 worldPos = piece.position;
                Quaternion worldRot = piece.rotation;
                Vector3 worldScale = piece.lossyScale;

                piece.SetParent(null, true);
                piece.localScale = worldScale;

                float pieceDuration = burstDuration * Random.Range(0.8f, 1.2f);
                float pieceDelay = Random.Range(0, 0.12f);

                // --- Direction with more randomness ---
                Vector3 dir = Quaternion.Euler(
                    Random.Range(-60f, 60f),
                    Random.Range(-60f, 60f),
                    Random.Range(-60f, 60f)
                ) * Vector3.up;
                dir.Normalize();

                SpriteRenderer pieceRenderer = piece.GetComponent<SpriteRenderer>();
                if (pieceRenderer != null)
                {
                    // Reset alpha
                    pieceRenderer.color = new Color(1f, 1f, 1f, 1f);
                    // Fade out with slight color shift
                    Color targetColor = new Color(
                        1f,
                        Random.Range(0.8f, 1f),
                        Random.Range(0.8f, 1f),
                        0f
                    );
                    pieceRenderer.DOColor(targetColor, pieceDuration * 0.7f)
                        .SetDelay(pieceDelay + pieceDuration * 0.3f)
                        .SetEase(Ease.InQuad);
                }

                // --- Movement with slight wiggle ---
                Vector3 midPoint = worldPos + dir * burstForce * 0.6f + Random.insideUnitSphere * 0.2f;
                piece.DOPath(new Vector3[] { worldPos, midPoint, worldPos + dir * burstForce }, pieceDuration, PathType.CatmullRom)
                    .SetDelay(pieceDelay)
                    .SetEase(Ease.OutBack, 1.2f);

                // --- Rotation on random axis ---
                piece.DORotate(
                    new Vector3(
                        Random.Range(-360f, 360f),
                        Random.Range(-360f, 360f),
                        Random.Range(-360f, 360f)
                    ),
                    pieceDuration,
                    RotateMode.FastBeyond360
                ).SetDelay(pieceDelay);

                // --- Scale punch + shrink ---
                piece.DOScale(worldScale * Random.Range(1.05f, 1.15f), pieceDuration * 0.2f)
                    .SetDelay(pieceDelay)
                    .SetEase(Ease.OutBack)
                    .OnComplete(() =>
                    {
                        piece.DOScale(0, pieceDuration * 0.8f)
                             .SetEase(Ease.InBack)
                             .OnComplete(() =>
                             {
                                 if (piece != null && this != null)
                                 {
                                     piece.gameObject.SetActive(false);
                                     piece.SetParent(this.transform);
                                     piece.localPosition = originalLocalPositions[i];
                                     piece.localRotation = originalLocalRotations[i];
                                     piece.localScale = originalLocalScales[i];
                                 }
                             });
                    });
            }
        }


        /// <summary>
        /// This is your burst logic, now 100% pool-safe.
        /// </summary> 

        //protected virtual void Burst()
        //{
        //    if (pieces == null || pieces.Length == 0 || !hasStoredPiecePositions)
        //        return;

        //    for (int i = 0; i < pieces.Length; i++)
        //    {
        //        var piece = pieces[i];
        //        if (piece == null) continue;

        //        // Restore original local transform first (under parent)
        //        piece.localPosition = originalLocalPositions[i];
        //        piece.localRotation = originalLocalRotations[i];
        //        piece.localScale = originalLocalScales[i];
        //        piece.gameObject.SetActive(true);

        //        // Cache its true world-space transform before detaching
        //        Vector3 worldPos = piece.position;
        //        Quaternion worldRot = piece.rotation;
        //        Vector3 worldScale = piece.lossyScale;

        //        // Detach safely, keeping world transform
        //        piece.SetParent(null, true);
        //        piece.localScale = worldScale; // preserve exact visual size

        //        // Random outward direction — controlled upward bias
        //        Vector3 dir = Quaternion.Euler(0, 0, Random.Range(-60f, 60f)) * Vector3.up;
        //        dir.Normalize();

        //        // Apply burst movement and rotation
        //        piece.DOMove(worldPos + dir * burstForce, burstDuration)
        //            .SetEase(Ease.OutCubic);

        //        piece.DORotate(
        //            new Vector3(0, 0, Random.Range(-360, 360)),
        //            burstDuration,
        //            RotateMode.FastBeyond360
        //        );

        //        int pieceIndex = i;
        //        piece.DOScale(0, burstDuration)
        //            .SetEase(Ease.InBack)
        //            .SetDelay.SetDelay(randomDelay + randomDuration * 0.6f))
        //            .OnComplete(() =>
        //            {
        //                if (piece != null && this != null)
        //                {
        //                    // Reset for reuse
        //                    piece.gameObject.SetActive(false);
        //                    piece.SetParent(this.transform);

        //                    piece.localPosition = originalLocalPositions[pieceIndex];
        //                    piece.localRotation = originalLocalRotations[pieceIndex];
        //                    piece.localScale = originalLocalScales[pieceIndex];
        //                }
        //            });
        //    }
        //}


        protected virtual void OnTriggerEnter2D(Collider2D other)
        { 
            Projectile projectile = other.GetComponent<Projectile>();
            if (projectile != null)
            {
                // Logic for projectile collision
            }
        }
    }
}