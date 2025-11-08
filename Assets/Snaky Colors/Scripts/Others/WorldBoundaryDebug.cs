using UnityEngine;

namespace SnakyColors
{
    public class WorldBoundaryDebug : MonoBehaviour
    {
        [Tooltip("The color of the boundary gizmo.")]
        [SerializeField] private Color gizmoColor = new Color(1f, 0f, 0f, 0.5f); // Red

        private void OnDrawGizmos()
        {
            if (NetworkClient.Instance == null)
            {
                // Try to get a default size if the game isn't running
                Gizmos.color = gizmoColor;
                Gizmos.DrawWireCube(Vector3.zero, new Vector3(150f, 150f, 0f)); // Default test size
                return;
            }

            // Get the live world size from the NetworkClient
            float worldSize = NetworkClient.Instance.CurrentWorldSize;

            if (worldSize <= 0) return;

            Gizmos.color = gizmoColor;

            // Draw a large wire cube from (0,0,0) out to the world size
            Vector3 boundarySize = new Vector3(worldSize, worldSize, 0f);
            Gizmos.DrawWireCube(Vector3.zero, boundarySize);
        }
    }
}