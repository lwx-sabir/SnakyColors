using Khela.Game.Models;
using Khela.Game.Models.Configs;
using Khela.Game.Services.Redis;
using Microsoft.Extensions.Options;

namespace Khela.Game.Services
{
    public class ArenaManagerService : BackgroundService
    {
        private readonly ILogger<ArenaManagerService> _logger;
        private readonly IRedisService _redis;
        private readonly WorldConfig _defaultConfig;

        private const string WORLD_KEY_PREFIX = "world:";
        private readonly TimeSpan _arenaTickInterval = TimeSpan.FromSeconds(3); 

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
                var startTime = DateTime.UtcNow;
                try
                { 
                    var worldKeys = await _redis.GetKeysByPatternAsync(WORLD_KEY_PREFIX + "*");
                     
                    var tasks = worldKeys.Select(worldKey => UpdateWorldDynamics(worldKey, stoppingToken));
                    await Task.WhenAll(tasks);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in ArenaManagerService loop.");
                }
                 
                var endTime = DateTime.UtcNow;
                var timeToWait = _arenaTickInterval - (endTime - startTime);
                if (timeToWait > TimeSpan.Zero)
                {
                    await Task.Delay(timeToWait, stoppingToken);
                }
            }
        }

        /// <summary>
        /// Updates the dynamic properties of a single world (Size, Food, AI)
        /// </summary>
        private async Task UpdateWorldDynamics(string worldKey, CancellationToken token)
        {
            if (token.IsCancellationRequested) return;

            string lockKey = $"lock:{worldKey}"; // <-- Use the SAME lock key
            string lockToken = Guid.NewGuid().ToString();
            var db = _redis.GetDatabase();

            if (await db.LockTakeAsync(lockKey, lockToken, TimeSpan.FromSeconds(1)))
            {
                try
                {
                    var world = await _redis.GetAsync<WorldState>(worldKey);
                    if (world == null || world.CurrentState != GameState.Running) return;

                    int humanPlayerCount = world.SnakeIds.Count;
                    int aiPlayerCount = world.AISnakeIds.Count;
                    int totalPlayerCount = humanPlayerCount + aiPlayerCount;

                    float newWorldSize = CalculateWorldSize(totalPlayerCount);
                    int newTargetFood = CalculateFoodCount(newWorldSize);

                    world.Config.WorldSize = newWorldSize;
                    world.Config.TargetFoodCount = newTargetFood;

                    await _redis.SetAsync(worldKey, world); // Save INSIDE the lock
                }
                finally
                {
                    await db.LockReleaseAsync(lockKey, lockToken);
                }
            }
        }

        /// <summary>
        /// Calculates the new world size based on player count.
        /// </summary>
        private float CalculateWorldSize(int totalPlayerCount)
        {
            // World grows by 15 units for every player
            float dynamicSize = MIN_WORLD_SIZE + (totalPlayerCount * SIZE_PER_PLAYER);

            // Clamp the size between the min and the absolute max (from appsettings)
            return Math.Clamp(dynamicSize, MIN_WORLD_SIZE, _defaultConfig.WorldSize);
        } 

        /// <summary>
        /// Calculates how much food should be in the world.
        /// </summary>
        private int CalculateFoodCount(float worldSize)
        { 
            float worldArea = worldSize * worldSize;
            return (int)(worldArea / 50f);
        }
    }
}