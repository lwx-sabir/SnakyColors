using Khela.Game.Models.Configs;
using Khela.Game.Models.States;

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

        // Runs every 3 seconds to adjust arenas
        private readonly TimeSpan _arenaTickInterval = TimeSpan.FromSeconds(3);

        // Dynamic sizing parameters
        private const float MIN_WORLD_SIZE = 200f;
        private const float SIZE_PER_PLAYER = 15f;

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
            int newTargetFood = CalculateFoodCount(newWorldSize);

            bool changed = false;

            if (Math.Abs(world.Config.WorldSize - newWorldSize) > 0.01f)
            {
                world.Config.WorldSize = newWorldSize;
                changed = true;
            }

            if (world.Config.TargetFoodCount != newTargetFood)
            {
                world.Config.TargetFoodCount = 100;//newTargetFood;
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
        /// Calculates food count target from world size.
        /// </summary>
        private int CalculateFoodCount(float worldSize)
        {
            float worldArea = worldSize * worldSize;
            return (int)(worldArea / 50f);
        }
    }
}
