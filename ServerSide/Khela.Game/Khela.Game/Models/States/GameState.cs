using System.Collections.Concurrent;
using Khela.Game.Dtos;

namespace Khela.Game.Models.States
{
    public sealed class GameState
    {
        private static readonly Lazy<GameState> _instance = new(() => new GameState());
        public static GameState Instance => _instance.Value;
        private GameState() { }

        // --- Core runtime data ---
        public ConcurrentDictionary<string, WorldState> Worlds { get; } = new();
        public ConcurrentDictionary<string, PlayerState> Players { get; } = new();
        public ConcurrentDictionary<int, FoodState> Foods { get; } = new(); 
        public ConcurrentDictionary<string, string> Connections { get; } = new();


        private readonly ConcurrentDictionary<string, SemaphoreSlim> _worldLocks = new();
        public ConcurrentDictionary<string, WorldUpdateDto> CachedWorldSnapshots { get; } = new();

        // =====================================================
        // BASIC ACCESS
        // =====================================================

        public SemaphoreSlim GetWorldLock(string worldId)
       => _worldLocks.GetOrAdd(worldId, _ => new SemaphoreSlim(1, 1));
        public bool TryGetWorld(string id, out WorldState world) => Worlds.TryGetValue(id, out world);
        public bool TryGetPlayer(string id, out PlayerState p) => Players.TryGetValue(id, out p);
        public bool TryGetFood(int id, out FoodState f) => Foods.TryGetValue(id, out f);

        public void AddOrUpdateWorld(WorldState world) => Worlds[world.WorldId] = world;
        public void AddOrUpdatePlayer(PlayerState player) => Players[player.PlayerId] = player;
        public void AddOrUpdateFood(FoodState food) => Foods[food.Id] = food;

        public void RemoveWorld(string id) => Worlds.TryRemove(id, out _);
        public void RemovePlayer(string id) => Players.TryRemove(id, out _);
        public void RemoveFood(int id) => Foods.TryRemove(id, out _);

        // =====================================================
        // SNAPSHOT BUILDERS
        // =====================================================

        public WorldUpdateDto BuildWorldSnapshot(string worldId, bool isPlayingWorld = false)
        {
            if (!Worlds.TryGetValue(worldId, out var world))
                return null;

            var allSnakeIds = world.SnakeIds.Keys
                                .Concat(world.AISnakeIds.Keys)
                                .Distinct();

            var snakes = allSnakeIds
                .Where(pid => Players.TryGetValue(pid, out _))
                .Select(pid =>
                {
                    var p = Players[pid];
                     
                    if (isPlayingWorld && (!p.IsAlive || p.BodySegments == null || p.BodySegments.Count == 0))
                        return null;

                    return new SnakeKinematicsDto
                    {
                        PlayerId = p.PlayerId,
                        SkinID = p.SkinID,
                        IsAI = p.IsAI,
                        Mass = p.Mass,
                        HeadPosition = p.HeadPosition,
                        BaseSpeed = p.BaseSpeed,
                        CurrentSpeed = p.CurrentSpeed,
                        MaxTurningAngle = p.MaxTurningAngle,
                        TargetLength = p.TargetLength
                    };
                })
                .Where(s => s != null)
                .ToList();

            var food = world.FoodIds?
                .Where(fid => Foods.TryGetValue(fid.Key, out _))
                .Select(fid =>
                {
                    var f = Foods[fid.Key];
                    return new FoodStateDto
                    {
                        Id = f.Id,
                        PosX = f.Position.X,
                        PosY = f.Position.Y,
                        ItemKey = f.ItemKey
                    };
                }).ToList() ?? new();

            var dto = new WorldUpdateDto
            {
                Snakes = [.. snakes],
                Food = [.. food],
                WorldSize = world.Config?.WorldSize ?? 100,
                Tick = world.Tick,
                TickRate = world.Config?.TickRate ?? 20,
                ServerUtc = DateTime.UtcNow
            };

            CachedWorldSnapshots[worldId] = dto;
            return dto;
        }


        // =====================================================
        // PERSISTENCE I/O (for Redis or local file snapshot)
        // =====================================================

        public void ImportFromRedisSnapshot(Dictionary<string, object> snapshot)
        {
            Worlds.Clear();
            Players.Clear();
            Foods.Clear();

            foreach (var (key, val) in snapshot)
            {
                if (key.StartsWith("world:") && val is WorldState w) Worlds[w.WorldId] = w;
                else if (key.StartsWith("snake:") && val is PlayerState p) Players[p.PlayerId] = p;
                else if (key.StartsWith("food:") && val is FoodState f) Foods[f.Id] = f;
            }
        }

