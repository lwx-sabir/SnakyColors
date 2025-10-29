using SnakyColors;
using System.Collections.Generic;
using UnityEngine;


namespace SnakyColors
{
    public class ItemSpawner : MonoBehaviour
    {
        [Header("Setup")]
        public ItemPooler itemPoolerPrefab;
        public List<ItemData> allAvailableItems;           // All individual items to pool
        public List<SpawnPatternData> spawnPatterns;       // The patterns to choose from
        public Transform player;

        [Header("Level Settings")]
        public float spawnDistance = 20f;         
        public float trackWidth = 5f;

        private ItemPooler pooler;
        private float nextSpawnY = 0f;
        private List<GameObject> activeSpawnedItems = new List<GameObject>(); // For quick cleanup

        public static ItemSpawner Instance { get; private set; }

        private void Awake()
        {
            // Standard Singleton Pattern
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);

                // Instantiate the Pooler and set it up
                pooler = Instantiate(itemPoolerPrefab, this.transform);
                pooler.gameObject.name = "ITEM_POOLER_MANAGER";
                pooler.SetupPools(allAvailableItems);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        public void SetPlayer(Transform playerTransform)
        {
            player = playerTransform;
        }

        void Update()
        {
            if (!player) return;

            // Spawn loop that ensures constant distance ahead of player
            while (player.position.y + spawnDistance > nextSpawnY)
            {
                SpawnPattern(nextSpawnY);
                // The vertical advance is determined by the pattern's height, not a fixed interval
            }
        }
         
        void SpawnPattern(float startY)
        {
            SpawnPatternData selectedPattern = GetWeightedRandomPattern();
            if (selectedPattern == null) return;

         //   Debug.Log($"[SPAWNER] Selected Pattern: {selectedPattern.name}. Spawning at Y={startY}");

            int currentActiveLanes = selectedPattern.activeLanes;

            // --- CALCULATE LANE SPACING ---
            if (currentActiveLanes <= 1)
            {
                currentActiveLanes = 1;
            }

            // Calculate lane spacing based on the dynamic lane count
            float laneSpacing = trackWidth / (currentActiveLanes > 1 ? currentActiveLanes - 1 : 1f);
            float minX = -trackWidth / 2f; // Absolute left edge of the track
            float maxX = trackWidth / 2f;  // Absolute right edge of the track

            // Safety margin to prevent items from rendering partially off-screen.
            const float clipBuffer = 0.2f; // <-- Hardcoded buffer (adjust if necessary)

            foreach (var entry in selectedPattern.entries)
            {
                if (entry.laneIndex >= currentActiveLanes)
                {
             //       Debug.LogWarning($"Pattern {selectedPattern.name} has an item entry with Lane Index {entry.laneIndex} but only defines {currentActiveLanes} active lanes. Skipping entry.");
                    continue;
                }

                // --- 1. CALCULATE BASE X POSITION & JITTER ---
                float xPos = 0f;
                if (currentActiveLanes > 1)
                {
                    // Calculate the base X position based on the lane index
                    xPos = minX + (entry.laneIndex * laneSpacing);
                }

                if (selectedPattern.enableDynamicXOffset)
                {
                    // Apply unique X jitter per item
                    float itemJitterX = Random.Range(
                        -selectedPattern.maxDynamicXOffset,
                         selectedPattern.maxDynamicXOffset
                    );
                    xPos += itemJitterX;
                }

                // --- OFF-SCREEN CLIPPING CHECK ---
                // If the final jittered X position is outside the track bounds (with buffer), skip spawning.
                if (xPos < minX + clipBuffer || xPos > maxX - clipBuffer)
                {
                //    Debug.Log($"[SPAWNER CLIP] Skipping item {entry.item.itemName}. X position ({xPos:F2}) is outside track bounds.");
                    continue;
                }
                // ---------------------------------

                // --- 2. CALCULATE Y POSITION & JITTER ---
                float yPos = startY + entry.yOffset; // Base Y position from pattern design

                if (selectedPattern.enableDynamicYOffset)
                {
                    // Apply unique Y jitter per item
                    float itemJitterY = Random.Range(
                        -selectedPattern.maxDynamicYOffset,
                         selectedPattern.maxDynamicYOffset
                    );
                    yPos += itemJitterY;
                }

                // --- 3. COMBINE X AND Y TO GET FINAL SPAWN POSITION ---
                Vector2 spawnPos = new Vector2(xPos, yPos);

                // --- 4. GET OBJECT AND ASSIGN POSITION ---
                GameObject obj = pooler.GetPooledObject(entry.item);

                if (obj != null)
                {
                    obj.transform.position = spawnPos;
                    obj.transform.rotation = Quaternion.identity;
                    obj.SetActive(true);
                    activeSpawnedItems.Add(obj);

              //      Debug.Log($"[SPAWNER] Successfully spawned {entry.item.itemName} at ({spawnPos.x:F2}, {spawnPos.y:F2})");

                    var itemComponent = obj.GetComponent<GeneratedItem>();
                    if (itemComponent != null)
                    {
                      //  itemComponent.spawner = this;
                        itemComponent.SetData(entry.item, player);
                    } 
                }
                else
                {
               //     Debug.LogError($"[SPAWNER] FAILED to retrieve object for: {entry.item.itemName}. Check allAvailableItems.");
                }
            }

            nextSpawnY += selectedPattern.verticalHeight;
           // Debug.Log($"[SPAWNER] Next Spawn Y is now: {nextSpawnY}");
        }

        private SpawnPatternData GetWeightedRandomPattern()
        {
            float totalWeight = 0f;
            foreach (var pattern in spawnPatterns) totalWeight += pattern.patternProbability;

            float randomValue = Random.Range(0f, totalWeight);

            foreach (var pattern in spawnPatterns)
            {
                if (randomValue <= pattern.patternProbability)
                {
                    return pattern;
                }
                randomValue -= pattern.patternProbability;
            }
            return spawnPatterns[0]; // Fallback
        }

        public void ResetSpawner()
        {
            // Efficiently return all active items to the pool
            foreach (var obj in activeSpawnedItems)
            {
                obj.SetActive(false);
                // Optionally call pooler.ReturnToPool(obj) if advanced tracking is needed
            }
            activeSpawnedItems.Clear();

            nextSpawnY = 0f;
            // Note: StopAllCoroutines is no longer necessary as we use Update() loop
        }
    }
}
