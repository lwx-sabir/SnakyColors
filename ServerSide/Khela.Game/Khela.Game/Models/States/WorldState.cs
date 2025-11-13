using Khela.Game.Models.Configs;
using System.Collections.Concurrent;

namespace Khela.Game.Models.States
{
    public class WorldState
    {
        // --- Core ---
        public string WorldId { get; set; } = Guid.NewGuid().ToString("N");
        public GameStatus CurrentStatus { get; set; } = GameStatus.Running;
        public DateTime CreationTime { get; set; } = DateTime.UtcNow;

        // --- Config (Managed by ArenaManagerService) ---
        public WorldConfig Config { get; set; } = new WorldConfig();

        // --- Entities (Holds IDs, not full objects) ---
        public ConcurrentDictionary<string, bool> SnakeIds { get; set; } = new ConcurrentDictionary<string, bool>();
        public ConcurrentDictionary<int, bool> FoodIds { get; set; } = new ConcurrentDictionary<int, bool>();
        public ConcurrentDictionary<string, bool> AISnakeIds { get; set; } = new ConcurrentDictionary<string, bool>();

        // --- REMOVED: Spatial Grid ---
        // public ConcurrentDictionary<ZoneKey, Zone> Zones { get; set; } = ...

        // --- Global State ---
        public List<string> GlobalEvents { get; set; } = new List<string>();
        public List<string> TopSnakeIds { get; set; } = new List<string>();

        // --- Tick & Timing ---
        public int Tick { get; set; } 

        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

        public WorldState() { }
        public WorldState(WorldConfig config) { Config = config; }
    }

    public enum GameStatus { Running, Paused, Finished } 
}