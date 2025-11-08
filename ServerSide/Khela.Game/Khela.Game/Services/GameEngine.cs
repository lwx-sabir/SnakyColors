using Khela.Game.Services.Redis; 
using Khela.Game.Models;
using Khela.Game.Models.Configs; 

namespace Khela.Game.Services
{
    public class GameEngine : BackgroundService
    {
        private readonly IRedisService _redis;
        private readonly ILogger<GameEngine> _logger;
        private readonly FoodService _foodService; 

        public event Action<string, int, string> OnFoodEaten;
        public event Action<PlayerState, PlayerState> PlayerDied;

        private readonly int _tickRate;
        private readonly WorldConfig _defaultConfig;
        private readonly TimeSpan _tickInterval;
        private const string SNAKE_KEY_PREFIX = "snake:";
        private const string WORLD_KEY_PREFIX = "world:";
        private const string FOOD_KEY_PREFIX = "food:";

        public GameEngine(IRedisService redis, ILogger<GameEngine> logger, FoodService foodService, WorldManagerService worldManager)
        {
            _redis = redis;
            _logger = logger;
            _foodService = foodService; 
            _defaultConfig = new WorldConfig();
            _tickRate = _defaultConfig.TickRate;
            _tickInterval = TimeSpan.FromMilliseconds(1000.0 / _tickRate);
        }

        public async Task OnPlayerStateUpdate(string playerId, List<SerializableVector2> bodySegments, bool isBoosting)
        {
            if (string.IsNullOrEmpty(playerId)) return;

            string playerKey = SNAKE_KEY_PREFIX + playerId;
            string lockKey = $"lock:{playerKey}";
            string lockToken = Guid.NewGuid().ToString(); 
            var db = _redis.GetDatabase();
             
            if (await db.LockTakeAsync(lockKey, lockToken, TimeSpan.FromSeconds(1)))
            {
                try
                {
                    var snake = await _redis.GetAsync<PlayerState>(playerKey);
                    if (snake == null || !snake.IsAlive) return; 

                    // Apply state update (server-authoritative)
                    snake.BodySegments = bodySegments;
                    snake.IsBoosting = isBoosting;
                    // Ignore client-reported speed; derive from boost/base
                    if (isBoosting)
                    {
                        snake.CurrentSpeed = snake.BoostSpeed;
                    }
                    else
                    {
                        // Use BaseSpeed when available; fallback to existing CurrentSpeed for backward compatibility
                        snake.CurrentSpeed = (snake.BaseSpeed > 0f) ? snake.BaseSpeed : snake.CurrentSpeed;
                    }

                    await _redis.SetAsync(playerKey, snake);
                }
                finally
                {
                    await db.LockReleaseAsync(lockKey, lockToken);
                }
            } 
        }

        public async Task OnPlayerAteFood(string playerId, int foodId)
        {
            if (string.IsNullOrEmpty(playerId)) return;

            string playerKey = SNAKE_KEY_PREFIX + playerId;
            string foodKey = FOOD_KEY_PREFIX + foodId;
            string lockKey = $"lock:{playerKey}";
            string lockToken = Guid.NewGuid().ToString();
            var db = _redis.GetDatabase();

            // Try to acquire the lock
            if (await db.LockTakeAsync(lockKey, lockToken, TimeSpan.FromSeconds(1)))
            {
                try
                {
                    var snake = await _redis.GetAsync<PlayerState>(playerKey);
                    var food = await _redis.GetAsync<Food>(foodKey);

                    if (snake == null || !snake.IsAlive || food == null) return;

                    // Validate proximity on server to prevent remote misreports
                    const float EAT_RADIUS = 0.75f; // tighten if bots appear to eat at range
                    var head = snake.HeadPosition;
                    float dx = food.Position.X - head.X;
                    float dy = food.Position.Y - head.Y;
                    if (dx * dx + dy * dy > EAT_RADIUS * EAT_RADIUS)
                    {
                        // Too far; ignore spurious report
                        return;
                    }

                    // 1. Grant score
                    snake.Score += 10;
                    await _redis.SetAsync(playerKey, snake); // Save INSIDE the lock

                    // 2. Remove food
                    var world = await _redis.GetAsync<WorldState>(WORLD_KEY_PREFIX + snake.CurrentWorldId);
                    await _foodService.RemoveFoodAsync(world, foodId);

                    // 3. Fire event
                    OnFoodEaten?.Invoke(playerId, foodId, snake.CurrentWorldId);
                    _logger.LogInformation($"OnPlayerAteFood: player={playerId}, food={foodId}, score={snake.Score}");
                }
                finally
                {
                    await db.LockReleaseAsync(lockKey, lockToken); // Always release the lock
                }
            } 
        }

        public async Task OnPlayerDied(string deadPlayerId, string killerPlayerId)
        {
            if (string.IsNullOrEmpty(deadPlayerId)) return;

            string playerKey = SNAKE_KEY_PREFIX + deadPlayerId;
            string lockKey = $"lock:{playerKey}";
            string lockToken = Guid.NewGuid().ToString();
            var db = _redis.GetDatabase();

            // We use a slightly longer lock here because
            // death is a critical, one-time event.
            if (await db.LockTakeAsync(lockKey, lockToken, TimeSpan.FromSeconds(3)))
            {
                try
                {
                    var deadSnake = await _redis.GetAsync<PlayerState>(playerKey);
                    // Check if already dead (e.g., race condition)
                    if (deadSnake == null || !deadSnake.IsAlive) return;

                    _logger.LogInformation($"Player {deadPlayerId} reported death.");
                    deadSnake.IsAlive = false;

                    // --- CRITICAL: Save the dead state immediately ---
                    // This prevents any other process (like OnPlayerStateUpdate)
                    // from reading an "Alive" state.
                    await _redis.SetAsync(playerKey, deadSnake);

                    // TODO: Spawn food from dead snake 
                    PlayerState killerSnake = null;
                    if (!string.IsNullOrEmpty(killerPlayerId))
                    { 
                        killerSnake = await _redis.GetAsync<PlayerState>(SNAKE_KEY_PREFIX + killerPlayerId);
                    }

                    // Fire event for broadcast
                    PlayerDied?.Invoke(deadSnake, killerSnake);
                }
                finally
                {
                    await db.LockReleaseAsync(lockKey, lockToken);
                }
            } 
        }

        // --- Main Game Loop ---
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var startTime = DateTime.UtcNow;

                var worldKeys = await _redis.GetKeysByPatternAsync(WORLD_KEY_PREFIX + "*");
                var processingTasks = worldKeys.Select(key => ProcessWorldTick(key, stoppingToken));
                await Task.WhenAll(processingTasks);

                var endTime = DateTime.UtcNow;
                var timeToWait = _tickInterval - (endTime - startTime);
                if (timeToWait > TimeSpan.Zero)
                {
                    await Task.Delay(timeToWait, stoppingToken);
                }
            }
        }

        private async Task ProcessWorldTick(string worldKey, CancellationToken token)
        {
            var world = await _redis.GetAsync<WorldState>(worldKey);
            if (world == null || world.Config == null || world.CurrentState != GameState.Running) return;
            if (world.Config.TargetAISnakeCount < 1) world.Config = _defaultConfig;
             
            await _foodService.ManageFoodSpawningAsync(world);
              
            world.Tick++;
            world.LastUpdated = DateTime.UtcNow;
            await _redis.SetAsync(worldKey, world);
        } 
    }
}
