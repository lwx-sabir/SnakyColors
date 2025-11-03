using SnakyColors;
using System.Collections.Generic;
using UnityEngine;


namespace SnakyColors
{
    public class ItemSpawner : MonoBehaviour, IItemSpawner
    {
        [Header("Setup")] 
        public List<ItemData> allAvailableItems;           // All individual items to pool
        public List<SpawnPatternData> spawnPatterns;       // The patterns to choose from
        public Transform player;

        [Header("Level Settings")]
        public float spawnDistance = 20f;         
        public float trackWidth = 5f;
         
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

            int currentActiveLanes = selectedPattern.activeLanes;
             
            if (currentActiveLanes <= 1)
            {
                currentActiveLanes = 1;
            }
             
            float laneSpacing = trackWidth / (currentActiveLanes > 1 ? currentActiveLanes - 1 : 1f);
            float minX = -trackWidth / 2f; // Absolute left edge of the track
            float maxX = trackWidth / 2f;  // Absolute right edge of the track

            // Safety margin to prevent items from rendering partially off-screen.
            const float clipBuffer = 0.2f; // <-- Hardcoded buffer (adjust if necessary)

            foreach (var entry in selectedPattern.entries)
            {
                if (entry.laneIndex >= currentActiveLanes)
                {
                    continue;
                }
                 
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

                if (xPos < minX + clipBuffer || xPos > maxX - clipBuffer)
                {
                    continue;
                }

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

                Vector2 spawnPos = new Vector2(xPos, yPos);

                GameObject obj = ItemPooler.Instance.GetPooledObject(entry.item);

                if (obj != null)
                {
                    obj.transform.position = spawnPos;
                    obj.transform.rotation = Quaternion.identity;
                    obj.SetActive(true);
                    activeSpawnedItems.Add(obj);

                    var itemComponent = obj.GetComponent<GeneratedItem>();
                    if (itemComponent != null)
                    {
                        itemComponent.spawner = this;
                        itemComponent.SetData(entry.item, player);
                    } 
                }
                else
                {
                }
            }

            nextSpawnY += selectedPattern.verticalHeight;
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

        public void OnItemDespawned(GameObject obj, ItemData item)
        {
            activeSpawnedItems.Remove(obj);
        }

        // ... (inside ItemSpawner.cs) ...

        public void ApplyConfig(PatternSpawnerConfig config)
        {
            // 1. Apply settings
            this.spawnDistance = config.spawnDistance;
            this.trackWidth = config.trackWidth;
            this.spawnPatterns = config.spawnPatterns; // Re-link the list 

            // 3. Reset spawner's state
            ResetSpawner();

            Debug.Log($"ItemSpawner configured for level: {config.name}");
        }
    }
}
