using Khela.Game.Models.Configs;
using Khela.Game.Models;
using Khela.Game.Services.Redis;
using Microsoft.Extensions.Options;
using System.Numerics;
using System.Text.Json;
using StackExchange.Redis;
using Khela.Game.Dtos; // For IBatch

namespace Khela.Game.Services
{
    public class WorldManagerService
    {
        private readonly IRedisService _redis;
        private readonly ILogger<WorldManagerService> _logger;
        private readonly WorldConfig _defaultWorldConfig;

        private const string SNAKE_KEY_PREFIX = "snake:";
        private const string WORLD_KEY_PREFIX = "world:";
        private const string CONNECTION_KEY_PREFIX = "connection:";

        public WorldManagerService(IRedisService redis, ILogger<WorldManagerService> logger)
        {
            _redis = redis;
            _logger = logger;
            _defaultWorldConfig = new WorldConfig();
        }

        public async Task<WorldState> GetOrCreateMainWorldAsync()
        {
            var world = await _redis.GetAsync<WorldState>(WORLD_KEY_PREFIX + "main");
            if (world == null)
            {
                _logger.LogInformation("Main world not found. Creating...");
                var worldConfig = new WorldConfig();
                world = new WorldState(worldConfig) { WorldId = "main" };
                await _redis.SetAsync(WORLD_KEY_PREFIX + "main", world);
            }
            return world;
        }

        public async Task<PlayerState> AddPlayerToWorldAsync(string connectionId, string worldId, string playerId, string skinId, bool isAi = false)
        {
            string worldKey = WORLD_KEY_PREFIX + worldId;
            string playerKey = SNAKE_KEY_PREFIX + playerId;
            string connectionKey = CONNECTION_KEY_PREFIX + connectionId;
            string lockKey = $"lock:{worldKey}"; // Lock the WORLD
            var db = _redis.GetDatabase();

            // Use connectionId as lock token for player joins
            if (await db.LockTakeAsync(lockKey, connectionId, TimeSpan.FromSeconds(5)))
            {
                try
                {
                    var world = await _redis.GetAsync<WorldState>(worldKey);
                    if (world == null) return null; // World not found

                    // Check if player is already in this world (stale mapping or reconnect)
                    if (world.SnakeIds.ContainsKey(playerId) || world.AISnakeIds.ContainsKey(playerId))
                    {
                        _logger.LogWarning($"Player {playerId} already in world {worldId}. Treating as reconnect and updating connection mapping.");

                        // Try to fetch existing player and update connection
                        var existingSnake = await _redis.GetAsync<PlayerState>(playerKey);
                        if (existingSnake != null)
                        {
                            existingSnake.ConnectionId = connectionId;

                            var reconnectBatch = db.CreateBatch();
                            _ = reconnectBatch.StringSetAsync(playerKey, System.Text.Json.JsonSerializer.Serialize(existingSnake));
                            if (!isAi)
                            {
                                _ = reconnectBatch.StringSetAsync(connectionKey, playerId);
                            }
                            reconnectBatch.Execute();

                            return existingSnake;
                        }

                        // If no player object, fall through to create anew
                    }

                    // --- Player is new, create them ---
                    float worldHalf = world.Config.WorldSize / 2f;
                    float padding = 50f;
                    Vector2 startPos = new Vector2(
                        (Random.Shared.NextSingle() * (world.Config.WorldSize - 2 * padding)) - (worldHalf - padding),
                        (Random.Shared.NextSingle() * (world.Config.WorldSize - 2 * padding)) - (worldHalf - padding)
                    );

                    var newSnake = new PlayerState(connectionId, startPos)
                    {
                        PlayerId = playerId,
                        CurrentWorldId = worldId,
                        SkinID = skinId ?? "DefaultSkin",
                        IsAI = isAi,
                        PlayerName = isAi ? "AI" : "Player" // TODO: Get from token
                    };

                    // --- Add player to world's list ---
                    if (isAi)
                    {
                        world.AISnakeIds.TryAdd(playerId, true);
                    }
                    else world.SnakeIds.TryAdd(playerId, true);
                     
                    var batch = db.CreateBatch();
                    _ = batch.StringSetAsync(playerKey, JsonSerializer.Serialize(newSnake)); // 1. Save new snake
                    _ = batch.StringSetAsync(worldKey, JsonSerializer.Serialize(world));      // 2. Save updated world
                    if (!isAi) // AI doesn't have a real connection
                    {
                        _ = batch.StringSetAsync(connectionKey, playerId); // 3. Save connection mapping
                    }
                    batch.Execute(); 

                    _logger.LogInformation($"Player {playerId} added to world {worldId}"); 
                    return newSnake;
                }
                finally
                {
                    await db.LockReleaseAsync(lockKey, connectionId);
                }
            }
            _logger.LogWarning($"Failed to acquire lock for world {worldKey} to add player {playerId}.");
            return null; // Failed to get lock
        }

