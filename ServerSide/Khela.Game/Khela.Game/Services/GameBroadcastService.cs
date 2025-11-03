using Khela.Game.Managers.SRHubs;
using Khela.Game.Services.Redis;
using Khela.Game.Slither.Items;
using Khela.Game.Slither;
using Microsoft.AspNetCore.SignalR;
using Khela.Game.Dtos;
using Khela.Game.Models;

namespace Khela.Game.Services
{
    public class GameBroadcastService : BackgroundService
    {
        private readonly IHubContext<SnakeHub> _hubContext;
        private readonly IRedisService _redis;
        private readonly GameEngine _gameEngine; // Reference to the engine
        private readonly ILogger<GameBroadcastService> _logger;

        // Game loop settings
        private readonly int _tickRate = 20; // 20 times per second
        private TimeSpan _tickInterval;
        private const string SNAKE_KEY_PREFIX = "snake:";

        public GameBroadcastService(IHubContext<SnakeHub> hubContext, IRedisService redis, GameEngine gameEngine, ILogger<GameBroadcastService> logger)
        {
            _hubContext = hubContext;
            _redis = redis;
            _gameEngine = gameEngine;
            _logger = logger;
            _tickInterval = TimeSpan.FromMilliseconds(1000.0 / _tickRate);

            // --- SUBSCRIBE TO GAME ENGINE EVENTS ---
            _gameEngine.OnFoodEaten += HandleFoodEaten;
            _gameEngine.OnPlayerDied += HandlePlayerDied;
            _gameEngine.OnPlayerJoined += HandlePlayerJoined;
            _gameEngine.OnPlayerLeft += HandlePlayerLeft;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // This loop is now *only* for broadcasting the main world state
            while (!stoppingToken.IsCancellationRequested)
            {
                var startTime = DateTime.UtcNow;

                // Broadcast the full state
                await BroadcastGameState(stoppingToken);

                var endTime = DateTime.UtcNow;
                var timeToWait = _tickInterval - (endTime - startTime);
                if (timeToWait > TimeSpan.Zero)
                {
                    await Task.Delay(timeToWait, stoppingToken);
                }
            }
        }

        // --- Event Handlers (run immediately when GameEngine fires them) ---

        private async void HandleFoodEaten(string playerId, int foodId)
        {
            try
            {
                await _hubContext.Clients.All.SendAsync("OnFoodEaten", foodId, playerId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error broadcasting HandleFoodEaten: {ex.Message}");
            }
        }

        private async void HandlePlayerDied(SlitherPlayerState deadPlayer, SlitherPlayerState killer)
        {
            try
            {
                // TODO: Spawn food from deadPlayer's body
                string message = $"{deadPlayer.PlayerName} was eaten by {killer.PlayerName}";
                await _hubContext.Clients.All.SendAsync("OnPlayerDied", message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error broadcasting HandlePlayerDied: {ex.Message}");
            }
        }

        private async void HandlePlayerJoined(SlitherPlayerState player)
        {
            try
            {
                await _hubContext.Clients.All.SendAsync("OnPlayerJoined", player.ConnectionId, player.PlayerName, player.SkinID);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error broadcasting HandlePlayerJoined: {ex.Message}");
            }
        }

        private async void HandlePlayerLeft(string connectionId)
        {
            try
            {
                await _hubContext.Clients.All.SendAsync("OnPlayerLeft", connectionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error broadcasting HandlePlayerLeft: {ex.Message}");
            }
        }
          
        private async Task BroadcastGameState(CancellationToken token)
        {
            // 1. Get snake data
            var allSnakeKeys = await _redis.GetKeysByPatternAsync(SNAKE_KEY_PREFIX + "*");
            var snakes = new List<SlitherPlayerState>();
            foreach (var key in allSnakeKeys)
            {
                var snake = await _redis.GetAsync<SlitherPlayerState>(key);
                if (snake != null && snake.IsAlive) snakes.Add(snake);
            }

            // 2. Create Snake DTO array
            var snakeState = snakes.Select(s => new SnakeStateDto
            {
                Id = s.ConnectionId,
                HeadX = s.HeadPosition.X,
                HeadY = s.HeadPosition.Y,
                Score = s.Score,
                Length = s.TargetLength,
                SkinID = "DefaultSkin"
            }).ToArray();

            var foodState = new FoodStateDto[]
            {
                new FoodStateDto { Id = 1, PosX = 17, PosY = 0, ItemKey = "RedApple" },
                new FoodStateDto { Id = 2, PosX = 50, PosY = 0, ItemKey = "Watermelon" },
                new FoodStateDto { Id = 3, PosX = 20, PosY = 0, ItemKey = "RedApple" },
            };

            var worldUpdate = new WorldUpdateDto
            {
                Snakes = snakeState,
                Food = foodState
            };

            if (token.IsCancellationRequested) return;

            // 5. Send the 'worldUpdate' object
            await _hubContext.Clients.All.SendAsync("WorldUpdate", worldUpdate, cancellationToken: token);
        }
    }
}
