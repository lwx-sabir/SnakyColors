using UnityEngine;
using UnityEngine.InputSystem;

namespace SnakyColors
{
    // This is your SlitherInputController, renamed to SlitherMovement
    public class SlitherMovement : MonoBehaviour
    {
        private Camera mainCamera;
        private bool isInputHeld = false;
          
        void Start()
        {
            mainCamera = Camera.main;
        }

        void Update()
        { 
            if (InputManager.Instance == null || NetworkClient.Instance == null) return;

            InputManager input = InputManager.Instance;
            isInputHeld = (input.IsInputDown || input.IsInputHeld);

            if (isInputHeld && !input.IsInputOverUI)
            {
                Vector3 worldPoint = mainCamera.ScreenToWorldPoint(input.ScreenPosition);
                 
                NetworkClient.Instance.SendTarget(new Vector2(worldPoint.x, worldPoint.y)); 
                 
                transform.position = new Vector3(worldPoint.x, worldPoint.y, 0f);
            }
             
            bool isBoosting = Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed;
            NetworkClient.Instance.SendBoost(isBoosting); 
        }
    }
}