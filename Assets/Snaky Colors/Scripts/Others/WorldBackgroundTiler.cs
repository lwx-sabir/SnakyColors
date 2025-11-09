using UnityEngine;

namespace SnakyColors
{
    // Attach this to the background GameObject that has a SpriteRenderer.
    // Set the SpriteRenderer to Draw Mode = Tiled. The script resizes it
    // to match the server-provided world size (via NetworkClient).
    [RequireComponent(typeof(SpriteRenderer))]
    public class WorldBackgroundTiler : MonoBehaviour
    {
        [Tooltip("If true, uses NetworkClient.CurrentWorldSize. If false, uses fallbackWorldSize.")]
        public bool followServerSize = true;

        [Tooltip("Extra visual padding beyond the world size (units).")]
        public float extraPadding = 0f;

        [Tooltip("Used before join/when server size is not available.")]
        public float fallbackWorldSize = 150f;

        private SpriteRenderer sr;
        private float lastAppliedSize = -1f;

        void Awake()
        {
            sr = GetComponent<SpriteRenderer>();
            if (sr != null)
            {
                sr.drawMode = SpriteDrawMode.Tiled;
            }

            // Ensure centered and unscaled for consistent sizing
            transform.position = Vector3.zero;
            transform.localScale = Vector3.one;
        }

        void LateUpdate()
        {
            float worldSize = fallbackWorldSize;

            if (followServerSize && NetworkClient.Instance != null)
            {
                float ws = NetworkClient.Instance.CurrentWorldSize;
                if (ws > 0f) worldSize = ws;
            }

            worldSize += extraPadding;

            if (sr != null && !Mathf.Approximately(lastAppliedSize, worldSize))
            {
                // In Tiled mode, SpriteRenderer repeats the sprite to fill this size
                sr.size = new Vector2(worldSize, worldSize);
                lastAppliedSize = worldSize;
            }
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            if (sr == null) sr = GetComponent<SpriteRenderer>();
            if (sr != null) sr.drawMode = SpriteDrawMode.Tiled;
        }
#endif
    }
}

