using System.Collections.Generic;
using UnityEngine;

namespace SnakyColors
{
    public class ProjectilePooler : MonoBehaviour
    {
        public static ProjectilePooler Instance { get; private set; }

        private Dictionary<GameObject, List<GameObject>> poolDictionary = new Dictionary<GameObject, List<GameObject>>();

        void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else
            {
                Destroy(gameObject);
            }
        }

        public GameObject Get(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            if (!poolDictionary.ContainsKey(prefab))
            {
                poolDictionary[prefab] = new List<GameObject>();
            }

            List<GameObject> pool = poolDictionary[prefab];

            // Find an inactive object in the pool
            for (int i = 0; i < pool.Count; i++)
            {
                if (pool[i] != null && !pool[i].activeInHierarchy)
                {
                    pool[i].transform.position = position;
                    pool[i].transform.rotation = rotation;
                    pool[i].SetActive(true);
                    return pool[i];
                }
            }

            // If no inactive object is found, create a new one
            GameObject obj = Instantiate(prefab, position, rotation);
            pool.Add(obj);
            return obj;
        }

        public void ReturnToPool(GameObject obj)
        {
            obj.SetActive(false);
        }
    }
}