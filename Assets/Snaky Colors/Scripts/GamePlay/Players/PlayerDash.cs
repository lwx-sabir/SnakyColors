using UnityEngine;
using System.Collections;

namespace SnakyColors
{
    public class PlayerDash : MonoBehaviour
    {
        [Header("Dash Settings")]
        public float dashTargetDistance = 10f;
        public float dashSpeedMultiplier = 10f;

        // Events
        public event System.Action OnDashStart;
        public event System.Action OnDashEnd;

        // State
        private bool isDashing = false;
        private Coroutine dashCoroutine;
        private Coroutine followResetCoroutine;

        // Component References
        private PlayerMovement playerMovement;
        private CameraFollow cameraFollow;

        /// <summary>
        /// Called by PlayerMovement to give this script the references it needs.
        /// </summary>
        public void Setup(PlayerMovement movement, CameraFollow camFollow)
        {
            this.playerMovement = movement;
            this.cameraFollow = camFollow;
        }

        /// <summary>
        /// Public accessor for other scripts to check the dash state.
        /// </summary>
        public bool IsDashing()
        {
            return isDashing;
        }

        /// <summary>
        /// Called by PlayerMovement when a "tap" is detected.
        /// </summary>
        public void TryStartDash()
        {
            if (isDashing || dashCoroutine != null || playerMovement == null) return;

            // Use the AudioManager Singleton
            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.Play(SoundType.Whoosh1);
            }

            // Command PlayerMovement to change its wiggle state
            playerMovement.ApplyDashWiggle(true);

            isDashing = true;
            if (cameraFollow != null) cameraFollow.isDashing = true;

            OnDashStart?.Invoke();
            dashCoroutine = StartCoroutine(DashRoutine());
        }

        private IEnumerator DashRoutine()
        {
            // Get data from PlayerMovement
            float originalVelocity = playerMovement.GetVelocity();
            float dashSpeed = originalVelocity * dashSpeedMultiplier;
            float calculatedDuration = dashTargetDistance / dashSpeed;

            if (dashSpeed <= 0)
            {
                Debug.LogWarning("Cannot dash with zero speed multiplier!");
                StopDash();
                yield break;
            }

            float elapsed = 0f;
            float startY = playerMovement.GetCurrentY(); // Read Y from PlayerMovement
            float endY = startY + dashTargetDistance;

            while (elapsed < calculatedDuration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / calculatedDuration;

                // Write the new Y value back to PlayerMovement
                playerMovement.SetCurrentY(Mathf.Lerp(startY, endY, t));

                yield return null;
            }

            playerMovement.SetCurrentY(endY);
            StopDash();
        }

        private void StopDash()
        {
            // Command PlayerMovement to restore its wiggle state
            playerMovement.ApplyDashWiggle(false);

            if (dashCoroutine != null)
            {
                StopCoroutine(dashCoroutine);
                dashCoroutine = null;
            }

            OnDashEnd?.Invoke();

            if (cameraFollow != null)
            {
                cameraFollow.EndDashTransition();
                if (followResetCoroutine != null) StopCoroutine(followResetCoroutine);
                followResetCoroutine = StartCoroutine(SmoothFollowResetRoutine(0.5f));
            }

            isDashing = false;
        }

        private IEnumerator SmoothFollowResetRoutine(float duration)
        {
            if (cameraFollow == null) yield break;

            cameraFollow.currentFollowFactor = 0.5f;
            float elapsed = 0f;
            float startFactor = cameraFollow.currentFollowFactor;
            float endFactor = 1f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                cameraFollow.currentFollowFactor = Mathf.Lerp(startFactor, endFactor, t);
                yield return null;
            }

            cameraFollow.currentFollowFactor = 1f;
            followResetCoroutine = null;
        }
    }
}