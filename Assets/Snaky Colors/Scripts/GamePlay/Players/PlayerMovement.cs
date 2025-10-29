using UnityEngine;
using System.Collections; // Required for Coroutines

namespace SnakyColors
{
    public class PlayerMovement : MonoBehaviour
    {
        [SerializeField]
        private Rigidbody2D rb;
        private float velocity = 2f;
        private Camera cam;
        private Vector2 point;
        private Vector2 startPoint;

        [Header("Movement Settings")]
        public float baseSpeed = 0.5f;
        public bool autoIncreaseAcceleration = false;
        public float autoAccelerationRate = 0.02f;
        public float steeringSpeed = 10f;
        public float rotationSpeed = 10f;
        public float horizontalBounds = 2.8f;

        [Header("Idle Wiggle")]
        public float wiggleAmplitude = 0.3f;
        public float wiggleFrequency = 4f;
        public float wiggleFrequencyDashMultiplier = 3f;
        public float wiggleAmplitudeDashMultiplier = 1.5f;
        private float wiggleStartOffset = 0f;
        private bool isWiggling = false;
        private float lastWiggleFrequency; // Kept for dash
        private float lastwiggleAmplitude; // Kept for dash

        private float currentFrameSpeedMagnitude = 0f;
        private float lastSteerX = 0f;

        private const float SteeringThreshold = 0.009f;
        const float MIN_STEERING_SPEED = 5f;
        private bool gameStarted = false;
        private float currentY;

        // --- All Dash Fields Removed ---

        // --- Input fields are now private for tap detection ---
        private float touchStartTime;
        private Vector2 touchStartPosition;
        [Header("Input Filtering")]
        private const float MaxTapDuration = 0.15f;
        private const float MaxTapMovement = 0.1f;

        private CameraFollow cameraFollow;
        private PlayerDash playerDash; // --- Reference to new dash script ---

        // --- All Dash Events Removed ---

        void Start()
        {
            rb = GetComponent<Rigidbody2D>();
            cam = Camera.main;
            currentY = transform.position.y;
            velocity = baseSpeed;
            point = Vector2.zero;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            Application.targetFrameRate = 60;
            lastSteerX = rb.position.x;
            lastWiggleFrequency = wiggleFrequency;

            // --- Get reference to PlayerDash ---
            playerDash = GetComponent<PlayerDash>();

            if (cam != null)
            {
                cameraFollow = cam.GetComponent<CameraFollow>();
            }

            // --- Setup the dash component ---
            if (playerDash != null)
            {
                playerDash.Setup(this, cameraFollow);
            }
            else
            {
                Debug.LogError("PlayerDash component is missing from the player prefab!");
            }
        }

        private void Update()
        {
            if (!gameStarted || InputManager.Instance == null)
                return;

            bool isSteering = false;
            InputManager input = InputManager.Instance;

            if (!input.IsInputOverUI)
            {
                if (input.IsInputDown)
                {
                    HandleInputDown(input.ScreenPosition);
                }
                else if (input.IsInputHeld)
                {
                    HandleInputHeld(input.ScreenPosition);
                    isSteering = true;
                }
                else if (input.IsInputUp)
                {
                    HandleInputUp(input.ScreenPosition);
                }
            }

            if (isSteering)
            {
                isWiggling = false;
            }
            else
            {
                if (!isWiggling)
                {
                    isWiggling = true;
                    wiggleStartOffset = Random.Range(0f, 10f);
                }
            }
        }

        private void HandleInputDown(Vector2 screenPos)
        {
            startPoint = cam.ScreenToWorldPoint(screenPos);
            startPoint.x = startPoint.x - rb.position.x;
            touchStartTime = Time.time;
            touchStartPosition = screenPos;
        }

        private void HandleInputHeld(Vector2 screenPos)
        {
            point = cam.ScreenToWorldPoint(screenPos);
            point.x = point.x - startPoint.x;
            point.x = Mathf.Clamp(point.x, -horizontalBounds, horizontalBounds);
            lastSteerX = point.x;
        }

        private void HandleInputUp(Vector2 screenPos)
        {
            // --- Check if dash is already active ---
            if (playerDash.IsDashing()) return;

            float touchDuration = Time.time - touchStartTime;
            float touchMovementDistance = Vector2.Distance(touchStartPosition, screenPos);

            if (touchDuration <= MaxTapDuration &&
                touchMovementDistance <= MaxTapMovement)
            {
                // --- Tell the dash script to try starting ---
                playerDash.TryStartDash();
            }
        }

        void FixedUpdate()
        {
            if (!gameStarted) return;

            float dt = Time.fixedDeltaTime;
            float currentX = rb.position.x;
            float targetX = point.x;

            if (isWiggling)
            {
                float wiggleMovement = Mathf.Sin(Time.time * wiggleFrequency + wiggleStartOffset) * wiggleAmplitude;
                targetX = lastSteerX + wiggleMovement;
                targetX = Mathf.Clamp(targetX, -horizontalBounds, horizontalBounds);
            }

            float newX = Mathf.Lerp(currentX, targetX, steeringSpeed * dt);
            float horizontalDifference = newX - currentX;
            Vector2 movementVector = new Vector2(horizontalDifference, velocity * dt);
            currentFrameSpeedMagnitude = movementVector.magnitude / dt;

            Quaternion targetRotation;
            if (Mathf.Abs(horizontalDifference) > SteeringThreshold)
            {
                float targetAngle = Mathf.Atan2(movementVector.x, movementVector.y) * Mathf.Rad2Deg;
                targetAngle = -targetAngle;
                targetRotation = Quaternion.Euler(0, 0, targetAngle);
            }
            else
            {
                targetRotation = Quaternion.identity;
            }
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * dt);

            // --- Check if dash is active ---
            if (playerDash == null || !playerDash.IsDashing())
            {
                currentY += velocity * dt;
                if (autoIncreaseAcceleration)
                {
                    velocity += autoAccelerationRate * dt;
                }
            }

            rb.MovePosition(new Vector2(newX, currentY));
        }

        // --- ALL DASH METHODS REMOVED ---

        // --- Helper Methods for PlayerDash ---

        /// <summary>
        /// Called by PlayerDash to modify wiggle values.
        /// </summary>
        public void ApplyDashWiggle(bool isStartingDash)
        {
            if (isStartingDash)
            {
                lastWiggleFrequency = wiggleFrequency;
                lastwiggleAmplitude = wiggleAmplitude;
                wiggleFrequency *= wiggleFrequencyDashMultiplier;
                wiggleAmplitude *= wiggleAmplitudeDashMultiplier;
            }
            else
            {
                wiggleFrequency = lastWiggleFrequency;
                wiggleAmplitude = lastwiggleAmplitude;
            }
        }

        public float GetCurrentY()
        {
            return currentY;
        }

        public void SetCurrentY(float newY)
        {
            currentY = newY;
        }

        // --- Original Public Methods ---
        public void StartMovement() { gameStarted = true; }
        public float GetVelocity() { return velocity; }
        public void SetPlayerY(float newY) { currentY = newY; rb.position = new Vector2(rb.position.x, currentY); }
        public Rigidbody2D Rb => rb;
         
        public float GetBaseVelocity() { return velocity; }
        public float GetInstantaneousSpeedMagnitude()
        {
            return Mathf.Max(currentFrameSpeedMagnitude, velocity);
        }

        // --- This method is now on PlayerDash ---
        // public float DashSpeedMultiplier => dashSpeedMultiplier;
    }
}