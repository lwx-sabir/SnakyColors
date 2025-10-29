using UnityEngine;


namespace SnakyColors
{
    [ExecuteAlways] // Works in Edit mode and Play mode
    public class PlayerDebugVisualizer : MonoBehaviour
    {
        public Color headColor = Color.green;
        public Color trailColor = Color.cyan;
        public Color colliderColor = Color.red;

        private SpriteRenderer sr;
        private TrailRenderer tr;
        private CircleCollider2D col;

        void Awake()
        {
            sr = GetComponent<SpriteRenderer>();
            tr = transform.Find("TrailEffect")?.GetComponent<TrailRenderer>();
            col = GetComponent<CircleCollider2D>();
        }

        void OnDrawGizmos()
        {
            if (sr != null)
            {
                // Head sprite bounds
                Bounds bounds = sr.bounds;
                Gizmos.color = headColor;
                Gizmos.DrawWireCube(bounds.center, bounds.size);
            }

            if (tr != null)
            {
                // Trail start and end width
                Vector3 startPos = transform.position;
                Vector3 endPos = transform.position + Vector3.down * 2f; // arbitrary length for visualization
                Gizmos.color = trailColor;
                Gizmos.DrawLine(startPos, startPos + Vector3.right * tr.startWidth);
                Gizmos.DrawLine(endPos, endPos + Vector3.right * tr.endWidth);
            }

            if (col != null)
            {
                // Collider radius
                Gizmos.color = colliderColor;
                Gizmos.DrawWireSphere(transform.position + (Vector3)col.offset, col.radius * transform.localScale.x);
            }
        }
    }
}


