// ==============================================
// SnakeHub.cs (Hardened Version)
// Author: Reza Sabir (CasualLabInteractive)
// Version: 1.0.2 (Hardened)
// ==============================================

using Khela.Game.Services;
using Microsoft.AspNetCore.SignalR;
using Khela.Game.Models;
using Khela.Game.Models.States;

namespace Khela.Game.Managers.SRHubs
{
    public class SnakeHub : Hub
    {
        private readonly WorldManagerService _worldManager;
        private readonly GameEngine _gameEngine;

        public SnakeHub(WorldManagerService worldManager, GameEngine gameEngine)
        {
            _worldManager = worldManager;
            _gameEngine = gameEngine;
        }

        // =====================================================
        // === Join Main World (only time client sends ID) ===
        // =====================================================
        public async Task JoinMainWorld(string playerId, string skinId, float segmentSpacing)
        {
            if (string.IsNullOrEmpty(playerId))
                return;

            string connectionId = Context.ConnectionId;

            var world = await _worldManager.GetOrCreateMainWorldAsync();
            if (world == null)
            {
                await Clients.Caller.SendAsync("JoinFailed", "Could not find or create world.");
                return;
            }

            // playerId only trusted DURING JOIN
            var player = await _worldManager.AddPlayerToWorldAsync(
                connectionId,
                world.WorldId,
                playerId,
                segmentSpacing,
                skinId
            );

            if (player == null)
            {
                await Clients.Caller.SendAsync("JoinFailed", "Player already in world or error.");
                return;
            }

            await Groups.AddToGroupAsync(connectionId, world.WorldId);
            await Clients.Caller.SendAsync("OnJoinSuccess", player); 
        }

        // =====================================================
        // === Disconnect Cleanup ===
        // =====================================================
        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var (player, world) = await _worldManager.RemovePlayerFromWorldAsync(Context.ConnectionId);

            if (player != null && world != null)
            {
                await Clients.Group(world.WorldId).SendAsync("OnPlayerLeft", player.PlayerId);
            }

            await base.OnDisconnectedAsync(exception);
        }

        // =====================================================
        // === Client → Server: Player State Update (HARDENED) ===
        // =====================================================
        public async Task UpdateState(SerializableVector2 inputDir, bool isBoosting)
        {
            var gs = GameState.Instance;

            // Strict: never trust client to pass playerId
            if (!gs.Connections.TryGetValue(Context.ConnectionId, out var playerId) ||
                string.IsNullOrEmpty(playerId))
                return;

            await _gameEngine.OnPlayerStateUpdate(playerId, inputDir, isBoosting);
        }

        // =====================================================
        // === Client → Server: Food Eaten (HARDENED) ===
        // =====================================================
        public async Task ReportFoodEaten(int foodId)
        {
            var gs = GameState.Instance;

            if (!gs.Connections.TryGetValue(Context.ConnectionId, out var playerId) ||
                string.IsNullOrEmpty(playerId))
                return;

            await _gameEngine.OnPlayerAteFood(playerId, foodId);
        }

        // =====================================================
        // === Client → Server: Player Died (HARDENED) ===
        // =====================================================
        public async Task ReportPlayerDied(string killerId)
        {
            var gs = GameState.Instance;

            if (!gs.Connections.TryGetValue(Context.ConnectionId, out var playerId) ||
                string.IsNullOrEmpty(playerId))
                return;

            await _gameEngine.OnPlayerDied(playerId, killerId);
        }

        // =====================================================
        // === Connection Health Check ===
        // =====================================================
        public async Task Ping()
        {
            await Clients.Caller.SendAsync("Pong");
        }
    }
}
