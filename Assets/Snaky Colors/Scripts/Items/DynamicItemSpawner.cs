using UnityEngine;
using System.Collections.Generic;

namespace SnakyColors
{
    public class DynamicItemSpawner : MonoBehaviour
    {
        [Header("Setup")]
        public ItemPooler itemPoolerPrefab;
        [Tooltip("List of ALL items this spawner can generate.")]
        public List<ItemData> allAvailableItems;

        [HideInInspector]
        public Transform player;

        [Header("Generation Settings")]
        [Tooltip("Total track width for random X placement.")]
        public float trackWidth = 5f;
        [Tooltip("Minimum distance ahead of the player to start spawning.")]
        public float spawnDistance = 10f;
        [Tooltip("How often, vertically, to attempt a spawn (e.g., 0.5f means one check every 0.5 units).")]
        public float spawnInterval = 0.5f;
        [Tooltip("A small margin to keep items off the edges.")]
        public float clipBuffer = 0.2f;

        [Header("Overlap Prevention")]
        [Tooltip("If checked, a new random item won't be spawned if it overlaps an existing active item.")]
        public bool noOverlap = false;
        [Tooltip("The minimum required distance (radius) between item centers to avoid overlap.")]
        public float minOverlapDistance = 0.8f;

        // --- NEW: Spatial Partitioning ---
        [Header("Performance Optimization")]
        [Tooltip("Cell size for the spatial hash grid. Should be >= largest 'minOverlapDistance'.")]
        public float gridCellSize = 2f; // Tune this based on your minOverlapDistance

        // This dictionary *is* the spatial hash grid.
        private Dictionary<Vector2Int, List<GameObject>> spatialGrid = new Dictionary<Vector2Int, List<GameObject>>();
        // ---------------------------------

        [Header("Dynamic Spawn Limit")]
        [Tooltip("Maximum number of active items allowed at once.")]
        public int maxActiveItems = 25; // Note: This is now less critical for performance due to O(1) grid.
        [Tooltip("When true, spawner slows or stops if max items are active.")]
        public bool limitSpawnWhenFull = true;

        // This list is still useful for tracking total count and for the reset loop.
        private List<GameObject> activeSpawnedItems = new List<GameObject>();
        private ItemPooler pooler;
        private float nextSpawnY = 0f;
        private float totalSpawnWeight;
        private HashSet<ItemData> uniqueItemsOnScreen = new HashSet<ItemData>();

        private void Awake()
        {
            // Instantiate the Pooler
            pooler = Instantiate(itemPoolerPrefab, this.transform);
            pooler.gameObject.name = "DYNAMIC_ITEM_POOLER";
            pooler.SetupPools(allAvailableItems);

            // Calculate total weight only once
            CalculateTotalWeight();
        }

        private void CalculateTotalWeight()
        {
            totalSpawnWeight = 0f;
            foreach (var item in allAvailableItems)
            {
                totalSpawnWeight += item.spawnProbability;
            }
        }

        void Update()
        {
            if (!player) return;

            while (player.position.y + spawnDistance > nextSpawnY)
            { 
                if (limitSpawnWhenFull && activeSpawnedItems.Count >= maxActiveItems)
                {
                    // If we are over limit, just advance the spawner Y position
                    // This prevents an infinite loop if the player stops moving
                    // but the spawner is "stuck" trying to spawn.
                    nextSpawnY += spawnInterval;
                    continue;
                }
                // --------------------------------------------------

                AttemptSpawn(nextSpawnY);
                nextSpawnY += spawnInterval;
            }
        }

        public void SetPlayer(Transform playerTransform)
        {
            player = playerTransform;
            if (player != null)
            {
                nextSpawnY = player ? player.position.y + spawnDistance : 0f;
                Debug.Log("[DynamicSpawner] Initial spawn Y position set based on player.");
            }
        }

        void AttemptSpawn(float spawnY)
        {
            if (!GameManager.Instance.isGameRunning)
                return;
            if (Random.value < 0.2f) return; // 20% chance to spawn nothing (tune this)

            ItemData selectedItem = GetWeightedRandomItem();
            if (selectedItem == null) return;

            if (selectedItem.isUniquePerScreen && uniqueItemsOnScreen.Contains(selectedItem))
            {
                return;
            }

            float minX = -trackWidth / 2f + clipBuffer;
            float maxX = trackWidth / 2f - clipBuffer;

            Vector2 spawnPos = Vector2.zero;
            float xPos = 0f;
            bool positionFound = false;
            const int maxAttempts = 5;

            bool doingOverlapCheck = noOverlap || selectedItem.sameItemCannotOverlap;

            if (doingOverlapCheck)
            {
                for (int i = 0; i < maxAttempts; i++)
                {
                    xPos = Random.Range(minX, maxX);
                    spawnPos = new Vector2(xPos, spawnY);

                    bool generalOverlap = false;
                    bool sameItemOverlap = false;

                    if (noOverlap)
                    {
                        // --- UPDATED: Use fast grid check ---
                        generalOverlap = IsOverlapping(spawnPos, minOverlapDistance);
                    }

                    if (selectedItem.sameItemCannotOverlap)
                    {
                        // --- UPDATED: Use fast grid check ---
                        sameItemOverlap = IsOverlappingSameItem(spawnPos, selectedItem, selectedItem.sameItemMinRadius);
                    }

                    if (!generalOverlap && !sameItemOverlap)
                    {
                        positionFound = true;
                        break;
                    }
                }
            }
            else
            {
                xPos = Random.Range(minX, maxX);
                spawnPos = new Vector2(xPos, spawnY);
                positionFound = true;
            }

            if (!positionFound && doingOverlapCheck)
            {
                //Debug.LogWarning($"[DYNAMIC SPAWNER] Failed to find a non-overlapping spot for {selectedItem.name} at Y={spawnY}. Skipping.");
                return;
            }

            GameObject obj = pooler.GetPooledObject(selectedItem);

            if (obj != null)
            {
                obj.transform.position = spawnPos;
                obj.transform.rotation = Quaternion.identity;
                obj.SetActive(true);

                // --- NEW: Add to lists *and* grid ---
                activeSpawnedItems.Add(obj);
                AddToGrid(obj);
                // ------------------------------------

                if (selectedItem.isUniquePerScreen)
                {
                    uniqueItemsOnScreen.Add(selectedItem);
                }

                var itemComponent = obj.GetComponent<GeneratedItem>();
                if (itemComponent != null)
                {
                    itemComponent.spawner = this;
                    itemComponent.SetData(selectedItem, player);
                    Debug.Log(itemComponent.name + " Generated.");
                }
                else
                {
                    Debug.LogError($"[DYNAMIC SPAWNER] Item prefab {selectedItem.itemName} is missing the GeneratedItem component!");
                }
            }
        }

