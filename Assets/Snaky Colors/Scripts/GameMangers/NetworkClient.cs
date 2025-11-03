using Microsoft.AspNetCore.SignalR.Client; 
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;  

namespace SnakyColors
{
    public class NetworkClient : MonoBehaviour
    {
        public static NetworkClient Instance { get; private set; }

        [Header("Configuration")]
        [SerializeField] private string hubUrl = "http://localhost:5000/snakehub";

        [Header("Asset References")]
        [SerializeField] private GameObject snakeBasePrefab;
        [SerializeField] private SkinDatabase skinDatabase; 
        [SerializeField] private ItemDatabase itemDatabase;

        private HubConnection hubConnection;
        private SegmentedCreator localPlayerSnake;
        private SlitherMovement localInputController; // Renamed from SlitherInputController

        // Dictionaries to track spawned objects
        private Dictionary<string, SegmentedCreator> otherSnakes = new Dictionary<string, SegmentedCreator>();
        private Dictionary<int, GameObject> activeFood = new Dictionary<int, GameObject>();

        // Queues for data from the network thread
        private ConcurrentQueue<WorldUpdateDto> worldUpdates = new ConcurrentQueue<WorldUpdateDto>();
        private ConcurrentQueue<FoodEatenEvent> foodEatenQueue = new ConcurrentQueue<FoodEatenEvent>();

        // Event struct for queuing
        private readonly struct FoodEatenEvent
        {
            public int FoodId { get; }
            public string PlayerId { get; }
            public FoodEatenEvent(int foodId, string playerId) { FoodId = foodId; PlayerId = playerId; }
        }


        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
                return;
            }

            if (skinDatabase != null)
                skinDatabase.Initialize();
            else
                Debug.LogError("NetworkClient: SkinDatabase is not assigned!");

