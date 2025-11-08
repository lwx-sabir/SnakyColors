using Microsoft.AspNetCore.SignalR.Client;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using SnakyColors;
using System;
using System.Linq; 
using UnityEngine.Playables; // You had this, so I'm keeping it

// --- The Main Client ---
public class NetworkClient : MonoBehaviour
{
    public static NetworkClient Instance { get; private set; }

    [Header("Configuration")]
    [SerializeField] private string hubUrl = "http://localhost:5000/snakehub";

    [Header("Asset References")]
    [SerializeField] private GameObject snakeBasePrefab; // Prefab for local player
    [SerializeField] private GameObject enemyBasePrefab; // Prefab for AI/Other players
    [SerializeField] private SkinDatabase skinDatabase;
    [SerializeField] private ItemDatabase itemDatabase;

    [Header("Runtime")]
    [SerializeField] private bool autoConnectOnStart = false;
    [SerializeField] private bool persistAcrossScenes = false;

    [Header("Visual Scale (Mass ? Thickness)")]
    [SerializeField] private float baseThickness = 0.5f;       // world units baseline thickness
    [SerializeField] private float thicknessPerMass = 0.02f;    // additional thickness per Mass unit
    [SerializeField] private float minThickness = 0.3f;         // clamp min
    [SerializeField] private float maxThickness = 2.0f;         // clamp max

    public float CurrentWorldSize { get; private set; } = 100f;

    private HubConnection hubConnection;
    private string myPlayerId;
    private SegmentedCreator localPlayerSnake;
    [HideInInspector] public PlayerStateDto localPlayerState;

    // --- Local World State ---
    private Dictionary<string, SegmentedCreator> otherSnakes = new Dictionary<string, SegmentedCreator>();
    private Dictionary<int, GameObject> activeFood = new Dictionary<int, GameObject>();

    // --- Thread-Safe Queues ---
    private ConcurrentQueue<WorldUpdateDto> worldUpdates = new ConcurrentQueue<WorldUpdateDto>();
    private ConcurrentQueue<FoodEatenEvent> foodEatenQueue = new ConcurrentQueue<FoodEatenEvent>();
    private ConcurrentQueue<PlayerDiedEvent> playerDiedQueue = new ConcurrentQueue<PlayerDiedEvent>();
    private ConcurrentQueue<string> playerLeftQueue = new ConcurrentQueue<string>();
    private ConcurrentQueue<PlayerStateDto> joinSuccessQueue = new ConcurrentQueue<PlayerStateDto>();

    // --- Event Structs ---
    private readonly struct FoodEatenEvent
    {
        public int FoodId { get; }
        public string PlayerId { get; }
        public FoodEatenEvent(int foodId, string playerId) { FoodId = foodId; PlayerId = playerId; }
    }
    private readonly struct PlayerDiedEvent
    {
        public string DeadPlayerId { get; }
        public string Message { get; }
        public PlayerDiedEvent(string deadId, string msg) { DeadPlayerId = deadId; Message = msg; }
    }


    private void Awake()
    {
        // Singleton assignment; optionally persist across scenes
        if (persistAcrossScenes)
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            // Allow replacing any previous instance within scene lifecycle
            Instance = this;
        }

        if (skinDatabase != null) skinDatabase.Initialize();
        else Debug.LogError("NetworkClient: SkinDatabase is not assigned!");

