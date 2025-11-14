using Khela.Game.Managers.SRHubs;
using Microsoft.AspNetCore.SignalR;
using Khela.Game.Dtos;
using Khela.Game.Models.States;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace Khela.Game.Services
{
    /// <summary>
    /// Broadcasts world state snapshots to SignalR clients (10Hz).
    /// Uses GameState.Instance instead of Redis — all runtime data is in memory.
    /// </summary>
    public class GameBroadcastService : BackgroundService
    {
        private readonly IHubContext<SnakeHub> _hubContext;
        private readonly GameEngine _gameEngine;
        private readonly ILogger<GameBroadcastService> _logger;

        private readonly ConcurrentDictionary<string, WorldUpdateDto> _lastSentState = new();
        private readonly ConcurrentDictionary<string, DateTime> _lastBroadcastPerWorld = new();

        private static readonly HashSet<string> _seenSnakes = new HashSet<string>(128);
        private static readonly HashSet<int> _seenFood = new HashSet<int>(512);

        private readonly TimeSpan _broadcastInterval = TimeSpan.FromMilliseconds(50); // 20Hz

        public GameBroadcastService(
            IHubContext<SnakeHub> hubContext,
            GameEngine gameEngine,
            ILogger<GameBroadcastService> logger)
        {
            _hubContext = hubContext;
            _gameEngine = gameEngine;
            _logger = logger;

            // Hook engine events
            _gameEngine.OnWorldTickCompleted += async (worldId, utcNow) => await HandleWorldTickCompleted(worldId, utcNow);
            _gameEngine.OnFoodEaten += async (playerId, foodId, worldId) => await HandleFoodEaten(playerId, foodId, worldId);
            _gameEngine.PlayerDied += async (dead, killer) => await HandlePlayerDied(dead, killer);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Delay(3000, stoppingToken);
            _logger.LogInformation("GameBroadcastService started (20Hz).");

            // Idle; updates are event-driven
            try { await Task.Delay(Timeout.Infinite, stoppingToken); } catch { }
        }

        // ---------------------------------------------------------------------
        // WORLD TICK → SNAPSHOT BROADCAST
        // ---------------------------------------------------------------------

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

            if (!GameState.Instance.TryGetWorld(worldId, out var world) ||
                world.CurrentStatus != GameStatus.Running)
                return;

            // Cached snapshot from GameState
            var worldState = GameState.Instance.CachedWorldSnapshots[worldId];
            var deltaFood = GameState.Instance.BuildFoodDelta(worldId);
             
            if (worldState == null)
                return;

            // Diff check
            //if (world.Tick % 10 == 0 && _lastSentState.TryGetValue(worldId, out var prev))
            //{
            //    bool snakesChanged = worldState.Snakes.Any(s =>
            //        prev.Snakes.All(p => p.PlayerId != s.PlayerId ||
            //                             p.HeadPosition.X != s.HeadPosition.X ||
            //                             p.HeadPosition.Y != s.HeadPosition.Y));

            //    bool foodChanged = worldState.Food.Any(f =>
            //        prev.Food.All(p => p.Id != f.Id ||
            //                           p.PosX != f.PosX ||
            //                           p.PosY != f.PosY));

            //    if (!snakesChanged && !foodChanged)
            //        return;
            //}

            try
            {
                worldState.ServerTimeSec = world.Tick / (double)world.Config.TickRate;

                // =============================
                //   Measure snapshot size
                // =============================
                //var json = JsonSerializer.Serialize(new { deltaFood , worldState});
                //var byteCount = Encoding.UTF8.GetByteCount(json);
                //var kb = byteCount / 1024.0;

                //_logger.LogInformation(
                //    $"[Broadcast] world={worldId} snapshot size = {kb:F2} KB ({byteCount} bytes), " +
                //    $"snakes={worldState.Snakes.Length}");

                // =============================

                await _hubContext.Clients.Group(worldId)
                    .SendAsync("WorldUpdate", worldState, deltaFood);

                _lastSentState[worldId] = worldState;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, $"Broadcast send failed for {world.WorldId}");
            }

            var ms = (DateTime.UtcNow - start).TotalMilliseconds;
            _logger.LogTrace($"Broadcast world={world.WorldId} took {ms:F1}ms (snakes={worldState.Snakes.Length}, food={deltaFood.Added.Count})");
        }


        // ---------------------------------------------------------------------
        // EVENT: FOOD EATEN
        // ---------------------------------------------------------------------

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

        // ---------------------------------------------------------------------
        // EVENT: PLAYER DIED
        // ---------------------------------------------------------------------

        private async Task HandlePlayerDied(PlayerState deadPlayer, PlayerState? killer)
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

        // ---------------------------------------------------------------------
        // BROADCAST RATE CONTROL
        // ---------------------------------------------------------------------

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