            if (itemDatabase != null)
                itemDatabase.Initialize();
            else
                Debug.LogError("NetworkClient: ItemDatabase is not assigned!");
        }

        async void Start()
        {
            hubConnection = new HubConnectionBuilder()
                .WithUrl(hubUrl)
                .WithAutomaticReconnect()
                .Build();


            hubConnection.On("Pong", () =>
            {
                Debug.LogError("PONG RECEIVED! Connection is working.");
            }); 

            hubConnection.On<WorldUpdateDto>("WorldUpdate", (worldState) =>
            {
                worldUpdates.Enqueue(worldState);  
            });

            hubConnection.On<int, string>("OnFoodEaten", (foodId, playerId) =>
            {
                foodEatenQueue.Enqueue(new FoodEatenEvent(foodId, playerId));
            }); 

            // --- End Handlers ---

            try
            {
                await hubConnection.StartAsync();
                Debug.Log("Network Client: Connection Started.");
                 
                await hubConnection.InvokeAsync("Ping"); 
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Network Client: Connection failed: {ex.Message}");
            }
        }

        private void Update()
        {
            // Process all queued data on the main thread
            while (worldUpdates.TryDequeue(out WorldUpdateDto worldState))
            {
                ProcessWorldUpdate(worldState);
            }

            while (foodEatenQueue.TryDequeue(out FoodEatenEvent foodEvent))
            {
                ProcessFoodEaten(foodEvent);
            }
        }

        /// <summary>
        /// Processes the main server state snapshot (snakes and food).
        /// </summary>
        private void ProcessWorldUpdate(WorldUpdateDto worldState)
        {
            if (worldState == null || hubConnection == null || hubConnection.State != HubConnectionState.Connected) return;

             HashSet<string> seenSnakes = new HashSet<string>();
            foreach (var snakeDto in worldState.Snakes)
            {
                if (snakeDto == null) continue;
                seenSnakes.Add(snakeDto.Id);
                seenSnakes.Add(snakeDto.Id);

                // --- LOCAL PLAYER ---
                if (snakeDto.Id == hubConnection.ConnectionId)
                {
                    if (localPlayerSnake == null)
                    {
                        SpawnLocalPlayer(snakeDto);
                    }
                    else
                    {
                        localPlayerSnake.ribCount = snakeDto.Length;
                        // TODO: Client-Side Prediction / Reconciliation logic goes here
                        // e.g., correct player position if it deviates too far from server
                    }
                }
                // --- OTHER PLAYER ---
                else if (otherSnakes.TryGetValue(snakeDto.Id, out SegmentedCreator snake))
                {
                    // Snake exists, update its target's position for smooth interpolation
                    if (snake.moveToTarget.Target != null)
                    {
                        snake.moveToTarget.Target.position = new Vector3(snakeDto.HeadX, snakeDto.HeadY, 0);
                    }
                    // Update snake length
                    snake.ribCount = snakeDto.Length;
                }
                else
                {
                    // Snake is new, spawn it
                    SpawnOtherSnake(snakeDto);
                }
            }

            // --- Process Food (Spawning) ---
            HashSet<int> seenFood = new HashSet<int>();
            foreach (var foodDto in worldState.Food)
            {
                if (foodDto == null) continue;
                seenFood.Add(foodDto.Id);

                // Only spawn food if we don't already know about it
                if (!activeFood.ContainsKey(foodDto.Id))
                {
                    //Get the correct ItemData from the database
                    ItemData itemToSpawn = itemDatabase.GetItemByKey(foodDto.ItemKey);
                    if (itemToSpawn == null)
                    {
                        Debug.LogError($"Failed to find ItemData for key: {foodDto.ItemKey}");
                        continue;
                    }

                    //Get that specific item from the pool
                    GameObject newFoodObj = ItemPooler.Instance.GetPooledObject(itemToSpawn); 

                    if (newFoodObj != null)
                    {
                        newFoodObj.transform.position = new Vector3(foodDto.PosX, foodDto.PosY, 0);
                         
                        newFoodObj.SetActive(true);
                         
                        if (newFoodObj.TryGetComponent<GeneratedItem>(out var genItem)) 
                        {
                            genItem.SetData(itemToSpawn, localPlayerSnake.transform);
                        }

                        activeFood.Add(foodDto.Id, newFoodObj);
                    }
                }
            }

            // --- Despawn Snakes ---
            List<string> snakesToDestroy = new List<string>();
            foreach (var snakeId in otherSnakes.Keys)
            {
                if (!seenSnakes.Contains(snakeId))
                    snakesToDestroy.Add(snakeId);
            }
            foreach (var snakeId in snakesToDestroy)
            {
                if (otherSnakes.TryGetValue(snakeId, out SegmentedCreator snakeToDestroy))
                {
                    if (snakeToDestroy.moveToTarget.Target != null)
                    {
                        Destroy(snakeToDestroy.moveToTarget.Target.gameObject); // Clean up target
                    }
                    Destroy(snakeToDestroy.gameObject); // Destroy snake
                    otherSnakes.Remove(snakeId);
                }
            }

            // --- Despawn Food (from state mismatch) ---
            //List<int> foodToDestroy = new List<int>();
            //foreach (var foodId in activeFood.Keys)
            //{
            //    if (!seenFood.Contains(foodId))
            //        foodToDestroy.Add(foodId);
            //}
            //foreach (var foodId in foodToDestroy)
            //{
            //    DespawnFood(foodId); // Use helper
            //}
        }

        /// <summary>
        /// Processes a specific "OnFoodEaten" event from the server.
        /// </summary> 
        private void ProcessFoodEaten(FoodEatenEvent foodEvent)
        {
            // Find the food object by its ID
            if (!activeFood.TryGetValue(foodEvent.FoodId, out GameObject foodObj))
            {
                // We received an "eaten" event for food we don't have.
                // This is fine, it might be a late message.
                return;
            }

            // We found the food, remove it from our tracking immediately
            activeFood.Remove(foodEvent.FoodId);

            if (foodObj == null) return;
             
            if (foodObj.TryGetComponent<GeneratedItem>(out var genItem) &&
                genItem.TryGetComponent<FruitCollectEffect>(out var effect) &&
                genItem.data != null) // Make sure the item has its data
            {
                Transform collectorHead = null;

                // Find who ate it
                if (hubConnection != null && foodEvent.PlayerId == hubConnection.ConnectionId && localPlayerSnake != null)
                {
                    collectorHead = localPlayerSnake.transform; // We ate it
                }
                else if (otherSnakes.TryGetValue(foodEvent.PlayerId, out var otherSnake))
                {
                    collectorHead = otherSnake.transform; // Another snake ate it
                }

                if (collectorHead != null)
                {
                    effect.playerHead = collectorHead; // Set the target
                     
                    effect.PlayCollectAnimation(
                        "0",  
                        genItem.data.itemColor,
                        genItem.data.collectibleType,
                        genItem.data.icon
                    ); 
                }
                else
                { 
                    genItem.ReturnToPool();
                }
            }
            else if (genItem != null)
            {
                // Has GeneratedItem but no FruitCollectEffect, so just pool it
                genItem.ReturnToPool();
            }
            else
            {
                // Fallback for an object without a GeneratedItem script
                foodObj.SetActive(false);
            }
        }

        /// <summary>
        /// Helper to despawn food and remove from tracking.
        /// </summary>
        private void DespawnFood(int foodId)
        {
            if (activeFood.TryGetValue(foodId, out GameObject foodObj))
            {
                if (foodObj.TryGetComponent<GeneratedItem>(out var genItem))
                {
                    genItem.ReturnToPool(); // Return to pool
                }
                else
                {
                    foodObj.SetActive(false); // Fallback
                }
                activeFood.Remove(foodId);
            }
        }


        // Inside NetworkClient.cs
        private void SpawnLocalPlayer(SnakeStateDto snakeDto)
        {
            if (snakeBasePrefab == null || skinDatabase == null)
            {
                Debug.LogError("Cannot spawn player: snakeBasePrefab or skinDatabase is not assigned!");
                return;
            }

            Vector3 startPos = new Vector3(snakeDto.HeadX, snakeDto.HeadY, 0);
            GameObject playerObj = Instantiate(snakeBasePrefab, startPos, Quaternion.identity);

            localPlayerSnake = playerObj.GetComponent<SegmentedCreator>();
            if (localPlayerSnake == null)
            {
                Debug.LogError($"CRITICAL: 'snakeBasePrefab' is missing the 'SegmentedCreator' script!", snakeBasePrefab);
                Destroy(playerObj);
                return;
            }

            // Apply skin and length
            Skin skin = skinDatabase.GetSkinByID(snakeDto.SkinID);
            localPlayerSnake.skin = skin;
            localPlayerSnake.ribCount = snakeDto.Length;
            localPlayerSnake.RefreshSprites();

            // --- THIS IS THE FIX ---
            // 1. Setup its movement target for local client-side prediction
            localPlayerSnake.moveToTarget.enableMoving = true;
            localPlayerSnake.moveToTarget.moveThroughTarget = true;
            // 2. Create the target
            Transform target = new GameObject($"_LOCAL_PLAYER_TARGET").transform;
            target.position = startPos;
            // 3. Assign the target
            localPlayerSnake.moveToTarget.Target = target;
            // ---------------------

            // Add and set up the input controller
            localInputController = playerObj.AddComponent<SlitherMovement>();

            // Tell the camera to follow our new player
            SlitherCameraFollow camFollow = FindObjectOfType<SlitherCameraFollow>();
            if (camFollow != null)
            {
                camFollow.SetPlayer(playerObj.transform);
            }

            // Add components for local interactions
            playerObj.AddComponent<SlitherCollisionManager>();
            if (playerObj.GetComponent<Collider2D>() == null)
            {
                var col = playerObj.AddComponent<CircleCollider2D>();
                col.isTrigger = true;
                col.radius = 0.5f;
            }
        }

        private void SpawnOtherSnake(SnakeStateDto snakeDto)
        {
            if (snakeBasePrefab == null || skinDatabase == null) return;

            GameObject newSnakeObj = Instantiate(snakeBasePrefab,
                new Vector3(snakeDto.HeadX, snakeDto.HeadY, 0),
                Quaternion.identity);

            SegmentedCreator newSnake = newSnakeObj.GetComponent<SegmentedCreator>();

            // --- ADD THIS CHECK ---
            if (newSnake == null)
            {
                Debug.LogError($"CRITICAL: 'snakeBasePrefab' is missing the 'SegmentedCreator' script!", snakeBasePrefab);
                Destroy(newSnakeObj);
                return;
            }
            // ---------------------

            Skin skin = skinDatabase.GetSkinByID(snakeDto.SkinID);
            newSnake.skin = skin;
            newSnake.ribCount = snakeDto.Length;
            newSnake.RefreshSprites();

            // Setup its movement target
            newSnake.moveToTarget.enableMoving = true;
            newSnake.moveToTarget.moveThroughTarget = true;
            Transform target = new GameObject($"{snakeDto.Id}_Target").transform;
            target.position = new Vector3(snakeDto.HeadX, snakeDto.HeadY, 0);
            newSnake.moveToTarget.Target = target;

            otherSnakes.Add(snakeDto.Id, newSnake);
        }

        // --- Public methods for input scripts to call ---

        public async Task SendTarget(UnityEngine.Vector2 targetPos)
        {
            if (hubConnection == null || hubConnection.State != HubConnectionState.Connected) return;
            try
            {
                await hubConnection.InvokeAsync("UpdateTarget", targetPos.x, targetPos.y);
            }
            catch (System.Exception ex) { Debug.LogWarning($"Failed to send target: {ex.Message}"); }
        }

        public async Task SendBoost(bool isBoosting)
        {
            if (hubConnection == null || hubConnection.State != HubConnectionState.Connected) return;
            try
            {
                await hubConnection.InvokeAsync("SetBoost", isBoosting);
            }
            catch (System.Exception ex) { Debug.LogWarning($"Failed to send boost: {ex.Message}"); }
        }

        private async void OnDestroy()
        {
            if (hubConnection != null)
            {
                await hubConnection.DisposeAsync();
            }
        }
    }
}


