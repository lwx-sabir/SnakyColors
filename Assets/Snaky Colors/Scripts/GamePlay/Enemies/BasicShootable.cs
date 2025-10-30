using DG.Tweening;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SnakyColors
{
    public class BasicShootable : Enemy // Inherits from the base Enemy class
    {
        [Header("Burst Effect")]
        [SerializeField] protected SpriteRenderer mainSprite;
        [SerializeField] protected Transform[] pieces;
        [SerializeField] protected float burstDuration = 0.7f;
        [SerializeField] protected float burstForce = 1.5f;

        private List<Vector3> originalLocalPositions;
        private List<Quaternion> originalLocalRotations;
        private List<Vector3> originalLocalScales;
        private bool hasStoredPiecePositions = false;

        protected override void Awake()
        {
            if (mainSprite == null) mainSprite = GetComponent<SpriteRenderer>();
            StorePieceTransforms();
            base.Awake(); 
        }

        protected override void OnEnable()
        {
            if (mainSprite != null) mainSprite.enabled = true; // Ensure sprite is visible
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
                StorePieceTransforms();
                OnEnable();
            }
            base.OnEnable();
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

        protected override void Move()
        {
            base.Move();
        }

        protected override IEnumerator DeathSequenceCoroutine()
        {
            // 1. Anticipation (Copied from base, or just call base.DeathSequenceCoroutine()?)
            // If you call base, you need to structure it differently. Let's keep it explicit here.
            if (mainSprite != null)
            {
                transform.DOPunchScale(Vector3.one * 0.1f, 0.2f, 5, 1);
                yield return new WaitForSeconds(0.2f); // Wait for punch
            }

            // 2. Play Base Death Effect & Hide Main Sprite
            if (deathEffectPrefab != null) Instantiate(deathEffectPrefab, transform.position, Quaternion.identity); // TODO: Pool effect
            if (mainSprite != null) mainSprite.enabled = false;

            // 3. Trigger THIS class's Burst
            Burst(); // Call the burst specific to BasicShootable

            // 4. Finally, Return to Pool via GeneratedItem
            if (generatedItem != null)
            {
                // Wait a very short moment for burst tweens to visually start
                yield return new WaitForSeconds(burstDuration * 0.1f);
                generatedItem.ReturnToPool();
            }
            else
            {
                gameObject.SetActive(false); // Fallback
            }
        }

        protected override void Die()
        { 
            base.Die();

            Sequence s = DOTween.Sequence();
            if (mainSprite != null)
            {
                s.Append(mainSprite.transform.DOPunchScale(Vector3.one * 0.1f, 0.05f, 5, 1));
            }

            s.OnComplete(() =>
            { 
                if (deathEffectPrefab != null)
                {
                    Instantiate(deathEffectPrefab, transform.position, Quaternion.identity);
                }
                 
                if (mainSprite != null)
                {
                    mainSprite.enabled = false;
                } 
                Burst();
                 
                if (generatedItem != null)
                {
                    generatedItem.ReturnToPool();
                }
            });
        } 
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