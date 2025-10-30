using System.Collections.Generic;
using UnityEngine;
using System;

namespace SnakyColors
{
    [CreateAssetMenu(fileName = "NewEnemyDropTable", menuName = "Game Data/Enemy Drop Table")]
    public class EnemyDropTable : ScriptableObject
    {
        [Header("Overall Drop Chance")]
        [Range(0f, 1f)]
        [Tooltip("The overall probability (0 to 1) that this enemy will drop *any* item upon death.")]
        public float overallDropChance = 0.5f;

        [Header("Possible Drops")]
        [Tooltip("List of items that *could* drop if the overall chance succeeds, and their relative weights.")]
        public List<DropItemData> potentialDrops;

        // --- Runtime cooldown tracking ---
        private static Dictionary<ItemData, float> cooldownTracker = new Dictionary<ItemData, float>();

        /// <summary>
        /// Returns a list of items to drop based on overall chance, weights, and cooldown limits.
        /// Supports singleton and flexible drops.
        /// </summary>
        public List<ItemData> GetDrops(int maxTotalDrops = 10)
        {
            List<ItemData> drops = new List<ItemData>();
            if (potentialDrops == null || potentialDrops.Count == 0)
                return drops;

            // Roll overall drop chance
            if (UnityEngine.Random.value > overallDropChance)
                return drops;

            float currentTime = Time.time;

            // --- Handle Singleton Items ---
            List<DropItemData> singletonItems = new List<DropItemData>();
            float singletonTotalWeight = 0f;

            foreach (var drop in potentialDrops)
            {
                if (drop.item == null) continue;

                if (drop.minDropCount == 1 && drop.maxDropCount == 1)
                {
                    // Cooldown check
                    if (drop.limitInSecond > 0 &&
                        cooldownTracker.TryGetValue(drop.item, out float nextAllowedTime) &&
                        currentTime < nextAllowedTime)
                        continue;

                    singletonItems.Add(drop);
                    singletonTotalWeight += drop.dropWeight;
                }
            }

            if (singletonItems.Count > 0)
            {
                if (singletonTotalWeight < 1f) singletonTotalWeight = 1f;
                float roll = UnityEngine.Random.Range(0f, singletonTotalWeight);
                float cumulative = 0f;

                foreach (var drop in singletonItems)
                {
                    cumulative += drop.dropWeight;
                    if (roll <= cumulative)
                    {
                        drops.Add(drop.item);

                        // Apply cooldown
                        if (drop.limitInSecond > 0)
                            cooldownTracker[drop.item] = currentTime + drop.limitInSecond;

                        return drops;
                    }
                }
            }

            // --- Handle Flexible Items ---
            List<DropItemData> flexibleItems = new List<DropItemData>();
            float flexibleTotalWeight = 0f;

            foreach (var drop in potentialDrops)
            {
                if (drop.item == null) continue;

                if (drop.maxDropCount > 1)
                {
                    // Cooldown check
                    if (drop.limitInSecond > 0 &&
                        cooldownTracker.TryGetValue(drop.item, out float nextAllowedTime) &&
                        currentTime < nextAllowedTime)
                        continue;

                    flexibleItems.Add(drop);
                    flexibleTotalWeight += drop.dropWeight;
                }
            }

            if (flexibleItems.Count > 0)
            {
                foreach (var drop in flexibleItems)
                {
                    float chance = drop.dropWeight / flexibleTotalWeight;
                    if (UnityEngine.Random.value <= chance)
                    {
                        int count = UnityEngine.Random.Range(drop.minDropCount, drop.maxDropCount + 1);

                        int spaceLeft = maxTotalDrops - drops.Count;
                        if (spaceLeft <= 0) break;

                        count = Mathf.Min(count, spaceLeft);

                        for (int i = 0; i < count; i++)
                            drops.Add(drop.item);

                        // Apply cooldown
                        if (drop.limitInSecond > 0)
                            cooldownTracker[drop.item] = currentTime + drop.limitInSecond;

                        if (drops.Count >= maxTotalDrops)
                            break;
                    }
                }
            }

            return drops;
        }

        public static void ResetCooldowns()
        {
            cooldownTracker?.Clear(); 
        }
    }
}
