using UnityEngine;

namespace SnakyColors
{
    [RequireComponent(typeof(Rigidbody2D))]
    public class Projectile : MonoBehaviour
    {
        private Rigidbody2D rb;
        private float damage;
        private bool hasHit = false;

        void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
        }

        private void OnEnable()
        {
            hasHit = false; // Reset hit flag when reused from pool
        }

        public void Fire(Vector2 direction, float speed, float dmg)
        {
            this.damage = dmg;
            transform.up = direction;
            rb.linearVelocity = direction * speed;
            hasHit = false; // Ensure reset before firing
        }

        void OnTriggerEnter2D(Collider2D other)
        {
            // Ignore if already hit something this flight, or if pooler is missing
            if (hasHit || ProjectilePooler.Instance == null) return;

            // --- Check if the hit object is an Enemy ---
            Enemy enemy = other.GetComponent<Enemy>(); // Try to get the Enemy component
            if (enemy != null)
            {
                Debug.Log($"Projectile hit Enemy: {other.name}", gameObject); // Log hit
                hasHit = true; // Register the hit

                // Call the enemy's TakeDamage method
                enemy.TakeDamage(this.damage);

                // Return projectile to pool immediately after hitting an enemy
                ProjectilePooler.Instance.ReturnToPool(gameObject);
                return; // Stop further processing for this collision
            } 
             
            //else if (other.CompareTag("Bounds")) // Example tag for boundaries
            //{
            //    Debug.Log($"Projectile hit Bounds: {other.name}", gameObject);
            //    hasHit = true;
            //    ProjectilePooler.Instance.ReturnToPool(gameObject);
            //    return;
            //}
        }

        private void OnBecameInvisible()
        { 
            if (gameObject.activeSelf && ProjectilePooler.Instance != null)
            {
                ProjectilePooler.Instance.ReturnToPool(gameObject);
            }
        }
    }
}