using System.Collections;
using UnityEngine;
using TMPro;

namespace SnakyColors
{
    public class FruitCollectEffect : MonoBehaviour
    {
        [Header("Setup")]
        public float pullDuration = 0.15f;
        public float popScale = 1.2f;
        public AnimationCurve ease = AnimationCurve.EaseInOut(0, 0, 1, 1);
        public bool useFruitColor = true;

        [Header("Score Popup")]
        [SerializeField] private TextMeshPro textMesh;

        private SpriteRenderer sr;
        private Vector3 initialScale;
        private bool isCollected = false;
        [HideInInspector]
        public Transform playerHead;

        private Color originalColor;
        private Color originalTextColor;
        private Color activeTextColor; // Color to be faded

        void Awake()
        {
            sr = GetComponent<SpriteRenderer>();
            initialScale = transform.localScale;
            originalColor = sr.color;

            if (textMesh != null)
            {
                originalTextColor = textMesh.color;
                textMesh.enabled = false;
            }
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
        }

        public void PlayCollectAnimation(string scoreText, Color itemColor)
        {
            if (isCollected) return;
            isCollected = true;

            if (textMesh != null)
            { 
                textMesh.text = $"+{scoreText}"; 

                activeTextColor = useFruitColor ? itemColor : originalTextColor;
                textMesh.color = activeTextColor;
                textMesh.enabled = true;
            }

            StartCoroutine(CollectRoutine());
        }
         
        private IEnumerator CollectRoutine()
        { 
            Vector3 startPos = transform.position;
            float t = 0;

            while (t < 1f)
            {
                t += Time.deltaTime / pullDuration;
                float clampedT = Mathf.Min(t, 1f);
                float curve = ease.Evaluate(clampedT);

                Vector3 currentTargetPos = (playerHead != null) ? playerHead.position : startPos;

                // Scale
                float scaleBase = Mathf.Lerp(1f, 0f, curve);
                float popMultiplier = Mathf.Sin(curve * Mathf.PI) * (popScale - 1f);
                transform.localScale = initialScale * (scaleBase + popMultiplier);

                // Pull
                if (playerHead != null)
                    transform.position = Vector3.Lerp(startPos, currentTargetPos, curve);

                float alpha = 1f - curve;

                // Fade Sprite
                if (sr != null)
                {
                    sr.color = new Color(originalColor.r, originalColor.g, originalColor.b, alpha);
                }

                // Fade Text
                if (textMesh != null)
                {
                    textMesh.color = new Color(activeTextColor.r, activeTextColor.g, activeTextColor.b, alpha);
                }

                yield return null;
            }

            transform.localScale = Vector3.zero;
            if (sr != null) sr.enabled = false;
            if (textMesh != null) textMesh.enabled = false;
        }
    }
}