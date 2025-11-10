using Khela.Game.Managers.SRHubs;
using Khela.Game.Services.Redis;
using Microsoft.AspNetCore.SignalR;
using Khela.Game.Dtos;
using Khela.Game.Models;
using System.Collections.Concurrent;

namespace Khela.Game.Services
{
    public class GameBroadcastService : BackgroundService
    {
        private readonly IHubContext<SnakeHub> _hubContext;
        private readonly IRedisService _redis;
        private readonly GameEngine _gameEngine;
        private readonly ILogger<GameBroadcastService> _logger;

        private readonly ConcurrentDictionary<string, (WorldState world, DateTime lastFetch)> _worldCache = new();
        private readonly TimeSpan _worldCacheTTL = TimeSpan.FromMilliseconds(300);

        private readonly ConcurrentDictionary<string, (Dictionary<string, PlayerState> snakes, DateTime lastFetch)> _snakeCache = new();
        private readonly TimeSpan _snakeCacheTTL = TimeSpan.FromMilliseconds(200);

        private readonly ConcurrentDictionary<string, (FoodStateDto[] foods, DateTime lastFetch)> _foodCache = new();
        private readonly TimeSpan _foodCacheTTL = TimeSpan.FromMilliseconds(300);

        private readonly ConcurrentDictionary<string, WorldUpdateDto> _lastSentState = new();
        private readonly ConcurrentDictionary<string, DateTime> _lastBroadcastPerWorld = new();

        private const string SNAKE_KEY_PREFIX = "snake:";
        private const string WORLD_KEY_PREFIX = "world:";
        private const string FOODCACHE_PREFIX = "foodcache:";

        private readonly TimeSpan _broadcastInterval = TimeSpan.FromMilliseconds(100); // 10Hz

        public GameBroadcastService(IHubContext<SnakeHub> hubContext, IRedisService redis, GameEngine gameEngine, ILogger<GameBroadcastService> logger)
        {
            _hubContext = hubContext;
            _redis = redis;
            _gameEngine = gameEngine;
            _logger = logger;
             
            _gameEngine.OnWorldTickCompleted += async (worldId, utcNow) => await HandleWorldTickCompleted(worldId, utcNow);
            _gameEngine.OnFoodEaten += async (playerId, foodId, worldId) => await HandleFoodEaten(playerId, foodId, worldId);
            _gameEngine.PlayerDied += async (dead, killer) => await HandlePlayerDied(dead, killer);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Delay(3000, stoppingToken);
            _logger.LogInformation("GameBroadcastService started (10Hz).");

            try { await Task.Delay(Timeout.Infinite, stoppingToken); } catch { }
        }

        // ✅ Now Task instead of async void
        private async Task HandleWorldTickCompleted(string worldId, DateTime utcNow)
        {
            if (!ShouldBroadcast(worldId))
                return;

            try
            {
                await BroadcastWorldById(worldId, utcNow);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Broadcast error for {worldId}: {ex.Message}");
            }
        }

