using UnityEngine;

namespace SnakyColors
{
    [RequireComponent(typeof(Collider2D))]
    public class SlitherCollisionManager : MonoBehaviour
    {
        private SegmentedCreator snake;
        private string myPlayerId; // Set by NetworkClient
        // Cooldown to avoid duplicate reports on the same food in rapid succession
        private System.Collections.Generic.Dictionary<int, float> _reportedFoodCooldown = new System.Collections.Generic.Dictionary<int, float>();
        private const float ReportCooldownSeconds = 0.25f;

        private void Awake()
        {
            snake = GetComponent<SegmentedCreator>();
        }

        public void SetPlayerId(string id)
        {
            myPlayerId = id;
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (NetworkClient.Instance == null) return;

            // --- 1. Check for Food ---
            GeneratedItem item = other.GetComponent<GeneratedItem>()
                                      ?? other.GetComponentInParent<GeneratedItem>()
                                      ?? other.GetComponentInChildren<GeneratedItem>();
            if (item != null)
            {
                if (!item.gameObject.activeInHierarchy) return;

                // De-duplicate rapid re-entries for the same item id
                if (item.Id != 0)
                {
                    float now = Time.time;
                    if (_reportedFoodCooldown.TryGetValue(item.Id, out float until) && now < until)
                        return;
                    _reportedFoodCooldown[item.Id] = now + ReportCooldownSeconds;
                }

           //     Debug.Log($"COLLISION: Food hit (id={item.Id}). Reporting to server and playing VFX.");
                // Tell the server we ate this
                NetworkClient.Instance.ReportFoodEaten(item.Id);

                // Let the item handle all VFX/SFX + pooling for slither
                item.CollectForSlither(this.transform);
                return;
            }

            // --- 2. Check for Enemy Snake Body ---
            // (This assumes other snakes have a collider on their body segments)
            if (other.CompareTag("EnemySnake")) // You'll need to tag your prefabs
            {
                // We hit another snake!
                // Unknown killer for now; avoid misattribution
                Debug.Log("COLLISION: EnemySnakeBody. Reporting death (killerId=null).");
                NetworkClient.Instance.ReportPlayerDied(null);
                HandleLocalDeath();
            }

            // --- 3. Check for Boundary ---
            if (other.CompareTag("Boundary"))
            {
                // We hit a wall.
                Debug.Log("COLLISION: Boundary. Reporting death (boundary).");
                NetworkClient.Instance.ReportPlayerDied(null); // null = hit a wall
                HandleLocalDeath();
            }
        }

        // Local collection visuals handled by GeneratedItem.CollectForSlither

        private void HandleLocalDeath()
        {
            // Disable this script and our input
            this.enabled = false;
            TryGetComponent<SlitherMovement>(out var mov);
            if( mov != null )
            {
                mov.enabled = false;
            }

            // TODO: Play local death particle effect
            // ...
        }
    }
}
