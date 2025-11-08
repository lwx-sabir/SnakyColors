using Khela.Game.Models;
using Khela.Game.Models.Configs;
using Khela.Game.Services.Redis;
using System.Numerics;
using StackExchange.Redis;
using System.Text.Json;
using System.Linq;

namespace Khela.Game.Services
{
    public class FoodService
    {
        private readonly IRedisService _redis;
        private readonly ILogger<FoodService> _logger;

        private const string FOOD_KEY_PREFIX = "food:";
        private const string WORLD_KEY_PREFIX = "world:";
        private static readonly Random _rng = new Random();

        public FoodService(IRedisService redis, ILogger<FoodService> logger)
        {
            _redis = redis;
            _logger = logger;
        }

        public async Task ManageFoodSpawningAsync(WorldState world)
        {
            // Reconcile missing food keys in Redis to prevent spawn from stalling
            if (world.FoodIds.Count > 0)
            {
                var existing = await GetFoodBatchAsync(world.FoodIds.Keys);
                var missingIds = world.FoodIds.Keys.Where(id => !existing.ContainsKey(FOOD_KEY_PREFIX + id)).ToList();
                if (missingIds.Count > 0)
                {
                    foreach (var id in missingIds)
                        world.FoodIds.TryRemove(id, out _);
                    // Persist pruned world so counts are correct even if we don't spawn this tick
                    await _redis.SetAsync(WORLD_KEY_PREFIX + world.WorldId, world);
                }
            }

            int foodNeeded = world.Config.TargetFoodCount - world.FoodIds.Count;
            if (foodNeeded <= 0) return;

            int itemsToSpawnThisTick = Math.Min(foodNeeded, 50);
            if (itemsToSpawnThisTick <= 0) return;

            string[] foodKeys = { "RedApple", "Watermelon" };
            var batch = _redis.GetDatabase().CreateBatch();

            float worldHalf = world.Config.WorldSize / 2f;
            float padding = 2f;
            float min = -worldHalf + padding;
            float max = worldHalf - padding;

            for (int i = 0; i < itemsToSpawnThisTick; i++)
            {
                Vector2 pos = new Vector2(
                    _rng.NextSingle() * (max - min) + min,
                    _rng.NextSingle() * (max - min) + min
                );
                string itemKey = foodKeys[_rng.Next(foodKeys.Length)];
                int id = _rng.Next(int.MaxValue);
                var food = new Food(id, pos, itemKey);

                // Add food to Redis
                _ = batch.StringSetAsync(FOOD_KEY_PREFIX + id, JsonSerializer.Serialize(food));

                // Add food ID to the world's master list
                world.FoodIds.TryAdd(id, true);

                // --- REMOVED: Spatial grid logic ---
            }

            _ = batch.StringSetAsync(WORLD_KEY_PREFIX + world.WorldId, JsonSerializer.Serialize(world));
            batch.Execute();
        }

        public async Task RemoveFoodAsync(WorldState world, int foodId)
        {
            // Remove from main list
            world.FoodIds.TryRemove(foodId, out _);

            // Start a batch to delete food and save world
            var batch = _redis.GetDatabase().CreateBatch();
            _ = batch.KeyDeleteAsync(FOOD_KEY_PREFIX + foodId);
            _ = batch.StringSetAsync(WORLD_KEY_PREFIX + world.WorldId, JsonSerializer.Serialize(world));
            batch.Execute();
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
