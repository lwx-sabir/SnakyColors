using Microsoft.Extensions.Caching.Memory;
using StackExchange.Redis;

namespace Khela.Game.Services.Redis
{
    public interface IRedisService
    {
        IDatabase GetDatabase();
        IMemoryCache GetMemoryCache();
        Task<string> GetStringAsync(string key);
        Task SetStringAsync(string key, string value);

        /// <summary>
        /// Gets an object from Redis by key, deserializing it from JSON.
        /// </summary>
        Task<T> GetAsync<T>(string key);

        /// <summary>
        /// Sets an object in Redis, serializing it to JSON.
        /// </summary>
        Task SetAsync<T>(string key, T value, TimeSpan? expiry = null);

        /// <summary>
        /// Deletes a key from Redis.
        /// </summary>
        Task DeleteAsync(string key);

        /// <summary>
        /// Gets all keys matching a specific pattern (e.g., "snake:*").
        /// </summary>
        Task<IEnumerable<string>> GetKeysByPatternAsync(string pattern);

        /// <summary>
        /// Gets multiple objects from Redis in a single batch operation.
        /// </summary>
        /// <param name="keys">The list of keys to fetch.</param>
        /// <returns>A dictionary mapping the key to the deserialized object. Keys not found are omitted.</returns>
        Task<Dictionary<string, T>> GetBatchAsync<T>(IEnumerable<string> keys);

        Task SetAddAsync(string key, string value);
        Task SetRemoveAsync(string key, string value);
        Task<string[]> SetMembersAsync(string key);
        Task<string[]> SetUnionAsync(string[] keys);
    }
}