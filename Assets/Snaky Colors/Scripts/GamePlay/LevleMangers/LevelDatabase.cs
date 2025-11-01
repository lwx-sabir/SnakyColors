using UnityEngine;
using System.Collections.Generic;

namespace SnakyColors
{
    [CreateAssetMenu(fileName = "LevelDatabase", menuName = "Game Data/Level Database")]
    public class LevelDatabase : ScriptableObject
    {
        [Tooltip("The master list of all levels in the game, in order.")]
        public List<LevelData> allLevels;
    }
}