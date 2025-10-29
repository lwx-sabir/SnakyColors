using SnakyColors;
using UnityEngine;

public class ArenaGameMode : GameMode
{
    public override string ModeName => "ObstacleArena";

    public override bool IsInitialized { get; set; }

    [Header("Player & Camera")]
    public GameObject playerPrefab;
    public CameraFollow mainCamera;

    private GameObject playerInstance;

    [HideInInspector]
    public bool isGameRunning { get; private set; }

    public override void Initialize()
    {
        if (IsInitialized) return;

        IsInitialized = true;
        Debug.Log("Arena initialized.");
        // setup arena, player spawn, etc.
    }

    public override void StartMode()
    {
        Debug.Log("Arena started.");
        // Reset score
        Vars.score = 0;
        Vars.starScore = 0;
        Vars.currentArena = 0;

        // Clean up old arenas
        GameObject[] arenas = GameObject.FindGameObjectsWithTag("Arena");
        foreach (var arena in arenas)
            Destroy(arena);

        // Instantiate player
        if (playerPrefab != null)
        {
            playerInstance = Instantiate(playerPrefab);
            playerInstance.name = "Player";
            playerInstance.transform.position = new Vector2(0, -5); 

            // Link camera
            if (mainCamera != null)
                mainCamera.player = playerInstance;

            // Enable camera
            if (mainCamera != null)
                mainCamera.enabled = true;

            PlayerMovement movement = playerInstance.GetComponent<PlayerMovement>();
            if (movement != null)
            {
                movement.enabled = true;
                movement.StartMovement();
            }
            InstantiateNewGameArena();
            isGameRunning = true;
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
        Debug.Log("Arena ended.");
        // cleanup, score save
    }

    public override void PauseMode()
    {
        if (!isGameRunning) return;
        Time.timeScale = 0f;
        if (playerInstance != null)
            playerInstance.GetComponent<PlayerMovement>().enabled = false;
    }

    public override void ResumeMode()
    {
        if (!isGameRunning) return;
        Time.timeScale = 1f;
        if (playerInstance != null)
            playerInstance.GetComponent<PlayerMovement>().enabled = true;
    }

    public override void GameOverMode()
    {
        isGameRunning = false;
        Time.timeScale = 1f;

        if (playerInstance != null)
            Destroy(playerInstance);

        // Destroy all arenas / obstacles
        GameObject[] arenas = GameObject.FindGameObjectsWithTag("Arena");
        foreach (GameObject arena in arenas)
            Destroy(arena);

        // Reset Vars
        Vars.currentArena = 0;
        Vars.score = 0;
        Camera.main.GetComponent<CameraFollow>().ResetCamera(); 
    }

    public override void RestartMode()
    {
        base.RestartMode();
    }

    private void InstantiateNewGameArena()
    {
        int arenaNumber = Random.Range(1, 16);
        GameObject newArena = Instantiate(Resources.Load("Arena" + arenaNumber)) as GameObject;
        newArena.tag = "Arena";
        newArena.name = "Arena" + arenaNumber;
        newArena.transform.position = new Vector2(0, Vars.currentArena * 15);
        Vars.currentArena++;
    }
}
