// ==============================================
// SnakeHub.cs
// Author: Reza Sabir (CasualLabInteractive)
// Version: 1.0.1 (Production Stable)
// Description:
// SignalR authoritative relay for player-world interaction.
// - Handles connection lifecycle
// - Joins, state updates, deaths, food reports
// - Forwards all logic to GameEngine (authoritative server)
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
        // === Join Main World ===
        // =====================================================
        public async Task JoinMainWorld(string playerId, string skinId)
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

            var player = await _worldManager.AddPlayerToWorldAsync(connectionId, world.WorldId, playerId, skinId);
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
                // --- FIX: Broadcast PlayerId, not ConnectionId ---
                await Clients.Group(world.WorldId).SendAsync("OnPlayerLeft", player.PlayerId);
            }

            await base.OnDisconnectedAsync(exception);
        }

        // =====================================================
        // === Client → Server: Player State Update ===
        // =====================================================
        public async Task UpdateState(List<SerializableVector2> bodySegments, bool isBoosting)
        {
            var gs = GameState.Instance;

            if (!gs.Connections.TryGetValue(Context.ConnectionId, out var playerId) ||
                string.IsNullOrEmpty(playerId))
                return;

            await _gameEngine.OnPlayerStateUpdate(playerId, bodySegments, isBoosting);
        }

        // =====================================================
        // === Client → Server: Food Eaten ===
        // =====================================================
        public async Task ReportFoodEaten(int foodId, string playerId)
        {
            if (string.IsNullOrEmpty(playerId))
                return;

            await _gameEngine.OnPlayerAteFood(playerId, foodId);
        }

        // =====================================================
        // === Client → Server: Player Died ===
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
