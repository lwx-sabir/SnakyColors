using Khela.Game.Models;
using Khela.Game.Models.Configs;
using Khela.Game.Services.Redis;
using System.Numerics;
using StackExchange.Redis;
using System.Text.Json;
using System.Linq;
using System.Collections.Generic;
using Khela.Game.Dtos;

namespace Khela.Game.Services
{
    public class FoodService
    {
        private readonly IRedisService _redis;
        private readonly ILogger<FoodService> _logger;

        private const string FOOD_KEY_PREFIX = "food:";
        private const string WORLD_KEY_PREFIX = "world:";
        private const string FOODHASH_PREFIX = "foodhash:";   
        private const string FOODCACHE_PREFIX = "foodcache:"; 
        private static readonly Random _rng = new Random();

        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> _lastSpawnUtc = new();
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> _lastReconcileUtc = new();

        public FoodService(IRedisService redis, ILogger<FoodService> logger)
        {
            _redis = redis;
            _logger = logger;
        }

        public async Task ManageFoodSpawningAsync(WorldState world)
        {
            var now = DateTime.UtcNow;
            var db = _redis.GetDatabase();

            // Reconcile world.FoodIds against HASH occasionally (cheap scan)
            if (!_lastReconcileUtc.TryGetValue(world.WorldId, out var lastRecon) || (now - lastRecon) >= TimeSpan.FromSeconds(4))
            {
                _lastReconcileUtc[world.WorldId] = now;
                var hashKey = FOODHASH_PREFIX + world.WorldId;
                var entries = await db.HashGetAllAsync(hashKey);

                if (entries.Length == 0)
                {
                    world.FoodIds.Clear();
                    await _redis.SetAsync(FOODCACHE_PREFIX + world.WorldId, Array.Empty<FoodStateDto>());
                }
                else
                {
                    var foodDtos = new List<FoodStateDto>(entries.Length);
                    var idSet = new HashSet<int>();

                    foreach (var e in entries)
                    {
                        if (int.TryParse(e.Name.ToString(), out var id))
                        {
                            idSet.Add(id);
                            try
                            {
                                var food = JsonSerializer.Deserialize<Food>(e.Value!);
                                if (food != null)
                                    foodDtos.Add(new FoodStateDto { Id = food.Id, PosX = food.Position.X, PosY = food.Position.Y, ItemKey = food.ItemKey });
                            }
                            catch { /* ignore broken entries */ }
                        }
                    }

                    // sync world.FoodIds
                    foreach (var id in world.FoodIds.Keys.Where(k => !idSet.Contains(k)).ToArray())
                        world.FoodIds.TryRemove(id, out _);

                    foreach (var id in idSet)
                        world.FoodIds[id] = true;

                    // update cache if stale or missing
                    var cacheKeyW = FOODCACHE_PREFIX + world.WorldId;
                    var existingF = await _redis.GetAsync<FoodStateDto[]>(cacheKeyW);

                    bool shouldUpdateCache = false;

                    if (existingF == null)
                    {
                        shouldUpdateCache = true; // cache missing
                    }
                    else if (existingF.Length != foodDtos.Count)
                    {
                        shouldUpdateCache = true; // count mismatch
                    }
                    else
                    {
                        // Check if IDs differ (avoid full object comparison)
                        var existingIds = existingF.Select(f => f.Id).OrderBy(id => id);
                        var newIds = foodDtos.Select(f => f.Id).OrderBy(id => id);
                        if (!existingIds.SequenceEqual(newIds))
                            shouldUpdateCache = true;
                    }

                    if (shouldUpdateCache)
                    {
                        await _redis.SetAsync(cacheKeyW, foodDtos.ToArray());
                        _logger.LogDebug($"[FoodCache] Rebuilt for world={world.WorldId}, count={foodDtos.Count}");
                    }
                }

                await _redis.SetAsync(WORLD_KEY_PREFIX + world.WorldId, world);
            }

            // Compute how many to spawn using current HASH length
            long currentCount = await db.HashLengthAsync(FOODHASH_PREFIX + world.WorldId);
            int foodNeeded = world.Config.TargetFoodCount - (int)currentCount;
            if (foodNeeded <= 0) return;

            // Throttle spawn cadence per world to reduce contention under load
            if (_lastSpawnUtc.TryGetValue(world.WorldId, out var last) && (now - last) < TimeSpan.FromMilliseconds(800))
                return;

            int itemsToSpawnThisTick = Math.Min(foodNeeded, 10);
            if (itemsToSpawnThisTick <= 0) return;
            _lastSpawnUtc[world.WorldId] = now;

            string[] foodKeys = { "RedApple", "Watermelon" };

            float worldHalf = world.Config.WorldSize / 2f;
            float padding = 2f;
            float min = -worldHalf + padding;
            float max = worldHalf - padding;

            var spawnStart = DateTime.UtcNow;
            var newEntries = new List<HashEntry>(itemsToSpawnThisTick);
            var newCacheItems = new List<FoodStateDto>(itemsToSpawnThisTick);
            for (int i = 0; i < itemsToSpawnThisTick; i++)
            {
                Vector2 pos = new Vector2(
                    _rng.NextSingle() * (max - min) + min,
                    _rng.NextSingle() * (max - min) + min
                );
                string itemKey = foodKeys[_rng.Next(foodKeys.Length)];
                int id;
                do { id = _rng.Next(int.MaxValue); } while (world.FoodIds.ContainsKey(id));
                var food = new Food(id, pos, itemKey);

                newEntries.Add(new HashEntry(id.ToString(), JsonSerializer.Serialize(food)));
                world.FoodIds[id] = true;
                newCacheItems.Add(new FoodStateDto { Id = id, PosX = pos.X, PosY = pos.Y, ItemKey = itemKey });
            }

            // Write HASH entries and world
            await db.HashSetAsync(FOODHASH_PREFIX + world.WorldId, newEntries.ToArray());
            await _redis.SetAsync(WORLD_KEY_PREFIX + world.WorldId, world);

            // Incrementally update broadcast/AI cache
            var cacheKey = FOODCACHE_PREFIX + world.WorldId;
            var existing = await _redis.GetAsync<FoodStateDto[]>(cacheKey) ?? Array.Empty<FoodStateDto>();
            var merged = new FoodStateDto[existing.Length + newCacheItems.Count];
            Array.Copy(existing, merged, existing.Length);
            for (int i = 0; i < newCacheItems.Count; i++) merged[existing.Length + i] = newCacheItems[i];
            await _redis.SetAsync(cacheKey, merged);

            var elapsed = (DateTime.UtcNow - spawnStart).TotalMilliseconds;
            if (itemsToSpawnThisTick > 0)
                _logger.LogInformation($"Food spawn: +{itemsToSpawnThisTick} (world={world.WorldId}) in {elapsed:F1}ms; total~={(int)(currentCount + itemsToSpawnThisTick)}");
        }

        public async Task RemoveFoodAsync(WorldState world, int foodId)
        {
            // Remove from world index
            world.FoodIds.TryRemove(foodId, out _);
            await _redis.SetAsync(WORLD_KEY_PREFIX + world.WorldId, world);

            // Delete from per-world HASH
            var db = _redis.GetDatabase();
            await db.HashDeleteAsync(FOODHASH_PREFIX + world.WorldId, foodId.ToString());

            // Remove from broadcast/AI cache incrementally
            var cacheKey = FOODCACHE_PREFIX + world.WorldId;
            var existing = await _redis.GetAsync<FoodStateDto[]>(cacheKey);
            if (existing != null && existing.Length > 0)
            {
                var filtered = existing.Where(f => f.Id != foodId).ToArray();
                await _redis.SetAsync(cacheKey, filtered);
            }
        }

        public async Task<Dictionary<string, Food>> GetFoodBatchAsync(IEnumerable<int> foodIds)
        {
            var foodKeys = foodIds.Select(id => FOOD_KEY_PREFIX + id).ToArray();
            if (foodKeys.Length == 0)
                return new Dictionary<string, Food>();

            return await _redis.GetBatchAsync<Food>(foodKeys);
        }
    }
}
