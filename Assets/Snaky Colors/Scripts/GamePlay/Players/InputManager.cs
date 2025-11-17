using System;
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

        public bool BoostInputActive { get; private set; }
        public event Action<bool> BoostInputChanged;

        private bool _gestureBoost;
        private bool _manualBoost;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else
            {
                Destroy(gameObject);
                return;
            }

            topMenuPos = Screen.height - (Screen.height * topUIIgnoreFraction);
        }

        public void SetBoostButtonState(bool pressed)
        {
            if (_manualBoost == pressed) return;
            _manualBoost = pressed;
            UpdateBoostState(_gestureBoost);
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

            UpdateGestureBoost();
        }

        private void UpdateGestureBoost()
        {
            bool gesture = false;
            var touchScreen = Touchscreen.current;
            if (touchScreen != null)
            {
                int active = 0;
                foreach (var touch in touchScreen.touches)
                {
                    if (touch == null) continue;
                    if (touch.isInProgress)
                    {
                        active++;
                        if (active >= 2)
                        {
                            gesture = true;
                            break;
                        }
                    }
                }
            }

            if (_gestureBoost != gesture)
            {
                _gestureBoost = gesture;
                UpdateBoostState(_gestureBoost);
            }
            else
            {
                // ensure derived state reflects manual button changes even if gesture unchanged
                UpdateBoostState(_gestureBoost);
            }
        }

        private void UpdateBoostState(bool gestureState)
        {
            bool newState = gestureState || _manualBoost;
            if (BoostInputActive == newState)
                return;

            BoostInputActive = newState;
            BoostInputChanged?.Invoke(newState);
        }
    }
}
