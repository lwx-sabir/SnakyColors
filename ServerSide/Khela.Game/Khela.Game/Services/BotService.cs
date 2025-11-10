using Khela.Game.Models;
using Khela.Game.Models.Configs;
using Khela.Game.Services.Redis;
using Microsoft.Extensions.Hosting;
using System.Numerics;
using System.Linq;
using Khela.Game.Dtos;
using StackExchange.Redis;
using System.Collections.Concurrent;

namespace Khela.Game.Services
{
    public class AIService : BackgroundService
    {
        private readonly ILogger<AIService> _logger;
        private readonly IRedisService _redis;
        private readonly WorldManagerService _worldManager;
        private readonly GameEngine _gameEngine;

        // === PERFORMANCE CONSTANTS ===
        private readonly TimeSpan _aiTickInterval = TimeSpan.FromMilliseconds(50); // 20 Hz movement
        private const string SNAKE_KEY_PREFIX = "snake:";
        private const string WORLD_KEY_PREFIX = "world:";
        private const string FOODCACHE_PREFIX = "foodcache:";

        // === Steering tunables ===
        private const float FOOD_ATTRACT_WEIGHT = 1.0f;
        private const float INWARD_WEIGHT = 0.35f;
        private const float PREV_DIR_WEIGHT = 0.3f;
        private const int SEGMENT_SAMPLE_STRIDE = 5;

        private const float EAT_RADIUS = 0.75f;
        private const float COLLISION_RADIUS = 0.75f;

        // Cache to avoid redundant Redis reads 
        private readonly ConcurrentDictionary<string, Dictionary<string, PlayerState>> _snakeCache = new();
        private readonly ConcurrentDictionary<string, (WorldState world, DateTime lastFetch)> _worldCache = new();
        private readonly TimeSpan _worldCacheTTL = TimeSpan.FromMilliseconds(500);
        private long _aiTickIndex = 0;


        // Persistent per-AI state
        private readonly ConcurrentDictionary<string, int> _targetFood = new();

        public AIService(ILogger<AIService> logger, IRedisService redis, WorldManagerService worldManager, GameEngine gameEngine)
        {
            _logger = logger;
            _redis = redis;
            _worldManager = worldManager;
            _gameEngine = gameEngine;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Delay(5000, stoppingToken); // Let other systems boot
            _logger.LogInformation("AI Service started (20Hz).");

            try { await Task.Delay(Random.Shared.Next(10, 25), stoppingToken); } catch { }

            while (!stoppingToken.IsCancellationRequested)
            {
                var tickStart = DateTime.UtcNow;
                try
                {
                    var worldKeys = await _redis.GetKeysByPatternAsync(WORLD_KEY_PREFIX + "*");

                    foreach (var worldKey in worldKeys)
                    {
                        try
                        {
                            await ManageAIForWorld(worldKey, stoppingToken);
                        }
                        catch(Exception ex)
                        {

                        } 
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "AI main loop error.");
                }

                var elapsed = DateTime.UtcNow - tickStart;
                var delay = _aiTickInterval - elapsed;
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, stoppingToken);

               // _logger.LogDebug($"AI tick took {elapsed.TotalMilliseconds:F1}ms");
            }
        }

