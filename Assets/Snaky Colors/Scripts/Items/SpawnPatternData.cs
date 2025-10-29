using SnakyColors;
using System.Collections.Generic;
using UnityEngine;

namespace SnakyColors
{
    [CreateAssetMenu(fileName = "NewPattern", menuName = "SnakyColors/Spawn Pattern")]
    public class SpawnPatternData : ScriptableObject
    {
        // The probability of this *entire pattern* being chosen over others
        public float patternProbability = 1f;

        [Tooltip("The vertical space consumed by this pattern, controlling the speed of progression.")]
        public float verticalHeight = 3f;

        [Header("Horizontal Layout")]
        [Tooltip("The number of lanes/slots this pattern uses (e.g., 3 for obstacles, 5 for dense coins).")]
        public int activeLanes = 3;

        [Header("Visual Jitter")]
        [Tooltip("If true, a random X offset will be applied to all elements in this pattern, breaking the grid look.")]
        public bool enableDynamicXOffset = true; 

        [Tooltip("The maximum random X displacement (e.g., 0.2 means X will shift between -0.2 and +0.2).")]
        public float maxDynamicXOffset = 0.5f; 
        [Tooltip("If true, a random Y offset will be applied to all elements in this pattern, breaking up vertical alignment.")]
        public bool enableDynamicYOffset = true; 

        [Tooltip("The maximum random Y displacement (e.g., 0.1 means Y will shift between -0.1 and +0.1).")]
        public float maxDynamicYOffset = 1f; 


        [System.Serializable]
        public class SpawnEntry
        {
            public ItemData item; // The ItemData defining the prefab, color, and pool
            public int laneIndex; // 0, 1, 2, etc. (Must be less than ItemSpawner.lanes)

            [Tooltip("Offset from the base Y position of the pattern.")]
            public float yOffset = 0f;
        }

        public List<SpawnEntry> entries = new List<SpawnEntry>();
    }
}  
