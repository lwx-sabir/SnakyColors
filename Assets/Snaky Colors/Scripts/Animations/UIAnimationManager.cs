using UnityEngine;
using DG.Tweening;
using SnakyColors;

public class UIAnimationManager : MonoBehaviour
{
    [Header("UI Elements")]
    public RectTransform playButton;
    public RectTransform logoElement;
    public RectTransform cardPanel;

    [Header("Animation Settings")]
    public float slideDuration = 0.5f;
    public float logoFadeDuration = 0.2f;

    // --- ADJUSTED FOR FLUFFY POP DOWN ---
    public float buttonPopDuration = 0.35f; // Longer duration for better effect
    public float buttonScaleFactor = 0.9f;
    public float buttonDropDistance = -15f; // New: Vertical drop for "pop down" feel

    private Vector2 buttonStartPos;
    private Vector2 cardTargetPos;
    private Vector2 cardOffscreenPos;
    private Vector3 logoStartScale;

    private CanvasGroup playButtonCanvasGroup;
    private CanvasGroup logoCanvasGroup;

    private bool isAnimating = false;

    void Awake()
    {
        // --- INITIALIZATION (Unchanged) ---
        buttonStartPos = playButton.anchoredPosition;
        cardTargetPos = cardPanel.anchoredPosition;
        logoStartScale = logoElement.localScale;

        playButtonCanvasGroup = playButton.GetComponent<CanvasGroup>();
        if (playButtonCanvasGroup == null) playButtonCanvasGroup = playButton.gameObject.AddComponent<CanvasGroup>();

        logoCanvasGroup = logoElement.GetComponent<CanvasGroup>();
        if (logoCanvasGroup == null) logoCanvasGroup = logoElement.gameObject.AddComponent<CanvasGroup>();

        RectTransform parentRect = cardPanel.parent as RectTransform;
        float offX = parentRect.rect.width + cardPanel.rect.width;
        cardOffscreenPos = new Vector2(offX, cardTargetPos.y);

        cardPanel.anchoredPosition = cardOffscreenPos;
    }

    public void OnPlayButtonClick()
    {
        if (isAnimating) return;
        isAnimating = true;
        AudioManager.Instance.Play(SoundType.MenuOpen1);
        Sequence sequence = DOTween.Sequence();

        // --- Setup: Kill existing tweens and ensure visibility ---
        DOTween.Kill(playButton);
        DOTween.Kill(logoElement);
        DOTween.Kill(cardPanel);
        playButton.gameObject.SetActive(true);
        logoElement.gameObject.SetActive(true);

        // ----------------------------------------------------
        // PHASE 1: LOGO & BUTTON POP-OUTS
        // ----------------------------------------------------

        // 1. Logo Scale and Fade Out
        sequence.Append(
            logoElement.DOScale(Vector3.zero, logoFadeDuration).SetEase(Ease.InBack)
        );
        sequence.Insert(0, logoCanvasGroup.DOFade(0f, logoFadeDuration).SetEase(Ease.Linear));
        sequence.AppendCallback(() => logoElement.gameObject.SetActive(false));

        // 2. Button Pop-Down and Fade Out (Starts immediately after logo is gone)
        float buttonStartTime = logoFadeDuration;

        // A) Pop Down (Vertical Movement) - NEW
        sequence.Append(
            playButton.DOAnchorPos(buttonStartPos + new Vector2(0, buttonDropDistance), buttonPopDuration)
                .SetEase(Ease.OutBack) // Use OutBack for the springy/fluffy feel
        );

        // B) Scale Down (Horizontal/Vertical Squish)
        sequence.Insert(buttonStartTime,
            playButton.DOScale(Vector3.one * buttonScaleFactor, buttonPopDuration)
                .SetEase(Ease.OutBack) // Use OutBack
        );

        // C) Fade Out
        sequence.Insert(buttonStartTime,
            playButtonCanvasGroup.DOFade(0f, buttonPopDuration).SetEase(Ease.OutSine)
        );

        // ----------------------------------------------------
        // PHASE 2: CARD PANEL SLIDE IN 
        // ----------------------------------------------------

        // Slide in the card panel. Starts when the button pop-out starts.
        sequence.Insert(buttonStartTime,
            cardPanel.DOAnchorPos(cardTargetPos, slideDuration)
                .SetEase(Ease.OutCubic)
        );

        // Disable the button after its animation completes (buttonStartTime + buttonPopDuration)
        sequence.AppendCallback(() => playButton.gameObject.SetActive(false));

        sequence.OnComplete(() => isAnimating = false)
                .Play();
    }

    public void ResetUI()
    {
        if (isAnimating) return;
        isAnimating = true;

        // --- Setup: Kill existing tweens and ensure ready state ---
        DOTween.Kill(playButton);
        DOTween.Kill(logoElement);
        DOTween.Kill(cardPanel);
        AudioManager.Instance.Play(SoundType.Button2);

        logoElement.gameObject.SetActive(true);
        logoElement.localScale = Vector3.zero;
        logoCanvasGroup.alpha = 0f;

        playButton.gameObject.SetActive(true);
        playButton.localScale = Vector3.one * buttonScaleFactor;
        playButtonCanvasGroup.alpha = 0f;
        playButton.anchoredPosition = buttonStartPos + new Vector2(0, buttonDropDistance); // Set to dropped position

        Sequence sequence = DOTween.Sequence();

        // ----------------------------------------------------
        // PHASE 1 (Reverse): CARD PANEL SLIDE OUT
        // ----------------------------------------------------
        sequence.Append(
            cardPanel.DOAnchorPos(cardOffscreenPos, slideDuration)
                .SetEase(Ease.OutCubic)
        );

        // ----------------------------------------------------
        // PHASE 2 (Reverse): BUTTON & LOGO POP-IN
        // ----------------------------------------------------

        float popInStartTime = slideDuration;

        // 1. Button Pop-Up and Fade In
        // A) Pop Up (Vertical Movement back to start) - NEW
        sequence.Insert(popInStartTime,
            playButton.DOAnchorPos(buttonStartPos, buttonPopDuration)
                .SetEase(Ease.OutBack) // Springy pop up
        );

        // B) Scale Up
        sequence.Insert(popInStartTime,
            playButton.DOScale(Vector3.one, buttonPopDuration)
                .SetEase(Ease.OutBack)
        );
        // C) Fade In
        sequence.Insert(popInStartTime,
            playButtonCanvasGroup.DOFade(1f, buttonPopDuration)
                .SetEase(Ease.Linear)
        );

        // 2. Logo Scale and Fade In (Runs after button pop-in finishes)
        sequence.Insert(popInStartTime + buttonPopDuration,
            logoElement.DOScale(logoStartScale, logoFadeDuration)
                .SetEase(Ease.OutBack)
        );
        sequence.Insert(popInStartTime + buttonPopDuration,
            logoCanvasGroup.DOFade(1f, logoFadeDuration)
                .SetEase(Ease.Linear)
        );

        sequence.OnComplete(() => isAnimating = false)
                .Play();
    }
}