        private async Task ManageAIForWorld(string worldKey, CancellationToken token)
        {
            var world = await GetWorldCachedAsync(worldKey);

            if (world == null || world.Config == null || world.CurrentState != GameState.Running)
                return;

            // Get AI snakes from Redis
            var aiKeys = world.AISnakeIds.Keys.Select(id => SNAKE_KEY_PREFIX + id).ToArray();
            var aiSnakes = new List<PlayerState>();

            if (aiKeys.Length > 0)
            {
                aiSnakes = (await _redis.GetBatchAsync<PlayerState>(aiKeys))
                           .Values.Where(s => s?.IsAlive == true)
                           .ToList();
            }

            int aliveAICount = aiSnakes.Count;
            int aiNeeded = world.Config.TargetAISnakeCount - aliveAICount;

            if (aiNeeded > 0)
            {
                _logger.LogInformation($"World {world.WorldId} needs {aiNeeded} AI snakes. Spawning...");

                for (int i = 0; i < aiNeeded; i++)
                {
                    string aiPlayerId = $"ai-pid-{Guid.NewGuid():N}";
                    string aiConnId = $"ai-conn-{Guid.NewGuid():N}";

                    await _worldManager.AddPlayerToWorldAsync(aiConnId, world.WorldId, aiPlayerId, "greenskin", isAi: true);
                     
                    world.AISnakeIds.TryAdd(aiPlayerId, true);
                }
                 
                await _redis.SetAsync(WORLD_KEY_PREFIX + world.WorldId, world);
                _worldCache.AddOrUpdate(WORLD_KEY_PREFIX + world.WorldId, (world, DateTime.UtcNow), (_, _) => (world, DateTime.UtcNow));

            }

            if (world.AISnakeIds.Count > 0)
                await MoveAIs(world, aiSnakes);
        }



        private async Task MoveAIs(WorldState world, List<PlayerState> aiSnakes)
        {
            _aiTickIndex++;
            var db = _redis.GetDatabase();
            float worldHalf = world.Config.WorldSize / 2f;
            float dt = (float)_aiTickInterval.TotalSeconds;

            // --- Lightweight food fetch ---
            var foodArr = await _redis.GetAsync<FoodStateDto[]>(FOODCACHE_PREFIX + world.WorldId);
            if (foodArr == null || foodArr.Length == 0) return;
            var foods = foodArr.Select(f => new Food(f.Id, new Vector2(f.PosX, f.PosY), f.ItemKey)).ToList();

            // --- Cached snake map for collisions ---
            if (!_snakeCache.TryGetValue(world.WorldId, out var allSnakesMap) || world.Tick % 10 == 0)
            {
                var allSnakeIds = world.SnakeIds.Keys.Concat(world.AISnakeIds.Keys).ToArray();
                var allSnakeKeys = allSnakeIds.Select(id => $"{SNAKE_KEY_PREFIX}{id}").ToArray();
                allSnakesMap = await _redis.GetBatchAsync<PlayerState>(allSnakeKeys);
                _snakeCache[world.WorldId] = allSnakesMap;
            }

            // --- AI Movement ---
            foreach (var snake in aiSnakes)
            {
                string aiId = snake.PlayerId;

                if (((StableHash(aiId) + _aiTickIndex) & 1) != 0)
                    continue; // staggered update to halve load

                var segments = snake.BodySegments ?? new List<SerializableVector2>();
                Vector2 head = segments.Count > 0 ? segments[^1] : Vector2.Zero;
                Vector2 prevDir = Vector2.Zero;
                if (segments.Count >= 2)
                {
                    var tail2 = segments[^1] - segments[^2];
                    if (tail2.Length() > 0.0001f) prevDir = Vector2.Normalize(tail2);
                }

                // --- Target food selection ---
                Food nearest = null;
                if (_targetFood.TryGetValue(aiId, out var targetId))
                    nearest = foods.FirstOrDefault(f => f.Id == targetId);

                if (nearest == null)
                {
                    var nearby = foods.OrderBy(f => Vector2.DistanceSquared(head, f.Position)).Take(8).ToList();
                    if (nearby.Count > 0)
                    {
                        int pick = Math.Abs(StableHash(aiId) + world.Tick) % nearby.Count;
                        nearest = nearby[pick];
                        _targetFood[aiId] = nearest.Id;
                    }
                }

                Vector2 desired = Vector2.Zero;

                if (nearest != null)
                    desired += NormalizeSafe(nearest.Position - (SerializableVector2)head) * FOOD_ATTRACT_WEIGHT;

                // Inward bias near walls
                float margin = MathF.Max(2f, worldHalf * 0.02f);
                float distEdgeX = worldHalf - MathF.Abs(head.X);
                float distEdgeY = worldHalf - MathF.Abs(head.Y);
                if (distEdgeX < margin || distEdgeY < margin)
                    desired += NormalizeSafe(new Vector2(-head.X, -head.Y)) * INWARD_WEIGHT;

                // Keep momentum
                if (prevDir.Length() > 0.0001f)
                    desired += prevDir * PREV_DIR_WEIGHT;

                desired = NormalizeSafe(desired);

                float speed = snake.IsBoosting ? snake.BoostSpeed : (snake.BaseSpeed > 0 ? snake.BaseSpeed : snake.CurrentSpeed);
                float turnDeg = snake.MaxTurningAngle > 0 ? snake.MaxTurningAngle : 360f;
                float maxRad = turnDeg * (MathF.PI / 180f) * dt;
                Vector2 moveDir = prevDir.Length() > 0.0001f ? RotateTowards(prevDir, desired, maxRad) : desired;
                Vector2 newHead = head + moveDir * speed * dt;

                // Boundary kill
                if (MathF.Abs(newHead.X) >= worldHalf || MathF.Abs(newHead.Y) >= worldHalf)
                {
                    _ = _gameEngine.OnPlayerDied(aiId, null);
                    continue;
                }

                // Collision check (sampled)
                bool collided = false;
                string killerId = null;
                float colR2 = COLLISION_RADIUS * COLLISION_RADIUS;

                foreach (var kv in allSnakesMap)
                {
                    var other = kv.Value;
                    if (other == null || !other.IsAlive || other.PlayerId == aiId) continue;
                    var segs = other.BodySegments;
                    if (segs == null || segs.Count == 0) continue;

                    for (int i = 0; i < segs.Count; i += SEGMENT_SAMPLE_STRIDE)
                    {
                        float dx = segs[i].X - newHead.X, dy = segs[i].Y - newHead.Y;
                        if (dx * dx + dy * dy <= colR2)
                        {
                            collided = true; killerId = other.PlayerId; break;
                        }
                    }
                    if (collided) break;
                }

                if (collided)
                {
                    _ = _gameEngine.OnPlayerDied(aiId, killerId);
                    continue;
                }

                // Update body
                segments.Add(new SerializableVector2(newHead));
                int targetLen = snake.TargetLength;
                while (segments.Count > targetLen && segments.Count > 1)
                    segments.RemoveAt(0);
                snake.BodySegments = segments;
                snake.CurrentSpeed = speed;
                snake.IsBoosting = false;

                await _redis.SetAsync($"{SNAKE_KEY_PREFIX}{aiId}", snake);

                // Eat check
                if (nearest != null)
                {
                    float ex = nearest.Position.X - newHead.X, ey = nearest.Position.Y - newHead.Y;
                    if (ex * ex + ey * ey <= EAT_RADIUS * EAT_RADIUS)
                    {
                        _ = _gameEngine.OnPlayerAteFood(aiId, nearest.Id);
                        _targetFood.TryRemove(aiId, out _);
                    }
                }
            }
        }

