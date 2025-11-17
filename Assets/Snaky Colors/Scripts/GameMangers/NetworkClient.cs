using Microsoft.AspNetCore.SignalR.Client;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using SnakyColors;
using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using UnityEngine.Playables;
using static UnityEngine.InputManagerEntry;

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
    [HideInInspector] public Vector2 PendingInput;

    // --- Local World State ---
    private Dictionary<string, SegmentedCreator> otherSnakes = new Dictionary<string, SegmentedCreator>();
    private Dictionary<int, GameObject> activeFood = new Dictionary<int, GameObject>();

    // --- Thread-Safe Queues ---
    private ConcurrentQueue<WorldUpdateDto> worldUpdates = new ConcurrentQueue<WorldUpdateDto>();
    private ConcurrentQueue<FoodEatenEvent> foodEatenQueue = new ConcurrentQueue<FoodEatenEvent>();
    private ConcurrentQueue<PlayerDiedEvent> playerDiedQueue = new ConcurrentQueue<PlayerDiedEvent>();
    private ConcurrentQueue<string> playerLeftQueue = new ConcurrentQueue<string>();
    private ConcurrentQueue<PlayerStateDto> joinSuccessQueue = new ConcurrentQueue<PlayerStateDto>();
    private ConcurrentQueue<FoodDeltaDto> foodDeltaQueue = new();

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
        // OnScreenDebug.Log("handle registering");
        hubConnection.On("Pong", () => { Debug.Log("PONG RECEIVED! Connection is working."); });

        hubConnection.On<WorldUpdateDto, FoodDeltaDto>("WorldUpdate",
        (state, foodDelta) =>
        {
            worldUpdates.Enqueue(state);
            foodDeltaQueue.Enqueue(foodDelta);
        });

        hubConnection.On<PlayerStateDto>("OnJoinSuccess", (playerState) => { joinSuccessQueue.Enqueue(playerState); });
        hubConnection.On<int, string>("OnFoodEaten", (foodId, playerId) => { foodEatenQueue.Enqueue(new FoodEatenEvent(foodId, playerId)); });
        hubConnection.On<string, string>("OnPlayerDied", (deadPlayerId, message) => { playerDiedQueue.Enqueue(new PlayerDiedEvent(deadPlayerId, message)); });
        hubConnection.On<string>("OnPlayerLeft", (playerId) => { playerLeftQueue.Enqueue(playerId); });
        hubConnection.On<string>("JoinFailed", (msg) => { /*OnScreenDebug.Log("join failed: " + msg);*/ Debug.LogError($"JoinFailed: {msg}"); });
    }

    public async Task ConnectAsync(string playerId, string skinId)
    {
        if (hubConnection != null && hubConnection.State == HubConnectionState.Connected)
        {
            Debug.LogWarning("NetworkClient: already connected.");
            return;
        }

        try
        {

            hubConnection = new HubConnectionBuilder()
                .WithUrl(hubUrl, options =>
                {
                    options.HttpMessageHandlerFactory = _ => new HttpClientHandler();
                })
                .AddNewtonsoftJsonProtocol(options =>
                {
                    options.PayloadSerializerSettings.NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore;
                    options.PayloadSerializerSettings.Formatting = Newtonsoft.Json.Formatting.None;
                })
                .ConfigureLogging(logging =>
                {
                    logging.SetMinimumLevel(LogLevel.Debug);
                })
                .WithAutomaticReconnect()
                .Build();

            RegisterHubHandlers();
            //  OnScreenDebug.Log("handle register complete: ");

            await hubConnection.StartAsync();
            Debug.Log("Network Client: Connection Started.");
            // OnScreenDebug.Log("connection started: "+  hubUrl);
            await hubConnection.InvokeAsync("Ping");

            float segmentSpacing = skinDatabase.GetSkinByID(skinId)?.segmentSpacing ?? 0.87f;

            await hubConnection.InvokeAsync("JoinMainWorld", playerId, skinId, segmentSpacing);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Network Client: Connection failed: {ex.Message}");
            //  OnScreenDebug.Log("connection failed: " + ex.Message +hubUrl);
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
        // Coalesce world updates: process only the most recent snapshot per frame
        WorldUpdateDto latestWorld = null;
        while (worldUpdates.TryDequeue(out WorldUpdateDto wst)) latestWorld = wst;
        if (latestWorld != null)
        {
            ProcessWorldUpdate(latestWorld);
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
        // Coalesce food deltas: apply only the latest delta per frame
        FoodDeltaDto latestDelta = null;
        while (foodDeltaQueue.TryDequeue(out var d)) latestDelta = d;
        if (latestDelta != null)
        {
            ApplyFoodDelta(latestDelta);
        }
    }

    private void HandleJoinSuccess(PlayerStateDto playerState)
    {
        myPlayerId = playerState.PlayerId;
        Debug.Log($"Successfully joined world. My PlayerID is: {myPlayerId}");
        // OnScreenDebug.Log("Successfully joined: " + playerState?.PlayerId);
        if (localPlayerSnake == null)
        {
            this.localPlayerState = playerState;
            SpawnSnake(playerState, Time.time + serverTimeOffset);
        }
    }

    private static readonly HashSet<string> _seenSnakes = new HashSet<string>(128);

    private void ProcessWorldUpdate(WorldUpdateDto state)
    {
        if (state == null || string.IsNullOrEmpty(myPlayerId))
            return;

        CurrentWorldSize = state.WorldSize;

        // --- SERVER CLOCK SYNC (smooth every update, not only first) ---

        {
            float serverSec = (float)state.ServerTimeSec;
            float localSec = Time.time;
            float rawOffset = serverSec - localSec;

            if (!serverClockInit)
            {
                serverTimeOffset = rawOffset;
                serverClockInit = true;
            }
            else
            {
                const float alpha = 0.03f;
                serverTimeOffset = Mathf.Lerp(serverTimeOffset, rawOffset, alpha);
            }
        }

        _seenSnakes.Clear();

        // ---------------------------------------------------------
        // PROCESS SNAKES (exact same behavior, but no LINQ)
        // ---------------------------------------------------------

        var snakesArray = state.Snakes;
        int snakeCount = snakesArray.Length;

        for (int i = 0; i < snakeCount; i++)
        {
            var kin = snakesArray[i];
            if (kin == null) continue;
            string id = kin.PlayerId;
            _seenSnakes.Add(id);

            if (id == myPlayerId)
            {
                // LOCAL snake reconciliation (same)
                if (localPlayerSnake != null)
                {
                    if (localPlayerState != null)
                    {
                        if (kin.CurrentSpeed > 0.01f) localPlayerState.CurrentSpeed = kin.CurrentSpeed;
                        if (kin.BaseSpeed > 0.01f) localPlayerState.BaseSpeed = kin.BaseSpeed;
                        localPlayerState.MaxTurningAngle = kin.MaxTurningAngle;
                        localPlayerState.Mass = kin.Mass;
                    }

                    if (!localPlayerSnake.TryGetComponent<LocalPredictedSnake>(out var remoteLocalPred))
                    {
                        remoteLocalPred = localPlayerSnake.gameObject.AddComponent<LocalPredictedSnake>();
                    }

                    remoteLocalPred.PendingInput = PendingInput;

                    float speedLP = (kin.CurrentSpeed > 0.01f ? kin.CurrentSpeed :
                                  (kin.BaseSpeed > 0f ? kin.BaseSpeed : 0f));

                    remoteLocalPred.OnServerUpdate(
                        new Vector2(kin.HeadPosition.X, kin.HeadPosition.Y),
                        (float)state.ServerTimeSec,
                        speedLP
                    );
                    remoteLocalPred.ServerOffset = serverTimeOffset;

                    if (localPlayerSnake.ribCount != kin.TargetLength)
                    {
                        localPlayerSnake.ribCount = kin.TargetLength;
                        localPlayerSnake.RefreshSprites();
                    }
                    ApplyScaleFromMass(localPlayerSnake, kin.Mass);
                }
                continue;
            }

            // REMOTE / AI SNAKE
            if (!otherSnakes.TryGetValue(id, out var snake))
            {
                SpawnRemoteSnake(kin, (float)state.ServerTimeSec);
                continue;
            }

            if (!snake.TryGetComponent<RemoteSnake>(out var remote))
            {
                remote = snake.gameObject.AddComponent<RemoteSnake>();
            }

            float speed = (kin.CurrentSpeed > 0.01f ? kin.CurrentSpeed :
                          (kin.BaseSpeed > 0f ? kin.BaseSpeed : 0f));

            remote.OnServerUpdate(
                new Vector2(kin.HeadPosition.X, kin.HeadPosition.Y),
                (float)state.ServerTimeSec,
                speed
            );
            remote.ServerOffset = serverTimeOffset;

            remote.SetTargetLength(kin.TargetLength);
            ApplyScaleFromMass(snake, kin.Mass);
        }

        // ---------------------------------------------------------
        // DESPAWN REMOVED SNAKES (no LINQ)
        // ---------------------------------------------------------

        foreach (var kv in otherSnakes.ToArray())
        {
            if (!_seenSnakes.Contains(kv.Key))
                DespawnSnake(kv.Key);
        }
    }


    /// <summary>
    /// Spawns any snake (local, remote, or AI)
    /// </summary>
    private void SpawnSnake(PlayerStateDto playerState, float serverTimeSec)
    {
        if (snakeBasePrefab == null)
        {
            Debug.LogError($"Prefab for {"snakeBasePrefab"} is not assigned!");
            return;
        }

        Vector3 startPos = new Vector3(playerState.HeadPosition.X, playerState.HeadPosition.Y, 0);
        GameObject newSnakeObj = Instantiate(snakeBasePrefab, startPos, Quaternion.identity);

        SegmentedCreator newSnake = newSnakeObj.GetComponent<SegmentedCreator>();
        if (newSnake == null)
        {
            Debug.LogError($"CRITICAL: Prefab '{snakeBasePrefab.name}' is missing 'SegmentedCreator'!", snakeBasePrefab);
            Destroy(newSnakeObj);
            return;
        }
        // Use server-provided SkinID for AI and players
        Skin skin = skinDatabase.GetSkinByID(playerState.SkinID);
        newSnake.skin = skin;
        newSnake.ribCount = playerState.TargetLength;
        newSnake.RefreshSprites();
        ApplyScaleFromMass(newSnake, playerState);

        localPlayerSnake = newSnake;
        newSnakeObj.AddComponent<SlitherMovement>();

        SlitherCameraFollow camFollow = FindObjectOfType<SlitherCameraFollow>();
        if (camFollow != null)
        {
            camFollow.SetPlayer(newSnakeObj.transform);
        }

        var remote = newSnakeObj.GetComponent<LocalPredictedSnake>();
        if (remote == null) remote = newSnakeObj.AddComponent<LocalPredictedSnake>();

        remote.SetTargetLength(playerState.TargetLength);
        remote.OnServerUpdate(new Vector2(startPos.x, startPos.y), serverTimeSec, playerState.CurrentSpeed > 0.01f
            ? playerState.CurrentSpeed : (playerState.BaseSpeed > 0f
            ? playerState.BaseSpeed : 0f));
        remote.ServerOffset = serverTimeOffset; 

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

    /// <summary>
    /// Spawns a remote/AI snake from kinematics-only data.
    /// </summary>
    private void SpawnRemoteSnake(SnakeKinematicsDto kin, float serverTimeSec)
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
        newSnake.moveToTarget.moveThroughTarget = false;
        newSnake.moveToTarget.enableWobble = false;

        otherSnakes.Add(kin.PlayerId, newSnake);

        // Remote interpolation component
        var remote = newSnakeObj.GetComponent<RemoteSnake>();
        if (remote == null) remote = newSnakeObj.AddComponent<RemoteSnake>();

        remote.SetTargetLength(kin.TargetLength);
        remote.OnServerUpdate(new Vector2(startPos.x, startPos.y), serverTimeSec, kin.CurrentSpeed > 0.01f ? kin.CurrentSpeed : (kin.BaseSpeed > 0f ? kin.BaseSpeed : 0f));
        remote.ServerOffset = serverTimeOffset;
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

    private void ApplyFoodDelta(FoodDeltaDto delta)
    {
        // --- Add new food ---
        foreach (var f in delta.Added)
        {
            if (!activeFood.TryGetValue(f.Id, out var obj))
            {
                var item = itemDatabase.GetItemByKey(f.ItemKey);
                if (item == null) continue;

                obj = ItemPooler.Instance.GetPooledObject(item);
                activeFood[f.Id] = obj;
            }

            if (obj == null)
                continue;

            // Ensure active, positioned, and bound to local player for magnet without expensive lookups
            obj.transform.position = new Vector3(f.PosX, f.PosY, 0);
            if (!obj.activeSelf) obj.SetActive(true);

            if (obj.TryGetComponent<GeneratedItem>(out var gen))
            {
                var item = itemDatabase.GetItemByKey(f.ItemKey);
                if (item != null && localPlayerSnake != null)
                    gen.SetData(item, localPlayerSnake.transform);
                gen.Id = f.Id;
            }
        }

        // --- Remove dead foods ---
        foreach (var id in delta.Removed)
        {
            if (activeFood.TryGetValue(id, out var obj))
            {
                if (obj.TryGetComponent<GeneratedItem>(out var gen))
                    gen.ReturnToPool();
                else
                    obj.SetActive(false);

                activeFood.Remove(id);
            }
        }
        // Avoid per-tick UI log spam; can be re-enabled for diagnostics
        // OnScreenDebug.Log("active food: " + activeFood.Count);
    }


    private void HandlePlayerDeath(string deadPlayerId, string message)
    {
        //Debug.Log($"DEATH: {message}");
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
                //  Debug.Log($"NET: OnFoodEaten food={foodEvent.FoodId} by={foodEvent.PlayerId} head=remote");
                genItem.PlayRemoteCollect(collectorHead, false);
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

    // Called by SlitherMovement
    public async Task SendState(SerializableVector2 inputDirection, bool isBoosting)
    {
        if (hubConnection == null || hubConnection.State != HubConnectionState.Connected) return;
        try
        {
            await hubConnection.InvokeAsync("UpdateState", inputDirection, isBoosting);
        }
        catch (Exception ex) { Debug.LogWarning($"Failed to send state: {ex.Message}"); }
    }

    // Called by SlitherCollisionManager
    public async Task ReportFoodEaten(int foodId)
    {
        if (hubConnection == null || hubConnection.State != HubConnectionState.Connected) return;
        try
        {
            // Debug.Log($"NET: ReportFoodEaten({foodId})");
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




