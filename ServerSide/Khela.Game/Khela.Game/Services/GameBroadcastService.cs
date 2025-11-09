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

        private readonly ConcurrentDictionary<string, WorldState> _worldCache = new();

        private const string SNAKE_KEY_PREFIX = "snake:";
        private const string WORLD_KEY_PREFIX = "world:";
        private const string FOODCACHE_PREFIX = "foodcache:";

        private readonly TimeSpan _broadcastInterval = TimeSpan.FromMilliseconds(100); // 10Hz
        private DateTime _lastBroadcast = DateTime.MinValue;

        public GameBroadcastService(IHubContext<SnakeHub> hubContext, IRedisService redis, GameEngine gameEngine, ILogger<GameBroadcastService> logger)
        {
            _hubContext = hubContext;
            _redis = redis;
            _gameEngine = gameEngine;
            _logger = logger;

            _gameEngine.OnWorldTickCompleted += HandleWorldTickCompleted;
            _gameEngine.OnFoodEaten += HandleFoodEaten;
            _gameEngine.PlayerDied += HandlePlayerDied;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Delay(3000, stoppingToken);
            _logger.LogInformation("GameBroadcastService started (10Hz).");
            try { await Task.Delay(Timeout.Infinite, stoppingToken); } catch { }
        }

        private async void HandleWorldTickCompleted(string worldId, DateTime utcNow)
        {
            if ((DateTime.UtcNow - _lastBroadcast) < _broadcastInterval)
                return; // throttle to 10Hz

            _lastBroadcast = DateTime.UtcNow;

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
            if (!_worldCache.TryGetValue(worldId, out var world) || world.Tick % 40 == 0)
            {
                world = await _redis.GetAsync<WorldState>(WORLD_KEY_PREFIX + worldId);
                if (world != null)
                    _worldCache[worldId] = world;
            }

            if (world == null || world.CurrentState != GameState.Running) return;

            var allIds = world.SnakeIds.Keys.Concat(world.AISnakeIds.Keys).ToArray();
            var snakeKeys = allIds.Select(id => SNAKE_KEY_PREFIX + id).ToArray();
            var snakesMap = (await _redis.GetBatchAsync<PlayerState>(snakeKeys)).Values;

            var snakeKinematics = snakesMap
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

            var foodState = await _redis.GetAsync<FoodStateDto[]>(FOODCACHE_PREFIX + world.WorldId) ?? Array.Empty<FoodStateDto>();

            var worldUpdate = new WorldUpdateDto
            {
                Snakes = snakeKinematics,
                Food = foodState,
                WorldSize = world.Config.WorldSize,
                Tick = world.Tick,
                TickRate = world.Config.TickRate,
                ServerUtc = utcNow
            };

            await _hubContext.Clients.Group(world.WorldId).SendAsync("WorldUpdate", worldUpdate);

            var ms = (DateTime.UtcNow - start).TotalMilliseconds;
            _logger.LogDebug($"Broadcast world={world.WorldId} took {ms:F1}ms (snakes={snakeKinematics.Length}, food={worldUpdate.Food.Length})");
        }

        private async void HandleFoodEaten(string playerId, int foodId, string worldId)
        {
            try
            {
                await _hubContext.Clients.Group(worldId).SendAsync("OnFoodEaten", foodId, playerId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error broadcasting HandleFoodEaten: {ex.Message}");
            }
        }

        private async void HandlePlayerDied(PlayerState deadPlayer, PlayerState killer)
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
    }
}
