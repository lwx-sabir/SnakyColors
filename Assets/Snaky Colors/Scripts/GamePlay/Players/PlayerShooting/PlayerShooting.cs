using UnityEngine;
using System.Collections;
using System.Collections.Generic; // Needed for List

namespace SnakyColors
{
    public class PlayerShooting : MonoBehaviour
    {
        [Header("Configuration")]
        public Transform firePoint;
        public WeaponData basicWeapon;

        // --- MODIFIED: Renamed Auto-Aim to Target Detection ---
        [Header("Target Detection")]
        [Tooltip("Maximum vertical distance to check for targets.")]
        public float detectionRange = 10f; // Height of the yellow box
        [Tooltip("Horizontal width of the detection area in front of the player.")]
        public float detectionWidth = 3f; // Width of the yellow box
        [Tooltip("Set this to your 'Obstacle' or 'Enemy' layer.")]
        public LayerMask targetMask;
        // --- REMOVED autoAimRange ---

        private WeaponData currentWeapon;
        private float lastFireTime;
        private Transform currentTarget;
        private bool isManualFire = false;
        private Coroutine weaponRevertCoroutine;

        // --- NEW: For drawing Gizmo ---
        private Vector2 boxCenter;
        private Vector2 boxSize;
        // -----------------------------

        void Awake()
        {
            currentWeapon = basicWeapon;
        }

        void Update()
        {
            // Reset target each frame before checking again
            currentTarget = null;

            // 1. Find a target if auto-firing is enabled for the weapon
            if (currentWeapon.autoFire)
            {
                FindTargetInBox(); // Changed method name
            }

            // 2. Check if we should fire
            if (CanShoot() && ShouldFire())
            {
                Fire();
            }
        }

        bool CanShoot()
        {
            return Time.time > lastFireTime + currentWeapon.fireRate;
        }

        bool ShouldFire()
        {
            if (isManualFire) return true;
            // Fire if auto-fire weapon and a target *was found* in the box
            return currentWeapon.autoFire && currentTarget != null;
        }

        /// <summary>
        /// Finds the closest valid "Enemy" target within a forward-facing box.
        /// </summary>
        void FindTargetInBox()
        {
            // Calculate the box parameters relative to the firePoint
            boxSize = new Vector2(detectionWidth, detectionRange);
            // Center the box slightly ahead of the fire point along its forward direction (up)
            boxCenter = (Vector2)firePoint.position + (Vector2.up * (detectionRange / 2.0f));

            // Use OverlapBoxAll to find all colliders within the defined box area
            Collider2D[] hits = Physics2D.OverlapBoxAll(boxCenter, boxSize, 0f, targetMask); // Angle is 0 for world-aligned box

            // currentTarget = null; // Already reset at start of Update
            float closestDistSqr = float.MaxValue; // Use squared distance for performance

            foreach (var hit in hits)
            {
                // Basic check: Is it tagged as "Enemy"?
                if (!hit.CompareTag("Enemy"))
                {
                    continue; // Skip if not an enemy
                }

                // Optional check: Ensure target is truly ahead if box check isn't enough
                // (OverlapBox might catch things slightly behind if player rotates fast)
                if (hit.transform.position.y < firePoint.position.y)
                {
                    continue; // Skip targets slightly behind the fire point
                }


                // Calculate squared distance for comparison (faster than Vector2.Distance)
                float distSqr = ((Vector2)hit.transform.position - (Vector2)firePoint.position).sqrMagnitude;

                if (distSqr < closestDistSqr)
                {
                    closestDistSqr = distSqr;
                    currentTarget = hit.transform;
                }
            }
        }

        void Fire()
        {
            lastFireTime = Time.time;

            GameObject bullet = ProjectilePooler.Instance.Get(
                currentWeapon.projectilePrefab,
                firePoint.position,
                firePoint.rotation // Use firePoint rotation in case head tilts
            );

            Vector2 fireDirection;
            // Aim directly at the target IF one was found within the box
            if (currentTarget != null)
            {
                fireDirection = (currentTarget.position - firePoint.position).normalized;
            }
            else // Otherwise, fire straight ahead from the fire point
            {
                fireDirection = firePoint.up; // Use firePoint's up direction
            }

            Projectile projectile = bullet.GetComponent<Projectile>();
            if (projectile != null)
            {
                // Pass speed and damage from the *current* weapon
                projectile.Fire(fireDirection, currentWeapon.projectileSpeed, currentWeapon.damage);
            }

            // --- Effects ---
            if (currentWeapon.muzzleEffect)
            {
                Instantiate(currentWeapon.muzzleEffect, firePoint.position, firePoint.rotation);
            }
            if (currentWeapon.fireSound && AudioManager.Instance != null)
            {
                AudioManager.Instance.PlayClip(currentWeapon.fireSound, 0.8f);
            }
        }

        // --- Draw the detection box in the editor ---
        void OnDrawGizmosSelected()
        {
            if (firePoint == null) return;

            Gizmos.color = Color.yellow;
            // Recalculate box parameters for gizmo drawing (mirrors FindTargetInBox)
            Vector2 size = new Vector2(detectionWidth, detectionRange);
            Vector2 center = (Vector2)firePoint.position + (Vector2.up * (detectionRange / 2.0f));

            // DrawWireCube takes center and FULL size
            Gizmos.DrawWireCube(center, size);
        }
        // -------------------------------------------

        public void SetManualFire(bool isFiring)
        {
            isManualFire = isFiring;
        }

        public void EquipWeapon(WeaponData newWeapon, float duration)
        {
            if (weaponRevertCoroutine != null) StopCoroutine(weaponRevertCoroutine);
            currentWeapon = newWeapon;
            weaponRevertCoroutine = StartCoroutine(RevertWeaponRoutine(duration));
        }

        private IEnumerator RevertWeaponRoutine(float duration)
        {
            yield return new WaitForSeconds(duration);
            currentWeapon = basicWeapon;
            weaponRevertCoroutine = null;
        }
    }
}