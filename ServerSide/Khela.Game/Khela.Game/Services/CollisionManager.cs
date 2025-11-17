using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Khela.Game.Models.States;

namespace Khela.Game.Services
{
    public sealed class CollisionManager
    {
        private const float BASE_THICKNESS = 0.5f;
        private const float THICKNESS_PER_MASS = 0.02f;
        private const float MIN_THICKNESS = 0.3f;
        private const float MAX_THICKNESS = 2.0f;
        private const float MIN_VISUAL_RADIUS = MIN_THICKNESS * 0.5f;
        private const float MAX_VISUAL_RADIUS = MAX_THICKNESS * 0.5f;
        private const float COLLISION_LATENCY_PADDING = 0.35f;
        private const float HEADHEAD_LATENCY_PADDING = 0.25f;
        private const int SEGMENT_SAMPLE_STRIDE = 4;
        private const float GRID_CELL_SCALE = 1.25f;
        private const float BASE_FOOD_EAT_RADIUS = 0.35f;
        private const float FOOD_EAT_RADIUS_PER_MASS = 0.04f;
        private const float MIN_FOOD_EAT_RADIUS = 0.35f;
        private const float MAX_FOOD_EAT_RADIUS = 1.20f;
        private const float FOOD_RADIUS = 0.5f;

        private readonly struct CellKey
        {
            public readonly int X;
            public readonly int Y;

            public CellKey(int x, int y)
            {
                X = x;
                Y = y;
            }

            public override int GetHashCode() => HashCode.Combine(X, Y);
            public override bool Equals(object? obj)
                => obj is CellKey other && other.X == X && other.Y == Y;
        }

        private readonly struct Candidate
        {
            public string OwnerId { get; init; }
            public Vector2 Pos { get; init; }
            public byte Flags { get; init; }

            public bool IsHead() => (Flags & 0x1) != 0;
        }

        private sealed class Grid
        {
            public readonly float CellSize;
            private readonly Dictionary<CellKey, List<Candidate>> _cells = new(1024);

            public Grid(float cellSize)
            {
                CellSize = cellSize;
            }

            private CellKey KeyFor(in Vector2 p)
            {
                int cx = (int)MathF.Floor(p.X / CellSize);
                int cy = (int)MathF.Floor(p.Y / CellSize);
                return new CellKey(cx, cy);
            }

            public void Add(Candidate c)
            {
                var key = KeyFor(c.Pos);
                if (!_cells.TryGetValue(key, out var list))
                {
                    list = new List<Candidate>(8);
                    _cells[key] = list;
                }
                list.Add(c);
            }

