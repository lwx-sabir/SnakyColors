using UnityEngine;
using UnityEngine.InputSystem;

namespace SnakyColors
{  
    public class InputManager : MonoBehaviour
    {
        public static InputManager Instance { get; private set; }

        [Header("Input Filtering")]
        [SerializeField] private float topUIIgnoreFraction = 1f / 7f;

        private float topMenuPos;

        public bool IsInputDown { get; private set; }
        public bool IsInputHeld { get; private set; }
        public bool IsInputUp { get; private set; }
        public Vector2 ScreenPosition { get; private set; }
        public bool IsInputOverUI { get; private set; }

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);

            topMenuPos = Screen.height - (Screen.height * topUIIgnoreFraction);
        }

        private void Update()
        {
            IsInputDown = false;
            IsInputUp = false;
            bool wasHeld = IsInputHeld;
            IsInputHeld = false;

            // Get primary pointer (touch or mouse) from new Input System
            Pointer pointer = Pointer.current;
            if (pointer != null)
            {
                Vector2 pos = pointer.position.ReadValue();
                ScreenPosition = pos;

                IsInputOverUI = pos.y >= topMenuPos;

                bool pressed = pointer.press.isPressed;

                if (!IsInputOverUI)
                {
                    if (pressed && !wasHeld)
                        IsInputDown = true;

                    if (pressed)
                        IsInputHeld = true;

                    if (!pressed && wasHeld)
                        IsInputUp = true;
                }
                else
                {
                    if (!pressed && wasHeld)
                        IsInputUp = true;
                }
            }
        }
    }

}