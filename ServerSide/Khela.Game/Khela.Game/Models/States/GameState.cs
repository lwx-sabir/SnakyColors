using System;
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

        private readonly Dictionary<string, HashSet<int>> LastFoodSet = new();

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

        /// <summary>
        /// Gets a list of all PlayerState objects (both human and AI)
        /// currently active in the specified world.
        /// </summary>
        /// <param name="worldId">The ID of the world to query.</param>
        /// <returns>A List of all PlayerState objects, or an empty list if the world is not found.</returns>
        public List<PlayerState> GetAllPlayersByWorld(string worldId)
        {
            if (!Worlds.TryGetValue(worldId, out var world))
            {
                return new List<PlayerState>();
            }

            // Combine the player and AI snake IDs from the world
            var allPlayerIds = world.SnakeIds.Keys.Concat(world.AISnakeIds.Keys);

            var players = new List<PlayerState>();
            foreach (var pid in allPlayerIds)
            {
                // Look up the full PlayerState from the global dictionary
                if (Players.TryGetValue(pid, out var player))
                {
                    players.Add(player);
                }
            }

            return players;
        }

        /// <summary>
        /// Gets a list of human-controlled PlayerState objects (excluding AI)
        /// currently active in the specified world.
        /// </summary>
        /// <param name="worldId">The ID of the world to query.</param>
        /// <returns>A List of human PlayerState objects, or an empty list if the world is not found.</returns>
        public List<PlayerState> GetPlayersByWorld(string worldId)
        {
            if (!Worlds.TryGetValue(worldId, out var world))
            {
                return new List<PlayerState>();
            }

            var players = new List<PlayerState>();
            // Iterate only over the SnakeIds, which contains human players
            foreach (var pid in world.SnakeIds.Keys)
            {
                // Look up the full PlayerState from the global dictionary
                if (Players.TryGetValue(pid, out var player))
                {
                    // Double-check IsAI flag just in case, though the list implies it.
                    if (!player.IsAI)
                    {
                        players.Add(player);
                    }
                }
            }

            return players;
        }

        // =====================================================
        // SNAPSHOT BUILDERS
        // =====================================================

        public WorldUpdateDto BuildWorldSnapshot(string worldId)
        {
            if (!Worlds.TryGetValue(worldId, out var world))
                return null;

            var snakes = new List<SnakeKinematicsDto>(
                world.SnakeIds.Count + world.AISnakeIds.Count);

            foreach (var pid in world.SnakeIds.Keys)
                TryAddSnake(pid, snakes);

            foreach (var pid in world.AISnakeIds.Keys)
                TryAddSnake(pid, snakes);

            var food = new List<FoodStateDto>(world.FoodIds.Count);
            foreach (var fid in world.FoodIds.Keys)
            {
                if (!Foods.TryGetValue(fid, out var f)) continue;

                food.Add(new FoodStateDto
                {
                    Id = f.Id,
                    PosX = f.Position.X,
                    PosY = f.Position.Y,
                    ItemKey = f.ItemKey
                });
            }

            var dto = new WorldUpdateDto
            {
                Snakes = [.. snakes], 
                WorldSize = world.Config.WorldSize,
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

        private void TryAddSnake(string pid, List<SnakeKinematicsDto> list)
        {
            if (!Players.TryGetValue(pid, out var p))
                return;

            if (!p.IsAlive)
                return;

            if (p.BodySegments == null || p.BodySegments.Count == 0)
                return;

            list.Add(new SnakeKinematicsDto
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
            });
        }

        public FoodDeltaDto BuildFoodDelta(string worldId)
        {
            var world = Worlds[worldId];

            var currentIds = world.FoodIds.Keys.ToHashSet();

            if (!LastFoodSet.TryGetValue(worldId, out var lastSet))
            {
                // First time: treat all as added
                LastFoodSet[worldId] = currentIds;
                return new FoodDeltaDto
                {
                    Added = currentIds.Select(id =>
                    {
                        var f = Foods[id];
                        return new FoodStateDto
                        {
                            Id = f.Id,
                            PosX = f.Position.X,
                            PosY = f.Position.Y,
                            ItemKey = f.ItemKey
                        };
                    }).ToList()
                };
            }

            var added = new List<FoodStateDto>();
            var removed = new List<int>();

            // detect added
            foreach (var id in currentIds)
            {
                if (!lastSet.Contains(id))
                {
                    var f = Foods[id];
                    added.Add(new FoodStateDto
                    {
                        Id = f.Id,
                        PosX = f.Position.X,
                        PosY = f.Position.Y,
                        ItemKey = f.ItemKey
                    });
                }
            }

            // detect removals
            foreach (var id in lastSet)
                if (!currentIds.Contains(id))
                    removed.Add(id);

            // update cache
            LastFoodSet[worldId] = currentIds;

            return new FoodDeltaDto
            {
                Added = added,
                Removed = removed
            };
        }

        public FoodStateDto[] BuildFullFoodSnapshot(string worldId)
        {
            if (!Worlds.TryGetValue(worldId, out var world) || world.FoodIds == null || world.FoodIds.IsEmpty)
                return Array.Empty<FoodStateDto>();

            var list = new List<FoodStateDto>(world.FoodIds.Count);
            foreach (var fid in world.FoodIds.Keys)
            {
                if (!Foods.TryGetValue(fid, out var food))
                    continue;

                list.Add(new FoodStateDto
                {
                    Id = food.Id,
                    PosX = food.Position.X,
                    PosY = food.Position.Y,
                    ItemKey = food.ItemKey
                });
            }

            return list.ToArray();
        }

    }
}
