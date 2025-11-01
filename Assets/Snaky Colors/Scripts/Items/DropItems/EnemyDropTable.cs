using System.Collections.Generic;
using UnityEngine;

namespace SnakyColors
{
    [CreateAssetMenu(fileName = "NewEnemyDropTable", menuName = "Game Data/Enemy Drop Table")]
    public class EnemyDropTable : ScriptableObject
    {
        [Header("Overall Drop Chance")]
        [Range(0f, 1f)]
        [Tooltip("The overall probability (0 to 1) that this enemy will drop *any* item upon death.")]
        public float overallDropChance = 0.5f;

        [Header("Category Drop Chances")]
        [Range(0f, 1f)]
        [Tooltip("Chance to attempt a singleton drop (if eligible). Flexible items get the remaining probability.")]
        public float singletonDropProbability = 0.4f;

        [Header("Possible Drops")]
        [Tooltip("List of items that *could* drop if the overall chance succeeds, and their relative weights.")]
        public List<DropItemData> potentialDrops;

        // --- Runtime cooldown tracking ---
        private static Dictionary<ItemData, float> cooldownTracker = new Dictionary<ItemData, float>();

        public List<ItemData> GetDrops(int maxTotalDrops = 10)
        {
            List<ItemData> drops = new List<ItemData>();
            if (potentialDrops == null || potentialDrops.Count == 0)
                return drops;

            // Roll overall drop chance
            if (UnityEngine.Random.value > overallDropChance)
                return drops;

            float currentTime = Time.time;

            // Separate singletons and flexible items
            List<DropItemData> singletonItems = new List<DropItemData>();
            List<DropItemData> flexibleItems = new List<DropItemData>();
            float singletonTotalWeight = 0f;
            float flexibleTotalWeight = 0f;

            foreach (var drop in potentialDrops)
            {
                if (drop.item == null) continue;

                bool onCooldown = drop.limitInSecond > 0 &&
                                  cooldownTracker.TryGetValue(drop.item, out float nextTime) &&
                                  currentTime < nextTime;

                if (onCooldown) continue;

                if (drop.minDropCount == 1 && drop.maxDropCount == 1)
                {
                    singletonItems.Add(drop);
                    singletonTotalWeight += drop.dropWeight;
                }
                else if (drop.maxDropCount > 1)
                {
                    flexibleItems.Add(drop);
                    flexibleTotalWeight += drop.dropWeight;
                }
            }

            // --- Decide category ---
            float categoryRoll = Random.value;

            if (categoryRoll <= singletonDropProbability && singletonItems.Count > 0)
            {
                // Pick one singleton
                float roll = Random.Range(0f, singletonTotalWeight);
                float cumulative = 0f;
                foreach (var drop in singletonItems)
                {
                    cumulative += drop.dropWeight;
                    if (roll <= cumulative)
                    { 
                        drops.Add(drop.item);
                        if (drop.limitInSecond > 0)
                            cooldownTracker[drop.item] = currentTime + drop.limitInSecond;
                        return drops; // Only one singleton allowed
                    }
                }
            }
            else if (flexibleItems.Count > 0)
            {
                // Pick flexible items
                foreach (var drop in flexibleItems)
                {
                    float chance = drop.dropWeight / flexibleTotalWeight;
                    if (Random.value <= chance)
                    {
                        int count = Random.Range(drop.minDropCount, drop.maxDropCount + 1);
                        int spaceLeft = maxTotalDrops - drops.Count;
                        if (spaceLeft <= 0) break;
                        count = Mathf.Min(count, spaceLeft);

                        for (int i = 0; i < count; i++)
                            drops.Add(drop.item);

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
