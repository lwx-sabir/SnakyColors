using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SnakyColors
{
    [RequireComponent(typeof(RectTransform))]
    public class SlitherJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public static SlitherJoystick Instance { get; private set; }

        [Header("Joystick Visuals")]
        [SerializeField] private Image backgroundImage;
        [SerializeField] private Image handleImage;

        [Header("Joystick Settings")]
        [Tooltip("How far the handle can be dragged from the center (in pixels).")]
        [SerializeField] private float handleRange = 100f;
        [Tooltip("The size of the 'deadzone' in the center (in pixels).")]
        [SerializeField] private float deadZone = 10f;

        public Vector2 Input { get; private set; } = Vector2.zero;
        public bool IsInputHeld { get; private set; } = false;

        private RectTransform baseRectTransform; // The full-screen panel
        private RectTransform backgroundRectTransform; // The background's transform
        private RectTransform handleRectTransform; // The handle's transform

        private Canvas canvas;
        private Camera canvasCamera;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else
            {
                Destroy(gameObject);
                return;
            }

            baseRectTransform = GetComponent<RectTransform>();
            canvas = GetComponentInParent<Canvas>();
            if (canvas.renderMode == RenderMode.ScreenSpaceCamera)
                canvasCamera = canvas.worldCamera;

            // --- THIS IS THE FIX ---
            // This setup assumes the Handle is a CHILD of the Background
            if (backgroundImage != null)
            {
                backgroundRectTransform = backgroundImage.rectTransform;
                // Force anchors and pivot to middle-center
                backgroundRectTransform.anchorMin = new Vector2(0.5f, 0.5f);
                backgroundRectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                backgroundRectTransform.pivot = new Vector2(0.5f, 0.5f);
                backgroundImage.gameObject.SetActive(false); // Hide at start
            }

            if (handleImage != null)
            {
                handleRectTransform = handleImage.rectTransform;
                // Force anchors and pivot to middle-center *of its parent (the background)*
                handleRectTransform.anchorMin = new Vector2(0.5f, 0.5f);
                handleRectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                handleRectTransform.pivot = new Vector2(0.5f, 0.5f);
                handleRectTransform.anchoredPosition = Vector2.zero; // Start at center
                handleImage.gameObject.SetActive(false); // Hide at start
            }
            // ---------------------
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            // Check if we clicked on the invisible input area
            if (eventData.pointerCurrentRaycast.gameObject != gameObject)
            {
                IsInputHeld = false;
                return;
            }

            // Show and move the joystick background to the tap position
            if (backgroundRectTransform)
            {
                backgroundImage.gameObject.SetActive(true);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    baseRectTransform,
                    eventData.position,
                    canvasCamera,
                    out Vector2 anchoredPos);

                backgroundRectTransform.anchoredPosition = anchoredPos;
            }

            if (handleRectTransform) handleImage.gameObject.SetActive(true);

            IsInputHeld = true;
            OnDrag(eventData); // Set initial input
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!IsInputHeld || backgroundRectTransform == null || handleRectTransform == null) return;

            // Get touch position relative to the background's center
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                backgroundRectTransform,
                eventData.position,
                canvasCamera,
                out Vector2 handlePos);

            // Clamp the handle's position
            Vector2 clampedPos = Vector2.ClampMagnitude(handlePos, handleRange);

            // Set the handle's position *relative to the background's center*
            handleRectTransform.anchoredPosition = clampedPos;

            // Calculate input
            if (clampedPos.magnitude > deadZone)
            {
                Input = clampedPos / handleRange;
            }
            else
            {
                Input = Vector2.zero;
            }
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            // Hide visuals, reset input
            if (backgroundImage) backgroundImage.gameObject.SetActive(false);
            if (handleImage) handleImage.gameObject.SetActive(false);

            Input = Vector2.zero;
            IsInputHeld = false;
        }
    }
}