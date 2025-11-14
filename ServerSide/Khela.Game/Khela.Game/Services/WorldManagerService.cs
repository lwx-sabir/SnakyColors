using Khela.Game.Models.Configs;
using Khela.Game.Models.States;
using Microsoft.Extensions.Logging;
using System.Numerics;

namespace Khela.Game.Services
{
    /// <summary>
    /// Thread-safe authoritative manager for player/world lifecycle.
    /// In-memory version: uses GameState.Instance; Redis persistence is handled elsewhere.
    /// </summary>
    public class WorldManagerService
    {
        private readonly ILogger<WorldManagerService> _logger;
        private readonly WorldConfig _defaultWorldConfig;

        private const string SNAKE_KEY_PREFIX = "snake:";
        private const string WORLD_KEY_PREFIX = "world:";
        private const string CONNECTION_KEY_PREFIX = "connection:";

        public WorldManagerService(ILogger<WorldManagerService> logger)
        {
            _logger = logger;
            _defaultWorldConfig = new WorldConfig();
        }

        // =====================================================================
        // ADD PLAYER TO WORLD (async API preserved)
        // =====================================================================

        public async Task<PlayerState?> AddPlayerToWorldAsync(
            string connectionId,
            string worldId,
            string playerId,
            string? skinId,
            bool isAi = false)
        {
            var gs = GameState.Instance;
            var worldLock = gs.GetWorldLock(worldId);

            // Try to mimic Redis lock semantics: wait a bit, else fallback
            if (!await worldLock.WaitAsync(TimeSpan.FromSeconds(5)))
            {
                _logger.LogWarning("Failed to acquire lock for world {WorldKey} to add player {PlayerId}.",
                    WORLD_KEY_PREFIX + worldId, playerId);

                // Passive re-check (as in original)
                await Task.Delay(Random.Shared.Next(50, 150));

                if (gs.TryGetWorld(worldId, out var maybeWorld) &&
                    (maybeWorld.SnakeIds.ContainsKey(playerId) ||
                     maybeWorld.AISnakeIds.ContainsKey(playerId)) &&
                    gs.TryGetPlayer(playerId, out var existingPlayer))
                {
                    _logger.LogInformation("[JoinRecovered] Player {PlayerId} already joined world {WorldId}.",
                        playerId, maybeWorld.WorldId);
                    return existingPlayer;
                }

                return null;
            }

            try
            {
                // Get or create world (same semantics)
                var world = await GetOrCreateWorldAsync(worldId);
                if (world == null)
                    return null;

                // Reconnect / stale mapping check
                if (world.SnakeIds.ContainsKey(playerId) || world.AISnakeIds.ContainsKey(playerId))
                {
                    _logger.LogWarning(
                        "Player {PlayerId} already in world {WorldId}. Treating as reconnect and updating connection mapping.",
                        playerId, worldId);

                    if (gs.TryGetPlayer(playerId, out var existingSnake))
                    {
                        existingSnake.ConnectionId = connectionId;
                        gs.AddOrUpdatePlayer(existingSnake);

                        if (!isAi)
                            gs.Connections[connectionId] = playerId;

                        return existingSnake;
                    }
                }

                // --- New player creation (unchanged math) ---
                float worldHalf = world.Config.WorldSize / 2f;
                float padding = 50f;

                Vector2 startPos = new(
                    (Random.Shared.NextSingle() * (world.Config.WorldSize - 2 * padding)) - (worldHalf - padding),
                    (Random.Shared.NextSingle() * (world.Config.WorldSize - 2 * padding)) - (worldHalf - padding)
                );

                var newSnake = new PlayerState(connectionId, startPos)
                {
                    PlayerId = playerId,
                    CurrentWorldId = worldId,
                    SkinID = skinId ?? "DefaultSkin",
                    IsAI = isAi,
                    PlayerName = isAi ? "AI" : "Player"
                };

                // Track in world indexes
                if (isAi)
                    world.AISnakeIds.TryAdd(playerId, true);
                else
                    world.SnakeIds.TryAdd(playerId, true);

                // Persist in in-memory state
                gs.AddOrUpdatePlayer(newSnake);
                gs.AddOrUpdateWorld(world);

                if (!isAi)
                    gs.Connections[connectionId] = playerId;

                //_logger.LogInformation("PlayerJoined {PlayerId} {WorldId} IsAI={IsAI}",
                //    playerId, worldId, isAi);

                return newSnake;
            }
            finally
            {
                worldLock.Release();
            }
        }

