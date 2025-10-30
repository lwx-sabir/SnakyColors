using UnityEngine;

namespace SnakyColors
{
    public class CameraFollow : MonoBehaviour
    {
        [Header("Target Player")]
        public GameObject player;

        [Header("Vertical Offset Settings")]
        public float verticalOffset = 3.2f; // base offset above player
        public float verticalOffsetOverride = 3.5f; // base offset above player
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
            Camera cam = Camera.main;
            if (cam == null) return;

            float aspect = (float)Screen.width / Screen.height;

            float baseAspect = 1080f / 1920f; // your design aspect (width/height)
            float baseSize = 6.7f;              // vertical size for base aspect 

            float newSize = baseSize * (baseAspect / aspect); // scale proportionally
            newSize = Mathf.Clamp(newSize, 4.7f, 8f);          // min/max clamp

            cam.orthographicSize = newSize;
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

            // Calculate dynamic vertical offset (sprite + trail)
            float dynamicOffset = CalculateDynamicOffset();

            // Base target Y with vertical offset
            float targetPlayerY = player.transform.position.y + dynamicOffset;

            if (isDashing)
            {
                // Dash anticipation: add extra offset
                float dashTargetY = targetPlayerY + dashLookAhead;

                // Smoothly follow with dash lag speed
                targetY = Mathf.Lerp(targetY, dashTargetY, Time.deltaTime * dashLagSpeed);
            }
            else
            {
                // Normal smooth follow with adjustable factor
                targetY = Mathf.Lerp(targetY, targetPlayerY, Time.deltaTime * followSpeed * currentFollowFactor);
            }

            // Clamp camera within min/max Y
            targetY = Mathf.Clamp(targetY, minY, maxY);

            // Apply camera position
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
            targetY = transform.position.y;
            this.verticalOffset = this.verticalOffsetOverride;
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
