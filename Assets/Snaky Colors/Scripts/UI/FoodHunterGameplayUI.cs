using UnityEngine;
using UnityEngine.UI;
using TMPro; // Make sure TextMeshPro is imported
using DG.Tweening; // Make sure DOTween is imported
using System.Collections;

namespace SnakyColors
{
    public class FoodHunterGameplayUI : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private Slider foodFillingMeter;
        [SerializeField] private Slider ammoFillingMeter;
        [SerializeField] private Slider healthBar;
        [SerializeField] private Slider dashChargerMeter;
        [SerializeField] private RectTransform meterIconRectTransform;
        [SerializeField] private RectTransform healthIconRectTransform;
        // [SerializeField] private RectTransform dashIconRectTransform; // Optional: for dash icon plump
        // [SerializeField] private TextMeshProUGUI scoreText; // Optional: assign score text
        // [SerializeField] private TextMeshProUGUI ammoText; // Optional: assign ammo text

        [Header("Meter Effects")]
        [SerializeField] private GameObject meterEffectAnimation; // The animation object (with CanvasGroup)
        [SerializeField] private RectTransform meterFillRect;
        [SerializeField] private float meterTweenDuration = 0.5f;
        [SerializeField] private float effectActiveDuration = 0.6f;
        [SerializeField] private float effectFadeOutDuration = 0.2f;

        [Header("Health Effects")]
        [SerializeField] private float healthTweenDuration = 0.2f;

        [Header("Dash Effects")]
        [SerializeField] private float dashTweenDuration = 0.1f; // Very fast tween

        [Header("Effect Positioning Offset")]
        [SerializeField] private float effectXOffset = 0f;
        [SerializeField] private float effectYOffset = 0f;

        [Header("Dynamic Offset Factors")]
        [SerializeField] private float lowFillOffsetXMultiplier = 1.8f;
        [SerializeField] private float highFillOffsetXMultiplier = 0.3f;

        // --- Private Tween Variables ---
        private Tween meterTween;
        private Coroutine effectDisableCoroutine;
        private CanvasGroup effectCanvasGroup;
        private Tween fadeTween;
        private Tween iconTween;
        private Tween healthTween;
        private Tween healthIconTween;
        private Tween dashTween;
        private Tween ammoTween; // Added for ammo animation

        // --- Private State ---
        private Vector3 originalIconScale = Vector3.one;
        private Vector3 originalHealthIconScale = Vector3.one;
        private bool scalesInitialized = false; // Flag to track if scales are cached
        private float lastDashValue = -1f; // sentinel
        private bool dashInitialized = false;

        private void Start()
        {
            // Set sliders to non-interactable
            if (foodFillingMeter != null) foodFillingMeter.interactable = false;
            if (ammoFillingMeter != null) ammoFillingMeter.interactable = false;
            if (healthBar != null) healthBar.interactable = false;
            if (dashChargerMeter != null) dashChargerMeter.interactable = false;

            // --- Get CanvasGroup Reference ---
            if (meterEffectAnimation != null)
            {
                effectCanvasGroup = meterEffectAnimation.GetComponent<CanvasGroup>();
                if (effectCanvasGroup == null)
                {
                    Debug.LogWarning("Meter Effect Animation is missing a CanvasGroup component! Adding one.", meterEffectAnimation);
                    effectCanvasGroup = meterEffectAnimation.AddComponent<CanvasGroup>();
                }
            }

            // --- Initialize UI (but NOT scales) ---
            InitializeUIState();

            // --- Start Coroutine to cache scales AFTER Canvas Scaler runs ---
            StartCoroutine(CacheIconScales());
        }

        // --- NEW: Coroutine to wait one frame for Canvas Scaler ---
        private IEnumerator CacheIconScales()
        {
            // Wait for the end of the first frame
            // By this time, the Canvas Scaler will have run and set the correct scales
            yield return new WaitForEndOfFrame();

            if (meterIconRectTransform != null)
                originalIconScale = meterIconRectTransform.localScale;

            if (healthIconRectTransform != null)
                originalHealthIconScale = healthIconRectTransform.localScale;

            scalesInitialized = true; // Mark as initialized
            // Debug.Log($"Scales Cached: MeterIcon={originalIconScale}, HealthIcon={originalHealthIconScale}");
        }

