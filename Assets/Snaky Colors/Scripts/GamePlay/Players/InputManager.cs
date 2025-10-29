using UnityEngine;

namespace SnakyColors
{
    public class InputManager : MonoBehaviour
    {
        public static InputManager Instance { get; private set; }

        [Header("Input Filtering")]
        [Tooltip("The fraction of the screen height from the top to ignore input.")]
        [SerializeField] private float topUIIgnoreFraction = 1f / 7f; // This is 1/7

        private float topMenuPos;

        // Public properties for other scripts to read
        public bool IsInputDown { get; private set; }
        public bool IsInputHeld { get; private set; }
        public bool IsInputUp { get; private set; }
        public Vector2 ScreenPosition { get; private set; }
        public bool IsInputOverUI { get; private set; }

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                // DontDestroyOnLoad(gameObject); // Optional: if you need it across scenes
            }
            else
            {
                Destroy(gameObject);
            }

            // Calculate the pixel position of the UI boundary
            topMenuPos = Screen.height - (Screen.height * topUIIgnoreFraction);
        }

        private void Update()
        {
            // Reset flags at the start of the frame
            IsInputDown = false;
            IsInputHeld = false;
            IsInputUp = false;
            IsInputOverUI = false;
            ScreenPosition = Vector2.zero;

            // --- 1. Handle Touch Input (Primary) ---
            if (Input.touchCount > 0)
            {
                Touch touch = Input.GetTouch(0); // Only track the first finger
                ScreenPosition = touch.position;

                // Check if touch is over the "UI" area
                IsInputOverUI = (touch.position.y >= topMenuPos);
                if (IsInputOverUI) return; // Ignore input if it's over the UI

                // Set flags based on touch phase
                if (touch.phase == TouchPhase.Began)
                {
                    IsInputDown = true;
                }
                else if (touch.phase == TouchPhase.Moved || touch.phase == TouchPhase.Stationary)
                {
                    IsInputHeld = true;
                }
                else if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                {
                    IsInputUp = true;
                }
            }
            // --- 2. Handle Mouse Input (Fallback for Editor) ---
            else if (Input.anyKey || Input.GetMouseButton(0) || Input.GetMouseButtonUp(0)) // Check for mouse input
            {
                ScreenPosition = Input.mousePosition;

                // Check if mouse is over the "UI" area
                IsInputOverUI = (Input.mousePosition.y >= topMenuPos);
                if (IsInputOverUI) return; // Ignore input if it's over the UI

                // Set flags based on mouse buttons
                if (Input.GetMouseButtonDown(0))
                {
                    IsInputDown = true;
                }
                else if (Input.GetMouseButton(0))
                {
                    IsInputHeld = true;
                }
                else if (Input.GetMouseButtonUp(0))
                {
                    IsInputUp = true;
                }
            }
        }
    }
}