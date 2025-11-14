using System.Numerics;

namespace Khela.Game.Models.States
{
    public class FoodState
    {
        public int Id { get; set; }

        public SerializableVector2 Position { get; set; }

        public string ItemKey { get; set; }

        public float ScoreValue { get; set; }

        public ItemCategory ItemType { get; set; }

        public CollectibleType CollectibleType { get; set; }

        public PowerupType PowerupType { get; set; }

        private int _powerUpDurationInSec;
        public int PowerUpDurationInSec
        {
            get => _powerUpDurationInSec;
            set
            {
                _powerUpDurationInSec = value;

                if (value > 0 && SpawnedAtTime > 0)
                {
                    DespawnAtTime = SpawnedAtTime + (value * 1000L);
                }
            }
        }

        public int SpawnWeight { get; set; }

        public int MaxInWorld { get; set; }

        /// <summary>Absolute server time when this food should despawn.</summary>
        public long DespawnAtTime { get; set; }

        /// <summary>Absolute server time when spawned (analytics & events)</summary>
        private long _spawnedAtTime;
        public long SpawnedAtTime
        {
            get => _spawnedAtTime;
            set
            {
                _spawnedAtTime = value;

                // Recalculate if duration already exists
                if (_powerUpDurationInSec > 0)
                {
                    DespawnAtTime = value + (_powerUpDurationInSec * 1000L);
                }
            }
        }

        // Computed helpers (fast, no overhead)
        public bool IsPowerUp => ItemType == ItemCategory.PowerUp;
        public bool IsHazard => ItemType == ItemCategory.Hazard;
        public bool IsCollectible => ItemType == ItemCategory.Collectible;

        public FoodState() { }

        public FoodState(int id, Vector2 pos, string itemKey,
                         ItemCategory category = ItemCategory.Collectible,
                         float score = 1f,
                         long spawnTime = 0)
        {
            Id = id;
            Position = pos;
            ItemKey = itemKey;
            ItemType = category;
            ScoreValue = score;
            SpawnedAtTime = spawnTime != 0 ? spawnTime : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        /// <summary>
        /// Creates a shallow clone. Good for world-state diffing & tick snapshots.
        /// </summary>
        public FoodState Clone() => (FoodState)this.MemberwiseClone();
    }

    public enum ItemCategory
    {
        Collectible,
        PowerUp,
        Hazard
    }

    public enum PowerupType
    {
        Magnet,
        Rush,
        Shield,
        X2ScoreMultiplier,
        X5ScoreMultiplier
    }

    public enum CollectibleType
    {
        Basic,
        DashCharge,
        Coin
    }
}
