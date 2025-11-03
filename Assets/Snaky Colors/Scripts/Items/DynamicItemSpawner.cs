using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace SnakyColors
{
    [System.Serializable]
    public class AvailableItemEntry
    {
        public ItemData itemData;

        [Range(0f, 10f)]
        [Tooltip("Relative spawn chance for this item within this spawner.")]
        public float spawnProbability = 1f;

        [Tooltip("If checked, only one instance of this item type can be active on-screen at once.")]
        public bool isUniquePerScreen = false;
    }

    public class DynamicItemSpawner : MonoBehaviour, IItemSpawner
    {
        [Header("Setup")] 
        [Tooltip("List of ALL items this spawner can generate.")]
        public List<AvailableItemEntry> allAvailableItems;

        [HideInInspector] public Transform player;

        [Header("Generation Settings")]
        public float trackWidth = 5f;
        public float spawnDistance = 10f;
        public float spawnInterval = 0.5f;
        public float clipBuffer = 0.2f;

        [Header("Overlap Prevention")]
        public bool noOverlap = false;
        public float minOverlapDistance = 0.8f;

        [Header("Performance Optimization")]
        public float gridCellSize = 2f;

        [Header("Dynamic Spawn Limit")]
        public int maxActiveItems = 25;
        public bool limitSpawnWhenFull = true;

        private List<GameObject> activeSpawnedItems = new List<GameObject>(); 
        private float nextSpawnY = 0f;
        private float totalSpawnWeight;
        private HashSet<ItemData> uniqueItemsOnScreen = new HashSet<ItemData>();
        private Dictionary<Vector2Int, List<GameObject>> spatialGrid = new Dictionary<Vector2Int, List<GameObject>>();

        public event System.Action<GameObject, ItemData> OnItemSpawned;

        private Coroutine spawnLoopRoutine;

        private void Awake()
        {  
            CalculateTotalWeight();
        }

        private void OnEnable()
        {
            if (spawnLoopRoutine == null)
                spawnLoopRoutine = StartCoroutine(SpawnLoop());
        }

        private void OnDisable()
        {
            if (spawnLoopRoutine != null)
            {
                StopCoroutine(spawnLoopRoutine);
                spawnLoopRoutine = null;
            }
        }

        private void CalculateTotalWeight()
        {
            totalSpawnWeight = 0f;
            foreach (var entry in allAvailableItems)
                totalSpawnWeight += entry.spawnProbability;
        }

        public void SetPlayer(Transform playerTransform)
        {
            player = playerTransform;
            nextSpawnY = player ? player.position.y + spawnDistance : 0f;
        }

        private IEnumerator SpawnLoop()
        {
            WaitForSeconds wait = new WaitForSeconds(spawnInterval);

            while (true)
            {
                yield return wait;
                if (player == null) continue;

                while (player.position.y + spawnDistance > nextSpawnY)
                {
                    if (limitSpawnWhenFull && activeSpawnedItems.Count >= maxActiveItems)
                    {
                        nextSpawnY += spawnInterval;
                        continue;
                    }

                    AttemptSpawn(nextSpawnY);
                    nextSpawnY += spawnInterval;
                }
            }
        }

        private void AttemptSpawn(float spawnY)
        {
            if (!GameManager.Instance.isGameRunning) return;
            if (Random.value < 0.2f) return;

            AvailableItemEntry selectedEntry = GetWeightedRandomEntry();
            if (selectedEntry == null || selectedEntry.itemData == null) return;

            ItemData selectedItem = selectedEntry.itemData;

            if (selectedEntry.isUniquePerScreen && uniqueItemsOnScreen.Contains(selectedItem)) return;

            float minX = -trackWidth / 2f + clipBuffer;
            float maxX = trackWidth / 2f - clipBuffer;

            Vector2 spawnPos = Vector2.zero;
            bool positionFound = false;
            const int maxAttempts = 5;
            bool doingOverlapCheck = noOverlap || selectedItem.sameItemCannotOverlap;

            for (int i = 0; i < (doingOverlapCheck ? maxAttempts : 1); i++)
            {
                float xPos = Random.Range(minX, maxX);
                spawnPos = new Vector2(xPos, spawnY);

                bool generalOverlap = noOverlap && IsOverlapping(spawnPos, minOverlapDistance);
                bool sameItemOverlap = selectedItem.sameItemCannotOverlap && IsOverlappingSameItem(spawnPos, selectedItem, selectedItem.sameItemMinRadius);

                if (!generalOverlap && !sameItemOverlap)
                {
                    positionFound = true;
                    break;
                }
            }

            if (!positionFound && doingOverlapCheck) return;

            GameObject obj = ItemPooler.Instance.GetPooledObject(selectedItem);
            if (obj == null) return;

            obj.transform.position = spawnPos;

            if (selectedItem.category == ItemCategory.Collectible)
            {
                Quaternion baseRot = obj.transform.rotation;
                float zRotation = Random.value < 0.7f
                    ? Random.Range(-90f, 90f)
                    : Random.Range(0f, 360f);
                obj.transform.rotation = baseRot * Quaternion.Euler(0f, 0f, zRotation);
            }

            obj.SetActive(true);
            activeSpawnedItems.Add(obj);
            AddToGrid(obj);

            if (selectedEntry.isUniquePerScreen)
                uniqueItemsOnScreen.Add(selectedItem);

            if (obj.TryGetComponent<GeneratedItem>(out var itemComponent))
            {
                itemComponent.spawner = this;
                itemComponent.SetData(selectedItem, player);
            }
            else
            {
                Debug.LogError($"[DYNAMIC SPAWNER] Item prefab {selectedItem.itemName} missing GeneratedItem component!");
            }

            OnItemSpawned?.Invoke(obj, selectedItem);
        }

        private AvailableItemEntry GetWeightedRandomEntry()
        {
            if (allAvailableItems.Count == 0 || totalSpawnWeight <= 0f) return null;

            float roll = Random.value * totalSpawnWeight;
            foreach (var entry in allAvailableItems)
            {
                roll -= entry.spawnProbability;
                if (roll <= 0f)
                    return entry;
            }

            return allAvailableItems[0];
        }

        public void OnItemDespawned(GameObject obj, ItemData item)
        {
            if (activeSpawnedItems.Remove(obj))
                RemoveFromGrid(obj); 

            foreach (var entry in allAvailableItems)
            {
                if (entry.itemData == item && entry.isUniquePerScreen)
                {
                    uniqueItemsOnScreen.Remove(item);
                    break;
                }
            }
        }

        public void ResetSpawner()
        {
            var snapshot = new List<GameObject>(activeSpawnedItems);
            foreach (var obj in snapshot)
                if (obj != null && obj.activeSelf)
                    ItemPooler.Instance.ReturnToPool(obj);

            activeSpawnedItems.Clear();
            spatialGrid.Clear();
            uniqueItemsOnScreen.Clear();
            nextSpawnY = player ? player.position.y + spawnDistance : 0f;
        }

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
                spatialGrid[gridPos] = new List<GameObject>();
            spatialGrid[gridPos].Add(obj);
        }

        private void RemoveFromGrid(GameObject obj)
        {
            Vector2Int gridPos = GetGridCoords(obj.transform.position);
            if (spatialGrid.ContainsKey(gridPos))
                spatialGrid[gridPos].Remove(obj);
        }

        private bool IsOverlapping(Vector2 checkPos, float minDistance)
        {
            float sqrMinDistance = minDistance * minDistance;
            Vector2Int centerGridPos = GetGridCoords(checkPos);

            for (int x = -1; x <= 1; x++)
            {
                for (int y = -1; y <= 1; y++)
                {
                    Vector2Int gridPos = centerGridPos + new Vector2Int(x, y);
                    if (!spatialGrid.TryGetValue(gridPos, out var cell)) continue;

                    foreach (var activeObj in cell)
                    {
                        if (!activeObj.activeSelf) continue;
                        float sqrDistance = (activeObj.transform.position - (Vector3)checkPos).sqrMagnitude;
                        if (sqrDistance < sqrMinDistance) return true;
                    }
                }
            }
            return false;
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
                    if (!spatialGrid.TryGetValue(gridPos, out var cell)) continue;

                    foreach (var activeObj in cell)
                    {
                        if (!activeObj.activeSelf) continue;

                        var itemComponent = activeObj.GetComponent<GeneratedItem>();
                        if (itemComponent == null || itemComponent.data != newItemData) continue;

                        float sqrDistance = (activeObj.transform.position - (Vector3)checkPos).sqrMagnitude;
                        if (sqrDistance < sqrMinDistance) return true;
                    }
                }
            }
            return false;
        }

        // ... (inside DynamicItemSpawner.cs) ...

        public void ApplyConfig(DynamicSpawnerConfig config)
        {
            // 1. Apply all settings from the config
            this.trackWidth = config.trackWidth;
            this.spawnDistance = config.spawnDistance;
            this.spawnInterval = config.spawnInterval;
            this.clipBuffer = config.clipBuffer;
            this.noOverlap = config.noOverlap;
            this.minOverlapDistance = config.minOverlapDistance;
            this.gridCellSize = config.gridCellSize;
            this.maxActiveItems = config.maxActiveItems;
            this.limitSpawnWhenFull = config.limitSpawnWhenFull;
            this.allAvailableItems = config.allAvailableItems; // Re-link the list
             
            CalculateTotalWeight(); 
            // 3. Reset spawner's state
            ResetSpawner(); // This will clear active items and reset nextSpawnY

            Debug.Log($"DynamicItemSpawner configured for level: {config.name}");
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (player == null) return;
            Gizmos.color = Color.cyan;
            Vector3 left = transform.position + Vector3.left * (trackWidth / 2f - clipBuffer);
            Vector3 right = transform.position + Vector3.right * (trackWidth / 2f - clipBuffer);
            Gizmos.DrawLine(left + Vector3.up * nextSpawnY, right + Vector3.up * nextSpawnY);

            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(new Vector3(-trackWidth / 2f, nextSpawnY, 0), new Vector3(trackWidth / 2f, nextSpawnY, 0));
        }
#endif
    }
}
