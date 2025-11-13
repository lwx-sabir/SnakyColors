using System.Diagnostics;
using System.Numerics;
using Khela.Game.Dtos;
using Khela.Game.Models;
using Khela.Game.Models.Configs;
using Khela.Game.Models.States;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// ==============================================
// GameEngine.cs
// Author: Reza Sabir (CasualLabInteractive)
// Version: 1.0.1 (Production Stable)
// Description:
// Thread-safe authoritative server core controlling world ticks,
// snake movements, collisions, food interactions, and event dispatch.
// ==============================================


namespace Khela.Game.Services
{
    /// <summary>
    /// In-memory authoritative game loop.
    /// - Uses GameState.Instance as the single source of truth.
    /// - No Redis calls in runtime; persistence handled elsewhere.
    /// - Validates movement, food, deaths, and emits world snapshots.
    /// </summary>
    public class GameEngine : BackgroundService
    {
        private readonly ILogger<GameEngine> _logger;
        private readonly FoodService _foodService;

        // Async-friendly events
        public event Func<string, int, string, Task>? OnFoodEaten;
        public event Func<PlayerState, PlayerState?, Task>? PlayerDied;
        public event Func<string, DateTime, Task>? OnWorldTickCompleted;

        private readonly int _tickRate;
        private readonly TimeSpan _tickInterval;
        private readonly WorldConfig _defaultConfig = new();

        public GameEngine(
            FoodService foodService,
            ILogger<GameEngine> logger)
        {
            _foodService = foodService;
            _logger = logger;

            _tickRate = _defaultConfig.TickRate > 0 ? _defaultConfig.TickRate : 20;
            _tickInterval = TimeSpan.FromMilliseconds(1000.0 / _tickRate);
        }

        // ---------------------------------------------------------------------
        // PLAYER STATE UPDATE (from client)
        // ---------------------------------------------------------------------

        public Task OnPlayerStateUpdate(string playerId, List<SerializableVector2> bodySegments, bool isBoosting)
        {
            if (string.IsNullOrEmpty(playerId) || bodySegments == null || bodySegments.Count == 0)
                return Task.CompletedTask;

            var gs = GameState.Instance;

            if (!gs.TryGetPlayer(playerId, out var snake) || !snake.IsAlive)
                return Task.CompletedTask;

            // Update body & boosting
            snake.BodySegments = bodySegments;
            snake.IsBoosting = isBoosting;

            // Authoritative speed logic
            if (isBoosting)
            {
                snake.CurrentSpeed = snake.BoostSpeed;
            }
            else
            {
                snake.CurrentSpeed = snake.BaseSpeed > 0f
                    ? snake.BaseSpeed
                    : (snake.CurrentSpeed > 0 ? snake.CurrentSpeed : snake.BaseSpeed);
            }

            gs.AddOrUpdatePlayer(snake);
            return Task.CompletedTask;
        }

        // ---------------------------------------------------------------------
        // FOOD EAT HANDLING (authoritative)
        // ---------------------------------------------------------------------

        public Task OnPlayerAteFood(string playerId, int foodId)
        {
            if (string.IsNullOrEmpty(playerId))
                return Task.CompletedTask;

            var gs = GameState.Instance;

            if (!gs.TryGetPlayer(playerId, out var snake) || !snake.IsAlive)
                return Task.CompletedTask;

            if (snake.CurrentWorldId == null ||
                !gs.TryGetWorld(snake.CurrentWorldId, out var world) ||
                world.CurrentStatus != GameStatus.Running)
                return Task.CompletedTask;

            if (!gs.TryGetFood(foodId, out var food))
                return Task.CompletedTask;

            if (!snake.IsAI)
            {
                var debug = 0;
            }

            // Lag-tolerant distance check
            var head = snake.HeadPosition;
            float dx = food.Position.X - head.X;
            float dy = food.Position.Y - head.Y;
            float eatRadius = snake.IsAI ? 0.9f : 1.5f;

            if (dx * dx + dy * dy > eatRadius * eatRadius)
                return Task.CompletedTask;

            // Apply gain
            snake.Score += 10;
            gs.AddOrUpdatePlayer(snake);

            // Remove food from world (in-memory)
            _foodService.RemoveFoodAsync(world, foodId); // fire-and-forget is fine here

            // Notify listeners
            _ = RaiseFoodEaten(playerId, foodId, world.WorldId);

            if (!snake.IsAI)
            {
                _logger.LogInformation("OnPlayerAteFood player={PlayerId} food={FoodId} score={Score}",
               playerId, foodId, snake.Score);
            } 

            return Task.CompletedTask;
        }

