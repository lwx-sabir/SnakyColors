using UnityEngine;
using System.Collections.Generic;

namespace SnakyColors
{
    public class LevelManager : MonoBehaviour
    {
        public static LevelManager Instance { get; private set; }

        [Header("Level Database")]
        [Tooltip("Assign your 'LevelDatabase' ScriptableObject asset here.")]
        public LevelDatabase levelDatabase; // <-- MODIFIED

        // Reference to the currently loaded level's data
        public LevelData CurrentLevelData { get; private set; }

        private void Awake()
        {
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

        public void LoadLevel(int levelIndex)
        {
            if (levelDatabase == null)
            {
                Debug.LogError("LevelDatabase is not assigned in LevelManager!");
                return;
            }

            if (levelIndex < 0 || levelIndex >= levelDatabase.allLevels.Count)
            {
                Debug.LogError($"Invalid level index: {levelIndex}");
                return;
            }

            CurrentLevelData = levelDatabase.allLevels[levelIndex]; 
            if (CurrentLevelData == null)
            {
                Debug.LogError($"LevelData for index {levelIndex} is null!");
                return;
            }

            // --- 1. Configure all Systems ---
            PlayerStats.Instance.SetConfig(CurrentLevelData.playerConfig, true);

            DynamicItemSpawner dynamicSpawner = FindObjectOfType<DynamicItemSpawner>();
            if (dynamicSpawner != null && CurrentLevelData.dynamicSpawnerConfig != null)
            {
                dynamicSpawner.ApplyConfig(CurrentLevelData.dynamicSpawnerConfig);
            }

            ItemSpawner patternSpawner = FindObjectOfType<ItemSpawner>();
            if (patternSpawner != null && CurrentLevelData.patternSpawnerConfig != null)
            {
                patternSpawner.ApplyConfig(CurrentLevelData.patternSpawnerConfig);
            }

            // --- 2. Tell GameManager to Start the Mode ---
            GameManager.Instance.StartMode("FoodHunter");
        }
    }
}