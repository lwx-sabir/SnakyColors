using System.Collections;
using UnityEngine;
using TMPro;

namespace SnakyColors
{
    public class FruitCollectEffect : MonoBehaviour
    {
        [Header("Setup")]
        public float pullDuration = 0.15f; // Basic item duration
        [Tooltip("The pop scale used for 'Basic' items.")]
        public float popScale = 1.2f;
        [Tooltip("The 'slightly bigger' pop scale for 'Special' items.")]
        public float specialPopScale = 1.6f;
        public float specialPullDuration = 0.9f; // Special item duration
        public bool useFruitColor = true;

        [Header("Animation Curves")]
        public AnimationCurve ease = AnimationCurve.EaseInOut(0, 0, 1, 1);

        [Header("References")]
        [SerializeField] private TextMeshPro textMesh;
        [SerializeField] private SpriteRenderer iconRenderer; // Child sprite for special items

        private SpriteRenderer sr; // Main fruit sprite
        private Vector3 initialScale;
        private bool isCollected = false;
        [HideInInspector]
        public Transform playerHead;

        private Color originalColor;
        private Color originalTextColor;
        private Color activeTextColor;

        void Awake()
        {
            sr = GetComponent<SpriteRenderer>();
            initialScale = transform.localScale;
            if (sr != null) originalColor = sr.color;

            if (textMesh != null)
                originalTextColor = textMesh.color;

            if (iconRenderer == null)
                Debug.LogWarning("FruitCollectEffect: iconRenderer not assigned. Special items won't show an icon.", this);
        }

        private void OnEnable()
        {
            isCollected = false;

            if (sr != null)
            {
                sr.color = originalColor;
                sr.enabled = true;
            }

            if (textMesh != null)
            {
                textMesh.enabled = false;
                textMesh.color = originalTextColor;
            }

            if (iconRenderer != null)
                iconRenderer.enabled = false;
        }

        /// <summary>
        /// Plays the collection animation.
        /// Returns the duration of the animation.
        /// </summary>
        public float PlayCollectAnimation(string scoreText, Color itemColor, CollectibleType type, Sprite icon)
        {
            if (isCollected) return 0f; // Already collected
            isCollected = true;

            // --- Setup Text ---
            if (textMesh != null)
            {
                textMesh.text = $"+{scoreText}";
                activeTextColor = useFruitColor ? itemColor : originalTextColor;
                textMesh.color = activeTextColor;
                textMesh.enabled = true;
            }

            // --- Special vs Basic ---
            if (type != CollectibleType.Basic && iconRenderer != null && icon != null)
            {
                // --- SPECIAL ITEM ---
                if (sr != null) sr.enabled = false;

                iconRenderer.sprite = icon;
                iconRenderer.color = Color.white;
                iconRenderer.enabled = true;

                // Start juicy special animation
                StartCoroutine(CollectRoutine(specialPopScale, specialPullDuration, true));
                return specialPullDuration;
            }
            else
            {
                // --- BASIC ITEM ---
                if (iconRenderer != null) iconRenderer.enabled = false;
                if (sr != null) sr.enabled = true;

                StartCoroutine(CollectRoutine(popScale, pullDuration, false));
                return pullDuration;
            }
        }

        /// <summary>
        /// Unified animation routine for both basic and special items.
        /// </summary>
        private IEnumerator CollectRoutine(float activePopScale, float duration, bool isJuicy)
        {
            Vector3 startPos = transform.position;
            float t = 0f;

            SpriteRenderer activeRenderer = (iconRenderer != null && iconRenderer.enabled) ? iconRenderer : sr;
            Color activeOriginalColor = (activeRenderer == sr && sr != null) ? originalColor : Color.white;

            while (t < 1f)
            {
                t += Time.deltaTime / duration;
                float clampedT = Mathf.Min(t, 1f);
                float curve = ease.Evaluate(clampedT);

                Vector3 targetPos = playerHead != null ? playerHead.position : startPos;

                // --- Scale & Pop ---
                float scaleBase = Mathf.Lerp(1f, 0f, curve);
                float popMultiplier = Mathf.Sin(curve * Mathf.PI) * (activePopScale - 1f);

                if (isJuicy)
                {
                    // Juicy squash & stretch
                    float squash = 1f + Mathf.Sin(curve * Mathf.PI * 4f) * 0.25f; // fast squash
                    transform.localScale = new Vector3(
                        initialScale.x * (scaleBase + popMultiplier),
                        initialScale.y * (scaleBase + popMultiplier * squash),
                        initialScale.z
                    );

                    // High-frequency rotation wiggle
                    float rotationAngle = Mathf.Sin(curve * Mathf.PI * 10f) * 25f;
                    transform.rotation = Quaternion.Euler(0f, 0f, rotationAngle);

                    // Snappy pull motion + overshoot
                    Vector3 overshootPos = targetPos + Vector3.up * 0.2f;
                    Vector3 pullOffset = Vector3.up * Mathf.Sin(curve * Mathf.PI * 2f) * 0.3f;
                    transform.position = Vector3.Lerp(startPos, overshootPos, curve) + pullOffset;

                    // Faster fade at end
                    float alpha = 1f - Mathf.Pow(curve, 2f);
                    if (activeRenderer != null)
                        activeRenderer.color = new Color(activeOriginalColor.r, activeOriginalColor.g, activeOriginalColor.b, alpha);
                    if (textMesh != null)
                        textMesh.color = new Color(activeTextColor.r, activeTextColor.g, activeTextColor.b, alpha);
                }
                else
                {
                    // Basic item (unchanged)
                    transform.localScale = initialScale * (scaleBase + popMultiplier);
                    if (playerHead != null)
                        transform.position = Vector3.Lerp(startPos, targetPos, curve);
                    transform.rotation = Quaternion.identity;

                    float alpha = 1f - curve;
                    if (activeRenderer != null)
                        activeRenderer.color = new Color(activeOriginalColor.r, activeOriginalColor.g, activeOriginalColor.b, alpha);
                    if (textMesh != null)
                        textMesh.color = new Color(activeTextColor.r, activeTextColor.g, activeTextColor.b, alpha);
                }

                yield return null;
            }

            // Cleanup
            transform.localScale = Vector3.zero;
            if (sr != null) sr.enabled = false;
            if (iconRenderer != null) iconRenderer.enabled = false;
            if (textMesh != null) textMesh.enabled = false;
            transform.rotation = Quaternion.identity;
        }
    }
}
