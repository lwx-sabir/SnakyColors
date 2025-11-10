using Khela.Game.Models.Configs;
using Khela.Game.Models;
using Khela.Game.Services.Redis;
using Microsoft.Extensions.Options;
using System.Numerics;
using System.Text.Json;
using StackExchange.Redis;
using Khela.Game.Dtos; // For IBatch

// ==============================================
//  WorldManagerService.cs
//  Author: Reza Sabir (CasualLabInteractive)
//  Version: 1.0.1 (Production Stable)
//  Description:
//  Thread-safe authoritative manager for player/world
//  lifecycle across distributed Redis instances.
//
//  - Handles atomic world creation
//  - Supports reconnect-safe joins
//  - Uses Redis locks for consistency
//  - Minimal Redis roundtrips
// ==============================================

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

        public async Task<PlayerState> AddPlayerToWorldAsync(string connectionId, string worldId, string playerId, string skinId, bool isAi = false)
        {
            string worldKey = WORLD_KEY_PREFIX + worldId;
            string playerKey = SNAKE_KEY_PREFIX + playerId;
            string connectionKey = CONNECTION_KEY_PREFIX + connectionId;
            string lockKey = $"lock:{worldKey}"; // Lock the WORLD
            var db = _redis.GetDatabase();

            // Use connectionId as lock token for player joins // Lock to prevent concurrent player joins in the same world
            if (await db.LockTakeAsync(lockKey, connectionId, TimeSpan.FromSeconds(5)))
            {
                try
                {
                    var world = await GetOrCreateWorldAsync(worldId); 
                    if (world == null) return null;

                    // Check if player is already in this world (stale mapping or reconnect) 
                    if (world.SnakeIds.ContainsKey(playerId) || world.AISnakeIds.ContainsKey(playerId))
                    {
                        _logger.LogWarning($"Player {playerId} already in world {worldId}. Treating as reconnect and updating connection mapping.");

                        // Try to fetch existing player and update connection
                        var existingSnake = await _redis.GetAsync<PlayerState>(playerKey);
                        if (existingSnake != null)
                        {
                            existingSnake.ConnectionId = connectionId;

                            var reconnTasks = new List<Task>
                            {
                                db.StringSetAsync(playerKey, JsonSerializer.Serialize(existingSnake))
                            };
                            if (!isAi)
                                reconnTasks.Add(db.StringSetAsync(connectionKey, playerId));

                            await Task.WhenAll(reconnTasks);
                            return existingSnake;
                        }
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

                    var tasks = new List<Task>
                        {
                            db.StringSetAsync(playerKey, JsonSerializer.Serialize(newSnake)),
                            db.StringSetAsync(worldKey, JsonSerializer.Serialize(world))
                        };
                    if (!isAi)
                        tasks.Add(db.StringSetAsync(connectionKey, playerId));

                    await Task.WhenAll(tasks);

                    _logger.LogInformation("PlayerJoined {@PlayerId} {@WorldId} {@IsAI}", playerId, worldId, isAi); 

                    return newSnake;
                }
                finally
                {
                    await db.LockReleaseAsync(lockKey, connectionId);
                }
            }
            _logger.LogWarning($"Failed to acquire lock for world {worldKey} to add player {playerId}.");

            // Passive re-check after a tiny delay (handles concurrent creation case)
            await Task.Delay(Random.Shared.Next(50, 150));
            var maybeWorld = await _redis.GetAsync<WorldState>(worldKey);
            if (maybeWorld != null && (maybeWorld.SnakeIds.ContainsKey(playerId) || maybeWorld.AISnakeIds.ContainsKey(playerId)))
            {
                var existingPlayer = await _redis.GetAsync<PlayerState>(playerKey);
                if (existingPlayer != null)
                {
                    _logger.LogInformation($"[JoinRecovered] Player {playerId} joined world {maybeWorld.WorldId} via concurrent thread.");
                    return existingPlayer;
                }
            }

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

                    var tasks = new List<Task>();

                    tasks.Add(db.KeyDeleteAsync(playerKey));

                    if (!string.IsNullOrEmpty(connIdToClear))
                        tasks.Add(db.KeyDeleteAsync(CONNECTION_KEY_PREFIX + connIdToClear));

                    if (world != null &&
                       (world.SnakeIds.TryRemove(playerId, out _) || world.AISnakeIds.TryRemove(playerId, out _)))
                    {
                        _logger.LogInformation($"Player {playerId} removed from world {world.WorldId}");
                        tasks.Add(db.StringSetAsync(worldKey, JsonSerializer.Serialize(world)));
                    } 
                    await Task.WhenAll(tasks);

                    return (player, world);
                }
                finally
                {
                    await db.LockReleaseAsync(lockKey, lockToken);
                }
            }

            // Failed to get lock, player is NOT removed from world list but might be deleted 
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

        public Task<WorldState> GetOrCreateMainWorldAsync() => GetOrCreateWorldAsync("main");

        public async Task<WorldState> GetOrCreateWorldAsync(string worldId)
        {
            string worldKey = WORLD_KEY_PREFIX + worldId;
            var world = await _redis.GetAsync<WorldState>(worldKey);
            if (world != null)
                return world;

            string lockKey = $"lock:create:{worldId}";
            string lockToken = Guid.NewGuid().ToString();
            var db = _redis.GetDatabase();

            if (await db.LockTakeAsync(lockKey, lockToken, TimeSpan.FromSeconds(5)))
            {
                try
                {
                    // Double check after acquiring lock
                    world = await _redis.GetAsync<WorldState>(worldKey);
                    if (world == null)
                    {
                        world = new WorldState(_defaultWorldConfig) { WorldId = worldId };
                        await _redis.SetAsync(worldKey, world);
                        _logger.LogInformation($"Created new world {worldId}");
                    }
                }
                finally
                {
                    await db.LockReleaseAsync(lockKey, lockToken);
                }
            }
            else
            {
                // Another thread is creating it — wait briefly and retry
                await Task.Delay(200);
                world = await _redis.GetAsync<WorldState>(worldKey);
            }

            return world!;
        } 
    }
}
