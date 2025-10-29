using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;
using System.Collections;

namespace SnakyColors
{ 
    [RequireComponent(typeof(CanvasGroup))] // Ensures the CanvasGroup exists 
    public class FoodHunterGameplayUI : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private Slider foodFillingMeter;
        [SerializeField] private Slider ammoFillingMeter;
        [SerializeField] private RectTransform meterIconRectTransform;

        [Header("Meter Effects")]
        [SerializeField] private GameObject meterEffectAnimation;
        [SerializeField] private RectTransform meterFillRect;
        [SerializeField] private float meterTweenDuration = 0.5f;
        [SerializeField] private float effectActiveDuration = 0.6f;
        [SerializeField] private float effectFadeOutDuration = 0.2f; 

        [Header("Effect Positioning Offset")]
        [SerializeField] private float effectXOffset = 0f;
        [SerializeField] private float effectYOffset = 0f;
         
        [Header("Dynamic Offset Factors")]
        [Tooltip("Multiplier for X offset when meter < 20%")]
        [SerializeField] private float lowFillOffsetXMultiplier = 1.8f; // (1 + 80%)
        [Tooltip("Multiplier for X offset when meter > 80%")]
        [SerializeField] private float highFillOffsetXMultiplier = 0.3f; // (1 - 70%)
        // ---------------------------------

        private Vector3 originalIconScale = Vector3.one;
        private Tween iconTween; // To manage the icon animation


        private Tween meterTween;
        private Coroutine effectDisableCoroutine;
        private CanvasGroup effectCanvasGroup; 
        private Tween fadeTween;

        private void Start()
        {
            if(foodFillingMeter != null)
            {
                foodFillingMeter.interactable = false;
            }
            if (ammoFillingMeter != null)
            {
                ammoFillingMeter.interactable = false;
            }
            // --- Store Original Icon Scale ---
            if (meterIconRectTransform != null)
            {
                originalIconScale = meterIconRectTransform.localScale;
            }

            // --- Get CanvasGroup Reference ---
            if (meterEffectAnimation != null)
            {
                effectCanvasGroup = meterEffectAnimation.GetComponent<CanvasGroup>();
                if (effectCanvasGroup == null)
                {
                    Debug.LogError("Meter Effect Animation is missing a CanvasGroup component!", meterEffectAnimation);
                }
            }
            // ---------------------------------

            if (PlayerStats.Instance != null)
            {
                if (foodFillingMeter != null)
                {
                    foodFillingMeter.maxValue = PlayerStats.Instance.GetMaxMeter();
                    foodFillingMeter.minValue = 0f;
                    if (meterFillRect == null) meterFillRect = foodFillingMeter.fillRect;
                    UpdateMeterFill(PlayerStats.Instance.GetCurrentMeter());
                }
                if (ammoFillingMeter != null) UpdateAmmoMeter(PlayerStats.Instance.GetCurrentAmmo());
                UpdateScoreText(PlayerStats.Instance.GetCurrentScore());

                if (meterEffectAnimation != null) meterEffectAnimation.SetActive(false);
            }
            else
            {
                Debug.LogError("GameplayUI: PlayerStats.Instance not found on Start!");
            }
        }

        private void OnEnable()
        {
            if (PlayerStats.Instance != null)
            {
                PlayerStats.Instance.OnMeterChanged += AnimateMeterFill;
                PlayerStats.Instance.OnAmmoChanged += UpdateAmmoMeter;
                PlayerStats.Instance.OnScoreChanged += UpdateScoreText;

                if (foodFillingMeter != null) UpdateMeterFill(PlayerStats.Instance.GetCurrentMeter());
                if (ammoFillingMeter != null) UpdateAmmoMeter(PlayerStats.Instance.GetCurrentAmmo());
                UpdateScoreText(PlayerStats.Instance.GetCurrentScore());
            }
        }

        private void OnDisable()
        {
            if (PlayerStats.Instance != null)
            {
                PlayerStats.Instance.OnMeterChanged -= AnimateMeterFill;
                PlayerStats.Instance.OnAmmoChanged -= UpdateAmmoMeter;
                PlayerStats.Instance.OnScoreChanged -= UpdateScoreText;
            }
            meterTween?.Kill();
            fadeTween?.Kill(); // --- Kill fade tween ---
            if (effectDisableCoroutine != null)
            {
                StopCoroutine(effectDisableCoroutine);
                effectDisableCoroutine = null;
            }
        }