        private async Task BroadcastWorldById(string worldId, DateTime utcNow)
        {
            var start = DateTime.UtcNow;
            var world = await GetWorldCachedAsync(worldId);
            if (world == null || world.CurrentState != GameState.Running)
                return;

            // --- Cached snakes ---
            var allIds = world.SnakeIds.Keys.Concat(world.AISnakeIds.Keys).ToArray();
            List<PlayerState> snakes;
            if (!_snakeCache.TryGetValue(worldId, out var entry) || DateTime.UtcNow - entry.lastFetch >= _snakeCacheTTL)
            {
                var snakeKeys = allIds.Select(id => SNAKE_KEY_PREFIX + id).ToArray();
                var snakesToCache = await _redis.GetBatchAsync<PlayerState>(snakeKeys);
                _snakeCache[worldId] = (snakesToCache, DateTime.UtcNow);
                snakes = [.. snakesToCache.Values];
            }
            else
            {
                snakes = [.. entry.snakes.Values];
            }

            var snakeKinematics = snakes
                .Where(s => s != null && s.IsAlive)
                .Select(s => new SnakeKinematicsDto
                {
                    PlayerId = s.PlayerId,
                    SkinID = s.SkinID,
                    IsAI = s.IsAI,
                    Mass = s.Mass,
                    HeadPosition = s.HeadPosition,
                    BaseSpeed = s.BaseSpeed,
                    CurrentSpeed = s.CurrentSpeed,
                    MaxTurningAngle = s.MaxTurningAngle,
                    TargetLength = s.TargetLength
                })
                .ToArray();

            var foodState = await GetFoodCachedAsync(world.WorldId);

            var worldUpdate = new WorldUpdateDto
            {
                Snakes = snakeKinematics,
                Food = foodState,
                WorldSize = world.Config.WorldSize,
                Tick = world.Tick,
                TickRate = world.Config.TickRate,
                ServerUtc = utcNow
            };

            // --- Diff check to reduce spam ---
            if (_lastSentState.TryGetValue(worldId, out var prev))
            {
                bool snakesChanged = snakeKinematics.Any(s =>
                    prev.Snakes.All(p => p.PlayerId != s.PlayerId ||
                                         p.HeadPosition != s.HeadPosition));

                bool foodChanged = foodState.Any(f =>
                    prev.Food.All(p => p.Id != f.Id || p.PosX != f.PosX || p.PosY != f.PosY));

                if (!snakesChanged && !foodChanged)
                    return;
            }

            try
            {
                await _hubContext.Clients.Group(world.WorldId).SendAsync("WorldUpdate", worldUpdate);
                _lastSentState[worldId] = worldUpdate;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"Broadcast send failed for {world.WorldId}");
            }

            var ms = (DateTime.UtcNow - start).TotalMilliseconds;
            _logger.LogTrace($"Broadcast world={world.WorldId} took {ms:F1}ms (snakes={snakeKinematics.Length}, food={worldUpdate.Food.Length})");
        }

        private async Task HandleFoodEaten(string playerId, int foodId, string worldId)
        {
            try
            {
                await _hubContext.Clients.Group(worldId)
                    .SendAsync("OnFoodEaten", foodId, playerId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error broadcasting HandleFoodEaten: {ex.Message}");
            }
        }

        private async Task HandlePlayerDied(PlayerState deadPlayer, PlayerState killer)
        {
            if (deadPlayer == null) return;

            string message = killer != null
                ? $"{deadPlayer.PlayerName} was eaten by {killer.PlayerName}"
                : $"{deadPlayer.PlayerName} hit a wall.";

            try
            {
                await _hubContext.Clients.Group(deadPlayer.CurrentWorldId)
                    .SendAsync("OnPlayerDied", deadPlayer.PlayerId, message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error broadcasting death: {ex.Message}");
            }
        }

        private async Task<WorldState?> GetWorldCachedAsync(string worldId)
        {
            if (_worldCache.TryGetValue(worldId, out var entry))
            {
                if (DateTime.UtcNow - entry.lastFetch < _worldCacheTTL)
                    return entry.world;
            }

            var world = await _redis.GetAsync<WorldState>(WORLD_KEY_PREFIX + worldId);
            if (world != null)
                _worldCache[worldId] = (world, DateTime.UtcNow);

            return world;
        }

        private async Task<FoodStateDto[]> GetFoodCachedAsync(string worldId)
        {
            if (_foodCache.TryGetValue(worldId, out var entry))
            {
                if (DateTime.UtcNow - entry.lastFetch < _foodCacheTTL)
                    return entry.foods;
            }

            var food = await _redis.GetAsync<FoodStateDto[]>(FOODCACHE_PREFIX + worldId)
                       ?? Array.Empty<FoodStateDto>();
            _foodCache[worldId] = (food, DateTime.UtcNow);
            return food;
        }

        private bool ShouldBroadcast(string worldId)
        {
            var now = DateTime.UtcNow;
            if (_lastBroadcastPerWorld.TryGetValue(worldId, out var last) &&
                (now - last) < _broadcastInterval)
                return false;

            _lastBroadcastPerWorld[worldId] = now;
            return true;
        }
    }
}
