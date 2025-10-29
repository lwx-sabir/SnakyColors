using UnityEngine;

namespace SnakyColors
{
    [CreateAssetMenu(fileName = "NewItemData", menuName = "Game Data/Item Data", order = 1)]
    public class ItemData : ScriptableObject
    {
        [Header("General Info")]
        [Tooltip("Display name of the item.")]
        public string itemName;

        [Tooltip("Prefab that represents this item in the game world.")]
        public GameObject prefab;

        [Tooltip("Primary category of this item for basic logic handling (collision, scoring).")]
        public ItemCategory category;

        [Tooltip("The specific powerup effect this item grants, if it's a PowerUp type.")]
        public PowerupType powerupEffect = PowerupType.None;

        [Tooltip("Primary value (e.g., fruit meter fill, health restored, ammo added, damage dealt).")]
        public float value = 1f;

        [Tooltip("The text to display for score popups (+ value).")]
        public string scoreText = "1";

        [Tooltip("Optional secondary value (e.g., slow amount for poison, shield strength).")]
        public float secondaryValue = 0f;

        [Tooltip("Duration for timed effects like PowerUps (in seconds).")]
        public float duration = 5f; // Sensible default for powerups

        [Header("Visual & Audio Feedback")]
        [Tooltip("Icon or sprite used for UI or pickups.")]
        public Sprite icon;

        [Tooltip("Optional sound to play when collected or triggered.")]
        public AudioClip collectSound;

        [Tooltip("Item color tint (used for glow, highlights, or color mode).")]
        public Color itemColor = Color.white;

        [Header("Spawn & Pool Settings")]
        [Range(0f, 1f)]
        [Tooltip("Spawn likelihood relative to other items.")]
        public float spawnProbability = 1f;

        [Tooltip("If checked, only one instance of this item type can be active on-screen at once.")]
        public bool isUniquePerScreen = false;

        [Tooltip("Initial number of objects to create in the object pool.")]
        [Min(1)] public int poolSize = 10;

        [Header("Overlap Rules")]
        [Tooltip("If true, prevent this item type from spawning too close to another instance of the *same* item type.")]
        public bool sameItemCannotOverlap = false;

        [Tooltip("Minimum distance required from another item of the *same* type if sameItemCannotOverlap is true.")]
        public float sameItemMinRadius = 0.5f;

        [Header("Behavior Flags")]
        [Tooltip("If true, item automatically returns to the pool after its 'duration' expires (even if not collected).")]
        public bool autoDespawnAfterDuration = false; // Renamed for clarity

        [Tooltip("If true, item can be pulled towards the player by magnet effects.")]
        public bool isAttractable = true;

        [Tooltip("If true, collecting this item can contribute to or break score combos.")]
        public bool comboEligible = true; // Default to true for collectibles

        
        [Header("Magnet Behavior")] 
        [Tooltip("How fast the item moves towards the player when magnet is active.")]
        [SerializeField] public float magnetPullSpeed = 15f; 
        [Tooltip("How far forward the magnet effect reaches (triangle height).")]
        [SerializeField] public float magnetRange = 5f; 
        [Tooltip("How wide the magnet effect is at its farthest point (triangle base).")]
        [SerializeField] public float magnetBaseWidth = 6f;
    }

    // Renamed from ItemType for better distinction from PowerupType
    public enum ItemCategory
    {
        Collectible,   // Adds score/meter (e.g., Fruit)
        Ammo,          // Adds ammo (e.g., Poison vial)
        PowerUp,       // Grants a temporary effect (Magnet, Rush, Shield, Weapon Change)
        Hazard,        // Deals damage or negative effect on collision (e.g., Obstacle, Bomb)
        Health,     // Could be a Collectible with specific logic or its own category
    }

    // Specific Powerup Effects
    public enum PowerupType
    {
        None,          // Not a powerup or no specific effect
        Magnet,        // Attracts nearby Collectibles/Ammo
        Rush,          // Temporary speed boost
        Shield,        // Temporary invulnerability
        WeaponUpgrade  // Temporarily equips a different WeaponData (handled slightly differently) 
    }
}