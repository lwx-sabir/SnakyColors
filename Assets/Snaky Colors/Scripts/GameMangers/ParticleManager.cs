using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SnakyColors
{ 

    [System.Serializable]
    public class ParticlePool
    {
        public ParticleType key;
        public GameObject prefab;
        public int preloadCount = 5;
    }

    public class ParticleManager : MonoBehaviour
    {
        public static ParticleManager Instance { get; private set; }

        [Header("Particle Pools")]
        public List<ParticlePool> particlePrefabs;

        // Using a Dictionary to map ParticleType to its object pool (Queue)
        private Dictionary<ParticleType, Queue<ParticleSystem>> pools = new Dictionary<ParticleType, Queue<ParticleSystem>>();

        private void Awake()
        {
            // --- Singleton Setup ---
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            InitializePools();
        }

        private void InitializePools()
        {
            // Preload all particle pools
            foreach (var poolData in particlePrefabs)
            {
                Queue<ParticleSystem> queue = new Queue<ParticleSystem>();

                // Create an empty GameObject to hold the pooled items neatly in the hierarchy
                GameObject parentGo = new GameObject($"Pool: {poolData.key}");
                parentGo.transform.SetParent(transform);

                for (int i = 0; i < poolData.preloadCount; i++)
                {
                    var ps = InstantiateParticle(poolData.prefab, parentGo.transform);
                    queue.Enqueue(ps);
                }

                pools.Add(poolData.key, queue);
            }
        }

        private ParticleSystem InstantiateParticle(GameObject prefab, Transform parent)
        {
            var go = Instantiate(prefab, parent);
            go.SetActive(false);

            // Note: We assume the ParticleSystem is on the root of the prefab.
            var ps = go.GetComponent<ParticleSystem>();
            if (ps == null)
            {
                Debug.LogError($"Prefab '{prefab.name}' is missing a ParticleSystem component!");
            }
            return ps;
        }

        /// <summary>
        /// Plays a particle effect from the pool at a given position.
        /// </summary>
        /// <param name="key">The type of particle to play.</param>
        /// <param name="position">The world position to spawn the particles at.</param>
        /// <param name="colorOverride">Optional color to override the particle's Start Color.</param>
        public void Play(ParticleType key, Vector3 position, Color? colorOverride = null)
        {
            if (!pools.ContainsKey(key))
            {
                Debug.LogWarning($"Particle key '{key}' not found in pools!");
                return;
            }

            var queue = pools[key];
            ParticleSystem ps;

            if (queue.Count == 0)
            {
                // Pool exhausted: find the original prefab and instantiate a new one
                var prefab = particlePrefabs.Find(p => p.key == key).prefab;
                ps = InstantiateParticle(prefab, transform.Find($"Pool: {key}"));
                Debug.LogWarning($"Pool for {key} exhausted. Instantiated a new particle dynamically.");
            }
            else
            {
                ps = queue.Dequeue();
            }

            // --- Setup and Play ---
            ps.transform.position = position;
            ps.gameObject.SetActive(true); // Must be active to play

            // Apply color if provided
            if (colorOverride.HasValue)
            {
                var main = ps.main;
                main.startColor = colorOverride.Value;
            }

            ps.Play();

            // Start the coroutine to return the particle to the pool after it finishes.
            StartCoroutine(ReturnToPoolAfter(ps, key));
        }

        private IEnumerator ReturnToPoolAfter(ParticleSystem ps, ParticleType key)
        {
            // Wait until the particle system has finished its main simulation (isStopped)
            // AND all individual particles have completed their Start Lifetime (IsAlive(true) is false).
            // This is the most reliable way to ensure the effect is not cut short.
            yield return new WaitUntil(() => ps.isStopped && !ps.IsAlive(true));

            // Optional: Wait one more frame to ensure Unity fully processes the stop state.
            yield return null;

            // --- Return to Pool ---
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.gameObject.SetActive(false);

            // Re-enqueue the particle for reuse
            pools[key].Enqueue(ps);
        }
    }
}