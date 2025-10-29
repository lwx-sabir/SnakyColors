using SnakyColors;
using System.Collections.Generic;
using UnityEngine;

namespace SnakyColors
{
    public class ItemPooler : MonoBehaviour
    {
        // Dictionary to hold the pools: Key is the ItemData prefab.
        private Dictionary<GameObject, List<GameObject>> poolDictionary = new Dictionary<GameObject, List<GameObject>>();
        private Dictionary<GameObject, ItemData> prefabToData = new Dictionary<GameObject, ItemData>(); 
        public static ItemPooler Instance { get; private set; }

        void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
        }

        // Called once by the Spawner at the start of the game
        public void SetupPools(List<ItemData> allItems)
        {
            foreach (var data in allItems)
            {
                if (data.prefab == null) continue;

                // Store the data reference
                prefabToData[data.prefab] = data;

                // Create the pool list
                if (!poolDictionary.ContainsKey(data.prefab))
                {
                    poolDictionary[data.prefab] = new List<GameObject>();
                }

                // Pre-instantiate
                for (int i = 0; i < data.poolSize; i++)
                {
                    GameObject obj = Instantiate(data.prefab, this.transform);
                    obj.SetActive(false);
                    poolDictionary[data.prefab].Add(obj);
                }
            }
        }
         
        public GameObject GetPooledObject(ItemData data)
        {
            if (data.prefab == null || !poolDictionary.ContainsKey(data.prefab))
            {
                Debug.LogError($"Pool for {data.itemName} not set up.");
                return null;
            }

            List<GameObject> pool = poolDictionary[data.prefab];
            for (int i = 0; i < pool.Count; i++)
            {
                // Check if the reference itself is valid (not destroyed) ===
                if (pool[i] == null)
                {
                    // If the object was destroyed externally, remove the invalid reference 
                    // and continue the search.
                    pool.RemoveAt(i);
                    i--; // Decrement index since we removed an item
                    continue;
                }

                // Now it's safe to check the object's properties
                if (!pool[i].activeInHierarchy)
                {
                    return pool[i];
                }
            }

            // If pool is exhausted (or cleaned up during the loop), create a new object
            GameObject newObj = Instantiate(data.prefab, this.transform);
            newObj.SetActive(false);
            pool.Add(newObj);
            return newObj;
        }

        // Used by the item itself when it goes off screen
        public void ReturnToPool(GameObject obj)
        {
            obj.SetActive(false); 
        }

        public void ClearAllPools()
        {
            foreach (var pool in poolDictionary.Values)
            {
                foreach (var obj in pool)
                {
                    if (obj != null)
                        Destroy(obj);
                }
                pool.Clear();
            }
            poolDictionary.Clear();
            prefabToData.Clear();
        }

    }
}