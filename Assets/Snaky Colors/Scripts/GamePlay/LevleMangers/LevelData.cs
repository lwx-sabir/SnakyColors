using UnityEngine;

namespace SnakyColors
{
    // No [CreateAssetMenu] needed. Just [System.Serializable]
    [System.Serializable]
    public class LevelData
    {
        [Header("Level Info")]
        public string levelName = "New Level";
        // 'levelIndex' is now just its position in the list

        public WorldConfig worldConfig;

        [Header("System Configurations")]
        [Tooltip("Player stats configuration for this level.")]
        public PlayerStatConfig playerConfig;

        [Tooltip("Random spawner configuration for this level.")]
        public DynamicSpawnerConfig dynamicSpawnerConfig;

        [Tooltip("Pattern spawner configuration for this level.")]
        public PatternSpawnerConfig patternSpawnerConfig; 
    }
}