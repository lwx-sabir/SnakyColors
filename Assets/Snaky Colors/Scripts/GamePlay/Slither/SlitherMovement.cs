using UnityEngine;
using UnityEngine.InputSystem;

namespace SnakyColors
{
    public class SlitherMovement : MonoBehaviour
    {
        private Camera mainCamera;
        private SegmentedCreator localSnake;
        private Transform snakeHead;
        private Transform snakeTarget; // The target this snake's logic will follow

        [Header("Movement Settings")]
        [Tooltip("How far the input can pull the target to the side.")]
        [SerializeField] private float steerSensitivity = 3f;
        [Tooltip("How far ahead of the snake the target stays.")]
        [SerializeField] private float targetDistance = 5f;

        private Vector3 targetDirection = Vector3.up; // Default forward
        private Vector3 currentVelocity = Vector3.zero; // For SmoothDamp

        void Start()
        {
            mainCamera = Camera.main;
            localSnake = GetComponent<SegmentedCreator>();

            if (localSnake == null)
            {
                Debug.LogError("SlitherMovement script placed on object without SegmentedCreator!");
                this.enabled = false;
                return;
            }

            // Get the references set by NetworkClient
            snakeHead = this.transform; // The head is the root transform
            snakeTarget = localSnake.moveToTarget.Target; // The target we must move

            if (snakeTarget == null)
            {
                Debug.LogError("SlitherMovement: The snake's Target was not set on spawn!", this);
            }
        }

        void Update()
        {
            if (InputManager.Instance == null || NetworkClient.Instance == null || localSnake == null || snakeTarget == null) return;

            InputManager input = InputManager.Instance;
            bool isInputHeld = (input.IsInputDown || input.IsInputHeld);

            // Get the snake's current forward direction (from its segments)
            Vector3 headDir = (localSnake.RibPositions.Count > 1)
                              ? (localSnake.RibPositions[^1] - localSnake.RibPositions[^2]).normalized
                              : snakeHead.up;
            if (headDir == Vector3.zero) headDir = Vector3.up; // Failsafe


            if (isInputHeld && !input.IsInputOverUI)
            {
                // --- Player is Steering ---
                Vector3 worldPoint = mainCamera.ScreenToWorldPoint(input.ScreenPosition);

                // Get direction from snake head to the input point
                Vector3 dirToMouse = (worldPoint - snakeHead.position).normalized;

                // This is the direction the player *wants* to go
                targetDirection = dirToMouse;
            }
            else
            {
                // --- Auto-Forward ---
                // No input, so the target direction is just the snake's *current* direction
                targetDirection = headDir;
            }

            // --- Update Target Position ---
            // Calculate the desired position of the target
            Vector3 targetPos = snakeHead.position + targetDirection * targetDistance;

            // Smoothly move the *actual target* towards the desired position
            snakeTarget.position = Vector3.SmoothDamp(snakeTarget.position, targetPos, ref currentVelocity, 0.05f);

            // --- Send the *aiming direction* to the server ---
            // (We send the raw mouse position, the server will calculate direction)
            NetworkClient.Instance.SendTarget(new Vector2(snakeTarget.position.x, snakeTarget.position.y));

            // --- Handle Boost ---
            bool isBoosting = Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed;
            NetworkClient.Instance.SendBoost(isBoosting);

            // --- CLIENT-SIDE PREDICTION (Boost) ---
            // Set our local snake's speed immediately
            if (PlayerStats.Instance != null && PlayerStats.Instance.GetActiveConfig() != null)
            {
                var config = PlayerStats.Instance.GetActiveConfig();
                localSnake.moveToTarget.movingSpeed = isBoosting ? config.dashSpeed : config.baseSpeed;
            }
        }
    }
}