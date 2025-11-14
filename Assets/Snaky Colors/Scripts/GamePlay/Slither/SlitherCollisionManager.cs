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

           //     Debug.Log($"COLLISION: Food hit (id={item.Id}). Playing VFX + pooling (server authoritative).");
                // Play VFX/SFX locally and return to pool; server will validate and broadcast.
                item.CollectForSlither(this.transform);
                return;
            }

            // --- 2. Player collisions and boundaries are authoritative on server --- 
            // We intentionally do nothing here for EnemySnake or Boundary collisions.
            if (other.CompareTag("EnemySnake") || other.CompareTag("Boundary"))
            {
                return;
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
