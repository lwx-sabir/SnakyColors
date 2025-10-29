
namespace SnakyColors
{
    using UnityEngine; 
    using DG.Tweening;

    public class HomeAnimation : MonoBehaviour
    {
        [SerializeField] private RectTransform playButton;
        [SerializeField] private RectTransform cardListPanel;

        [Header("Animation Settings")]
        [SerializeField] private float slideDuration = 0.5f;
        [SerializeField] private float playButtonShiftX = -300f; // move left by this
        [SerializeField] private Ease easeType = Ease.OutCubic;

        private Vector3 playButtonStartPos;
        private Vector3 cardListStartPos;
        private Vector3 cardListTargetPos;

        private void Awake()
        {
            playButtonStartPos = playButton.anchoredPosition;
            cardListStartPos = cardListPanel.anchoredPosition;
            cardListTargetPos = cardListStartPos; // final position (onscreen)
            cardListPanel.anchoredPosition = new Vector3(Screen.width, cardListStartPos.y, 0); // offscreen right
        }

        public void OnPlayButtonClicked()
        {
            // 1️ Slide Play button left
            playButton.DOAnchorPosX(playButtonStartPos.x + playButtonShiftX, slideDuration).SetEase(easeType);

            // 2️ Slide Card List panel in from right
            cardListPanel.DOAnchorPosX(cardListTargetPos.x, slideDuration).SetEase(easeType);
        }
    }

}