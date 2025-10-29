using UnityEngine;

namespace SnakyColors
{
    public class CameraFollow : MonoBehaviour
    {
        [Header("Target Player")]
        public GameObject player;

        [Header("Vertical Offset Settings")]
        public float verticalOffset = 2.5f; // base offset above player
        public float maxTrailOffset = 2f;   // max additional offset for trail

        [Header("Follow Settings")]
        public float followSpeed = 4f;      // camera smooth follow speed
        public float dashLagSpeed = 1.5f;   // camera speed during dash
        public float dashLookAhead = 0.5f;  // extra Y offset when dashing

        [Header("Clamp Settings")]
        public float minY = -Mathf.Infinity;
        public float maxY = Mathf.Infinity;

        [HideInInspector]
        public bool isDashing = false;
        [HideInInspector]
        public float currentFollowFactor = 1f;

        private Vector3 startPosition;
        private float targetY;

        void Awake()
        {
            startPosition = transform.position;
            AdjustCameraSize();
        }

        private void AdjustCameraSize()
        {
            float screenAspectRatio = (float)Screen.width / Screen.height;
            float orthographicSize = 6f - (screenAspectRatio - 0.485f) * 11f;
            if (orthographicSize < 4f) orthographicSize = 4f;
            Camera.main.orthographicSize = orthographicSize;
        }

        public void InitializePosition()
        {
            if (player == null) return;

            float dynamicOffset = CalculateDynamicOffset();
            targetY = player.transform.position.y + dynamicOffset;
            transform.position = new Vector3(transform.position.x, targetY, -10f);
        }

        void LateUpdate()
        {
            if (player == null) return;

            float dynamicOffset = CalculateDynamicOffset();
            float targetPlayerY = player.transform.position.y + dynamicOffset;

            float finalFollowSpeed = followSpeed;

            if (isDashing)
            {
                // Dash anticipation logic (unchanged)
                targetPlayerY += dashLookAhead;
                targetY = Mathf.Lerp(targetY, targetPlayerY, Time.deltaTime * dashLagSpeed);
            }
            else
            {
                // Use the adjustable factor here
                finalFollowSpeed = followSpeed * currentFollowFactor;

                // Smooth follow
                targetY = Mathf.Lerp(targetY, targetPlayerY, Time.deltaTime * finalFollowSpeed);
            }

            // Clamp to allowed Y range
            targetY = Mathf.Clamp(targetY, minY, maxY);

            // Apply position
            transform.position = new Vector3(transform.position.x, targetY, -10f);
        } 

        public void ResetCamera()
        {
            player = null;
            targetY = startPosition.y;
            transform.position = startPosition;
        }
         
        public void EndDashTransition()
        { 
            // gradually closing the large gap to the player's new position.
            targetY = transform.position.y;
        }

        private float CalculateDynamicOffset()
        {
            if (player == null) return verticalOffset;

            float spriteHeight = 1f;
            SpriteRenderer sr = player.GetComponent<SpriteRenderer>();
            if (sr != null)
                spriteHeight = sr.bounds.size.y;

            // Include trail offset
            float trailOffset = 0f;
            TrailRenderer tr = player.GetComponentInChildren<TrailRenderer>();
            if (tr != null)
            {
                trailOffset = Mathf.Min(tr.time * tr.startWidth, maxTrailOffset);
            }

            return verticalOffset + (spriteHeight * 0.5f) + trailOffset;
        }
    }
}