            public IEnumerable<Candidate> QueryNeighborhood(Vector2 p)
            {
                var center = KeyFor(p);
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        var key = new CellKey(center.X + dx, center.Y + dy);
                        if (_cells.TryGetValue(key, out var list))
                        {
                            for (int i = 0; i < list.Count; i++)
                                yield return list[i];
                        }
                    }
                }
            }
        }

        private sealed class FoodGrid
        {
            public readonly float CellSize;
            private readonly Dictionary<CellKey, List<(int id, Vector2 pos)>> _cells = new(1024);

            public FoodGrid(float cellSize) { CellSize = cellSize; }

            private CellKey KeyFor(in Vector2 p)
            {
                int cx = (int)MathF.Floor(p.X / CellSize);
                int cy = (int)MathF.Floor(p.Y / CellSize);
                return new CellKey(cx, cy);
            }

            public void Add(int id, in Vector2 p)
            {
                var key = KeyFor(p);
                if (!_cells.TryGetValue(key, out var list))
                {
                    list = new List<(int id, Vector2 pos)>(8);
                    _cells[key] = list;
                }
                list.Add((id, p));
            }

            public IEnumerable<(int id, Vector2 pos)> QueryNeighborhood(Vector2 p)
            {
                var center = KeyFor(p);
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        var key = new CellKey(center.X + dx, center.Y + dy);
                        if (_cells.TryGetValue(key, out var list))
                        {
                            for (int i = 0; i < list.Count; i++)
                                yield return list[i];
                        }
                    }
                }
            }
        }

        public Dictionary<string, string?> Resolve(WorldState world, List<PlayerState> allSnakes)
        {
            var deaths = new Dictionary<string, string?>(capacity: allSnakes?.Count ?? 0);
            if (world == null || allSnakes == null || allSnakes.Count == 0)
                return deaths;

            float worldHalf = world.Config.WorldSize * 0.5f;
            float cellSize = MathF.Max(0.5f, MAX_VISUAL_RADIUS * GRID_CELL_SCALE);
            var grid = new Grid(cellSize);

            foreach (var s in allSnakes)
            {
                if (s == null || !s.IsAlive) continue;
                var segsArr = s.BodySegments?.ToArray();
                if (segsArr == null || segsArr.Length == 0) continue;

                int stride = Math.Max(1, s.IsAI ? SEGMENT_SAMPLE_STRIDE : 1);
                for (int i = 0; i < segsArr.Length - 1; i += stride)
                {
                    var v = (Vector2)segsArr[i];
                    grid.Add(new Candidate { OwnerId = s.PlayerId, Pos = v, Flags = 0 });
                }

                var head = s.HeadPosition;
                var hp = new Vector2(head.X, head.Y);
                grid.Add(new Candidate { OwnerId = s.PlayerId, Pos = hp, Flags = 0x1 });
            }

            foreach (var s in allSnakes)
            {
                if (s == null || !s.IsAlive) continue;
                if (deaths.ContainsKey(s.PlayerId)) continue;

                var head = s.HeadPosition;
                var hp = new Vector2(head.X, head.Y);

                if (MathF.Abs(hp.X) >= worldHalf || MathF.Abs(hp.Y) >= worldHalf)
                {
                    deaths[s.PlayerId] = null;
                    continue;
                }

                Candidate? bestBody = null;
                Candidate? bestHead = null;
                float bestBodyD2 = float.MaxValue;
                float bestHeadD2 = float.MaxValue;

                foreach (var c in grid.QueryNeighborhood(hp))
                {
                    if (c.OwnerId == s.PlayerId) continue;

                    float dx = c.Pos.X - hp.X;
                    float dy = c.Pos.Y - hp.Y;
                    float d2 = dx * dx + dy * dy;

                    if (c.IsHead())
                    {
                        if (d2 < bestHeadD2)
                        {
                            bestHeadD2 = d2;
                            bestHead = c;
                        }
                    }
                    else
                    {
                        if (d2 < bestBodyD2)
                        {
                            bestBodyD2 = d2;
                            bestBody = c;
                        }
                    }
                }

                if (bestBody.HasValue && TryGetPlayer(allSnakes, bestBody.Value.OwnerId, out var victim))
                {
            float threshold = GetCollisionRadius(s) + GetCollisionRadius(victim);
                    if (bestBodyD2 <= threshold * threshold)
                    {
                        LogCollision("HEAD_BODY", s, victim, MathF.Sqrt(bestBodyD2), threshold);
                        deaths[s.PlayerId] = victim.PlayerId;
                        continue;
                    }
                }

                if (bestHead.HasValue && TryGetPlayer(allSnakes, bestHead.Value.OwnerId, out var otherSnake))
                {
            float threshold = GetHeadHeadRadius(s) + GetHeadHeadRadius(otherSnake);
                    if (bestHeadD2 <= threshold * threshold)
                    {
                        if (!deaths.ContainsKey(otherSnake.PlayerId))
                        {
                            if (otherSnake.Mass > s.Mass)
                            {
                                LogCollision("HEAD_HEAD", s, otherSnake, MathF.Sqrt(bestHeadD2), threshold);
                                deaths[s.PlayerId] = otherSnake.PlayerId;
                            }
                            else if (otherSnake.Mass < s.Mass)
                            {
                                LogCollision("HEAD_HEAD", otherSnake, s, MathF.Sqrt(bestHeadD2), threshold);
                                deaths[otherSnake.PlayerId] = s.PlayerId;
                            }
                            else
                            {
                                LogCollision("HEAD_HEAD_TIE", s, otherSnake, MathF.Sqrt(bestHeadD2), threshold);
                                deaths[s.PlayerId] = otherSnake.PlayerId;
                                deaths[otherSnake.PlayerId] = s.PlayerId;
                            }
                        }
                    }
                }
            }

            return deaths;
        }

        private static bool TryGetPlayer(List<PlayerState> players, string id, out PlayerState player)
        {
            player = players.FirstOrDefault(p => p.PlayerId == id);
            return player != null;
        }

        public float FoodItemRadius => FOOD_RADIUS;

        public float GetFoodEatRadius(PlayerState snake)
        {
            if (snake == null) return MIN_FOOD_EAT_RADIUS;
            float radius = BASE_FOOD_EAT_RADIUS + (snake.Mass * FOOD_EAT_RADIUS_PER_MASS);
            return Math.Clamp(radius, MIN_FOOD_EAT_RADIUS, MAX_FOOD_EAT_RADIUS);
        }

        public List<(string playerId, int foodId)> ResolveFoodEats(
            WorldState world,
            List<PlayerState> allSnakes,
            GameState gameState)
        {
            var results = new List<(string, int)>(allSnakes?.Count ?? 0);
            if (world == null || allSnakes == null || allSnakes.Count == 0 || gameState == null)
                return results;

            if (!gameState.TryGetWorld(world.WorldId, out var w))
                return results;
            if (w.FoodIds == null || w.FoodIds.Count == 0)
                return results;

            float cell = MathF.Max(0.5f, MAX_FOOD_EAT_RADIUS * GRID_CELL_SCALE);
            var grid = new FoodGrid(cell);

            foreach (var fid in w.FoodIds.Keys)
            {
                if (!gameState.TryGetFood(fid, out var food))
                    continue;
                var p = new Vector2(food.Position.X, food.Position.Y);
                grid.Add(food.Id, p);
            }

            var consumed = new HashSet<int>();

            foreach (var s in allSnakes)
            {
                if (s == null || !s.IsAlive) continue;

                var head = s.HeadPosition;
                var hp = new Vector2(head.X, head.Y);

                int bestId = 0;
                float bestD2 = float.MaxValue;
                float eatRadius = GetFoodEatRadius(s) + FOOD_RADIUS;
                float eatR2 = eatRadius * eatRadius;

                foreach (var f in grid.QueryNeighborhood(hp))
                {
                    if (consumed.Contains(f.id)) continue;
                    float dx = f.pos.X - hp.X;
                    float dy = f.pos.Y - hp.Y;
                    float d2 = dx * dx + dy * dy;
                    if (d2 <= eatR2 && d2 < bestD2)
                    {
                        bestD2 = d2;
                        bestId = f.id;
                    }
                }

                if (bestId != 0)
                {
                    consumed.Add(bestId);
                    results.Add((s.PlayerId, bestId));
                }
            }

            return results;
        }

        private static float GetCollisionRadius(PlayerState snake)
        {
            return GetVisualRadius(snake) + COLLISION_LATENCY_PADDING;
        }

        private static float GetHeadHeadRadius(PlayerState snake)
        {
            return GetVisualRadius(snake) + HEADHEAD_LATENCY_PADDING;
        }

        private static float GetVisualRadius(PlayerState snake)
        {
            if (snake == null) return MIN_VISUAL_RADIUS;
            float thickness = BASE_THICKNESS + (snake.Mass * THICKNESS_PER_MASS);
            thickness = Math.Clamp(thickness, MIN_THICKNESS, MAX_THICKNESS);
            return thickness * 0.5f;
        }

        private static void LogCollision(string type, PlayerState victim, PlayerState? killer, float distance, float threshold)
        {
            try
            {
                if((!victim.IsAI && killer.IsAI) || (victim.IsAI && !killer.IsAI))
                {
                    string killerId = killer?.PlayerId ?? "none";
                    Console.WriteLine($"[CollisionDebug] type={type} victim={victim?.PlayerId ?? "null"} killer={killerId} dist={distance:F3} thresh={threshold:F3} victimHead=({victim.HeadPosition.X:F2},{victim.HeadPosition.Y:F2}) killerHead=({killer?.HeadPosition.X:F2},{killer?.HeadPosition.Y:F2}) time={DateTime.UtcNow:O}");
                } 
            }
            catch
            {
                // ignore logging issues
            }
        }
    }
}
