using SnakyColors;
using UnityEngine;


namespace SnakyColors
{
    public class FoodHunterGameMood : GameMode
    {
        public override string ModeName => "FoodHunter";

        public override bool IsInitialized { get; set; } = false;
  

        [Header("Player & Camera")]
        public GameObject playerPrefab;
        public CameraFollow mainCamera;
        public GameObject playerInstance;

        [Header("Spawner")]
        [SerializeField] private DynamicItemSpawner spawnerPrefab;
        private DynamicItemSpawner spawnerInstance;

        public override void Initialize()
        { 
            if(IsInitialized) return;

            spawnerInstance = Instantiate(spawnerPrefab);
            spawnerInstance.name = "DynamicItemSpawner";
            IsInitialized = true;
        }

        public override void StartMode()
        {
            Debug.Log("Arena started.");
            // Reset score
            Vars.score = 0;
            Vars.starScore = 0;
            Vars.currentArena = 0;

            GameObject[] arenas = GameObject.FindGameObjectsWithTag("Arena");
            foreach (var arena in arenas)
                Destroy(arena);

            // Instantiate player
            if (playerPrefab != null)
            {
                playerInstance = Instantiate(playerPrefab);
                playerInstance.name = "Player"; 

                if (mainCamera == null)
                    mainCamera = Camera.main.GetComponent<CameraFollow>();

                if (mainCamera != null)
                {
                    mainCamera.player = playerInstance;
                    mainCamera.InitializePosition();
                    mainCamera.enabled = true;
                } 
                spawnerInstance.SetPlayer(playerInstance.transform);

                PlayerMovement movement = playerInstance.GetComponent<PlayerMovement>();
                if (movement != null)
                {
                    movement.enabled = true;
                    movement.StartMovement();
                }

                Time.timeScale = 1f;
                PlayerPrefs.SetInt("GamesPlayed", PlayerPrefs.GetInt("GamesPlayed", 0) + 1);
            }
        }

        public override void UpdateMode()
        {
            // arena gameplay logic (obstacle spawns, scoring)
        }

        public override void EndMode()
        {
            Debug.Log("Food Hunter ended.");
            if (playerInstance != null) { Destroy(playerInstance); }
            Time.timeScale = 1f;

            Camera.main.GetComponent<CameraFollow>().ResetCamera();
             
            if (spawnerInstance != null)
            {
                spawnerInstance.ResetSpawner(); 
            } 
        }

        public override void PauseMode()
        { 
            Time.timeScale = 0f;
            if (playerInstance != null)
                playerInstance.GetComponent<PlayerMovement>().enabled = false;
        }

        public override void ResumeMode()
        { 
            Time.timeScale = 1f;
            if (playerInstance != null)
                playerInstance.GetComponent<PlayerMovement>().enabled = true;
        }

        public override void GameOverMode()
        {
            Time.timeScale = 1f;

            Vars.score = 0;
            Camera.main.GetComponent<CameraFollow>().ResetCamera();
             
            if (spawnerInstance != null)
            {
                spawnerInstance.ResetSpawner();
            } 

            if (playerInstance != null) { Destroy(playerInstance); }
        }

        public override void RestartMode()
        {
            base.RestartMode();
        } 

        private void OnDestroy()
        {
            Debug.Log("FoodHunterGameMood is being destroyed. Cleaning up spawner.");
            if (spawnerInstance != null)
            {
                Destroy(spawnerInstance.gameObject);
            }
        }
    }
}

