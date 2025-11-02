using UnityEngine;
using UnityEngine.InputSystem; // <-- 1. ADD THIS namespace

namespace SnakyColors
{
    public class SlitherCameraFollow : MonoBehaviour
    {
        [Header("Target")]
        [Tooltip("Assign the 'SnakePlayer' GameObject here.")]
        public Transform playerHead;

        [Header("Following")]
        [Tooltip("How long (in seconds) it takes the camera to catch up. Smaller = faster/snappier.")]
        [SerializeField] private float smoothTime = 0.3f;

        [Header("Zoom")]
        [Tooltip("The orthographic size when 'boosting' (zooms out). Set this > your camera's default size.")]
        [SerializeField] private float boostOrthoSize = 8.5f;
        [Tooltip("How long (in seconds) it takes to zoom.")]
        [SerializeField] private float zoomSmoothTime = 0.2f;

        private Camera cam;
        private Vector3 targetPosition;
        private float targetOrthoSize;
        private float defaultOrthoSize;

        private Vector3 followVelocity = Vector3.zero;
        private float zoomVelocity = 0f;

        private void Awake()
        {
            cam = GetComponent<Camera>();
            if (cam == null)
            {
                Debug.LogError("SlitherCameraFollow must be on a Camera object!");
                this.enabled = false;
                return;
            }

            cam.orthographic = true;

            // Read the size you set in the Inspector as the default
            defaultOrthoSize = cam.orthographicSize;
            targetOrthoSize = defaultOrthoSize;
        }

        private void Start()
        {
            if (playerHead != null)
            {
                transform.position = new Vector3(playerHead.position.x, playerHead.position.y, transform.position.z);
            }
        }

        private void Update()
        {
            if (playerHead == null) return;

            // --- 2. FIX: Use new Input System ---
            // Check if the keyboard exists and the key is pressed
            bool isBoosting = Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed;
            // ----------------------------------

            targetOrthoSize = isBoosting ? boostOrthoSize : defaultOrthoSize;

            // Smoothly damp the camera's orthographic size
            cam.orthographicSize = Mathf.SmoothDamp(cam.orthographicSize, targetOrthoSize, ref zoomVelocity, zoomSmoothTime);
        }

        private void LateUpdate()
        {
            if (playerHead == null) return;

            targetPosition = new Vector3(playerHead.position.x, playerHead.position.y, transform.position.z);

            // Use SmoothDamp for frame-rate independent, correct smoothing
            transform.position = Vector3.SmoothDamp(transform.position, targetPosition, ref followVelocity, smoothTime);
        }

        public void SetPlayer(Transform player)
        {
            playerHead = player;
            if (playerHead != null)
            {
                transform.position = new Vector3(playerHead.position.x, playerHead.position.y, transform.position.z);
                followVelocity = Vector3.zero;
            }
        }
    }
}