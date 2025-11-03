using UnityEngine;
using System.Collections.Generic;

namespace SnakyColors
{
    public class ItemPooler : MonoBehaviour
    {
        public static ItemPooler Instance { get; private set; }

        [Header("Master Pool Configuration")]
        [Tooltip("Assign ALL ItemData assets that can *ever* be pooled, from spawners or enemy drops.")]
        [SerializeField] private List<ItemData> allPoolableItems;

        private Dictionary<GameObject, List<GameObject>> poolDictionary;
        private Dictionary<GameObject, ItemData> prefabToData; // Optional: for reverse lookup if needed

        void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject); // Make it persistent across scene loads

                // Initialize the pools immediately
                InitializePools();
            }
            else
            {
                Destroy(gameObject); // Destroy duplicates
            }
        }

        /// <summary>
        /// Initializes all pools based on the 'allPoolableItems' list.
        /// </summary>
        private void InitializePools()
        {
            poolDictionary = new Dictionary<GameObject, List<GameObject>>();
            prefabToData = new Dictionary<GameObject, ItemData>();

            if (allPoolableItems == null) return;

            foreach (var data in allPoolableItems)
            {
                if (data == null || data.prefab == null)
                {
                    Debug.LogWarning("[ItemPooler] Null ItemData or prefab in 'allPoolableItems' list. Skipping.");
                    continue;
                }

                // Skip if this prefab has already been added
                if (poolDictionary.ContainsKey(data.prefab)) continue;

                // Store the data reference
                prefabToData[data.prefab] = data;

                // Create the pool list
                List<GameObject> newPool = new List<GameObject>();
                poolDictionary[data.prefab] = newPool;

                // Pre-instantiate
                for (int i = 0; i < data.poolSize; i++)
                {
                    GameObject obj = Instantiate(data.prefab, this.transform);
                    obj.SetActive(false);
                    newPool.Add(obj);
                }
            }
            Debug.Log($"[ItemPooler] Master pool initialized with {poolDictionary.Count} item types.");
        }

        /// <summary>
        /// Gets an inactive GameObject from the pool for the specified ItemData.
        /// </summary>
        public GameObject GetPooledObject(ItemData data)
        {
            if (data == null || data.prefab == null)
            {
                Debug.LogError("[ItemPooler] Tried to get pooled object with null data or prefab!");
                return null;
            }

            // Check if a pool for this prefab exists
            if (!poolDictionary.TryGetValue(data.prefab, out List<GameObject> pool))
            {
                // This item was not in the 'allPoolableItems' list.
                // We must create a new pool for it on-the-fly.
                Debug.LogWarning($"[ItemPooler] Pool for {data.itemName} not found! Creating new pool on-the-fly. Add it to 'allPoolableItems' to pre-warm.", data);

                pool = new List<GameObject>();
                poolDictionary[data.prefab] = pool;

                // Also add to reverse lookup
                if (!prefabToData.ContainsKey(data.prefab))
                {
                    prefabToData[data.prefab] = data;
                }
            }

            // Find an inactive object in the pool
            for (int i = 0; i < pool.Count; i++)
            {
                if (pool[i] == null)
                {
                    // Object was destroyed externally, clean up the bad reference
                    pool.RemoveAt(i);
                    i--;
                    continue;
                }

                if (!pool[i].activeInHierarchy)
                {
                    return pool[i]; // Found one!
                }
            }

            // Pool is exhausted, create a new object
            Debug.LogWarning($"[ItemPooler] Pool for {data.itemName} exhausted. Creating new instance.");
            GameObject newObj = Instantiate(data.prefab, this.transform);
            // OnEnable in GeneratedItem will handle its state reset
            newObj.SetActive(false);
            pool.Add(newObj);
            return newObj;
        }

        /// <summary>
        /// Deactivates a pooled object, returning it to the pool.
        /// </summary>
        public void ReturnToPool(GameObject obj)
        {
            obj.SetActive(false);
            // OnEnable() in GeneratedItem will handle resetting its state
        }

        /// <summary>
        /// Deactivates all active objects in all pools.
        /// Call this when restarting a level to clean up.
        /// </summary>
        public void ResetAllPools()
        {
            foreach (var pool in poolDictionary.Values)
            {
                foreach (var obj in pool)
                {
                    if (obj != null && obj.activeSelf)
                    {
                        obj.SetActive(false);
                    }
                }
            }
        }

        /// <summary>
        /// Destroys all pooled objects. Called when changing game modes
        /// or shutting down.
        /// </summary>
        public void ClearAllPools()
        {
            foreach (var pool in poolDictionary.Values)
            {
                foreach (var obj in pool)
                {
                    if (obj != null) Destroy(obj);
                }
            }
            poolDictionary.Clear();
            prefabToData.Clear();
        }
    }
}