        // Utility methods
        private static Vector2 NormalizeSafe(Vector2 v) => v.Length() < 0.0001f ? Vector2.Zero : Vector2.Normalize(v);

        private static Vector2 RotateTowards(Vector2 from, Vector2 to, float maxRadians)
        {
            var f = NormalizeSafe(from);
            var t = NormalizeSafe(to);
            float angle = MathF.Atan2(f.X * t.Y - f.Y * t.X, f.X * t.X + f.Y * t.Y);
            float clamped = Math.Clamp(angle, -maxRadians, maxRadians);
            float cos = MathF.Cos(clamped), sin = MathF.Sin(clamped);
            return new Vector2(f.X * cos - f.Y * sin, f.X * sin + f.Y * cos);
        }

        private static int StableHash(string s)
        {
            unchecked
            {
                int h = 23;
                foreach (char c in s) h = h * 31 + c;
                return h;
            }
        }

        private async Task<WorldState?> GetWorldCachedAsync(string worldKey)
        {
            if (_worldCache.TryGetValue(worldKey, out var entry))
            {
                if (DateTime.UtcNow - entry.lastFetch < _worldCacheTTL)
                    return entry.world;
            }

            var world = await _redis.GetAsync<WorldState>(worldKey);
            if (world != null)
                _worldCache[worldKey] = (world, DateTime.UtcNow);
            return world;
        }
    }
}
