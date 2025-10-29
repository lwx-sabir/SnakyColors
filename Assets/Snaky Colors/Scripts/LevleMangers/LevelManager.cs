using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static SnakyColors.LevelDatabase;

namespace SnakyColors
{
    public class LevelManager : MonoBehaviour
    {
        [Tooltip("Reference to the LevelDatabase asset containing all levels.")]
        public LevelDatabase levelDatabase;

        private List<LevelDataEntry> sortedLevels;

        public static LevelManager Instance { get; private set; }

        void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
                SortLevels();
            }
            else
            {
                Destroy(gameObject);
            }
        }

        private void SortLevels()
        {
            if (levelDatabase != null)
                sortedLevels = levelDatabase.levels.OrderBy(l => l.missionNumber).ToList();
            else
                sortedLevels = new List<LevelDataEntry>();
        }

        public LevelDataEntry GetLevel(int missionNumber)
        {
            return sortedLevels.FirstOrDefault(l => l.missionNumber == missionNumber);
        }

        public int GetTotalLevelCount()
        {
            return sortedLevels.Count;
        }
    }
}
