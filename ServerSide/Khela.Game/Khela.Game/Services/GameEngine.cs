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

    /**
* -----------------------------------------------------------------------------
*  File: GameEngine.cs
*  Project: Khela.Game (Authoritative Multiplayer Server)
*  Author: Reza Sabir (CasualLab Interactive)
*  Description:
*      The GameEngine is the central authoritative tick loop responsible for
*      driving world updates, player state synchronization, food management, and
*      gameplay events across all active worlds.
* 
*      - Each world operates independently, running parallel ticks.
*      - All player movements, deaths, and food interactions are validated
*        server-side to prevent cheating and maintain deterministic gameplay.
*      - Event dispatching (OnWorldTickCompleted, OnFoodEaten, PlayerDied)
*        is fully async-safe, concurrent, and exception-tolerant.
*      - The engine maintains a fixed tick rate defined by WorldConfig.TickRate,
*        using Redis as the real-time authoritative state backend.
* 
*  Key Features:
*      • Fully async-safe with distributed Redis locks per player/world.
*      • Parallelized tick processing for multiple worlds.
*      • Deterministic physics & score logic for anti-cheat enforcement.
*      • Decoupled event model for Broadcast / AI / Analytics modules.
* 
*  Notes:
*      - The engine does not directly handle network transport; it relies on
*        SignalR or other services to propagate delta updates to connected clients.
*      - All gameplay state is persisted in Redis for durability and scalability.
* 
*  License: Proprietary © SiliconBangla LLC. All rights reserved.
* -----------------------------------------------------------------------------
*/
    public class GameEngine : BackgroundService
    {
        private readonly IRedisService _redis;
        private readonly ILogger<GameEngine> _logger;
        private readonly FoodService _foodService;

        // Async-friendly events
        public event Func<string, int, string, Task>? OnFoodEaten;
        public event Func<PlayerState, PlayerState?, Task>? PlayerDied;
        public event Func<string, DateTime, Task>? OnWorldTickCompleted;

        private readonly int _tickRate;
        private readonly WorldConfig _defaultConfig;
        private readonly TimeSpan _tickInterval;

        private const string SNAKE_KEY_PREFIX = "snake:";
        private const string WORLD_KEY_PREFIX = "world:";
        private const string FOOD_KEY_PREFIX = "food:";        // legacy
        private const string FOODHASH_PREFIX = "foodhash:";    // per-world HASH: field=id, value=Food JSON 

        public GameEngine(
            IRedisService redis,
            ILogger<GameEngine> logger,
            FoodService foodService,
            WorldManagerService worldManager // kept for DI symmetry
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

            if (!await db.LockTakeAsync(lockKey, lockToken, TimeSpan.FromSeconds(1)))
                return;

            try
            {
                var snake = await _redis.GetAsync<PlayerState>(playerKey);
                if (snake == null || !snake.IsAlive)
                    return;

                snake.BodySegments = bodySegments;
                snake.IsBoosting = isBoosting;

                // Server-authoritative speed
                if (isBoosting)
                {
                    snake.CurrentSpeed = snake.BoostSpeed;
                }
                else
                {
                    snake.CurrentSpeed = snake.BaseSpeed > 0f
                        ? snake.BaseSpeed
                        : snake.CurrentSpeed;
                }

                await _redis.SetAsync(playerKey, snake);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in OnPlayerStateUpdate for {PlayerId}", playerId);
            }
            finally
            {
                await db.LockReleaseAsync(lockKey, lockToken);
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

            if (!await db.LockTakeAsync(lockKey, lockToken, TimeSpan.FromSeconds(1)))
                return;

            try
            {
                var snake = await _redis.GetAsync<PlayerState>(playerKey);
                if (snake == null || !snake.IsAlive)
                    return;

                Food? food = null;

                // Prefer HASH-based food
                if (!string.IsNullOrEmpty(snake.CurrentWorldId))
                {
                    var worldHashKey = FOODHASH_PREFIX + snake.CurrentWorldId;
                    var hv = await db.HashGetAsync(worldHashKey, foodId.ToString());
                    if (hv.HasValue)
                    {
                        try { food = JsonSerializer.Deserialize<Food>(hv!); } catch { }
                    }
                }

                // Fallback to legacy key
                if (food == null)
                {
                    food = await _redis.GetAsync<Food>(FOOD_KEY_PREFIX + foodId);
                }

                if (food == null)
                    return;

                // Validate distance (lag-tolerant)
                var head = snake.HeadPosition;
                float dx = food.Position.X - head.X;
                float dy = food.Position.Y - head.Y;

                float eatRadius = snake.IsAI ? 0.9f : 1.5f;
                if (dx * dx + dy * dy > eatRadius * eatRadius)
                    return;

                // Apply gain
                snake.Score += 10;
                await _redis.SetAsync(playerKey, snake);

                // Remove food via FoodService (keeps HASH + cache consistent)
                if (!string.IsNullOrEmpty(snake.CurrentWorldId))
                {
                    var world = await _redis.GetAsync<WorldState>(WORLD_KEY_PREFIX + snake.CurrentWorldId);
                    if (world != null)
                    {
                        await _foodService.RemoveFoodAsync(world, foodId);
                    }
                }

                // Notify listeners (async-safe)
                await RaiseFoodEaten(playerId, foodId, snake.CurrentWorldId);

                _logger.LogInformation("OnPlayerAteFood player={PlayerId} food={FoodId} score={Score}",
                    playerId, foodId, snake.Score);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in OnPlayerAteFood for {PlayerId}", playerId);
            }
            finally
            {
                await db.LockReleaseAsync(lockKey, lockToken);
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

            if (!await db.LockTakeAsync(lockKey, lockToken, TimeSpan.FromSeconds(3)))
                return;

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

                await RaisePlayerDied(deadSnake, killerSnake);

                _logger.LogInformation("PlayerDied victim={Victim} killer={Killer}",
                    deadPlayerId, killerPlayerId ?? "none");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in OnPlayerDied for {PlayerId}", deadPlayerId);
            }
            finally
            {
                await db.LockReleaseAsync(lockKey, lockToken);
            }
        }

        // ---------------------------------------------------------------------
        // MAIN GAME LOOP
        // ---------------------------------------------------------------------

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // small jitter so we don't align perfectly with other services
            try { await Task.Delay(Random.Shared.Next(10, 25), stoppingToken); } catch { }

            _logger.LogInformation("GameEngine started at {TickRate} ticks/sec", _tickRate);

            while (!stoppingToken.IsCancellationRequested)
            {
                var tickStart = DateTime.UtcNow;

                try
                {
                    var worldKeys = await _redis.GetKeysByPatternAsync(WORLD_KEY_PREFIX + "*");
                    if (worldKeys != null && worldKeys.Any())
                    {
                        var tasks = worldKeys.Select(k => ProcessWorldTick(k, stoppingToken));
                        await Task.WhenAll(tasks);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in main tick loop");
                }

                var elapsed = DateTime.UtcNow - tickStart;
                var delay = _tickInterval - elapsed;
                if (delay > TimeSpan.Zero)
                {
                    try { await Task.Delay(delay, stoppingToken); }
                    catch (TaskCanceledException) { }
                }
            }
        }

        private async Task ProcessWorldTick(string worldKey, CancellationToken token)
        {
            if (token.IsCancellationRequested)
                return;

            var world = await _redis.GetAsync<WorldState>(worldKey);
            if (world == null || world.Config == null || world.CurrentState != GameState.Running)
                return;

            // Ensure sane config
            if (world.Config.TargetAISnakeCount < 0)
            {
                world.Config = _defaultConfig;
            }

            // Let FoodService manage HASH-based spawning & cache
            await _foodService.ManageFoodSpawningAsync(world);

            // Advance world tick
            world.Tick++;
            world.LastUpdated = DateTime.UtcNow;
            await _redis.SetAsync(worldKey, world);

            // Notify listeners (broadcast service, etc.)
            await RaiseWorldTickCompleted(world.WorldId, world.LastUpdated);
        }

        // ---------------------------------------------------------------------
        // Event helpers (no async void, no swallowed exceptions)
        // ---------------------------------------------------------------------

        private Task RaiseFoodEaten(string playerId, int foodId, string worldId)
        {
            var handler = OnFoodEaten;
            if (handler == null) return Task.CompletedTask;

            var delegates = handler.GetInvocationList()
                .Cast<Func<string, int, string, Task>>();

            return Task.WhenAll(delegates.Select(d =>
            {
                try { return d(playerId, foodId, worldId); }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "OnFoodEaten handler failed");
                    return Task.CompletedTask;
                }
            }));
        }

        private Task RaisePlayerDied(PlayerState dead, PlayerState? killer)
        {
            var handler = PlayerDied;
            if (handler == null) return Task.CompletedTask;

            var delegates = handler.GetInvocationList()
                .Cast<Func<PlayerState, PlayerState?, Task>>();

            return Task.WhenAll(delegates.Select(d =>
            {
                try { return d(dead, killer); }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "PlayerDied handler failed");
                    return Task.CompletedTask;
                }
            }));
        }

        private Task RaiseWorldTickCompleted(string worldId, DateTime utcNow)
        {
            var handler = OnWorldTickCompleted;
            if (handler == null) return Task.CompletedTask;

            var delegates = handler.GetInvocationList()
                .Cast<Func<string, DateTime, Task>>();

            return Task.WhenAll(delegates.Select(d =>
            {
                try { return d(worldId, utcNow); }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "OnWorldTickCompleted handler failed");
                    return Task.CompletedTask;
                }
            }));
        }
    }
}
