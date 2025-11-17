using Khela.Game.Models.Configs;
using Khela.Game.Models.States;
using System.Collections.Concurrent;
using System.Runtime.ConstrainedExecution;

namespace Khela.Game.Services
{
    /// <summary>
    /// Dynamically adjusts arena (world) size and target food count based on player population.
    /// Runs every few seconds and updates in-memory GameState.
    /// </summary>
    public class ArenaManagerService : BackgroundService
    {
        private readonly ILogger<ArenaManagerService> _logger;
        private readonly WorldConfig _defaultConfig;
        private static readonly Random _rng = new();

        // Runs every 3 seconds to adjust arenas
        private readonly TimeSpan _arenaTickInterval = TimeSpan.FromSeconds(3);
        private readonly ConcurrentDictionary<string, int> _decayCursor = new();

        // Dynamic sizing parameters
        private const float MIN_WORLD_SIZE = 150f;
        private const float SIZE_PER_PLAYER = 15f;
        private const int FOOD_PER_PLAYER = 35;

        public ArenaManagerService(ILogger<ArenaManagerService> logger)
        {
            _logger = logger;
            _defaultConfig = new WorldConfig();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Arena Manager Service started (in-memory mode).");

            while (!stoppingToken.IsCancellationRequested)
            {
                var tickStart = DateTime.UtcNow;

                try
                {
                    var worlds = GameState.Instance.Worlds.Values.ToArray();

                    if (worlds.Length > 0)
                    {
                        var tasks = new List<Task>(worlds.Length);
                        foreach (var world in worlds)
                        {
                            tasks.Add(UpdateWorldDynamics(world, stoppingToken));
                        }
                        await Task.WhenAll(tasks);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in ArenaManagerService loop.");
                }

                // Maintain fixed tick rate
                var elapsed = DateTime.UtcNow - tickStart;
                var delay = _arenaTickInterval - elapsed;
                if (delay > TimeSpan.Zero)
                {
                    try { await Task.Delay(delay, stoppingToken); }
                    catch (TaskCanceledException) { /* ignore on shutdown */ }
                }
            }
        }

        /// <summary>
        /// Safely updates dynamic properties (world size, food target) for a single world.
        /// </summary>
        private Task UpdateWorldDynamics(WorldState world, CancellationToken token)
        {
            if (token.IsCancellationRequested)
                return Task.CompletedTask;

            if (world == null || world.CurrentStatus != GameStatus.Running)
                return Task.CompletedTask;

            int humanCount = world.SnakeIds.Count;
            int aiCount = world.AISnakeIds.Count;
            int totalPlayers = humanCount + aiCount;

            float newWorldSize = CalculateWorldSize(totalPlayers);
            int newTargetFood = CalculateFoodTarget(totalPlayers, newWorldSize);

            bool changed = false;

            if (Math.Abs(world.Config.WorldSize - newWorldSize) > 0.01f)
            {
                world.Config.WorldSize = newWorldSize;
                changed = true;
            }

            if (world.Config.TargetFoodCount != newTargetFood)
            {
                world.Config.TargetFoodCount = newTargetFood;
                changed = true;
            }

            if (changed)
            {
                world.LastUpdated = DateTime.UtcNow;
                GameState.Instance.AddOrUpdateWorld(world);

                _logger.LogDebug(
                    "ArenaTick world={WorldId} players={Total} size={Size:F1} food={Food}",
                    world.WorldId,
                    totalPlayers,
                    world.Config.WorldSize,
                    world.Config.TargetFoodCount);
            }

            //ProcessFoodDecay(world, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

            return Task.CompletedTask;
        }

        /// <summary>
        /// Calculates dynamic world size based on player count.
        /// </summary>
        private float CalculateWorldSize(int totalPlayerCount)
        {
            float dynamicSize = MIN_WORLD_SIZE + (totalPlayerCount * SIZE_PER_PLAYER);
            return Math.Clamp(dynamicSize, MIN_WORLD_SIZE, _defaultConfig.WorldSize);
        }

        /// <summary>
        /// Calculates final target food count.
        /// Auto-adaptive based on player count and world size.
        /// Zero overhead, no bottlenecks.
        /// </summary>
        private int CalculateFoodTarget(int playersCount, float worldSize)
        {
            // 1) Minimum baseline (world should never feel empty)
            const int BASELINE_FOOD = 150;

            // 2) Player-based component (fixed O(1))
            int playerBased = playersCount * FOOD_PER_PLAYER;

            // 3) Density-based component (tight & consistent)
            float worldArea = worldSize * worldSize;
            int densityBased = (int)(worldArea / 60f);
            // Higher divisor = less food density (optimized for mobile)

            // 4) Smooth scaling factor for mid-range population
            // Avoids spikes when many players join suddenly
            float smoothScale = 1f + (playersCount * 0.08f);

            int final = (int)(Math.Max(playerBased, densityBased) * smoothScale);

            // 5) Clamp minimum
            if (final < BASELINE_FOOD) final = BASELINE_FOOD;

            // 6) Hard safety limit (server must never overload)
            // For extremely large worlds or stress events
            const int MAX_FOOD_LIMIT = 3000;
            if (final > MAX_FOOD_LIMIT) final = MAX_FOOD_LIMIT;

            return 100;
        }
        public void ProcessFoodDecay(WorldState world, long serverTime)
        {
            var gs = GameState.Instance;
            var foodIds = world.FoodIds;
            int count = foodIds.Count;

            if (count == 0)
                return;

            // Batch size: ~5% or at least 20 items
            int batchSize = Math.Max(20, count / 20);

            if (!_decayCursor.TryGetValue(world.WorldId, out int cursor))
                cursor = 0;

            int index = 0;
            int processed = 0;

            List<int> toRemove = null;

            foreach (var fid in foodIds.Keys)
            {
                if (index++ < cursor)
                    continue; // skip until cursor

                // Remove missing references
                if (!gs.TryGetFood(fid, out var food))
                {
                    (toRemove ??= new()).Add(fid);
                }
                else
                {
                    // 1) Powerup expiration
                    if (food.DespawnAtTime > 0 && serverTime >= food.DespawnAtTime)
                        (toRemove ??= new()).Add(fid);

                    // 2) Global expiration
                    else if (serverTime - food.SpawnedAtTime >= 90000)
                        (toRemove ??= new()).Add(fid);

                    // 3) Density cleanup
                    else if (count > world.Config.TargetFoodCount * 1.3)
                    {
                        if (_rng.NextDouble() < 0.02)
                            (toRemove ??= new()).Add(fid);
                    }
                }

                if (++processed >= batchSize)
                    break; // done for this round
            }

            // Advance cursor
            cursor += processed;
            if (cursor >= count)
                cursor = 0;

            _decayCursor[world.WorldId] = cursor;

            // Remove foods
            if (toRemove != null)
            {
                foreach (var id in toRemove)
                {
                    foodIds.TryRemove(id, out _);
                    gs.RemoveFood(id);
                }
            }
        }

    }
}
