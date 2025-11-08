using Microsoft.Extensions.Caching.Memory;
using StackExchange.Redis;
using System.Text.Json; // Added for JSON serialization

namespace Khela.Game.Services.Redis
{
    public class RedisService : IRedisService
    {
        private readonly IConnectionMultiplexer _redis;
        private readonly IMemoryCache _cache;

        public RedisService(IConnectionMultiplexer redis, IMemoryCache memoryCache)
        {
            _redis = redis;
            _cache = memoryCache;
        }

        public IDatabase GetDatabase()
        {
            return _redis.GetDatabase();
        }

        public IMemoryCache GetMemoryCache()
        {
            return _cache;
        }

        public async Task<string> GetStringAsync(string key)
        {
            var db = _redis.GetDatabase();
            return await db.StringGetAsync(key);
        }

        public async Task SetStringAsync(string key, string value)
        {
            var db = _redis.GetDatabase();
            await db.StringSetAsync(key, value);
        } 

        public async Task<T> GetAsync<T>(string key)
        {
            var db = _redis.GetDatabase();
            RedisValue json = await db.StringGetAsync(key);
            if (json.IsNullOrEmpty)
            {
                return default; // Return default (null) if key doesn't exist
            }
            return JsonSerializer.Deserialize<T>(json);
        }

        public async Task SetAsync<T>(string key, T value, TimeSpan? expiry = null)
        {
            var db = _redis.GetDatabase();
            var json = JsonSerializer.Serialize(value);
            await db.StringSetAsync(key, json, expiry);
        }

        public async Task DeleteAsync(string key)
        {
            var db = _redis.GetDatabase();
            await db.KeyDeleteAsync(key);
        }

        public async Task<IEnumerable<string>> GetKeysByPatternAsync(string pattern)
        {
            var db = _redis.GetDatabase();
            // Assumes a single server endpoint, which is fine for this setup
            var server = _redis.GetServer(_redis.GetEndPoints().First());

            var keys = new List<string>();
            // Asynchronously stream keys matching the pattern
            await foreach (var key in server.KeysAsync(pattern: pattern))
            {
                keys.Add(key.ToString());
            }
            return keys;
        }

        public async Task<Dictionary<string, T>> GetBatchAsync<T>(IEnumerable<string> keys)
        {
            var db = _redis.GetDatabase();
            var results = new Dictionary<string, T>();

            // Convert string keys to RedisKey[]
            var redisKeys = keys.Select(k => (RedisKey)k).ToArray();
            if (redisKeys.Length == 0)
            {
                return results;
            }

            // Get all values in one network call
            RedisValue[] redisValues = await db.StringGetAsync(redisKeys);

            for (int i = 0; i < redisKeys.Length; i++)
            {
                var key = redisKeys[i].ToString();
                var value = redisValues[i];

                if (!value.IsNullOrEmpty)
                {
                    try
                    {
                        results[key] = JsonSerializer.Deserialize<T>(value);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[RedisService] Failed to deserialize key {key} in batch: {ex.Message}");
                    }
                }
            }

            return results;
        }

        public async Task SetAddAsync(string key, string value)
        {
            var db = _redis.GetDatabase();
            await db.SetAddAsync(key, value);
        }

        public async Task SetRemoveAsync(string key, string value)
        {
            var db = _redis.GetDatabase();
            await db.SetRemoveAsync(key, value);
        }

        public async Task<string[]> SetMembersAsync(string key)
        {
            var db = _redis.GetDatabase();
            var members = await db.SetMembersAsync(key);
            return Array.ConvertAll(members, m => (string)m);
        }

        public async Task<string[]> SetUnionAsync(string[] keys)
        {
            var db = _redis.GetDatabase();
            var redisKeys = Array.ConvertAll(keys, k => (RedisKey)k);
            var members = await db.SetCombineAsync(SetOperation.Union, redisKeys);
            return Array.ConvertAll(members, m => (string)m);
        }
    }
}