using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.SignalR.Protocol;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using SnakyColors;
using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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

    [Header("Remote Render")]
    [SerializeField] private bool remoteDirectRender = true; // place remote heads directly at interpolated server head
    [SerializeField] private float remoteSnapLerp = 1.0f;    // 1 = hard snap to interpolated head; <1 = smooth toward it
    [SerializeField] private float remoteDirLerp = 0.5f;     // orientation smoothing toward interpolated dir
    [SerializeField] private float remoteSmoothTime = 0.06f; // SmoothDamp time for direct-render head
    [SerializeField] private float remoteLeadSeconds = 0.08f; // render a bit ahead to counter latency
    [SerializeField] private float remoteVelLerpRate = 10f;   // per-second rate to blend toward server velocity
    [SerializeField] private float remoteCorrectionRate = 8f; // per-second rate to pull toward anchor

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

    // --- Server Clock Sync ---
    private bool serverClockInit = false;
    private float serverTickToSec = 0.05f; // default (20 Hz)
    private float serverTimeOffset = 0f;    // serverSeconds - Time.time
    private System.DateTime serverStartUtc;
    private float clientStartTimeSec = 0f;


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
        try { await ConnectAsync(Guid.NewGuid().ToString(), "greenskin"); }
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
            .AddMessagePackProtocol()
            .ConfigureLogging(logging =>
            { 
                logging.SetMinimumLevel(LogLevel.Debug);
            })
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

        // Initialize server clock once (absolute server seconds)
        if (!serverClockInit)
        {
            if (worldState.TickRate > 0)
                serverTickToSec = 1f / Mathf.Max(1, worldState.TickRate);
            float serverSecondsAbs = (float)(worldState.ServerUtc - System.DateTime.UnixEpoch).TotalSeconds;
            float clientSecondsAbs = Time.time;
            serverTimeOffset = serverSecondsAbs - clientSecondsAbs; // serverTime = Time.time + offset
            serverClockInit = true;
         //   Debug.Log($"[ClockInit] ServerAbs={serverSecondsAbs:F3}  Client={clientSecondsAbs:F3}  Offset={serverTimeOffset:F3}");
        }

        // Per-update diagnostics for timing alignment
  //      Debug.Log($"[NET] ServerUtc: {worldState.ServerUtc:o}  Offset: {serverTimeOffset:F3}  LocalTime: {Time.time:F3}");

        HashSet<string> seenSnakes = new HashSet<string>();
        foreach (var kin in worldState.Snakes)
        {
            if (kin == null) continue;
            seenSnakes.Add(kin.PlayerId);

            // --- LOCAL PLAYER ---
            if (kin.PlayerId == myPlayerId)
            {
                if (localPlayerSnake != null)
                {
                    // --- RECONCILIATION ---
                    // Server is the authority on our target length
                    if (localPlayerSnake.ribCount != kin.TargetLength)
                    {
                        localPlayerSnake.ribCount = kin.TargetLength;
                        // Force a refresh only when the target length changes so growth is visible immediately
                        localPlayerSnake.RefreshSprites();
                        Debug.Log($"LOCAL STATE: targetLen={kin.TargetLength}");
                    }
                    // Keep local visual scale in sync with Mass
                    ApplyScaleFromMass(localPlayerSnake, kin.Mass);

                    // Keep local speeds/turn settings in sync
                    if (localPlayerState != null)
                    {
                        if (kin.CurrentSpeed > 0.01f) localPlayerState.CurrentSpeed = kin.CurrentSpeed;
                        if (kin.BaseSpeed > 0.01f) localPlayerState.BaseSpeed = kin.BaseSpeed;
                        localPlayerState.MaxTurningAngle = kin.MaxTurningAngle;
                        localPlayerState.Mass = kin.Mass;
                    }
                }
            }
            // --- OTHER PLAYER or AI ---
            else if (otherSnakes.TryGetValue(kin.PlayerId, out SegmentedCreator snake))
            {
                // Simple timestamped interpolation; never simulate remotes locally
                if (!snake.TryGetComponent<RemoteSnake>(out var remote))
                    remote = snake.gameObject.AddComponent<RemoteSnake>();
                remote.ConfigureServerClock(serverTickToSec, serverTimeOffset);
                remote.SetTargetLength(kin.TargetLength);
                if (snake.moveToTarget != null) snake.moveToTarget.enableMoving = false;
                Vector2 head2 = new Vector2(kin.HeadPosition.X, kin.HeadPosition.Y);
                float spd = (kin.CurrentSpeed > 0.01f) ? kin.CurrentSpeed : (kin.BaseSpeed > 0f ? kin.BaseSpeed : 0f);
                float serverSecondsNow = (float)(worldState.ServerUtc - System.DateTime.UnixEpoch).TotalSeconds;
                remote.OnServerUpdate(head2, serverSecondsNow, spd);
                ApplyScaleFromMass(snake, kin.Mass);
            }
            else
            {
                // This is a new snake. Spawn it.
                SpawnRemoteSnake(kin);
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
        if (!isLocalPlayer) 
        {
            newSnakeObj.tag = "EnemySnake";
        }

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
        // Local uses mover; remote renders by snapshot/extrapolation
        newSnake.moveToTarget.moveThroughTarget = true;
        newSnake.moveToTarget.enableWobble = false;

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
            // Remote/AI snakes are driven by server snapshots.
            // Ensure no local AI scripts override movement (they can zero out speed).
           // var aiComp = newSnakeObj.GetComponent<AIMovement>();
         //   if (aiComp != null) Destroy(aiComp);
            var localMove = newSnakeObj.GetComponent<SlitherMovement>();
            if (localMove != null) Destroy(localMove);

            otherSnakes.Add(playerState.PlayerId, newSnake);
            // Do not move AI locally; server drives via snapshots via RemoteSnake interpolator
            newSnake.moveToTarget.enableMoving = false;
            var remote = newSnakeObj.GetComponent<RemoteSnake>();
            if (remote == null) remote = newSnakeObj.AddComponent<RemoteSnake>();
            remote.ConfigureServerClock(serverTickToSec, serverTimeOffset);
            remote.SetTargetLength(playerState.TargetLength);
            float serverSecondsSeed = (float)(System.DateTime.UtcNow - System.DateTime.UnixEpoch).TotalSeconds;
            remote.OnServerUpdate(new Vector2(startPos.x, startPos.y), serverSecondsSeed, playerState.CurrentSpeed > 0.01f ? playerState.CurrentSpeed : (playerState.BaseSpeed > 0f ? playerState.BaseSpeed : 0f));
        }
    }

    /// <summary>
    /// Spawns a remote/AI snake from kinematics-only data.
    /// </summary>
    private void SpawnRemoteSnake(SnakeKinematicsDto kin)
    {
        if (enemyBasePrefab == null)
        {
            Debug.LogError("Prefab for enemyBasePrefab is not assigned!");
            return;
        }

        Vector3 startPos = new Vector3(kin.HeadPosition.X, kin.HeadPosition.Y, 0);
        GameObject newSnakeObj = Instantiate(enemyBasePrefab, startPos, Quaternion.identity);
        newSnakeObj.tag = "EnemySnake";

        SegmentedCreator newSnake = newSnakeObj.GetComponent<SegmentedCreator>();
        if (newSnake == null)
        {
            Debug.LogError($"CRITICAL: Prefab '{enemyBasePrefab.name}' is missing 'SegmentedCreator'!", enemyBasePrefab);
            Destroy(newSnakeObj);
            return;
        }

        // Apply skin and length
        Skin skin = skinDatabase.GetSkinByID(kin.SkinID);
        newSnake.skin = skin;
        newSnake.ribCount = Mathf.Max(2, kin.TargetLength);
        newSnake.RefreshSprites();
        ApplyScaleFromMass(newSnake, kin.Mass);

        // Configure movement for remote rendering only
        newSnake.moveToTarget.enableMoving = false;
        newSnake.moveToTarget.moveThroughTarget = true;
        newSnake.moveToTarget.enableWobble = false;

        Transform target = new GameObject($"{kin.PlayerId}_Target").transform;
        target.position = startPos;
        newSnake.moveToTarget.Target = target;

        otherSnakes.Add(kin.PlayerId, newSnake);

        // Remote interpolation component
        var remote = newSnakeObj.GetComponent<RemoteSnake>();
        if (remote == null) remote = newSnakeObj.AddComponent<RemoteSnake>();
        remote.ConfigureServerClock(serverTickToSec, serverTimeOffset);
        float serverSecondsNow = (float)(System.DateTime.UtcNow - System.DateTime.UnixEpoch).TotalSeconds;
        remote.SetTargetLength(kin.TargetLength);
        remote.OnServerUpdate(new Vector2(startPos.x, startPos.y), serverSecondsNow, kin.CurrentSpeed > 0.01f ? kin.CurrentSpeed : (kin.BaseSpeed > 0f ? kin.BaseSpeed : 0f));
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
    
    // Overload for kinematics-only payloads
    private void ApplyScaleFromMass(SegmentedCreator snake, int mass)
    {
        if (snake == null || snake.skin == null) return;
        var refSprite = snake.skin.BodySprite != null ? snake.skin.BodySprite : snake.skin.HeadSprite;
        if (refSprite == null) return;
        var size = refSprite.bounds.size;
        float spriteThickness = Mathf.Max(size.x, size.y);
        if (spriteThickness <= 0.0001f) return;
        float desiredThickness = Mathf.Clamp(baseThickness + (mass * thicknessPerMass), minThickness, maxThickness);
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
            await hubConnection.InvokeAsync("ReportFoodEaten", foodId, myPlayerId);
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




