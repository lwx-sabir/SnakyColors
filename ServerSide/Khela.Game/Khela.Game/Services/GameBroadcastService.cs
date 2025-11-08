using Khela.Game.Managers.SRHubs;
using Khela.Game.Services.Redis;
using Microsoft.AspNetCore.SignalR;
using Khela.Game.Dtos;
using Khela.Game.Models;

namespace Khela.Game.Services
{
    public class GameBroadcastService : BackgroundService
    {
        private readonly IHubContext<SnakeHub> _hubContext;
        private readonly IRedisService _redis;
        private readonly GameEngine _gameEngine;
        private readonly ILogger<GameBroadcastService> _logger;

        private readonly TimeSpan _broadcastInterval;
        private const string SNAKE_KEY_PREFIX = "snake:";
        private const string WORLD_KEY_PREFIX = "world:";
        private const string FOOD_KEY_PREFIX = "food:";

        public GameBroadcastService(IHubContext<SnakeHub> hubContext, IRedisService redis, GameEngine gameEngine, ILogger<GameBroadcastService> logger)
        {
            _hubContext = hubContext;
            _redis = redis;
            _gameEngine = gameEngine;
            _logger = logger;
            _broadcastInterval = TimeSpan.FromMilliseconds(1000.0 / 20); // 20Hz broadcast for smoother sync

            // Subscribe to GameEngine's events
            _gameEngine.OnFoodEaten += HandleFoodEaten;
            _gameEngine.PlayerDied += HandlePlayerDied;
            // (We don't need Join/Left, Hub does that)
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var startTime = DateTime.UtcNow;

                // Get all active worlds and broadcast their state in parallel
                var worldKeys = await _redis.GetKeysByPatternAsync(WORLD_KEY_PREFIX + "*");
                var broadcastTasks = worldKeys.Select(key => BroadcastWorldTick(key, stoppingToken));
                await Task.WhenAll(broadcastTasks);

                var endTime = DateTime.UtcNow;
                var timeToWait = _broadcastInterval - (endTime - startTime);
                if (timeToWait > TimeSpan.Zero)
                {
                    await Task.Delay(timeToWait, stoppingToken);
                }
            }
        }

        /// <summary>
        /// Gathers all data for ONE world and broadcasts it to that world's group.
        /// </summary>
        private async Task BroadcastWorldTick(string worldKey, CancellationToken token)
        {
            var world = await _redis.GetAsync<WorldState>(worldKey);
            if (world == null || world.CurrentState != GameState.Running) return;

            // --- Get all snake data for this world ---
            var snakeKeys = world.SnakeIds.Keys.Select(id => SNAKE_KEY_PREFIX + id).ToArray();
            var aiKeys = world.AISnakeIds.Keys.Select(id => SNAKE_KEY_PREFIX + id).ToArray();
            var allKeys = snakeKeys.Concat(aiKeys).ToArray();
            var snakes = (await _redis.GetBatchAsync<PlayerState>(allKeys)).Values;

            var snakeState = snakes.Where(s => s != null && s.IsAlive).ToArray();

            // --- Get all food data for this world ---
            var foodKeys = world.FoodIds.Keys.Select(id => FOOD_KEY_PREFIX + id).ToArray();
            var foods = (await _redis.GetBatchAsync<Food>(foodKeys)).Values;

            var foodState = foods.Where(f => f != null)
                                 .Select(f => new FoodStateDto
                                 {
                                     Id = f.Id,
                                     PosX = f.Position.X,
                                     PosY = f.Position.Y,
                                     ItemKey = f.ItemKey
                                 }).ToArray();

            var worldUpdate = new WorldUpdateDto
            {
                Snakes = snakeState,
                Food = foodState,
                WorldSize = world.Config.WorldSize,
            };

            if (token.IsCancellationRequested) return;

            // Send this world's update ONLY to the group for this world
            await _hubContext.Clients.Group(world.WorldId).SendAsync("WorldUpdate", worldUpdate, cancellationToken: token);
        }

        // --- Event Handlers (Broadcast discrete events) ---

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
            try
            {
                string message = killer != null
                    ? $"{deadPlayer.PlayerName} was eaten by {killer.PlayerName}"
                    : $"{deadPlayer.PlayerName} hit a wall.";

                await _hubContext.Clients.Group(deadPlayer.CurrentWorldId).SendAsync("OnPlayerDied", deadPlayer.PlayerId, message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error broadcasting HandlePlayerDied: {ex.Message}");
            }
        }
    }
}
