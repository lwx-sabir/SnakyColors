using Khela.Game.Services;
using Microsoft.AspNetCore.SignalR;
using System.Numerics;
using Khela.Game.Services.Redis; // For IRedisService
using Khela.Game.Models; // For SerializableVector2

namespace Khela.Game.Managers.SRHubs
{
    public class SnakeHub : Hub
    {
        private readonly WorldManagerService _worldManager;
        private readonly GameEngine _gameEngine;
        private readonly IRedisService _redis;

        private const string CONNECTION_KEY_PREFIX = "connection:";

        public SnakeHub(WorldManagerService worldManager, GameEngine gameEngine, IRedisService redis)
        {
            _worldManager = worldManager;
            _gameEngine = gameEngine;
            _redis = redis;
        }

        public async Task JoinMainWorld(string playerId, string skinId)
        {
            if (string.IsNullOrEmpty(playerId)) return;
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
         
        public async Task UpdateState(List<SerializableVector2> bodySegments, bool isBoosting)
        {
            string playerId = await _redis.GetStringAsync(CONNECTION_KEY_PREFIX + Context.ConnectionId);
            if (string.IsNullOrEmpty(playerId)) return;

            await _gameEngine.OnPlayerStateUpdate(playerId, bodySegments, isBoosting);
        }
         
        public async Task ReportFoodEaten(int foodId, string playerId)
        { 
            if (string.IsNullOrEmpty(playerId)) return; 
            await _gameEngine.OnPlayerAteFood(playerId, foodId);
        }
         
        public async Task ReportPlayerDied(string killerId) // killerId can be null (for boundaries)
        {
            string playerId = await _redis.GetStringAsync(CONNECTION_KEY_PREFIX + Context.ConnectionId);
            if (string.IsNullOrEmpty(playerId)) return;

            await _gameEngine.OnPlayerDied(playerId, killerId);
        }

        public async Task Ping()
        {
            await Clients.Caller.SendAsync("Pong");
        }
    }
}
