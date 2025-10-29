#if UNITY_EDITOR
using SnakyColors;
using UnityEditor;
using UnityEngine;
using static SnakyColors.LevelDatabase;

[CustomEditor(typeof(LevelDatabase))]
public class LevelDatabaseEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        LevelDatabase db = (LevelDatabase)target;

        GUILayout.Space(10);
        if (GUILayout.Button("➕ Add New Level With Defaults"))
        {
            var newLevel = new LevelDataEntry
            {
                missionNumber = db.levels.Count + 1,
                missionName = "New Mission " + (db.levels.Count + 1),
                missionDescription = "Describe this mission...",
                basePlayerSpeed = 2f,
                steeringSpeed = 10f,
                rotationSpeed = 15f,
                speedMultiplier = 1f,
                starReward = 10,
                objectiveValue = 5,
            };

            db.levels.Add(newLevel);
            EditorUtility.SetDirty(db);
        }
    }
}
#endif
