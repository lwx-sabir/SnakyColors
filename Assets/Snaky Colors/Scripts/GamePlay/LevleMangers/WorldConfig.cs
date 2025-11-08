using UnityEngine;

namespace SnakyColors
{
    [CreateAssetMenu(fileName = "WorldConfig_Default", menuName = "Game Data/World Config")]
    public class WorldConfig : ScriptableObject
    {
        [Header("World Properties")]
        public float WorldSize = 30000f;
        public float ZoneCellSize = 500f; // For the server's spatial grid

        [Header("Game Rules")]
        public int MaxPlayers = 600;
        public int TargetFoodCount = 6000; 
        public int TickRate = 20; 
    }
}