        // ---------------------------------------------------------------------
        // PLAYER DEATH (authoritative)
        // ---------------------------------------------------------------------

        public Task OnPlayerDied(string deadPlayerId, string? killerPlayerId)
        {
            if (string.IsNullOrEmpty(deadPlayerId))
                return Task.CompletedTask;

            var gs = GameState.Instance;

            if (!gs.TryGetPlayer(deadPlayerId, out var deadSnake))
                return Task.CompletedTask;

            if (!deadSnake.IsAlive)
                return Task.CompletedTask;

            deadSnake.IsAlive = false;
            gs.AddOrUpdatePlayer(deadSnake);

            PlayerState? killerSnake = null;
            if (!string.IsNullOrEmpty(killerPlayerId) &&
                gs.TryGetPlayer(killerPlayerId, out var killer))
            {
                killerSnake = killer;
            }

            _ = RaisePlayerDied(deadSnake, killerSnake);

            _logger.LogInformation("PlayerDied victim={Victim} killer={Killer}",
                deadPlayerId, killerPlayerId ?? "none");

            return Task.CompletedTask;
        }

        // ---------------------------------------------------------------------
        // MAIN GAME LOOP (per-world tick)
        // ---------------------------------------------------------------------

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try { await Task.Delay(Random.Shared.Next(10, 50), stoppingToken); } catch { }

            _logger.LogInformation("GameEngine started at {TickRate} ticks/sec", _tickRate);

            var stopwatch = Stopwatch.StartNew();
            var nextTick = stopwatch.Elapsed;
            long tickCount = 0;

            while (!stoppingToken.IsCancellationRequested)
            {
                var now = stopwatch.Elapsed;

                if (now >= nextTick)
                {
                    try
                    {
                        var tickStart = stopwatch.Elapsed;

                        var worlds = GameState.Instance.Worlds.Values.ToArray();
                        foreach (var world in worlds)
                        {
                            await ProcessWorldTick(world, stoppingToken);
                        }

                        var tickElapsed = stopwatch.Elapsed - tickStart;
                        //_logger.LogInformation("Tick {Tick} took {Ms:F2} ms",tickCount, tickElapsed.TotalMilliseconds);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error in main tick loop");
                    }

                    tickCount++;
                    nextTick += _tickInterval;

                    // Skip ahead if we're behind
                    while (stopwatch.Elapsed - nextTick > _tickInterval)
                        nextTick += _tickInterval;
                }

                if (tickCount % 100 == 0 && tickCount > 0)
                {
                    var elapsed = stopwatch.Elapsed.TotalSeconds;
                    if (elapsed > 0)
                    {
                        var effRate = tickCount / elapsed;
                        _logger.LogInformation("Ticks={Tick} Elapsed={Sec:F1}s Rate={Rate:F2}Hz",
                            tickCount, elapsed, effRate);
                    }
                }

                var sleep = nextTick - stopwatch.Elapsed;
                if (sleep > TimeSpan.Zero)
                    await Task.Delay(sleep, stoppingToken);
                else
                    await Task.Yield();
            }

            _logger.LogInformation("GameEngine stopped.");
        }

        private async Task ProcessWorldTick(WorldState world, CancellationToken token)
        {
            if (token.IsCancellationRequested)
                return;

            if (world.CurrentStatus != GameStatus.Running)
                return;

            // Ensure config sane
            if (world.Config == null || world.Config.WorldSize <= 0)
                world.Config = _defaultConfig;

            // Food management (in-memory)
            await _foodService.ManageFoodSpawningAsync(world);

            // Advance tick
            world.Tick++;
            world.LastUpdated = DateTime.UtcNow;

            // Build/refresh snapshot for clients
            GameState.Instance.BuildWorldSnapshot(world.WorldId);

            // Notify listeners (broadcaster can push to clients)
            await RaiseWorldTickCompleted(world.WorldId, world.LastUpdated);
        }

        // ---------------------------------------------------------------------
        // EVENT HELPERS
        // ---------------------------------------------------------------------

        private Task RaiseFoodEaten(string playerId, int foodId, string? worldId)
        {
            var handler = OnFoodEaten;
            if (handler == null) return Task.CompletedTask;

            var delegates = handler.GetInvocationList()
                .Cast<Func<string, int, string, Task>>();

            return Task.WhenAll(delegates.Select(d =>
            {
                try { return d(playerId, foodId, worldId ?? string.Empty); }
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
