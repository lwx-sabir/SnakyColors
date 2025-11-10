using Khela.Game.Models;
using Khela.Game.Models.Configs;
using Khela.Game.Services.Redis;

namespace Khela.Game.Services
{
    public class ArenaManagerService : BackgroundService
    {
        private readonly ILogger<ArenaManagerService> _logger;
        private readonly IRedisService _redis;
        private readonly WorldConfig _defaultConfig;

        private const string WORLD_KEY_PREFIX = "world:";

        // Runs every 3 seconds to adjust arenas
        private readonly TimeSpan _arenaTickInterval = TimeSpan.FromSeconds(3);

        // Dynamic sizing parameters
        private const float MIN_WORLD_SIZE = 150f;
        private const float SIZE_PER_PLAYER = 15f;

        public ArenaManagerService(ILogger<ArenaManagerService> logger, IRedisService redis)
        {
            _logger = logger;
            _redis = redis;
            _defaultConfig = new WorldConfig();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Arena Manager Service started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                var tickStart = DateTime.UtcNow;

                try
                {
                    var worldKeys = await _redis.GetKeysByPatternAsync(WORLD_KEY_PREFIX + "*");

                    if (worldKeys != null && worldKeys.Any())
                    { 
                        var tasks = new List<Task>(worldKeys.Count());
                        foreach (var worldKey in worldKeys)
                        { 
                            tasks.Add(UpdateWorldDynamics(worldKey, stoppingToken));
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
                    try
                    {
                        await Task.Delay(delay, stoppingToken);
                    }
                    catch (TaskCanceledException)
                    {
                        // ignore on shutdown
                    }
                }
            }
        }

        /// <summary>
        /// Safely updates dynamic properties (world size, food target) for a single world.
        /// </summary>
        private async Task UpdateWorldDynamics(string worldKey, CancellationToken token)
        {
            if (token.IsCancellationRequested)
                return;

            string lockKey = $"lock:{worldKey}";
            string lockToken = Guid.NewGuid().ToString();
            var db = _redis.GetDatabase();

            // Same semantics as your original: try lock, skip if busy.
            if (!await db.LockTakeAsync(lockKey, lockToken, TimeSpan.FromSeconds(1)))
                return;

            try
            {
                var world = await _redis.GetAsync<WorldState>(worldKey);
                if (world == null || world.CurrentState != GameState.Running)
                    return;

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
                    world.Config.TargetFoodCount = newTargetFood;
                    changed = true;
                }

                if (changed)
                {
                    await _redis.SetAsync(worldKey, world);

                    _logger.LogDebug(
                        "ArenaTick world={WorldId} players={Total} size={Size} food={Food}",
                        world.WorldId,
                        totalPlayers,
                        world.Config.WorldSize,
                        world.Config.TargetFoodCount);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating world dynamics for {WorldKey}", worldKey);
            }
            finally
            {
                try
                {
                    await db.LockReleaseAsync(lockKey, lockToken);
                }
                catch
                {
                    // If lock already expired or stolen, ignore.
                }
            }
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