        if (itemDatabase != null) itemDatabase.Initialize();
        else Debug.LogError("NetworkClient: ItemDatabase is not assigned!");
    }

    async void Start()
    {
        if (!autoConnectOnStart) return;
        try { await ConnectAsync(Guid.NewGuid().ToString(), "cobra"); }
        catch (Exception ex) { Debug.LogError($"Network Client: auto-connect failed: {ex.Message}"); }
    }

    private void RegisterHubHandlers()
    {
        if (hubConnection == null) return;
        hubConnection.On("Pong", () => { Debug.Log("PONG RECEIVED! Connection is working."); });
        hubConnection.On<WorldUpdateDto>("WorldUpdate", (worldState) => { worldUpdates.Enqueue(worldState); });
        hubConnection.On<PlayerStateDto>("OnJoinSuccess", (playerState) => { joinSuccessQueue.Enqueue(playerState); });
        hubConnection.On<int, string>("OnFoodEaten", (foodId, playerId) => { foodEatenQueue.Enqueue(new FoodEatenEvent(foodId, playerId)); });
        hubConnection.On<string, string>("OnPlayerDied", (deadPlayerId, message) => { playerDiedQueue.Enqueue(new PlayerDiedEvent(deadPlayerId, message)); });
        hubConnection.On<string>("OnPlayerLeft", (playerId) => { playerLeftQueue.Enqueue(playerId); });
        hubConnection.On<string>("JoinFailed", (msg) => { Debug.LogError($"JoinFailed: {msg}"); });
    }

    public async Task ConnectAsync(string playerId, string skinId)
    {
        if (hubConnection != null && hubConnection.State == HubConnectionState.Connected)
        {
            Debug.LogWarning("NetworkClient: already connected.");
            return;
        }

        hubConnection = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect()
            .Build();

        RegisterHubHandlers();

        try
        {
            await hubConnection.StartAsync();
            Debug.Log("Network Client: Connection Started.");
            await hubConnection.InvokeAsync("Ping");
            await hubConnection.InvokeAsync("JoinMainWorld", playerId, skinId);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Network Client: Connection failed: {ex.Message}");
        }
    }

    public async Task DisconnectAsync()
    {
        try
        {
            if (hubConnection != null)
            {
                await hubConnection.StopAsync();
                await hubConnection.DisposeAsync();
            }
        }
        catch { }
        hubConnection = null;

        // Despawn local player
        if (localPlayerSnake != null)
        {
            if (localPlayerSnake.moveToTarget.Target != null)
                Destroy(localPlayerSnake.moveToTarget.Target.gameObject);
            Destroy(localPlayerSnake.gameObject);
            localPlayerSnake = null;
        }

        // Despawn others
        foreach (var kv in otherSnakes)
        {
            var snake = kv.Value;
            if (snake == null) continue;
            if (snake.moveToTarget.Target != null)
                Destroy(snake.moveToTarget.Target.gameObject);
            Destroy(snake.gameObject);
        }
        otherSnakes.Clear();

        // Clear food
        foreach (var kv in activeFood)
        {
            var go = kv.Value;
            if (go == null) continue;
            if (go.TryGetComponent<GeneratedItem>(out var gen)) gen.ReturnToPool();
            else go.SetActive(false);
        }
        activeFood.Clear();

        myPlayerId = null;
        localPlayerState = null;
    }

    private async void OnDestroy()
    {
        if (Instance == this) Instance = null;
        await DisconnectAsync();
    }

    private void Update()
    {
        while (joinSuccessQueue.TryDequeue(out PlayerStateDto playerState))
        {
            HandleJoinSuccess(playerState);
        }
        while (worldUpdates.TryDequeue(out WorldUpdateDto worldState))
        {
            ProcessWorldUpdate(worldState);
        }
        while (foodEatenQueue.TryDequeue(out FoodEatenEvent foodEvent))
        {
            ProcessFoodEaten(foodEvent);
        }
        while (playerDiedQueue.TryDequeue(out PlayerDiedEvent deathEvent))
        {
            HandlePlayerDeath(deathEvent.DeadPlayerId, deathEvent.Message);
        }
        while (playerLeftQueue.TryDequeue(out string playerId))
        {
            HandlePlayerLeft(playerId);
        }
    }

    private void HandleJoinSuccess(PlayerStateDto playerState)
    {
        myPlayerId = playerState.PlayerId;
        Debug.Log($"Successfully joined world. My PlayerID is: {myPlayerId}");

        if (localPlayerSnake == null)
        {
            this.localPlayerState = playerState;
            SpawnSnake(playerState, isLocalPlayer: true);
        }
    }

    private void ProcessWorldUpdate(WorldUpdateDto worldState)
    {
        if (worldState == null || string.IsNullOrEmpty(myPlayerId)) return;

        CurrentWorldSize = worldState.WorldSize;

        HashSet<string> seenSnakes = new HashSet<string>();
        foreach (var snakeDto in worldState.Snakes)
        {
            if (snakeDto == null) continue;
            seenSnakes.Add(snakeDto.PlayerId);

            // --- LOCAL PLAYER ---
            if (snakeDto.PlayerId == myPlayerId)
            {
                if (localPlayerSnake != null)
                {
                    this.localPlayerState = snakeDto;
                    // --- RECONCILIATION ---
                    // Server is the authority on our score and length
                    if (localPlayerSnake.ribCount != snakeDto.TargetLength)
                    {
                        localPlayerSnake.ribCount = snakeDto.TargetLength;
                        // Force a refresh only when the target length changes so growth is visible immediately
                        localPlayerSnake.RefreshSprites();
                        Debug.Log($"LOCAL STATE: score={snakeDto.Score} targetLen={snakeDto.TargetLength}");
                    }
                    // Keep local visual scale in sync with Mass
                    ApplyScaleFromMass(localPlayerSnake, snakeDto);
                    // We *could* also apply speed here if server calculates it
                    // localPlayerSnake.baseSpeed = snakeDto.BaseSpeed;
                    // localPlayerSnake.boostSpeed = snakeDto.BoostSpeed;
                }
            }
            // --- OTHER PLAYER or AI ---
            else if (otherSnakes.TryGetValue(snakeDto.PlayerId, out SegmentedCreator snake))
            {
                // --- INTERPOLATION ---
                // This is an existing snake. We smoothly move its target.
                if (snake.moveToTarget.Target != null)
                {
                    // We just move the target. The snake's own MoveToTarget
                    // component will handle the visual smoothing.
                    snake.moveToTarget.Target.position = new Vector3(snakeDto.HeadPosition.X, snakeDto.HeadPosition.Y, 0);
                }
                snake.ribCount = snakeDto.TargetLength;
                snake.moveToTarget.movingSpeed = snakeDto.CurrentSpeed;
                // Keep remote visual scale in sync with Mass
                ApplyScaleFromMass(snake, snakeDto);
            }
            else
            {
                // This is a new snake. Spawn it.
                SpawnSnake(snakeDto, isLocalPlayer: false);
            }
        }

        // --- Process Food (Spawning) ---
        HashSet<int> seenFood = new HashSet<int>();
        foreach (var foodDto in worldState.Food)
        {
            if (foodDto == null) continue;
            seenFood.Add(foodDto.Id);

            if (!activeFood.ContainsKey(foodDto.Id))
            {
                ItemData itemToSpawn = itemDatabase.GetItemByKey(foodDto.ItemKey);
                if (itemToSpawn == null) continue;

                GameObject newFoodObj = ItemPooler.Instance.GetPooledObject(itemToSpawn);
                if (newFoodObj != null)
                {
                    newFoodObj.transform.position = new Vector3(foodDto.PosX, foodDto.PosY, 0);
                    newFoodObj.SetActive(true);

                    if (newFoodObj.TryGetComponent<GeneratedItem>(out var genItem))
                    {
                        if (localPlayerSnake != null)
                            genItem.SetData(itemToSpawn, localPlayerSnake.transform);
                        genItem.Id = foodDto.Id;
                    }
                    activeFood.Add(foodDto.Id, newFoodObj);
                }
            }
        }

        DespawnUnseenSnakes(seenSnakes);
        DespawnUnseenFood(seenFood);
    }

    /// <summary>
    /// Spawns any snake (local, remote, or AI)
    /// </summary>
    private void SpawnSnake(PlayerStateDto playerState, bool isLocalPlayer)
    {  
        GameObject prefabToSpawn = isLocalPlayer ? snakeBasePrefab : enemyBasePrefab;
        if (prefabToSpawn == null)
        {
            Debug.LogError($"Prefab for {(isLocalPlayer ? "snakeBasePrefab" : "enemyBasePrefab")} is not assigned!");
            return;
        } 

        Vector3 startPos = new Vector3(playerState.HeadPosition.X, playerState.HeadPosition.Y, 0);
        GameObject newSnakeObj = Instantiate(prefabToSpawn, startPos, Quaternion.identity);

        SegmentedCreator newSnake = newSnakeObj.GetComponent<SegmentedCreator>();
        if (newSnake == null)
        {
            Debug.LogError($"CRITICAL: Prefab '{prefabToSpawn.name}' is missing 'SegmentedCreator'!", prefabToSpawn);
            Destroy(newSnakeObj);
            return;
        }
        // Use server-provided SkinID for AI and players
        Skin skin = skinDatabase.GetSkinByID(playerState.SkinID);
        newSnake.skin = skin;
        newSnake.ribCount = playerState.TargetLength; 
        newSnake.RefreshSprites();
        // Normalize visual scale from Mass
        ApplyScaleFromMass(newSnake, playerState);

        newSnake.moveToTarget.enableMoving = true;
        newSnake.moveToTarget.moveThroughTarget = true;

        Transform target = new GameObject($"{playerState.PlayerId}_Target").transform;
        target.position = startPos;
        newSnake.moveToTarget.Target = target;

        if (isLocalPlayer)
        { 
            localPlayerSnake = newSnake;
            newSnakeObj.AddComponent<SlitherMovement>();

            SlitherCameraFollow camFollow = FindObjectOfType<SlitherCameraFollow>();
            if (camFollow != null)
            {
                camFollow.SetPlayer(newSnakeObj.transform);
            }

            var collisionManager = newSnakeObj.AddComponent<SlitherCollisionManager>();
            collisionManager.SetPlayerId(myPlayerId);

            if (newSnakeObj.GetComponent<Collider2D>() == null)
            {
                var col = newSnakeObj.AddComponent<CircleCollider2D>();
                col.isTrigger = true;
                col.radius = 0.5f;
            }
            // Ensure trigger callbacks fire: one object in the pair must have a Rigidbody2D
            var rb2d = newSnakeObj.GetComponent<Rigidbody2D>();
            if (rb2d == null)
            {
                rb2d = newSnakeObj.AddComponent<Rigidbody2D>();
                rb2d.bodyType = RigidbodyType2D.Kinematic;
                rb2d.gravityScale = 0f;
                rb2d.constraints = RigidbodyConstraints2D.FreezeRotation;
            }
        }
        else
        { 
            otherSnakes.Add(playerState.PlayerId, newSnake);

            // Do not move AI locally; server drives AI movement via snapshots
        }
    }
    
    // Mass ? uniform visual scale to normalize sprite size differences
    private void ApplyScaleFromMass(SegmentedCreator snake, PlayerStateDto state)
    {
        if (snake == null || state == null || snake.skin == null) return;

        // Prefer body sprite as reference; fallback to head
        var refSprite = snake.skin.BodySprite != null ? snake.skin.BodySprite : snake.skin.HeadSprite;
        if (refSprite == null) return;

        // Estimate sprite thickness in world units (use larger dimension)
        var size = refSprite.bounds.size;
        float spriteThickness = Mathf.Max(size.x, size.y);
        if (spriteThickness <= 0.0001f) return;

        // Map Mass to desired world-space thickness, then derive uniform scale
        float desiredThickness = Mathf.Clamp(baseThickness + (state.Mass * thicknessPerMass), minThickness, maxThickness);
        float uniformScale = desiredThickness / spriteThickness;

        snake.transform.localScale = new Vector3(uniformScale, uniformScale, uniformScale);
    }
    

    private void HandlePlayerDeath(string deadPlayerId, string message)
    {
        Debug.Log($"DEATH: {message}");
        if (deadPlayerId == myPlayerId)
        {
            Debug.LogError("WE DIED!");
            if (localPlayerSnake != null)
            {
                if (localPlayerSnake.moveToTarget.Target != null)
                {
                    Destroy(localPlayerSnake.moveToTarget.Target.gameObject);
                }
                Destroy(localPlayerSnake.gameObject);
                localPlayerSnake = null;
            }
            // TODO: Show "Game Over" screen
        }
        else
        {
            DespawnSnake(deadPlayerId);
        }
    }

    private void HandlePlayerLeft(string playerId)
    {
        Debug.Log($"PLAYER LEFT: {playerId}");
        DespawnSnake(playerId); // Use helper
    }

    private void DespawnSnake(string playerId)
    {
        if (otherSnakes.TryGetValue(playerId, out SegmentedCreator snakeToDestroy))
        {
            if (snakeToDestroy.moveToTarget.Target != null)
            {
                Destroy(snakeToDestroy.moveToTarget.Target.gameObject);
            }
            Destroy(snakeToDestroy.gameObject);
            otherSnakes.Remove(playerId);
        }
    }

    private void DespawnFood(int foodId)
    {
        if (activeFood.TryGetValue(foodId, out GameObject foodObj))
        {
            if (foodObj.TryGetComponent<GeneratedItem>(out var genItem))
            {
                genItem.ReturnToPool();
            }
            else
            {
                foodObj.SetActive(false);
            }
            activeFood.Remove(foodId);
        }
    }

    /// <summary>
    /// Processes a specific "OnFoodEaten" event from the server.
    /// </summary>
    private void ProcessFoodEaten(FoodEatenEvent foodEvent)
    {
        if (!activeFood.TryGetValue(foodEvent.FoodId, out GameObject foodObj)) return;
        activeFood.Remove(foodEvent.FoodId);
        if (foodObj == null) return;

        // If already handled locally (deactivated), do nothing
        if (!foodObj.activeInHierarchy) return;

        if (foodObj.TryGetComponent<GeneratedItem>(out var genItem))
        {
            // If this client was the eater, we already played local VFX; just despawn safely
            if (foodEvent.PlayerId == myPlayerId)
            {
                genItem.ReturnToPool();
                return;
            }

            // Remote eater: play VFX into that snake's head
            Transform collectorHead = null;
            if (otherSnakes.TryGetValue(foodEvent.PlayerId, out var otherSnake))
                collectorHead = otherSnake.transform;

            if (collectorHead != null)
            {
                Debug.Log($"NET: OnFoodEaten food={foodEvent.FoodId} by={foodEvent.PlayerId} head=remote");
                genItem.PlayRemoteCollect(collectorHead);
            }
            else
            {
                genItem.ReturnToPool();
            }
        }
        else
        {
            foodObj.SetActive(false);
        }
    }

    private void DespawnUnseenSnakes(HashSet<string> seenSnakes)
    {
        // We must create a copy of the keys to iterate over
        // otherwise we can't modify the dictionary`
        List<string> snakesToDespawn = otherSnakes.Keys.ToList();

        foreach (string snakeId in snakesToDespawn)
        {
            if (!seenSnakes.Contains(snakeId))
            {
                // This snake is in our local list but not in the server state.
                // It must have disconnected or died.
                DespawnSnake(snakeId);
            }
        }
    }

    private void DespawnUnseenFood(HashSet<int> seenFood)
    {
        List<int> foodToDespawn = activeFood.Keys.ToList();

        foreach (int foodId in foodToDespawn)
        {
            if (!seenFood.Contains(foodId))
            {
                // This food is in our local list but not in the server state.
                // It was eaten by someone (or despawned).
                DespawnFood(foodId);
            }
        }
    }

    // Called by SlitherMovement
    public async Task SendState(List<SerializableVector2> bodySegments, bool isBoosting)
    {
        if (hubConnection == null || hubConnection.State != HubConnectionState.Connected) return;
        try
        {
            await hubConnection.InvokeAsync("UpdateState", bodySegments, isBoosting);
        }
        catch (Exception ex) { Debug.LogWarning($"Failed to send state: {ex.Message}"); }
    }

    // Called by SlitherCollisionManager
    public async Task ReportFoodEaten(int foodId)
    {
        if (hubConnection == null || hubConnection.State != HubConnectionState.Connected) return;
        try
        {
            Debug.Log($"NET: ReportFoodEaten({foodId})");
            await hubConnection.InvokeAsync("ReportFoodEaten", foodId);
        }
        catch (Exception ex) { Debug.LogWarning($"Failed to report food: {ex.Message}"); }
    }

    // Called by SlitherCollisionManager
    public async Task ReportPlayerDied(string killerId)
    {
        if (hubConnection == null || hubConnection.State != HubConnectionState.Connected) return;
        try
        {
            Debug.Log($"NET: ReportPlayerDied(killerId={(killerId ?? "null")})");
            await hubConnection.InvokeAsync("ReportPlayerDied", killerId);
        }
        catch (Exception ex) { Debug.LogWarning($"Failed to report death: {ex.Message}"); }
    }
     
}



