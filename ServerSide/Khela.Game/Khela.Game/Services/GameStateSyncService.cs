// ==============================================
// GameStateRestoreService.cs
// Author: Reza Sabir (CasualLabInteractive)
// Version: 1.0.1 (Production Stable)
// Description:
// Provides API methods to rebuild in-memory GameState
// from Redis (after crash, cold start, or deployment restart).
// - Can restore a single world or all worlds
// - Reads foods from Hashes (foodhash:<worldId>)
// - Rehydrates players, worlds, and food caches
// ==============================================

using System.Text.Json;
using Khela.Game.Dtos;
using Khela.Game.Models.States;
using Khela.Game.Services.Redis;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Khela.Game.Services
{
    public sealed class GameStateSyncService:BackgroundService
    {
        private readonly ILogger<GameStateSyncService> _logger;
        private readonly IRedisService _redis;
        private readonly TimeSpan _syncInterval = TimeSpan.FromMilliseconds(500); // 0.5s tick

        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = false,
            IncludeFields = true,
            PropertyNameCaseInsensitive = true
        };

        private const string FOODHASH_PREFIX = "foodhash:";
        private const string FOODCACHE_PREFIX = "foodcache:";
        private const string WORLD_KEY_PREFIX = "world:";
        private const string SNAKE_KEY_PREFIX = "snake:";
        private const string CONNECTION_KEY_PREFIX = "connection:"; 

        public GameStateSyncService(IRedisService redis, ILogger<GameStateSyncService> logger)
        {
            _redis = redis;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("✅ GameStateSyncService started (interval = {Interval} ms)", _syncInterval.TotalMilliseconds);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await SyncOnceAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error during GameState sync");
                }

                await Task.Delay(_syncInterval, stoppingToken);
            }

            _logger.LogInformation("🛑 GameStateSyncService stopped.");
        }

        private async Task SyncOnceAsync(CancellationToken token)
        {
            var gs = GameState.Instance;
            var db = _redis.GetDatabase();
            int totalWrites = 0;

            // --- Sync Worlds ---
            foreach (var kv in gs.Worlds)
            {
                string worldKey = WORLD_KEY_PREFIX + kv.Key;
                string json = JsonSerializer.Serialize(kv.Value, _jsonOptions);
                await db.StringSetAsync(worldKey, json);
                totalWrites++;
            }

            // --- Sync Players ---
            foreach (var kv in gs.Players)
            {
                string playerKey = SNAKE_KEY_PREFIX + kv.Key;
                string json = JsonSerializer.Serialize(kv.Value, _jsonOptions);
                await db.StringSetAsync(playerKey, json);
                totalWrites++;
            }

            // --- Sync Connections (ConnID -> PlayerID) ---
            foreach (var kv in gs.Connections)
            {
                await db.StringSetAsync(CONNECTION_KEY_PREFIX + kv.Key, kv.Value);
                totalWrites++;
            }

            // --- Sync Foods (Grouped by World) ---
            foreach (var world in gs.Worlds.Values)
            {
                if (world.FoodIds == null || world.FoodIds.IsEmpty)
                    continue;

                var hashKey = FOODHASH_PREFIX + world.WorldId;
                var cacheKey = FOODCACHE_PREFIX + world.WorldId;
                var hashEntries = new List<HashEntry>(world.FoodIds.Count);
                var foodDtos = new List<FoodStateDto>(world.FoodIds.Count);

                foreach (var foodId in world.FoodIds.Keys)
                {
                    if (!gs.Foods.TryGetValue(foodId, out var food))
                        continue;

                    string json = JsonSerializer.Serialize(food, _jsonOptions);
                    hashEntries.Add(new HashEntry(food.Id.ToString(), json));

                    foodDtos.Add(new FoodStateDto
                    {
                        Id = food.Id,
                        PosX = food.Position.X,
                        PosY = food.Position.Y,
                        ItemKey = food.ItemKey
                    });
                }

                if (hashEntries.Count > 0)
                    await db.HashSetAsync(hashKey, hashEntries.ToArray());

                if (foodDtos.Count > 0)
                    await _redis.SetAsync(cacheKey, foodDtos.ToArray());

                totalWrites++;
            }

            _logger.LogDebug("🧠 Synced {Count} entries (Worlds+Players+Foods) at {Utc}", totalWrites, DateTime.UtcNow);
        }

        // ============================================================
        // === PUBLIC API ===
        // ============================================================

        /// <summary>
        /// Restores a single world and all its related data (players, foods).
        /// </summary>
        public async Task<bool> RestoreWorldAsync(string worldId)
        {
            var gs = GameState.Instance;
            var db = _redis.GetDatabase();

            try
            {
                string worldKey = WORLD_KEY_PREFIX + worldId;
                var world = await _redis.GetAsync<WorldState>(worldKey);

                if (world == null)
                {
                    _logger.LogWarning($"[Restore] No world found with ID={worldId}");
                    return false;
                }

                // === Restore Foods ===
                var foodHashKey = FOODHASH_PREFIX + worldId;
                var foodEntries = await db.HashGetAllAsync(foodHashKey);
                foreach (var entry in foodEntries)
                {
                    try
                    {
                        var food = JsonSerializer.Deserialize<FoodState>(entry.Value!, _jsonOptions);
                        if (food != null)
                        {
                            gs.Foods[food.Id] = food;
                            world.FoodIds[food.Id] = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, $"[Restore] Corrupted food entry {entry.Name}");
                    }
                }

                // === Restore Players ===
                var allSnakeKeys = world.SnakeIds.Keys.Concat(world.AISnakeIds.Keys)
                    .Select(pid => SNAKE_KEY_PREFIX + pid)
                    .ToArray();

                var snakeBatch = await _redis.GetBatchAsync<PlayerState>(allSnakeKeys);
                foreach (var kv in snakeBatch)
                {
                    var player = kv.Value;
                    if (player == null) continue;
                    gs.Players[player.PlayerId] = player;
                }

                // === Register the world ===
                gs.Worlds[world.WorldId] = world;

                _logger.LogInformation($"[Restore] Restored world={world.WorldId} (Players={world.SnakeIds.Count + world.AISnakeIds.Count}, Foods={world.FoodIds.Count})");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"[Restore] Error restoring world={worldId}");
                return false;
            }
        }

        /// <summary>
        /// Restores all worlds, players, and foods from Redis.
        /// </summary>
        public async Task<int> RestoreAllAsync()
        {
            var gs = GameState.Instance;
            gs.ResetAll(); // Ensure fresh memory state

            var worldKeys = await _redis.GetKeysByPatternAsync(WORLD_KEY_PREFIX + "*");
            int restored = 0;

            foreach (var worldKey in worldKeys)
            {
                var worldId = worldKey.Replace(WORLD_KEY_PREFIX, "");
                bool ok = await RestoreWorldAsync(worldId);
                if (ok) restored++;
            }

            // === Restore Connections ===
            var connKeys = await _redis.GetKeysByPatternAsync(CONNECTION_KEY_PREFIX + "*");
            foreach (var connKey in connKeys)
            {
                var playerId = await _redis.GetStringAsync(connKey);
                if (!string.IsNullOrEmpty(playerId))
                {
                    string connId = connKey.Replace(CONNECTION_KEY_PREFIX, "");
                    gs.Connections[connId] = playerId;
                }
            }

            _logger.LogInformation($"[Restore] Completed restoring {restored} world(s). Total players={gs.Players.Count}, foods={gs.Foods.Count}");
            return restored;
        }
    }
}
