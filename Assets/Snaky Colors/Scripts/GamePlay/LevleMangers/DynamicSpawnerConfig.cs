using UnityEngine;
using System.Collections.Generic;

namespace SnakyColors
{
    [CreateAssetMenu(fileName = "DynamicConfig_Level_01", menuName = "Game Data/Spawner Config/Dynamic")]
    public class DynamicSpawnerConfig : ScriptableObject
    {
        [Header("Generation Settings")]
        public float trackWidth = 5f;
        public float spawnDistance = 10f;
        public float spawnInterval = 0.5f;
        public float clipBuffer = 0.2f;

        [Header("Overlap Prevention")]
        public bool noOverlap = false;
        public float minOverlapDistance = 0.8f;
        public float gridCellSize = 2f;

        [Header("Dynamic Spawn Limit")]
        public int maxActiveItems = 25;
        public bool limitSpawnWhenFull = true;

        [Header("Items")]
        [Tooltip("List of all items this spawner can generate.")]
        public List<AvailableItemEntry> allAvailableItems;
    }
}