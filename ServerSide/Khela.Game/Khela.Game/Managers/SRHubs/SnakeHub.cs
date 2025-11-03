using Khela.Game.Slither;
using Microsoft.AspNetCore.SignalR;
using System.Numerics;

namespace Khela.Game.Managers.SRHubs
{
    public class SnakeHub : Hub
    {
        private readonly GameEngine _gameEngine;

        // Use Dependency Injection to get your singleton GameEngine
        public SnakeHub(GameEngine gameEngine)
        {
            _gameEngine = gameEngine;
        }

        // Called when a new player (Unity client) connects
        public override async Task OnConnectedAsync()
        {
            // Get the unique connection ID for this player
            string connectionId = Context.ConnectionId;

            // Tell the GameEngine to create a new snake for this player
            await _gameEngine.AddPlayer(connectionId);

            await base.OnConnectedAsync();
        }

        // Called when a player disconnects
        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            // Get the unique connection ID for this player
            string connectionId = Context.ConnectionId;

            // Tell the GameEngine to remove the snake for this player
            await _gameEngine.RemovePlayer(connectionId);

            await base.OnDisconnectedAsync(exception);
        }

        /// <summary>
        /// This is the main method your Unity client will call every frame.
        /// It's a "fire and forget" update of the player's target direction.
        /// </summary>
        /// <param name="targetX">The world X-coordinate the player is pointing at.</param>
        /// <param name="targetY">The world Y-coordinate the player is pointing at.</param>
        public async Task UpdateTarget(float targetX, float targetY)
        {
            await _gameEngine.OnPlayerInput(Context.ConnectionId, new Vector2(targetX, targetY));
        }

        /// <summary>
        /// Called when the player clicks the "boost" button.
        /// </summary>
        public async Task SetBoost(bool isBoosting)
        {
            await _gameEngine.OnPlayerBoost(Context.ConnectionId, isBoosting);
        }

        /// <summary>
        /// A simple test method for the client to check the connection.
        /// </summary>
        public async Task Ping()
        {
            // Replies *only* to the client that called this method
            await Clients.Caller.SendAsync("Pong");
        } 
    }
}
