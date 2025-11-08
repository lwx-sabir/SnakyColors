using Khela.Game.Models;
using Khela.Game.Models.Configs; // Import Configs
using Khela.Game.Services.Redis;
using Microsoft.Extensions.Hosting;
using System.Numerics;
using System.Linq;

namespace Khela.Game.Services
{
    public class AIService : BackgroundService
    {
        private readonly ILogger<AIService> _logger;
        private readonly IRedisService _redis;
        private readonly WorldManagerService _worldManager;
        private readonly GameEngine _gameEngine;

        private readonly TimeSpan _aiTickInterval = TimeSpan.FromMilliseconds(100); // 10Hz AI updates
        private const string SNAKE_KEY_PREFIX = "snake:";
        private const string WORLD_KEY_PREFIX = "world:"; 
        public AIService(ILogger<AIService> logger, IRedisService redis, WorldManagerService worldManager, GameEngine gameEngine)
        {
            _logger = logger;
            _redis = redis;
            _worldManager = worldManager; 
            _gameEngine = gameEngine;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Delay(5000, stoppingToken); // Wait for services
            _logger.LogInformation("AI Service started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                var startTime = DateTime.UtcNow;
                try
                {
                    var worldKeys = await _redis.GetKeysByPatternAsync(WORLD_KEY_PREFIX + "*");
                     
                    foreach (var worldKey in worldKeys)
                    {
                        await ManageAIForWorld(worldKey, stoppingToken);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in AI Service loop.");
                }
                 
                var endTime = DateTime.UtcNow;
                var timeToWait = _aiTickInterval - (endTime - startTime);
                if (timeToWait > TimeSpan.Zero)
                {
                    await Task.Delay(timeToWait, stoppingToken);
                }
            }
        }

        public AIService(ILogger<AIService> logger, IRedisService redis, WorldManagerService worldManager)
        {
            _logger = logger;
            _redis = redis;
            _worldManager = worldManager;
        }
         
        private async Task ManageAIForWorld(string worldKey, CancellationToken token)
        {
            var world = await _redis.GetAsync<WorldState>(worldKey);
            if (world == null || world.Config == null || world.CurrentState != GameState.Running) return;

            // 1. Get all *alive* AI snake count
            var aiKeys = world.AISnakeIds.Keys.Select(id => SNAKE_KEY_PREFIX + id).ToArray();
            int aliveAICount = 0;
            if (aiKeys.Length > 0)
            {
                var aiSnakes = (await _redis.GetBatchAsync<PlayerState>(aiKeys)).Values;
                foreach (var aiSnake in aiSnakes)
                {
                    if (aiSnake != null && aiSnake.IsAlive)
                    {
                        aliveAICount++;
                    }
                }
            }

            // 2. Spawn new AI if needed
            int aiNeeded = world.Config.TargetAISnakeCount - aliveAICount;
            if (aiNeeded > 0)
            {
                _logger.LogInformation($"World {world.WorldId} needs {aiNeeded} AI. Spawning...");
                for (int i = 0; i < aiNeeded; i++)
                {
                    string aiPlayerId = $"ai-pid-{Guid.NewGuid():N}";
                    string aiConnId = $"ai-conn-{Guid.NewGuid():N}"; // This is just a placeholder

                    await _worldManager.AddPlayerToWorldAsync(aiConnId, world.WorldId, aiPlayerId, "greenskin", isAi: true);
                }
            }

            // Move AI toward nearest food and report eats
            await MoveAIsAndEat(world);
             
        } 

        private const float EAT_RADIUS = 0.75f;

        private async Task MoveAIsAndEat(WorldState world)
        {
            if (world.AISnakeIds.Count == 0) return;

            var db = _redis.GetDatabase();
            float worldHalf = world.Config.WorldSize / 2f;

            var foodIds = world.FoodIds.Keys.ToList();
            var foodMap = await _redis.GetBatchAsync<Food>(foodIds.Select(id => $"food:{id}"));

            foreach (var aiId in world.AISnakeIds.Keys.ToList())
            {
                string playerKey = $"{SNAKE_KEY_PREFIX}{aiId}";
                string lockKey = $"lock:{playerKey}";
                string lockToken = Guid.NewGuid().ToString();
                if (await db.LockTakeAsync(lockKey, lockToken, TimeSpan.FromMilliseconds(50)))
                {
                    try
                    {
                        var snake = await _redis.GetAsync<PlayerState>(playerKey);
                        if (snake == null || !snake.IsAlive) continue;

                        var segments = snake.BodySegments ?? new List<SerializableVector2>();
                        Vector2 head = segments.Count > 0 ? segments[^1] : Vector2.Zero;

                        // Find nearest food
                        Food nearest = null;
                        float nearestDist2 = float.MaxValue;
                        foreach (var kv in foodMap)
                        {
                            var f = kv.Value;
                            if (f == null) continue;
                            float dx = f.Position.X - head.X;
                            float dy = f.Position.Y - head.Y;
                            float d2 = dx * dx + dy * dy;
                            if (d2 < nearestDist2)
                            {
                                nearestDist2 = d2;
                                nearest = f;
                            }
                        }

                        // Direction
                        Vector2 dir;
                        if (nearest != null)
                        {
                            var toFood = new Vector2(nearest.Position.X - head.X, nearest.Position.Y - head.Y);
                            dir = toFood.Length() > 0.0001f ? Vector2.Normalize(toFood) : Vector2.Zero;
                        }
                        else
                        {
                            dir = new Vector2(Random.Shared.NextSingle() * 2 - 1, Random.Shared.NextSingle() * 2 - 1);
                            if (dir.Length() > 0.0001f) dir = Vector2.Normalize(dir);
                        }

                        float speed = snake.IsBoosting ? snake.BoostSpeed : (snake.BaseSpeed > 0 ? snake.BaseSpeed : snake.CurrentSpeed);
                        float dt = (float)_aiTickInterval.TotalSeconds; // match service tick
                        Vector2 newHead = new Vector2(head.X + dir.X * speed * dt, head.Y + dir.Y * speed * dt);

                        // clamp and gently steer inward if near boundary to prevent sticking outside
                        newHead.X = Math.Clamp(newHead.X, -worldHalf, worldHalf);
                        newHead.Y = Math.Clamp(newHead.Y, -worldHalf, worldHalf);
                        if (Math.Abs(newHead.X) >= worldHalf - 0.5f || Math.Abs(newHead.Y) >= worldHalf - 0.5f)
                        {
                            var inward = Vector2.Zero - newHead;
                            if (inward.Length() > 0.0001f)
                                inward = Vector2.Normalize(inward);
                            // Nudge head slightly inward to escape boundary
                            newHead += inward * (speed * dt * 0.5f);
                            newHead.X = Math.Clamp(newHead.X, -worldHalf, worldHalf);
                            newHead.Y = Math.Clamp(newHead.Y, -worldHalf, worldHalf);
                        }

                        // update segments
                        segments.Add(new SerializableVector2(newHead));
                        int targetLen = snake.TargetLength;
                        while (segments.Count > targetLen && segments.Count > 1)
                            segments.RemoveAt(0);
                        snake.BodySegments = segments;

                        await _redis.SetAsync(playerKey, snake);

                        // eat check
                        if (nearest != null)
                        {
                            float ex = nearest.Position.X - newHead.X;
                            float ey = nearest.Position.Y - newHead.Y;
                            if (ex * ex + ey * ey <= EAT_RADIUS * EAT_RADIUS && _gameEngine != null)
                            {
                                await _gameEngine.OnPlayerAteFood(aiId, nearest.Id);
                                foodMap.Remove($"food:{nearest.Id}");
                            }
                        }
                    }
                    finally
                    {
                        await db.LockReleaseAsync(lockKey, lockToken);
                    }
                }
            }
        }
    }
}
