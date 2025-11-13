using Khela.Game.Models.Configs;
using Khela.Game.Models.States;
using Microsoft.Extensions.Logging;
using System.Numerics;
using System.Collections.Concurrent;

namespace Khela.Game.Services
{
    /// <summary>
    /// In-memory food manager for each world.
    /// No Redis I/O in runtime — all data lives in GameState.
    /// Persistence handled by GameStateSyncService.
    /// </summary>
    public class FoodService
    {
        private readonly ILogger<FoodService> _logger;
        private static readonly Random _rng = new();
        private readonly ConcurrentDictionary<string, DateTime> _lastSpawnUtc = new();
        private readonly ConcurrentDictionary<string, DateTime> _lastReconcileUtc = new();

        public FoodService(ILogger<FoodService> logger)
        {
            _logger = logger;
        }

        // =====================================================
        // MAIN ENTRY: MANAGE FOOD SPAWNING
        // =====================================================

        public Task ManageFoodSpawningAsync(WorldState world)
        {
            var now = DateTime.UtcNow;
            var gs = GameState.Instance;

            // Reconcile occasionally (remove stale foods not in global dictionary)
            if (!_lastReconcileUtc.TryGetValue(world.WorldId, out var lastRecon) || (now - lastRecon) >= TimeSpan.FromSeconds(5))
            {
                _lastReconcileUtc[world.WorldId] = now;
                var toRemove = world.FoodIds.Keys
                    .Where(id => !gs.Foods.ContainsKey(id))
                    .ToArray();

                foreach (var fid in toRemove)
                    world.FoodIds.TryRemove(fid, out _);
            }

            // Limit spawn cadence per world
            if (_lastSpawnUtc.TryGetValue(world.WorldId, out var lastSpawn) && (now - lastSpawn) < TimeSpan.FromMilliseconds(800))
                return Task.CompletedTask;

            // Calculate deficit
            int currentCount = world.FoodIds.Count;
            int targetCount = world.Config.TargetFoodCount;
            int deficit = targetCount - currentCount;
            if (deficit <= 0)
                return Task.CompletedTask;

            _lastSpawnUtc[world.WorldId] = now;

            int spawnCount = Math.Min(deficit, 10);
            float worldHalf = world.Config.WorldSize / 2f;
            float padding = 2f;
            float min = -worldHalf + padding;
            float max = worldHalf - padding;
            string[] foodKeys = { "RedApple", "Watermelon", "Berry", "Banana" };

            for (int i = 0; i < spawnCount; i++)
            {
                int id;
                do { id = _rng.Next(int.MaxValue); } while (gs.Foods.ContainsKey(id));

                Vector2 pos = new(
                    _rng.NextSingle() * (max - min) + min,
                    _rng.NextSingle() * (max - min) + min
                );

                string itemKey = foodKeys[_rng.Next(foodKeys.Length)];
                var food = new FoodState(id, pos, itemKey);

                gs.AddOrUpdateFood(food);
                world.FoodIds[id] = true;
            }

            _logger.LogDebug("Spawned {Count} foods in world {WorldId} (now {Total})",
                spawnCount, world.WorldId, world.FoodIds.Count);

            return Task.CompletedTask;
        }

        // =====================================================
        // FOOD REMOVAL
        // =====================================================

        public Task RemoveFoodAsync(WorldState world, int foodId)
        {
            var gs = GameState.Instance;

            world.FoodIds.TryRemove(foodId, out _);
            gs.RemoveFood(foodId);

            _logger.LogDebug("Removed food {FoodId} from world {WorldId}", foodId, world.WorldId);
            return Task.CompletedTask;
        }

        // =====================================================
        // BULK ACCESS
        // =====================================================

        public Dictionary<int, FoodState> GetFoodsForWorld(string worldId)
        {
            var gs = GameState.Instance;
            if (!gs.TryGetWorld(worldId, out var world))
                return new();

            var dict = new Dictionary<int, FoodState>(world.FoodIds.Count);
            foreach (var fid in world.FoodIds.Keys)
            {
                if (gs.TryGetFood(fid, out var f))
                    dict[fid] = f;
            }

            return dict;
        }

        // =====================================================
        // DEBUG / RESET HELPERS
        // =====================================================

        public void ResetWorldFoods(string worldId)
        {
            if (!GameState.Instance.TryGetWorld(worldId, out var world))
                return;

            foreach (var fid in world.FoodIds.Keys)
                GameState.Instance.RemoveFood(fid);

            world.FoodIds.Clear();
            _logger.LogInformation("Cleared all foods for world {WorldId}", worldId);
        }
    }
}
