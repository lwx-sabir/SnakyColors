using UnityEngine;
using System.Collections;

namespace SnakyColors
{
    public class PlayerDash : MonoBehaviour
    {
        [Header("Dash Settings")]
        public float dashTargetDistance = 10f;
        public float dashSpeedMultiplier = 10f;
         
        [Header("Dash Cost")]
        [Tooltip("Amount of charge consumed per dash.")]
        [SerializeField] private int dashCost = 15; // Example cost
        // --------------------

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

        public void Setup(PlayerMovement movement, CameraFollow camFollow)
        {
            this.playerMovement = movement;
            this.cameraFollow = camFollow;
        }

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
             
            if (PlayerStats.Instance != null && PlayerStats.Instance.TryConsumeDashCharge(dashCost))
            { 
                if (AudioManager.Instance != null)
                {
                    AudioManager.Instance.Play(SoundType.Whoosh1);
                }

                playerMovement.ApplyDashWiggle(true);
                isDashing = true;
                if (cameraFollow != null) cameraFollow.isDashing = true;

                OnDashStart?.Invoke();
                dashCoroutine = StartCoroutine(DashRoutine()); 
            }
            else
            { 
                Debug.Log("Dash failed: Not enough charge."); 
            }
        } 

        private IEnumerator DashRoutine()
        {
            float originalVelocity = playerMovement.GetVelocity();
            float dashSpeed = originalVelocity * dashSpeedMultiplier;
            float calculatedDuration = dashTargetDistance / dashSpeed;

            if (dashSpeed <= 0)
            {
                StopDash();
                yield break;
            }

            float elapsed = 0f;
            float startY = playerMovement.GetCurrentY();
            float endY = startY + dashTargetDistance;

            while (elapsed < calculatedDuration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / calculatedDuration;
                playerMovement.SetCurrentY(Mathf.Lerp(startY, endY, t));
                yield return null;
            }

            playerMovement.SetCurrentY(endY);
            StopDash();
        }

        private void StopDash()
        {
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

            float startFactor = 3.0f;
            float endFactor = 1f;
            float elapsed = 0f;

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