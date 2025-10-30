using UnityEngine;

namespace SnakyColors
{
    [System.Serializable]
    public struct DropItemData
    {
        [Tooltip("The ItemData asset representing the item that can drop.")]
        public ItemData item;

        [Tooltip("Relative chance/weight for this item to drop compared to others in the same table.")]
        [Min(0f)]
        public float dropWeight; // Higher weight = more likely

        [Tooltip("Minimum quantity to drop.")]
        [Min(1)]
        public int minDropCount;

        [Tooltip("Max quantity to drop.")]
        [Min(1)]
        public int maxDropCount;

        [Tooltip("Spawn limit in second")]
        [Min(0)]
        public float limitInSecond;
    }
}