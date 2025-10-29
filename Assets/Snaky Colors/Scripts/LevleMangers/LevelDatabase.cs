using System.Collections.Generic;
using UnityEngine;

namespace SnakyColors
{
    [CreateAssetMenu(fileName = "LevelDatabase", menuName = "Game Data/Level Database")]
    public class LevelDatabase : ScriptableObject
    {
        [Header("All Levels / Missions")]
        public List<LevelDataEntry> levels = new List<LevelDataEntry>();

        private void OnValidate()
        {
            foreach (var lvl in levels)
            {
                if (lvl.missionName == "")
                    lvl.missionName = "New Mission";

                if (lvl.missionDescription == "")
                    lvl.missionDescription = "Your mission description here.";

                if (lvl.basePlayerSpeed == 0)
                    lvl.basePlayerSpeed = 2f;

                if (lvl.steeringSpeed == 0)
                    lvl.steeringSpeed = 10f;

                if (lvl.rotationSpeed == 0)
                    lvl.rotationSpeed = 15f;

                if (lvl.speedMultiplier == 0)
                    lvl.speedMultiplier = 1f;

                if (lvl.starReward == 0)
                    lvl.starReward = 10;

                if (lvl.objectiveValue == 0)
                    lvl.objectiveValue = 5;

                if (lvl.missionNumber == 0)
                    lvl.missionNumber = levels.IndexOf(lvl) + 1;

                lvl.gameType = GameType.ObstacleMaster;
            }
        }

        [System.Serializable]
        public class LevelDataEntry
        {
            [Header("Mission Info")]
            public int missionNumber = 1;
            public string missionName = "First Steps";
            [TextArea] public string missionDescription;

            [Header("Player Physics")]
            public GameType gameType;

            [Header("Player Physics")]
            public float basePlayerSpeed = 2f;
            public float steeringSpeed = 10f;
            public float rotationSpeed = 15f;
            public float speedMultiplier = 1f;

            [Header("Arena / Layout")]
            public List<GameObject> arenas = new List<GameObject>();
            public int loopArenaCount = 1;

            [Header("Objective")]
            public ObjectiveType objectiveType;
            public int objectiveValue = 5;

            [Header("Color / Difficulty")]
            public PaletteType colorPalette;
            [Range(0f, 1f)] public float colorDifficulty;

            [Header("Rewards")]
            public int starReward = 10;
            public string unlockableItemID = "DefaultSkin_01";


            [Header("Advanced / Optional Settings")]
            [Tooltip("Time in seconds to complete this mission (0 = unlimited)")]
            public float timeLimit = 0f;

            [Tooltip("Background color for this level (if supported)")]
            public Color backgroundColor = Color.black;

            [Tooltip("Background music for this level")]
            public AudioClip levelMusic;

            [Tooltip("Multiplier for enemy/obstacle intensity")]
            public float aiIntensity = 1.0f;

            [Tooltip("Optional: Custom gameplay rule ID (for code logic)")]
            public string specialRule = "";
        }
    }
}
