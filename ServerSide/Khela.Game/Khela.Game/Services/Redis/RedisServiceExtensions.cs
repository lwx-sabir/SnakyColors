using System.Text.Json;
using StackExchange.Redis;

namespace Khela.Game.Services.Redis
{
    public static class RedisServiceExtensions
    {
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = false,
            IncludeFields = true,
            PropertyNameCaseInsensitive = true
        };

        /// <summary>
        /// Writes multiple objects to Redis in a single pipelined batch.
        /// Ideal for syncing entire GameState snapshots efficiently.
        /// </summary>
        public static async Task BatchWriteAsync(this IRedisService redis, Dictionary<string, object> data, CancellationToken token = default)
        {
            if (data == null || data.Count == 0)
                return;

            var db = redis.GetDatabase();
            var batch = db.CreateBatch();

            var tasks = new List<Task>(data.Count);
            foreach (var (key, value) in data)
            {
                if (token.IsCancellationRequested)
                    break;

                var json = JsonSerializer.Serialize(value, _jsonOptions);
                tasks.Add(batch.StringSetAsync(key, json));
            }

            batch.Execute();
            await Task.WhenAll(tasks);
        }
    }
}
