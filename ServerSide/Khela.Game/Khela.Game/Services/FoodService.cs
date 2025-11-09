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

        private const string FOOD_KEY_PREFIX = "food:"; // legacy per-food key (kept for compatibility)
        private const string WORLD_KEY_PREFIX = "world:";
        private const string FOODHASH_PREFIX = "foodhash:";   // Redis HASH per world: field=id, value=Food JSON
        private const string FOODCACHE_PREFIX = "foodcache:"; // cached FoodStateDto[] for broadcast/AI
        private const string FOODLIST_PREFIX = "foodlist:";
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
            if (!_lastReconcileUtc.TryGetValue(world.WorldId, out var lastRecon) || (now - lastRecon) >= TimeSpan.FromSeconds(5))
            {
                _lastReconcileUtc[world.WorldId] = now;
                var hashKey = FOODHASH_PREFIX + world.WorldId;
                var hlen = await db.HashLengthAsync(hashKey);
                if (hlen == 0)
                {
                    world.FoodIds.Clear();
                }
                else
                {
                    // Use HKEYS to avoid fetching values
                    var keys = await db.HashKeysAsync(hashKey);
                    var idSet = new HashSet<int>(keys.Select(k => int.TryParse(k.ToString(), out var id) ? id : -1).Where(i => i >= 0));
                    foreach (var id in world.FoodIds.Keys)
                    {
                        if (!idSet.Contains(id)) world.FoodIds.TryRemove(id, out _);
                    }
                    foreach (var id in idSet)
                    {
                        world.FoodIds[id] = true;
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
