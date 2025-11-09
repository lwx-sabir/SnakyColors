using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Numerics;
using System.Text.Json;
using Khela.Game.Services.Redis;
using Khela.Game.Models;
using Khela.Game.Models.Configs;
using Khela.Game.Dtos;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Khela.Game.Services
{
    public class GameEngine : BackgroundService
    {
        private readonly IRedisService _redis;
        private readonly ILogger<GameEngine> _logger;
        private readonly FoodService _foodService;

        public event Action<string, int, string>? OnFoodEaten;
        public event Action<PlayerState, PlayerState?>? PlayerDied;
        public event Action<string, DateTime>? OnWorldTickCompleted;

        private readonly int _tickRate;
        private readonly WorldConfig _defaultConfig;
        private readonly TimeSpan _tickInterval;

        private const string SNAKE_KEY_PREFIX = "snake:";
        private const string WORLD_KEY_PREFIX = "world:";
        private const string FOOD_KEY_PREFIX = "food:";        // legacy
        private const string FOODHASH_PREFIX = "foodhash:";    // per-world HASH: field=id, value=Food JSON
        private const string FOODCACHE_PREFIX = "foodcache:";  // per-world FoodStateDto[] cache

        public GameEngine(
            IRedisService redis,
            ILogger<GameEngine> logger,
            FoodService foodService,
            WorldManagerService worldManager // kept to match your ctor signature, even if unused directly
        )
        {
            _redis = redis;
            _logger = logger;
            _foodService = foodService;

            _defaultConfig = new WorldConfig();
            _tickRate = _defaultConfig.TickRate;
            _tickInterval = TimeSpan.FromMilliseconds(1000.0 / _tickRate);
        }

        // ---------------------------------------------------------------------
        // PLAYER STATE UPDATE (authoritative)
        // ---------------------------------------------------------------------

        public async Task OnPlayerStateUpdate(string playerId, List<SerializableVector2> bodySegments, bool isBoosting)
        {
            if (string.IsNullOrEmpty(playerId) || bodySegments == null || bodySegments.Count == 0)
                return;

            string playerKey = SNAKE_KEY_PREFIX + playerId;
            string lockKey = $"lock:{playerKey}";
            string lockToken = Guid.NewGuid().ToString();
            var db = _redis.GetDatabase();

            if (await db.LockTakeAsync(lockKey, lockToken, TimeSpan.FromSeconds(1)))
            {
                try
                {
                    var snake = await _redis.GetAsync<PlayerState>(playerKey);
                    if (snake == null || !snake.IsAlive)
                        return;

                    snake.BodySegments = bodySegments;
                    snake.IsBoosting = isBoosting;

                    // Derive speed server-side
                    if (isBoosting)
                    {
                        snake.CurrentSpeed = snake.BoostSpeed;
                    }
                    else
                    {
                        snake.CurrentSpeed = (snake.BaseSpeed > 0f)
                            ? snake.BaseSpeed
                            : snake.CurrentSpeed;
                    }

                    // HeadPosition is computed from BodySegments; no direct assignment.

                    await _redis.SetAsync(playerKey, snake);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error in OnPlayerStateUpdate for {playerId}");
                }
                finally
                {
                    await db.LockReleaseAsync(lockKey, lockToken);
                }
            }
        }

        // ---------------------------------------------------------------------
        // FOOD EAT HANDLING
        // ---------------------------------------------------------------------

        public async Task OnPlayerAteFood(string playerId, int foodId)
        {
            if (string.IsNullOrEmpty(playerId))
                return;

            string playerKey = SNAKE_KEY_PREFIX + playerId;
            string lockKey = $"lock:{playerKey}";
            string lockToken = Guid.NewGuid().ToString();
            var db = _redis.GetDatabase();

            if (await db.LockTakeAsync(lockKey, lockToken, TimeSpan.FromSeconds(1)))
            {
                try
                {
                    var snake = await _redis.GetAsync<PlayerState>(playerKey);
                    if (snake == null || !snake.IsAlive)
                        return;

                    // Try new HASH-based food first
                    Food? food = null;

                    if (!string.IsNullOrEmpty(snake.CurrentWorldId))
                    {
                        var worldHashKey = FOODHASH_PREFIX + snake.CurrentWorldId;
                        var hv = await db.HashGetAsync(worldHashKey, foodId.ToString());
                        if (hv.HasValue)
                        {
                            try { food = JsonSerializer.Deserialize<Food>(hv!); } catch { }
                        }
                    }

                    // Fallback to legacy single key if needed
                    if (food == null)
                    {
                        food = await _redis.GetAsync<Food>(FOOD_KEY_PREFIX + foodId);
                    }

                    if (food == null)
                        return;

                    // Validate distance using authoritative head position
                    var head = snake.HeadPosition; // computed from BodySegments
                    float dx = food.Position.X - head.X;
                    float dy = food.Position.Y - head.Y;

                    // Larger radius for players to hide latency, slightly tighter for AI
                    float eatRadius = snake.IsAI ? 0.9f : 1.5f;
                    if (dx * dx + dy * dy > eatRadius * eatRadius)
                    {
                        // Too far: reject (fixes infinite "orbiting" from bad reports)
                        return;
                    }

                    // 1) Grant score / growth
                    snake.Score += 10;
                    await _redis.SetAsync(playerKey, snake);

                    // 2) Remove food from world via FoodService (handles hash + cache)
                    var world = await _redis.GetAsync<WorldState>(WORLD_KEY_PREFIX + snake.CurrentWorldId);
                    if (world != null)
                    {
                        await _foodService.RemoveFoodAsync(world, foodId);
                    }

                    // 3) Notify listeners
                    OnFoodEaten?.Invoke(playerId, foodId, snake.CurrentWorldId);
                    _logger.LogInformation($"OnPlayerAteFood: player={playerId}, food={foodId}, score={snake.Score}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error in OnPlayerAteFood for {playerId}");
                }
                finally
                {
                    await db.LockReleaseAsync(lockKey, lockToken);
                }
            }
        }

        // ---------------------------------------------------------------------
        // PLAYER DEATH
        // ---------------------------------------------------------------------

        public async Task OnPlayerDied(string deadPlayerId, string? killerPlayerId)
        {
            if (string.IsNullOrEmpty(deadPlayerId))
                return;

            string playerKey = SNAKE_KEY_PREFIX + deadPlayerId;
            string lockKey = $"lock:{playerKey}";
            string lockToken = Guid.NewGuid().ToString();
            var db = _redis.GetDatabase();

            if (await db.LockTakeAsync(lockKey, lockToken, TimeSpan.FromSeconds(3)))
            {
                try
                {
                    var deadSnake = await _redis.GetAsync<PlayerState>(playerKey);
                    if (deadSnake == null || !deadSnake.IsAlive)
                        return; // already dead or missing

                    deadSnake.IsAlive = false;
                    await _redis.SetAsync(playerKey, deadSnake);

                    PlayerState? killerSnake = null;
                    if (!string.IsNullOrEmpty(killerPlayerId))
                    {
                        killerSnake = await _redis.GetAsync<PlayerState>(SNAKE_KEY_PREFIX + killerPlayerId);
                    }

                    PlayerDied?.Invoke(deadSnake, killerSnake);
                    _logger.LogInformation($"PlayerDied: victim={deadPlayerId}, killer={killerPlayerId ?? "none"}");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error in OnPlayerDied for {deadPlayerId}");
                }
                finally
                {
                    await db.LockReleaseAsync(lockKey, lockToken);
                }
            }
        }

        // ---------------------------------------------------------------------
        // MAIN GAME LOOP
        // ---------------------------------------------------------------------

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // small jitter so we don't align perfectly with other services
            try { await Task.Delay(Random.Shared.Next(10, 25), stoppingToken); } catch { }

            while (!stoppingToken.IsCancellationRequested)
            {
                var startTime = DateTime.UtcNow;

                try
                {
                    var worldKeys = await _redis.GetKeysByPatternAsync(WORLD_KEY_PREFIX + "*");
                    var tasks = worldKeys.Select(k => ProcessWorldTick(k, stoppingToken));
                    await Task.WhenAll(tasks);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in main tick loop");
                }

                // Fixed-step schedule
                var elapsed = DateTime.UtcNow - startTime;
                var delay = _tickInterval - elapsed;
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, stoppingToken);

                _logger.LogInformation($"Tick took {elapsed.TotalMilliseconds:F1}ms");
            }
        }

        private async Task ProcessWorldTick(string worldKey, CancellationToken token)
        {
            var world = await _redis.GetAsync<WorldState>(worldKey);
            if (world == null || world.Config == null || world.CurrentState != GameState.Running)
                return;

            // Make sure world has sane config
            if (world.Config.TargetAISnakeCount < 0)
            {
                world.Config = _defaultConfig;
            }

            // Food spawning/maintenance (HASH + cache)
            await _foodService.ManageFoodSpawningAsync(world);

            // Advance tick
            world.Tick++;
            world.LastUpdated = DateTime.UtcNow;
            await _redis.SetAsync(worldKey, world);

            // Notify broadcast service
            OnWorldTickCompleted?.Invoke(world.WorldId, DateTime.UtcNow);
        }
    }
}