        private ItemData GetWeightedRandomItem()
        {
            if (allAvailableItems.Count == 0 || totalSpawnWeight == 0) return null;

            float roll = Random.value * totalSpawnWeight;
            foreach (var item in allAvailableItems)
            {
                roll -= item.spawnProbability;
                if (roll <= 0f)
                    return item;
            }

            return allAvailableItems[0];
        }

        public void OnItemDespawned(GameObject obj, ItemData item)
        { 
            if (activeSpawnedItems.Remove(obj))
            {
                RemoveFromGrid(obj);
            }

            if (item.isUniquePerScreen)
                uniqueItemsOnScreen.Remove(item);
        }

        public void ResetSpawner()
        {
            var snapshot = new List<GameObject>(activeSpawnedItems);

            foreach (var obj in snapshot)
            {
                if (obj != null && obj.activeSelf)
                {
                    pooler.ReturnToPool(obj);
                }
            }

            // --- NEW: Clear grid ---
            activeSpawnedItems.Clear();
            spatialGrid.Clear();
            // -----------------------

            uniqueItemsOnScreen.Clear();
            nextSpawnY = player ? player.position.y + spawnDistance : 0f;
            Debug.Log("[DynamicSpawner] Reset complete. All items returned to pool.");
        }

        // --- NEW: Spatial Grid Helper Methods ---

        private Vector2Int GetGridCoords(Vector2 position)
        {
            return new Vector2Int(
                Mathf.FloorToInt(position.x / gridCellSize),
                Mathf.FloorToInt(position.y / gridCellSize)
            );
        }

        private void AddToGrid(GameObject obj)
        {
            Vector2Int gridPos = GetGridCoords(obj.transform.position);
            if (!spatialGrid.ContainsKey(gridPos))
            {
                spatialGrid[gridPos] = new List<GameObject>();
            }
            spatialGrid[gridPos].Add(obj);
        }

        private void RemoveFromGrid(GameObject obj)
        {
            Vector2Int gridPos = GetGridCoords(obj.transform.position);
            if (spatialGrid.ContainsKey(gridPos))
            {
                spatialGrid[gridPos].Remove(obj);
            }
        }

        // ---  Overlap Checks --- 
        private bool IsOverlapping(Vector2 checkPos, float minDistance)
        {
            float sqrMinDistance = minDistance * minDistance;
            Vector2Int centerGridPos = GetGridCoords(checkPos);

            // Check center cell + 8 neighboring cells
            for (int x = -1; x <= 1; x++)
            {
                for (int y = -1; y <= 1; y++)
                {
                    Vector2Int gridPos = centerGridPos + new Vector2Int(x, y);

                    if (spatialGrid.TryGetValue(gridPos, out List<GameObject> cell))
                    {
                        foreach (var activeObj in cell)
                        {
                            if (!activeObj.activeSelf) continue; // Skip inactive objects

                            float sqrDistance = (activeObj.transform.position - (Vector3)checkPos).sqrMagnitude;
                            if (sqrDistance < sqrMinDistance)
                            {
                                return true; // Overlap detected
                            }
                        }
                    }
                }
            }
            return false; // No overlap
        }

        private bool IsOverlappingSameItem(Vector2 checkPos, ItemData newItemData, float minDistance)
        {
            float sqrMinDistance = minDistance * minDistance;
            Vector2Int centerGridPos = GetGridCoords(checkPos);

            for (int x = -1; x <= 1; x++)
            {
                for (int y = -1; y <= 1; y++)
                {
                    Vector2Int gridPos = centerGridPos + new Vector2Int(x, y);

                    if (spatialGrid.TryGetValue(gridPos, out List<GameObject> cell))
                    {
                        foreach (var activeObj in cell)
                        {
                            if (!activeObj.activeSelf) continue;

                            var itemComponent = activeObj.GetComponent<GeneratedItem>();
                            if (itemComponent == null || itemComponent.data != newItemData)
                            {
                                continue; // Not the same item type
                            }

                            float sqrDistance = (activeObj.transform.position - (Vector3)checkPos).sqrMagnitude;
                            if (sqrDistance < sqrMinDistance)
                            {
                                return true; // Overlap with same item type detected
                            }
                        }
                    }
                }
            }
            return false;
        }
    }
}