        // =====================================================================
        // REMOVE BY PLAYER ID (async API preserved)
        // =====================================================================

        public async Task<(PlayerState? player, WorldState? world)>
            RemovePlayerFromWorldByPlayerIdAsync(string playerId, string? connectionId = null)
        {
            var gs = GameState.Instance;

            if (!gs.TryGetPlayer(playerId, out var player) ||
                string.IsNullOrEmpty(player.CurrentWorldId))
            {
                // Cleanup connection mapping only (equivalent to old behavior)
                if (!string.IsNullOrEmpty(connectionId))
                    gs.Connections.TryRemove(connectionId, out _);

                return (player, null);
            }

            string worldId = player.CurrentWorldId;
            var worldLock = gs.GetWorldLock(worldId);

            if (!await worldLock.WaitAsync(TimeSpan.FromSeconds(5)))
            {
                _logger.LogWarning("Failed to get lock for world {WorldKey} to remove player {PlayerId}.",
                    WORLD_KEY_PREFIX + worldId, playerId);
                return (player, null);
            }

            try
            {
                if (!gs.TryGetWorld(worldId, out var world))
                {
                    // World missing: just delete player + connection
                    gs.RemovePlayer(playerId);
                    if (!string.IsNullOrEmpty(connectionId))
                        gs.Connections.TryRemove(connectionId, out _);
                    return (player, null);
                }

                // Remove player from dictionaries
                gs.RemovePlayer(playerId);

                world.SnakeIds.TryRemove(playerId, out _);
                world.AISnakeIds.TryRemove(playerId, out _);

                gs.AddOrUpdateWorld(world);

                // Remove connection mapping
                var connToClear = connectionId ?? player.ConnectionId;
                if (!string.IsNullOrEmpty(connToClear))
                    gs.Connections.TryRemove(connToClear, out _);

                _logger.LogInformation("Player {PlayerId} removed from world {WorldId}", playerId, world.WorldId);
                return (player, world);
            }
            finally
            {
                worldLock.Release();
            }
        }

        // =====================================================================
        // REMOVE BY CONNECTION ID (same contract)
        // =====================================================================

        public async Task<(PlayerState? player, WorldState? world)>
            RemovePlayerFromWorldAsync(string connectionId)
        {
            var gs = GameState.Instance;

            if (!gs.Connections.TryGetValue(connectionId, out var playerId) ||
                string.IsNullOrEmpty(playerId))
            {
                _logger.LogWarning(
                    "Player (Conn: {ConnectionId}) disconnected but had no PlayerId mapping.",
                    connectionId);
                return (null, null);
            }

            return await RemovePlayerFromWorldByPlayerIdAsync(playerId, connectionId);
        }

        // =====================================================================
        // GET OR CREATE WORLD (async signature preserved)
        // =====================================================================

        public Task<WorldState> GetOrCreateMainWorldAsync()
            => GetOrCreateWorldAsync("main");

        public async Task<WorldState> GetOrCreateWorldAsync(string worldId)
        {
            var gs = GameState.Instance;

            if (gs.TryGetWorld(worldId, out var existing))
                return existing;

            var worldLock = gs.GetWorldLock(worldId);

            if (!await worldLock.WaitAsync(TimeSpan.FromSeconds(5)))
            {
                // Another thread probably created it; try again
                if (gs.TryGetWorld(worldId, out var w2))
                    return w2;

                // Fallback: create anyway
                var fallback = new WorldState(_defaultWorldConfig) { WorldId = worldId };
                gs.AddOrUpdateWorld(fallback);
                _logger.LogInformation("Created new world {WorldId} (fallback, no lock).", worldId);
                return fallback;
            }

            try
            {
                if (!gs.TryGetWorld(worldId, out var world))
                {
                    world = new WorldState(_defaultWorldConfig) { WorldId = worldId };
                    gs.AddOrUpdateWorld(world);
                    _logger.LogInformation("Created new world {WorldId}", worldId);
                }

                return world;
            }
            finally
            {
                worldLock.Release();
            }
        }
    }
}
