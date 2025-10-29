using System.Collections.Generic;
using UnityEngine;

namespace SnakyColors
{
    /// <summary>
    /// Generic, type-safe object pooler that can handle any prefab type.
    /// </summary>
    public class GenericPooler<T> : MonoBehaviour where T : Component
    {
        [System.Serializable]
        public class Pool
        {
            public T prefab;
            public int initialSize = 10;
        }

        [Header("Setup")]
        public List<Pool> pools;

        private Dictionary<T, Queue<T>> poolDictionary = new Dictionary<T, Queue<T>>();

        public static GenericPooler<T> Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            InitializePools();
        }

        private void InitializePools()
        {
            foreach (var pool in pools)
            {
                if (pool.prefab == null)
                {
                    Debug.LogWarning($"[GenericPooler<{typeof(T).Name}>] Missing prefab for pool entry!");
                    continue;
                }

                var objectPool = new Queue<T>();

                for (int i = 0; i < pool.initialSize; i++)
                {
                    T obj = Instantiate(pool.prefab, transform);
                    obj.gameObject.SetActive(false);
                    objectPool.Enqueue(obj);
                }

                poolDictionary.Add(pool.prefab, objectPool);
            }
        }

        /// <summary>
        /// Get an inactive object from the pool or create a new one if needed.
        /// </summary>
        public T Get(T prefab)
        {
            if (!poolDictionary.ContainsKey(prefab))
            {
                Debug.LogWarning($"[GenericPooler<{typeof(T).Name}>] No pool found for {prefab.name}, creating dynamically.");
                poolDictionary[prefab] = new Queue<T>();
            }

            if (poolDictionary[prefab].Count == 0)
            {
                T obj = Instantiate(prefab, transform);
                obj.gameObject.SetActive(false);
                poolDictionary[prefab].Enqueue(obj);
            }

            T pooledObj = poolDictionary[prefab].Dequeue();
            pooledObj.gameObject.SetActive(true);
            return pooledObj;
        }

        /// <summary>
        /// Return object to pool and deactivate it.
        /// </summary>
        public void ReturnToPool(T obj, T prefab)
        {
            obj.gameObject.SetActive(false);
            if (!poolDictionary.ContainsKey(prefab))
                poolDictionary[prefab] = new Queue<T>();

            poolDictionary[prefab].Enqueue(obj);
        }

        /// <summary>
        /// Clears all pooled objects (for scene resets).
        /// </summary>
        public void ClearAllPools()
        {
            foreach (var queue in poolDictionary.Values)
            {
                foreach (var obj in queue)
                    if (obj != null)
                        Destroy(obj.gameObject);
            }
            poolDictionary.Clear();
        }
    }
}