        // --- Helper for initial UI setup ---
        private void InitializeUIState()
        {
            if (PlayerStats.Instance != null)
            {
                // Food Meter
                if (foodFillingMeter != null)
                {
                    foodFillingMeter.maxValue = PlayerStats.Instance.GetMaxMeter();
                    foodFillingMeter.minValue = 0f;
                    UpdateMeterFill(PlayerStats.Instance.GetCurrentMeter()); // Set initial value
                }
                // Health Bar
                if (healthBar != null)
                {
                    healthBar.maxValue = PlayerStats.Instance.GetMaxHealth();
                    healthBar.minValue = 0f;
                    UpdateHealthBar(PlayerStats.Instance.GetCurrentHealth());
                }
                // Dash Meter
                if (dashChargerMeter != null)
                {
                    dashChargerMeter.maxValue = PlayerStats.Instance.GetMaxDashCharge();
                    dashChargerMeter.minValue = 0f;
                    UpdateDashMeter(PlayerStats.Instance.GetCurrentDashCharge());
                }
                // Ammo Meter
                if (ammoFillingMeter != null)
                {
                    ammoFillingMeter.maxValue = PlayerStats.Instance.GetMaxAmmo();
                    ammoFillingMeter.minValue = 0f;
                    UpdateAmmoMeter(PlayerStats.Instance.GetCurrentAmmo());
                }
                // Score
                UpdateScoreText(PlayerStats.Instance.GetCurrentScore());
                // Effect
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
                // Subscribe to events
                PlayerStats.Instance.OnMeterChanged += AnimateMeterFill;
                PlayerStats.Instance.OnAmmoChanged += AnimateAmmoMeter;
                PlayerStats.Instance.OnScoreChanged += UpdateScoreText;
                PlayerStats.Instance.OnHealthChanged += AnimateHealthBar;
                PlayerStats.Instance.OnDashChargeChanged += AnimateDashMeter;

                // Immediately update UI with current values (in case stats changed while disabled)
                InitializeUIState();
            }
        }

        private void OnDisable()
        {
            if (PlayerStats.Instance != null)
            {
                // Unsubscribe
                PlayerStats.Instance.OnMeterChanged -= AnimateMeterFill;
                PlayerStats.Instance.OnAmmoChanged -= AnimateAmmoMeter;
                PlayerStats.Instance.OnScoreChanged -= UpdateScoreText;
                PlayerStats.Instance.OnHealthChanged -= AnimateHealthBar;
                PlayerStats.Instance.OnDashChargeChanged -= AnimateDashMeter;
            }

            // Kill all tweens
            meterTween?.Kill();
            fadeTween?.Kill();
            iconTween?.Kill();
            healthTween?.Kill();
            healthIconTween?.Kill();
            dashTween?.Kill();
            ammoTween?.Kill();

            if (effectDisableCoroutine != null)
            {
                StopCoroutine(effectDisableCoroutine);
                effectDisableCoroutine = null;
            }
        }

        // --- Meter Fill Animation ---
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

