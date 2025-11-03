using UnityEngine;

namespace SnakyColors
{
    [RequireComponent(typeof(Collider2D))] // The snake's head collider
    public class SlitherCollisionManager : MonoBehaviour
    {
        private SegmentedCreator snake; // Reference to our own snake

        private void Awake()
        {
            snake = GetComponent<SegmentedCreator>();
            if (snake == null)
            {
                Debug.LogError("SlitherCollisionManager must be on the same GameObject as SegmentedCreator!", this);
            }
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            // --- 1. Check for Food ---
            if (other.TryGetComponent<GeneratedItem>(out var item))
            {
                // In an authoritative server model, the client *should not*
                // tell PlayerStats to add score. It should just play
                // the "eat" animation and let the server handle the rest.

                // We'll let NetworkClient handle the "eat" animation
                // when it receives the "OnFoodEaten" event from the server.

                // For now, we can play a local sound immediately.
                if (item.data != null && item.data.collectSound != null)
                {
                    AudioManager.Instance?.PlayClip(item.data.collectSound, Random.Range(0.9f, 1.1f));
                }

                // We *could* destroy the food locally for instant feedback,
                // but it's better to let the server tell us it was eaten.

                // So, for now, this script does almost nothing for food.
                // The server is handling the collision.
                return;
            }

            // --- 2. Check for Enemy Snake Body ---
            // TODO: This logic needs to be implemented
            // if (other.TryGetComponent<EnemySnakeBody>(out var bodyPart))
            // {
            //     // We hit another snake!
            //     // In a server-auth model, we do nothing. We wait for
            //     // the server to send us the "OnPlayerDied" message.
            //     
            //     // For client-side prediction, we could *predict* our death:
            //     // HandleLocalDeath(); 
            // }

            // --- 3. Check for Boundary ---
            // if(other.CompareTag("Boundary"))
            // {
            //     // We hit a wall.
            //     // HandleLocalDeath();
            // }
        }

        // private void HandleLocalDeath()
        // {
        //     // Play death effect, disable input, etc.
        //     Debug.Log("Local Player Died (Prediction)");
        //     this.enabled = false;
        //     GetComponent<SlitherMovement>().enabled = false;
        // }
    }
}