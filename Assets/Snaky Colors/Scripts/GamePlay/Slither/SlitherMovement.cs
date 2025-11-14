using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

namespace SnakyColors
{
    [RequireComponent(typeof(SegmentedCreator))]
    public class SlitherMovement : MonoBehaviour
    {
        private SegmentedCreator localSnake;
        private Transform snakeHead;

        // Keep the target a fixed distance ahead of the head
        [SerializeField] private float targetDistance = 5f;

        // Network throttle
        private float nextStateSendTime = 0.001f;
        [SerializeField] private float stateSendRate = 0.05f; // 20 Hz 

        private Transform snakeTarget;

        // Local state
        private bool isBoosting = false;
        private Vector2 currentInputDirection = Vector2.up; // default forward
        private PlayerStateDto playerState;

        // Reference non-boost speed for turn scaling
        private float baseNonBoostSpeedRef = -1f;

        void Start()
        {
            localSnake = GetComponent<SegmentedCreator>();
            if (localSnake == null || localSnake.moveToTarget == null)
            {
                Debug.LogError("SlitherMovement is missing references!", this);
                enabled = false;
                return;
            }

            snakeHead = transform;
            snakeTarget = localSnake.moveToTarget.Target;
            if (snakeTarget == null)
            {
                Debug.LogError("SlitherMovement's snake has no Target assigned!", this);
                enabled = false;
                return;
            }

            // Ensure movement is enabled on the underlying mover
            localSnake.moveToTarget.enableMoving = true;
            localSnake.moveToTarget.moveThroughTarget = true;

            // Initialize target orientation and position ahead of the head
            if (snakeTarget.up.sqrMagnitude < 0.0001f)
                snakeTarget.up = Vector3.up;

            snakeTarget.position = snakeHead.position + snakeTarget.up * targetDistance;
        }

        void Update()
        {
            if (NetworkClient.Instance == null || localSnake == null)
                return;

            // Prefer on-screen joystick; fallback to keyboard if joystick missing
            var joystick = SlitherJoystick.Instance; // may be null
            bool hasJoyInput = joystick != null && joystick.IsInputHeld && joystick.Input.sqrMagnitude > 0f;
            Vector2 inputDir = hasJoyInput ? joystick.Input : GetKeyboardInput();

            // Only update desired direction if any input (otherwise keep last heading)
            if (inputDir.sqrMagnitude > 0.0001f)
                currentInputDirection = inputDir.normalized;

            playerState = NetworkClient.Instance.localPlayerState;

            // Boosting via keyboard
            isBoosting = Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed;

            // Apply local steering and target placement
            RunLocalMovement();
        }

        void LateUpdate()
        { 
            if (Time.time >= nextStateSendTime && NetworkClient.Instance != null && NetworkClient.Instance.localPlayerState != null)
            {
                SendStateToServer();
                nextStateSendTime = Time.time + stateSendRate;
            }
        }
         
        void RunLocalMovement()
        {
            NetworkClient.Instance.PendingInput = currentInputDirection; 
        }

        Vector2 GetKeyboardInput()
        {
            if (Keyboard.current == null) return Vector2.zero;
            Vector2 v = Vector2.zero;
            if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) v += Vector2.up;
            if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) v += Vector2.down;
            if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) v += Vector2.left;
            if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) v += Vector2.right;
            return v;
        }

        // Converts our local visual state into serializable data and sends it. 
        void SendStateToServer()
        {
            if (NetworkClient.Instance == null || NetworkClient.Instance.localPlayerState == null)
                return;
              
            NetworkClient.Instance.SendState(new SerializableVector2(currentInputDirection.x, currentInputDirection.y), isBoosting);
        } 
    }
}
