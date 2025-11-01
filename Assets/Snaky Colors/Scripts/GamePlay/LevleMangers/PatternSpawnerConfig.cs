using UnityEngine;
using System.Collections.Generic;

namespace SnakyColors
{
    [CreateAssetMenu(fileName = "PatternConfig_Level_01", menuName = "Game Data/Spawner Config/Pattern")]
    public class PatternSpawnerConfig : ScriptableObject
    {
        [Header("Generation Settings")]
        public float spawnDistance = 20f;
        public float trackWidth = 5f;

        [Header("Patterns")]
        [Tooltip("The patterns this spawner is allowed to use.")]
        public List<SpawnPatternData> spawnPatterns;
    }
}