        public async Task<(PlayerState, WorldState)> RemovePlayerFromWorldByPlayerIdAsync(string playerId, string connectionId = null)
        {
            string playerKey = SNAKE_KEY_PREFIX + playerId;
            var player = await _redis.GetAsync<PlayerState>(playerKey);

            string connIdToClear = connectionId ?? player?.ConnectionId;
            string worldKey = WORLD_KEY_PREFIX + player?.CurrentWorldId;
            string lockKey = $"lock:{worldKey}";
            string lockToken = Guid.NewGuid().ToString(); // Use a unique token for removal

            if (player == null || string.IsNullOrEmpty(player.CurrentWorldId))
            {
                // Player or world is missing, just clean up
                var dB = _redis.GetDatabase();
                var cleanupBatch = dB.CreateBatch();
                _ = cleanupBatch.KeyDeleteAsync(playerKey);
                if (!string.IsNullOrEmpty(connIdToClear))
                {
                    _ = cleanupBatch.KeyDeleteAsync(CONNECTION_KEY_PREFIX + connIdToClear);
                }
                cleanupBatch.Execute();
                return (player, null);
            }

            var db = _redis.GetDatabase();
            if (await db.LockTakeAsync(lockKey, lockToken, TimeSpan.FromSeconds(5)))
            {
                WorldState world = null;
                try
                {
                    world = await _redis.GetAsync<WorldState>(worldKey);
                    var batch = db.CreateBatch();

                    // 1. Delete player
                    _ = batch.KeyDeleteAsync(playerKey);
                    // 2. Delete connection mapping
                    if (!string.IsNullOrEmpty(connIdToClear))
                    {
                        _ = batch.KeyDeleteAsync(CONNECTION_KEY_PREFIX + connIdToClear);
                    }

                    // 3. Update and save world
                    if (world != null)
                    {
                        if (world.SnakeIds.TryRemove(playerId, out _) || world.AISnakeIds.TryRemove(playerId, out _))
                        {
                            _logger.LogInformation($"Player {playerId} removed from world {world.WorldId}");
                            _ = batch.StringSetAsync(worldKey, JsonSerializer.Serialize(world));
                        }
                    }

                    batch.Execute();
                    return (player, world);
                }
                finally
                {
                    await db.LockReleaseAsync(lockKey, lockToken);
                }
            }

            // Failed to get lock, player is NOT removed from world list but might be deleted
            // This is a partial failure, but we return what we have
            _logger.LogWarning($"Failed to get lock for world {worldKey} to remove player {playerId}.");
            return (player, null);
        }

        public async Task<(PlayerState, WorldState)> RemovePlayerFromWorldAsync(string connectionId)
        {
            string connectionKey = CONNECTION_KEY_PREFIX + connectionId;
            string playerId = await _redis.GetStringAsync(connectionKey);

            if (string.IsNullOrEmpty(playerId))
            {
                _logger.LogWarning($"Player (Conn: {connectionId}) disconnected but had no PlayerId mapping.");
                return (null, null);
            }
            return await RemovePlayerFromWorldByPlayerIdAsync(playerId, connectionId);
        }
    }
}
