using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SnakyColors
{
    public class GameManager : MonoBehaviour
    {
        [Header("Game Mode Prefabs")]
        public List<GameObject> modePrefabs;

        [HideInInspector]
        public static GameManager Instance;

        [HideInInspector]
        public bool isGameRunning = false;

        // Helper property to get the name of the currently running mode
        public string CurrentModeName => currentMode != null ? currentMode.ModeName : string.Empty;

        private Dictionary<string, GameObject> modePrefabDict;
        private Dictionary<string, GameMode> modeInstancePool; // Pooled instances
        public GameMode currentMode;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);

            // Build prefab dictionary for quick lookup
            modePrefabDict = new Dictionary<string, GameObject>();
            modeInstancePool = new Dictionary<string, GameMode>();

            foreach (var prefab in modePrefabs)
            {
                var modeComp = prefab.GetComponent<GameMode>();
                // Only add if it has a GameMode component and ModeName is unique
                if (modeComp != null && !modePrefabDict.ContainsKey(modeComp.ModeName))
                {
                    modePrefabDict.Add(modeComp.ModeName, prefab);
                }
            }
        }  
         
        public void StartMode(string modeName)
        { 
            if (currentMode != null)
            { 
                currentMode.EndMode();
                currentMode.gameObject.SetActive(false);
            }
             
            if (!modeInstancePool.TryGetValue(modeName, out GameMode mode))
            {
                if (!modePrefabDict.TryGetValue(modeName, out GameObject prefab))
                {
                    Debug.LogError($"Mode '{modeName}' not found in prefabs list!");
                    return;
                }

                GameObject modeGO = Instantiate(prefab, transform);
                mode = modeGO.GetComponent<GameMode>();
                // Initially instantiate as inactive, will be activated below
                modeGO.SetActive(false);
                modeInstancePool.Add(modeName, mode);
            }

            currentMode = mode;
            currentMode.gameObject.SetActive(true);
             
            if (!currentMode.IsInitialized)
                currentMode.Initialize();  

            currentMode.StartMode();
            isGameRunning = true;
            EnemyDropTable.ResetCooldowns();
        }

        public void RestartCurrentMode()
        {
            if (currentMode != null)
            {
                string modeToRestart = currentMode.ModeName;
                EndCurrentMode(destroy: false);
                StartCoroutine(RestartModeCoroutine(modeToRestart));
            }
            else
            {
                Debug.LogError("Cannot restart: currentMode is null.");
            }
        }

        private IEnumerator RestartModeCoroutine(string modeName)
        {
            yield return null;
            StartMode(modeName);
        }

        public void EndCurrentMode(bool destroy = true)
        {
            if (currentMode != null)
            {
                string modeName = currentMode.ModeName;
                currentMode.EndMode();

                if (destroy)
                {
                    if (modeInstancePool.ContainsKey(modeName))
                        modeInstancePool.Remove(modeName);

                    Destroy(currentMode.gameObject);
                }
                else
                {
                    currentMode.gameObject.SetActive(false);
                }

                currentMode = null;
            }
            isGameRunning = false;
        }


        public void PauseGame()
        {
            currentMode?.PauseMode();
            isGameRunning = false;
        }

        public void ResumeGame()
        {
            currentMode?.ResumeMode();
            isGameRunning = true;
        }

        // Simplified EndGame to just rely on EndCurrentMode cleanup
        public void EndGame()
        {
            EndCurrentMode(destroy: true);
            // Any global cleanup logic can go here (e.g., scene transition prep)
        }

        public void GameOver()
        {
            currentMode?.GameOverMode();
            isGameRunning = false;
        }
    }
}
