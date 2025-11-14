using Khela.Game.Models.Configs;
using Khela.Game.Models.States;
using Microsoft.Extensions.Logging;
using System.Numerics;
using System.Collections.Concurrent;
using Khela.Game.Models;

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
        private readonly List<FoodState> _localItemsDefinitions = new List<FoodState>();
     
        public FoodService(ILogger<FoodService> logger)
        {
            _logger = logger;
            _localItemsDefinitions = LocalDataProvider.GetFoodDefinitions();
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

                Vector2 pos = GetSpawnPosition(world.Config.WorldSize);

                var ft = _localItemsDefinitions[_rng.Next(_localItemsDefinitions.Count)];  
                if(ft != null)
                {
                    var food = CreateFromTemplate(ft, id, pos);
                    gs.AddOrUpdateFood(food);
                    world.FoodIds[id] = true;
                } 
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

        public static FoodState CreateFromTemplate(FoodState template, int id, Vector2 pos, long serverTime = 0)
        {
            var food = new FoodState(id, pos, template.ItemKey, template.ItemType, template.ScoreValue, serverTime)
            {
                CollectibleType = template.CollectibleType,
                PowerupType = template.PowerupType,
                PowerUpDurationInSec = template.PowerUpDurationInSec,
                MaxInWorld = template.MaxInWorld,
                SpawnWeight = template.SpawnWeight
            };

            return food;
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

        private static Vector2 GetSpawnPosition(float worldSize)
        {
            // 70% clustered, 30% random
            if (_rng.NextDouble() < 0.70)
                return SpawnClustered(worldSize);

            return SpawnRandom(worldSize);
        }

        private static Vector2 SpawnRandom(float worldSize)
        {
            float half = worldSize / 2f;
            return new Vector2(
                _rng.NextSingle() * (half * 2) - half,
                _rng.NextSingle() * (half * 2) - half
            );
        }

        private static Vector2 SpawnClustered(float worldSize)
        {
            float half = worldSize / 2f;

            // pick a cluster center
            float cx = (_rng.NextSingle() * (half * 1.2f)) - (half * 0.6f);
            float cy = (_rng.NextSingle() * (half * 1.2f)) - (half * 0.6f);

            // radius around center
            float r = _rng.NextSingle() * 8f + 2f; // 2–10 radius

            // angle offset
            float angle = (float)(_rng.NextDouble() * Math.PI * 2);
            float dx = r * MathF.Cos(angle);
            float dy = r * MathF.Sin(angle);

            return new Vector2(cx + dx, cy + dy);
        }

    }
}