        private void AnimateMeterFill(float targetMeterValue)
        {
            if (foodFillingMeter != null)
            {
                meterTween?.Kill();
                float previousValue = foodFillingMeter.value;

                meterTween = foodFillingMeter.DOValue(targetMeterValue, meterTweenDuration)
                    .SetEase(Ease.OutQuad)
                    .OnUpdate(UpdateEffectPosition)
                    .OnComplete(() => meterTween = null);

                if (meterEffectAnimation != null && targetMeterValue > previousValue && effectCanvasGroup != null)
                {
                    // --- PLAY ICON ANIMATION ---
                    PlayIconPlumpAnimation();
                    // --- Reset alpha before showing ---
                    fadeTween?.Kill(); // Kill any ongoing fade out
                    effectCanvasGroup.alpha = 1f; // Make sure it's fully visible
                    // ---------------------------------

                    UpdateEffectPosition();
                    meterEffectAnimation.SetActive(true);

                    if (effectDisableCoroutine != null) StopCoroutine(effectDisableCoroutine);
                    effectDisableCoroutine = StartCoroutine(DisableEffectAfterDelay(effectActiveDuration));
                }
            }
        } 

        private IEnumerator DisableEffectAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);

            // Start fading out
            if (meterEffectAnimation != null && effectCanvasGroup != null)
            {
                // Kill previous fade just in case
                fadeTween?.Kill();
                fadeTween = effectCanvasGroup.DOFade(0f, effectFadeOutDuration)
                    .SetEase(Ease.Linear) // Or other ease for fade
                    .OnComplete(() => {
                        meterEffectAnimation.SetActive(false); // Disable AFTER fading
                        fadeTween = null; // Clear tween reference
                    });
            }
            effectDisableCoroutine = null;
        } 
         
        private void UpdateEffectPosition()
        {
            if (meterEffectAnimation != null && meterFillRect != null && foodFillingMeter != null)
            {
                Vector3[] fillCorners = new Vector3[4];
                meterFillRect.GetWorldCorners(fillCorners);

                float normalizedValue = foodFillingMeter.normalizedValue; // Value 0 to 1

                // Calculate dynamic X offset based on fill percentage
                float currentXOffset = effectXOffset;
                if (normalizedValue < 0.2f)
                {
                    currentXOffset *= lowFillOffsetXMultiplier;
                }
                else if (normalizedValue > 0.8f)
                {
                    currentXOffset *= highFillOffsetXMultiplier;
                }
                // --- End Dynamic Offset Calculation ---

                float edgeX = Mathf.Lerp(fillCorners[0].x, fillCorners[3].x, normalizedValue);
                float edgeY = (fillCorners[0].y + fillCorners[1].y) / 2f;

                Vector3 finalPos = new Vector3(
                    edgeX + currentXOffset, // Use the dynamically calculated offset
                    edgeY + effectYOffset,
                    meterEffectAnimation.transform.position.z
                );

                meterEffectAnimation.transform.position = finalPos;
            }
        }

        // Sets the value instantly (used in OnEnable/Start)
        private void UpdateMeterFill(float currentMeterValue)
        {
            if (foodFillingMeter != null)
            {
                meterTween?.Kill();
                foodFillingMeter.value = currentMeterValue;
                UpdateEffectPosition();
            }
        }

        private void UpdateAmmoMeter(int newAmmo)
        {
            if (ammoFillingMeter != null)
            {
                ammoFillingMeter.value = newAmmo;
            }
        }

        // Icon Animation Method ---
        private void PlayIconPlumpAnimation()
        {
            if (meterIconRectTransform == null) return;

            // Kill any previous icon animation to restart it
            iconTween?.Kill();

            // Sequence: Scale up quickly, then scale back down with a bounce
            iconTween = DOTween.Sequence()
                .Append(meterIconRectTransform.DOScale(originalIconScale * 1.3f, 0.1f).SetEase(Ease.OutQuad)) // Scale up fast
                .Append(meterIconRectTransform.DOScale(originalIconScale, 0.3f).SetEase(Ease.OutBack)) // Scale back down with overshoot/bounce
                .OnComplete(() => iconTween = null); // Clear tween reference
        }

        private void UpdateScoreText(int newScore)
        {
            // Update scoreText if assigned
        }
    }
}