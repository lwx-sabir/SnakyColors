using UnityEngine;

namespace SnakyColors
{
    [CreateAssetMenu(fileName = "PlayerStatConfig", menuName = "SnakyColors/Player Stat Config", order = 0)]
    public class PlayerStatConfig : ScriptableObject
    {
        [Header("Starting Values")]
        public int startingAmmo = 40;
        public float startingHealth = 100f;
        public float startingDashCharge = 100f;

        [Header("Maximum Values")]
        public float maxFruitMeter = 1000f;
        public int maxAmmo = 50;
        public float maxHealth = 100f;
        public float maxDashCharge = 100f;

        [Header("Ammo Regeneration")]
        [Tooltip("How often ammo regenerates (seconds)")]
        public float ammoGenerationInterval = 2f;
        public int ammoRegenRate = 1;

        [Header("Health Regeneration")]
        [Tooltip("Enable automatic health regeneration")]
        public bool enableHealthRegen = false;
        public float healthRegenRate = 2f;
        public float healthRegenInterval = 1f;

        [Header("Dash Regeneration")]
        [Tooltip("Enable automatic dash charge regeneration")]
        public bool enableDashRegen = true;
        public float dashRegenRate = 5f;
        public float dashRegenInterval = 0.5f;

        [Header("Movement Settings")]
        public float baseSpeed = 2f;
        public bool autoIncreaseAcceleration = false;
        public float autoAccelerationRate = 0.02f;
        public float steeringSpeed = 15f;
        public float rotationSpeed = 10f;
        public float horizontalBounds = 3.4f;
        public float dashSpeed = 6f;
    }
}