        // =====================================================
        // RESET METHODS
        // =====================================================

        /// <summary>
        /// Completely reset the entire simulation (worlds, snakes, food).
        /// </summary>
        public void ResetAll()
        {
            Worlds.Clear();
            Players.Clear();
            Foods.Clear();
            CachedWorldSnapshots.Clear();
        }

        /// <summary>
        /// Reset a single world and all its related snakes/foods.
        /// </summary>
        public void ResetWorld(string worldId)
        {
            if (!Worlds.TryRemove(worldId, out var world))
                return;

            if (world.SnakeIds != null)
            {
                foreach (var pid in world.SnakeIds)
                    Players.TryRemove(pid.Key, out _);
            }

            if (world.AISnakeIds != null)
            {
                foreach (var pid in world.AISnakeIds)
                    Players.TryRemove(pid.Key, out _);
            }

            if (world.FoodIds != null)
            {
                foreach (var fid in world.FoodIds)
                    Foods.TryRemove(fid.Key, out _);
            }

            CachedWorldSnapshots.TryRemove(worldId, out _);
        }

        /// <summary>
        /// Remove all foods from a specific world.
        /// </summary>
        public void ResetFoods(string worldId)
        {
            if (!Worlds.TryGetValue(worldId, out var world))
                return;

            if (world.FoodIds == null)
                return;

            foreach (var fid in world.FoodIds)
                Foods.TryRemove(fid.Key, out _);

            world.FoodIds.Clear();
        }

        /// <summary>
        /// Remove all AI snakes from a world (keep players intact).
        /// </summary>
        public void ResetAISnakes(string worldId)
        {
            if (!Worlds.TryGetValue(worldId, out var world))
                return;

            var toRemove = world.AISnakeIds
                .Where(pid => pid.Key.StartsWith("ai-pid-"))
                .ToList();

            foreach (var pid in toRemove)
            {
                Players.TryRemove(pid.Key, out _);
                world.AISnakeIds.TryRemove(pid);
            }
        }

        /// <summary>
        /// Reset all player-controlled snakes only (keep AI).
        /// </summary>
        public void ResetPlayerSnakes(string worldId)
        {
            if (!Worlds.TryGetValue(worldId, out var world))
                return;

            var toRemove = world.SnakeIds
                .Where(pid => !pid.Key.StartsWith("ai-pid-"))
                .ToList();

            foreach (var pid in toRemove)
            {
                Players.TryRemove(pid.Key, out _);
                world.SnakeIds.TryRemove(pid);
            }
        }

        /// <summary>
        /// Removes all dead or invalid snakes from memory and world references.
        /// Automatically cleans up Players, Worlds, and cached snapshots.
        /// </summary>
        public int ClearDeadSnakes(string worldId = null)
        {
            int removed = 0;

            // --- If specific world requested ---
            if (!string.IsNullOrEmpty(worldId))
            {
                if (!Worlds.TryGetValue(worldId, out var world))
                    return 0;

                var allIds = world.SnakeIds.Keys.Concat(world.AISnakeIds.Keys).ToList();

                foreach (var pid in allIds)
                {
                    if (!Players.TryGetValue(pid, out var player))
                        continue;

                    if (!player.IsAlive)
                    {
                        Players.TryRemove(pid, out _);

                        if (player.IsAI)
                            world.AISnakeIds.TryRemove(pid, out _);
                        else
                            world.SnakeIds.TryRemove(pid, out _);

                        removed++;
                    }
                }

                // Also remove from cached snapshot
                CachedWorldSnapshots.TryRemove(worldId, out _);

                return removed;
            }

            // --- Otherwise: clear across ALL worlds ---
            foreach (var world in Worlds.Values)
            {
                var allIds = world.SnakeIds.Keys.Concat(world.AISnakeIds.Keys).ToList();

                foreach (var pid in allIds)
                {
                    if (!Players.TryGetValue(pid, out var player))
                        continue;

                    if (!player.IsAlive)
                    {
                        Players.TryRemove(pid, out _);

                        if (player.IsAI)
                            world.AISnakeIds.TryRemove(pid, out _);
                        else
                            world.SnakeIds.TryRemove(pid, out _);

                        removed++;
                    }
                }

                CachedWorldSnapshots.TryRemove(world.WorldId, out _);
            }

            return removed;
        }

    }
}