                if (targetMeterValue > previousValue)
                {
                    PlayIconPlumpAnimation(meterIconRectTransform, originalIconScale);

                    if (meterEffectAnimation != null && effectCanvasGroup != null)
                    {
                        fadeTween?.Kill();
                        effectCanvasGroup.alpha = 1f;
                        UpdateEffectPosition();
                        meterEffectAnimation.SetActive(true);
                        if (effectDisableCoroutine != null) StopCoroutine(effectDisableCoroutine);
                        effectDisableCoroutine = StartCoroutine(DisableEffectAfterDelay(effectActiveDuration));
                    }
                }
            }
        }

        // --- Health Bar Animation Method ---
        private void AnimateHealthBar(float targetHealthValue)
        {
            if (healthBar != null)
            {
                healthTween?.Kill();
                bool isDamage = targetHealthValue < healthBar.value;
                healthTween = healthBar.DOValue(targetHealthValue, healthTweenDuration)
                    .SetEase(Ease.OutQuad)
                    .OnComplete(() => healthTween = null);

                PlayHealthIconAnimation(isDamage, originalHealthIconScale);
            }
        }

        // --- Ammo Meter Animation Method ---
        private void AnimateAmmoMeter(int newAmmo)
        {
            if (ammoFillingMeter != null)
            {
                ammoTween?.Kill();
                ammoTween = ammoFillingMeter.DOValue(newAmmo, dashTweenDuration) // Re-use fast dash tween
                    .SetEase(Ease.OutQuad)
                    .OnComplete(() => ammoTween = null);
            }
        }

        // --- Dash Meter Animation Method ---
        private void AnimateDashMeter(float targetDashValue)
        {
            if (dashChargerMeter == null) return;

            // Don't animate the very first update from ResetStats
            if (!dashInitialized)
            {
                UpdateDashMeter(targetDashValue); // Set instantly
                return;
            }

            if (targetDashValue == lastDashValue) return; // No change

            dashTween?.Kill();
            dashTween = dashChargerMeter.DOValue(targetDashValue, dashTweenDuration)
                .SetEase(Ease.OutQuad)
                .OnComplete(() => dashTween = null);

            lastDashValue = targetDashValue;
        }

        // --- Health Icon Animation ---
        private void PlayHealthIconAnimation(bool isDamage, Vector3 originalScale)
        {
            if (healthIconRectTransform == null || !scalesInitialized) return;
            healthIconTween?.Kill();

            if (isDamage)
            {
                healthIconTween = healthIconRectTransform.DOPunchRotation(new Vector3(0, 0, 45f), 0.3f, 10, 1)
                    .OnComplete(() => {
                        healthIconRectTransform.localScale = originalScale; // Reset to correct scale
                        healthIconTween = null;
                    });
            }
            else
            {
                PlayIconPlumpAnimation(healthIconRectTransform, originalScale);
            }
        }

        // --- Reusable Icon Plump Animation ---
        private void PlayIconPlumpAnimation(RectTransform iconRect, Vector3 originalScale)
        {
            if (iconRect == null || !scalesInitialized) return; // Don't animate if scales not ready

            if (iconRect == meterIconRectTransform)
            {
                iconTween?.Kill();
                iconTween = DOTween.Sequence()
                    .Append(iconRect.DOScale(originalScale * 1.3f, 0.1f).SetEase(Ease.OutQuad))
                    .Append(iconRect.DOScale(originalScale, 0.3f).SetEase(Ease.OutBack))
                    .OnComplete(() => iconTween = null);
            }
            else if (iconRect == healthIconRectTransform)
            {
                healthIconTween?.Kill();
                healthIconTween = DOTween.Sequence()
                    .Append(iconRect.DOScale(originalScale * 1.3f, 0.1f).SetEase(Ease.OutQuad))
                    .Append(iconRect.DOScale(originalScale, 0.3f).SetEase(Ease.OutBack))
                    .OnComplete(() => healthIconTween = null);
            }
        }

        // --- Coroutine for effect ---
        private IEnumerator DisableEffectAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);
            if (meterEffectAnimation != null && effectCanvasGroup != null)
            {
                fadeTween?.Kill();
                fadeTween = effectCanvasGroup.DOFade(0f, effectFadeOutDuration)
                    .SetEase(Ease.Linear)
                    .OnComplete(() => {
                        meterEffectAnimation.SetActive(false);
                        fadeTween = null;
                    });
            }
            effectDisableCoroutine = null;
        }

        // --- Position effect ---
        private void UpdateEffectPosition()
        {
            if (meterEffectAnimation != null && meterFillRect != null && foodFillingMeter != null)
            {
                Vector3[] fillCorners = new Vector3[4];
                meterFillRect.GetWorldCorners(fillCorners);
                float normalizedValue = foodFillingMeter.normalizedValue;
                float currentXOffset = effectXOffset;
                if (normalizedValue < 0.2f) currentXOffset *= lowFillOffsetXMultiplier;
                else if (normalizedValue > 0.8f) currentXOffset *= highFillOffsetXMultiplier;
                float edgeX = Mathf.Lerp(fillCorners[0].x, fillCorners[3].x, normalizedValue);
                float edgeY = (fillCorners[0].y + fillCorners[1].y) / 2f;
                Vector3 finalPos = new Vector3(edgeX + currentXOffset, edgeY + effectYOffset, meterEffectAnimation.transform.position.z);
                meterEffectAnimation.transform.position = finalPos;
            }
        }

        // --- Instant update methods (called by Start/OnEnable) ---
        private void UpdateMeterFill(float currentMeterValue)
        {
            if (foodFillingMeter != null)
            {
                meterTween?.Kill();
                foodFillingMeter.value = currentMeterValue;
                UpdateEffectPosition(); // Position effect correctly on init
            }
        }

        private void UpdateHealthBar(float currentHealthValue)
        {
            if (healthBar != null)
            {
                healthTween?.Kill();
                healthBar.value = currentHealthValue;
            }
        }

        private void UpdateDashMeter(float currentDashValue)
        {
            if (dashChargerMeter != null)
            {
                dashTween?.Kill();
                dashChargerMeter.value = currentDashValue;
                lastDashValue = currentDashValue; // Sync last value
                if (!dashInitialized) dashInitialized = true; // Mark as initialized
            }
        }

        private void UpdateAmmoMeter(int newAmmo)
        {
            if (ammoFillingMeter != null)
            {
                ammoTween?.Kill(); // Kill animation if instantly setting
                ammoFillingMeter.value = newAmmo;
            }
        }

        private void UpdateScoreText(int newScore)
        {
            // if (scoreText != null) scoreText.text = newScore.ToString();
        }
    }
}