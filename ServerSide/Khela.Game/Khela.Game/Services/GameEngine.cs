using System.Diagnostics;
using System.Numerics;
using Khela.Game.Dtos;
using Khela.Game.Models;
using Khela.Game.Models.Configs;
using Khela.Game.Models.States;
using Khela.Game.Services.Simulators;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

// ==============================================
// GameEngine.cs
// Author: Reza Sabir (CasualLabInteractive)
// Version: 1.0.1 (Production Stable)
// Description:
// Thread-safe authoritative server core controlling world ticks,
// snake movements, collisions, food interactions, and event dispatch.
// ==============================================


namespace Khela.Game.Services
{
    /// <summary>
    /// In-memory authoritative game loop.
    /// - Uses GameState.Instance as the single source of truth.
    /// - No Redis calls in runtime; persistence handled elsewhere.
    /// - Validates movement, food, deaths, and emits world snapshots.
    /// </summary>
    public class GameEngine : BackgroundService
    {
        private readonly ILogger<GameEngine> _logger;
        private readonly FoodService _foodService;

        // Async-friendly events
        public event Func<string, int, string, Task>? OnFoodEaten;
        public event Func<PlayerState, PlayerState?, Task>? PlayerDied;
        public event Func<string, DateTime, Task>? OnWorldTickCompleted;

        private readonly int _tickRate;
        private readonly TimeSpan _tickInterval;
        private readonly WorldConfig _defaultConfig = new();

        // === Collision tuning (authoritative) ===
        private const float COLLISION_RADIUS = 0.75f;          // head vs body radius
        private const float HEAD_HEAD_RADIUS = 0.60f;          // head vs head radius
        private const int SEGMENT_SAMPLE_STRIDE = 4;           // sample every Nth segment for grid
        private const float GRID_CELL_SCALE = 1.25f;           // cell size multiplier over radius
        private const float FOOD_EAT_RADIUS = 0.70f;           // matches OnPlayerAteFood check

        public GameEngine(
            FoodService foodService,
            ILogger<GameEngine> logger)
        {
            _foodService = foodService;
            _logger = logger;

            _tickRate = _defaultConfig.TickRate > 0 ? _defaultConfig.TickRate : 20;
            _tickInterval = TimeSpan.FromMilliseconds(1000.0 / _tickRate);
        }

        // ---------------------------------------------------------------------
        // PLAYER STATE UPDATE (from client)
        // ---------------------------------------------------------------------
         

        public Task OnPlayerStateUpdate(string playerId, SerializableVector2 inputDir, bool isBoosting)
        {
            if (string.IsNullOrEmpty(playerId))
                return Task.CompletedTask;

            var gs = GameState.Instance;

            if (!gs.TryGetPlayer(playerId, out var snake) || !snake.IsAlive)
                return Task.CompletedTask;

            Vector2 dir = inputDir;

            if (dir.LengthSquared() > 0.0001f)
                dir = Vector2.Normalize(dir);
            else
                dir = Vector2.Zero;

            // Just store input & boost. Movement happens in the tick loop.
            snake.PendingInputDir = dir;
            snake.IsBoosting = isBoosting;

            gs.AddOrUpdatePlayer(snake);
            return Task.CompletedTask;
        }


        // ---------------------------------------------------------------------
        // FOOD EAT HANDLING (authoritative)
        // ---------------------------------------------------------------------

        public Task OnPlayerAteFood(string playerId, int foodId)
        {
            if (string.IsNullOrEmpty(playerId))
                return Task.CompletedTask;

            var gs = GameState.Instance;

            if (!gs.TryGetPlayer(playerId, out var snake) || !snake.IsAlive)
                return Task.CompletedTask;

            if (snake.CurrentWorldId == null ||
                !gs.TryGetWorld(snake.CurrentWorldId, out var world) ||
                world.CurrentStatus != GameStatus.Running)
                return Task.CompletedTask;

            if (!gs.TryGetFood(foodId, out var food))
                return Task.CompletedTask;

            if (!snake.IsAI)
            {
                var debug = 0;
            }

            // Lag-tolerant distance check
            var head = snake.HeadPosition;
            float dx = food.Position.X - head.X;
            float dy = food.Position.Y - head.Y;
            float eatRadius = snake.IsAI ? 0.7f : 0.7f;

            if (dx * dx + dy * dy > eatRadius * eatRadius)
                return Task.CompletedTask;

            // Apply gain
            snake.Score += 10;
            gs.AddOrUpdatePlayer(snake);

            // Remove food from world (in-memory)
            _foodService.RemoveFoodAsync(world, foodId); // fire-and-forget is fine here

            // Notify listeners
            _ = RaiseFoodEaten(playerId, foodId, world.WorldId);

            if (!snake.IsAI)
            {
                _logger.LogInformation("OnPlayerAteFood player={PlayerId} food={FoodId} score={Score}",
               playerId, foodId, snake.Score);
            } 

            return Task.CompletedTask;
        }

        // ---------------------------------------------------------------------
        // PLAYER DEATH (authoritative)
        // ---------------------------------------------------------------------

        public Task OnPlayerDied(string deadPlayerId, string? killerPlayerId)
        {
            if (string.IsNullOrEmpty(deadPlayerId))
                return Task.CompletedTask;

            var gs = GameState.Instance;

            if (!gs.TryGetPlayer(deadPlayerId, out var deadSnake))
                return Task.CompletedTask;

            if (!deadSnake.IsAlive)
                return Task.CompletedTask;

            deadSnake.IsAlive = false;
            gs.AddOrUpdatePlayer(deadSnake);

            PlayerState? killerSnake = null;
            if (!string.IsNullOrEmpty(killerPlayerId) &&
                gs.TryGetPlayer(killerPlayerId, out var killer))
            {
                killerSnake = killer;
            }

            _ = RaisePlayerDied(deadSnake, killerSnake);

            if(!deadSnake.IsAI)
            {
                _logger.LogInformation("PlayerDied victim={Victim} killer={Killer}",
               deadPlayerId, killerPlayerId ?? "none");
            }  
            return Task.CompletedTask;
        }

        // ---------------------------------------------------------------------
        // MAIN GAME LOOP (per-world tick)
        // ---------------------------------------------------------------------

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try { await Task.Delay(Random.Shared.Next(10, 50), stoppingToken); } catch { }

            _logger.LogInformation("GameEngine started at {TickRate} ticks/sec", _tickRate);

            var stopwatch = Stopwatch.StartNew();
            var nextTick = stopwatch.Elapsed;
            long tickCount = 0;

            while (!stoppingToken.IsCancellationRequested)
            {
                var now = stopwatch.Elapsed;

                if (now >= nextTick)
                {
                    try
                    {
                        var tickStart = stopwatch.Elapsed;

                        var worlds = GameState.Instance.Worlds.Values.ToArray();
                        foreach (var world in worlds)
                        {
                            await ProcessWorldTick(world, stoppingToken);
                        }

                        var tickElapsed = stopwatch.Elapsed - tickStart;
                        //_logger.LogInformation("Tick {Tick} took {Ms:F2} ms",tickCount, tickElapsed.TotalMilliseconds);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error in main tick loop");
                    }

                    tickCount++;
                    nextTick += _tickInterval;

                    // Skip ahead if we're behind
                    while (stopwatch.Elapsed - nextTick > _tickInterval)
                        nextTick += _tickInterval;
                }

                if (tickCount % 100 == 0 && tickCount > 0)
                {
                    var elapsed = stopwatch.Elapsed.TotalSeconds;
                    if (elapsed > 0)
                    {
                        var effRate = tickCount / elapsed;
                        _logger.LogInformation("Ticks={Tick} Elapsed={Sec:F1}s Rate={Rate:F2}Hz",
                            tickCount, elapsed, effRate);
                    }
                }

                var sleep = nextTick - stopwatch.Elapsed;
                if (sleep > TimeSpan.Zero)
                    await Task.Delay(sleep, stoppingToken);
                else
                    await Task.Yield();
            }

            _logger.LogInformation("GameEngine stopped.");
        }

        private async Task ProcessWorldTick(WorldState world, CancellationToken token)
        {
            if (token.IsCancellationRequested)
                return;

            if (world.CurrentStatus != GameStatus.Running)
                return;

            // Ensure config
            if (world.Config == null || world.Config.WorldSize <= 0)
                world.Config = _defaultConfig;
             
            float dt = 1f / world.Config.TickRate;
            var gs = GameState.Instance;

            // Step human player snakes (AI handled by AIService)
            var humanSnakes = gs.GetPlayersByWorld(world.WorldId);
            foreach (var snake in humanSnakes)
            {
                if (!snake.IsAI)
                {
                    SnakeSimulation.StepPlayerSnake(snake, dt);
                }
            }

            // Authoritative collisions (players vs world and vs all snakes)
            try
            {
                var allSnakes = gs.GetAllPlayersByWorld(world.WorldId);
                ResolveCollisions(world, allSnakes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Collision resolution failed for world {WorldId}", world.WorldId);
            }

            // Authoritative food collection for all players (no client collision dependency)
            try
            {
                var allSnakes = gs.GetAllPlayersByWorld(world.WorldId);
                ResolveFoodEats(world, allSnakes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Food resolution failed for world {WorldId}", world.WorldId);
            }

            // Food management (in-memory)
            await _foodService.ManageFoodSpawningAsync(world);

            // Advance tick
            world.Tick++;
            world.LastUpdated = DateTime.UtcNow;

            // Build/refresh snapshot for clients
            GameState.Instance.BuildWorldSnapshot(world.WorldId);

            // Notify listeners (broadcaster can push to clients)
            await RaiseWorldTickCompleted(world.WorldId, world.LastUpdated);
        }

        // ---------------------------------------------------------------------
        // EVENT HELPERS
        // ---------------------------------------------------------------------

        private Task RaiseFoodEaten(string playerId, int foodId, string? worldId)
        {
            var handler = OnFoodEaten;
            if (handler == null) return Task.CompletedTask;

            var delegates = handler.GetInvocationList()
                .Cast<Func<string, int, string, Task>>();

            return Task.WhenAll(delegates.Select(d =>
            {
                try { return d(playerId, foodId, worldId ?? string.Empty); }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "OnFoodEaten handler failed");
                    return Task.CompletedTask;
                }
            }));
        }

        private Task RaisePlayerDied(PlayerState dead, PlayerState? killer)
        {
            var handler = PlayerDied;
            if (handler == null) return Task.CompletedTask;

            var delegates = handler.GetInvocationList()
                .Cast<Func<PlayerState, PlayerState?, Task>>();

            return Task.WhenAll(delegates.Select(d =>
            {
                try { return d(dead, killer); }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "PlayerDied handler failed");
                    return Task.CompletedTask;
                }
            }));
        }

        private Task RaiseWorldTickCompleted(string worldId, DateTime utcNow)
        {
            var handler = OnWorldTickCompleted;
            if (handler == null) return Task.CompletedTask;

            foreach (var d in handler.GetInvocationList()
                                     .Cast<Func<string, DateTime, Task>>())
            {
                // Fire-and-forget; don't block the tick loop
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await d(worldId, utcNow);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "OnWorldTickCompleted handler failed");
                    }
                });
            }

            return Task.CompletedTask;
        }

        // ===============================
        // === Collision Grid Helpers ===
        // ===============================
        private readonly struct CellKey : IEquatable<CellKey>
        {
            public readonly int X;
            public readonly int Y;
            public CellKey(int x, int y) { X = x; Y = y; }
            public bool Equals(CellKey other) => X == other.X && Y == other.Y;
            public override bool Equals(object? obj) => obj is CellKey ck && Equals(ck);
            public override int GetHashCode() => HashCode.Combine(X, Y);
        }

        private struct Candidate
        {
            public string OwnerId;
            public Vector2 Pos;
            public byte Flags; // bit0: isHead
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool IsHead() => (Flags & 0x1) != 0;
        }

        private sealed class Grid
        {
            public readonly float CellSize;
            private readonly Dictionary<CellKey, List<Candidate>> _cells = new(1024);

            public Grid(float cellSize) { CellSize = cellSize; }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private CellKey KeyFor(in Vector2 p)
            {
                int cx = (int)MathF.Floor(p.X / CellSize);
                int cy = (int)MathF.Floor(p.Y / CellSize);
                return new CellKey(cx, cy);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Add(in Candidate c)
            {
                var key = KeyFor(c.Pos);
                if (!_cells.TryGetValue(key, out var list))
                {
                    list = new List<Candidate>(8);
                    _cells[key] = list;
                }
                list.Add(c);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

        private void ResolveCollisions(
            WorldState world,
            List<PlayerState> allSnakes)
        {
            if (allSnakes == null || allSnakes.Count == 0)
                return;

            float worldHalf = world.Config.WorldSize * 0.5f;
            float cellSize = MathF.Max(0.5f, COLLISION_RADIUS * GRID_CELL_SCALE);
            var grid = new Grid(cellSize);

            // Build spatial index from all snakes' sampled segments and heads
            foreach (var s in allSnakes)
            {
                if (s == null || !s.IsAlive) continue;
                var segsArr = s.BodySegments != null ? s.BodySegments.ToArray() : null;
                if (segsArr == null || segsArr.Length == 0) continue;

                // Sampled body segments
                int stride = Math.Max(1, SEGMENT_SAMPLE_STRIDE);
                for (int i = 0; i < segsArr.Length - 1; i += stride)
                {
                    var v = (Vector2)segsArr[i];
                    grid.Add(new Candidate { OwnerId = s.PlayerId, Pos = v, Flags = 0 });
                }

                // Head
                var head = s.HeadPosition; // SerializableVector2
                var hp = new Vector2(head.X, head.Y);
                grid.Add(new Candidate { OwnerId = s.PlayerId, Pos = hp, Flags = 0x1 });
            }

            float r2 = COLLISION_RADIUS * COLLISION_RADIUS;
            float hh2 = HEAD_HEAD_RADIUS * HEAD_HEAD_RADIUS;

            // Accumulate deaths for this tick to avoid duplicate processing
            var toKill = new Dictionary<string, string?>(capacity: allSnakes.Count);

            foreach (var s in allSnakes)
            {
                if (s == null || !s.IsAlive) continue;
                if (toKill.ContainsKey(s.PlayerId)) continue; // already scheduled

                var head = s.HeadPosition;
                var hp = new Vector2(head.X, head.Y);

                // Boundary kill (authoritative)
                if (MathF.Abs(hp.X) >= worldHalf || MathF.Abs(hp.Y) >= worldHalf)
                {
                    toKill[s.PlayerId] = null; // boundary
                    continue;
                }

                // Query neighborhood for candidates
                Candidate? bestBody = null;
                Candidate? bestHead = null;
                float bestBodyD2 = float.MaxValue;
                float bestHeadD2 = float.MaxValue;

                foreach (var c in grid.QueryNeighborhood(hp))
                {
                    if (c.OwnerId == s.PlayerId) continue; // ignore self body

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

                // Head vs Body has priority
                if (bestBody.HasValue && bestBodyD2 <= r2)
                {
                    toKill[s.PlayerId] = bestBody.Value.OwnerId;
                    continue;
                }

                // Head vs Head resolution
                if (bestHead.HasValue && bestHeadD2 <= hh2)
                {
                    var otherId = bestHead.Value.OwnerId;
                    if (string.IsNullOrEmpty(otherId))
                    {
                        toKill[s.PlayerId] = null;
                    }
                    else
                    {
                        // Tie-break: heavier mass survives; if equal, both die
                        bool otherAlive = !toKill.ContainsKey(otherId);
                        if (GameState.Instance.TryGetPlayer(otherId, out var other) && otherAlive)
                        {
                            if (other.Mass > s.Mass)
                            {
                                toKill[s.PlayerId] = otherId;
                            }
                            else if (other.Mass < s.Mass)
                            {
                                // we survive; let other's iteration handle its own case
                            }
                            else
                            {
                                // equal mass: both die
                                toKill[s.PlayerId] = otherId;
                                if (!toKill.ContainsKey(otherId)) toKill[otherId] = s.PlayerId;
                            }
                        }
                        else
                        {
                            toKill[s.PlayerId] = null;
                        }
                    }
                }
            }

            if (toKill.Count == 0) return;

            // Execute deaths (fire-and-forget per original contract)
            foreach (var kv in toKill)
            {
                _ = OnPlayerDied(kv.Key, kv.Value);
            }
        }

        // ---------------------------------
        // Food collection (authoritative)
        // ---------------------------------
        private sealed class FoodGrid
        {
            public readonly float CellSize;
            private readonly Dictionary<CellKey, List<(int id, Vector2 pos)>> _cells = new(1024);
            public FoodGrid(float cellSize) { CellSize = cellSize; }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private CellKey KeyFor(in Vector2 p)
            {
                int cx = (int)MathF.Floor(p.X / CellSize);
                int cy = (int)MathF.Floor(p.Y / CellSize);
                return new CellKey(cx, cy);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

        private void ResolveFoodEats(WorldState world, List<PlayerState> allSnakes)
        {
            if (allSnakes == null || allSnakes.Count == 0) return;

            var gs = GameState.Instance;
            if (!gs.TryGetWorld(world.WorldId, out var w)) return;
            if (w.FoodIds == null || w.FoodIds.Count == 0) return;

            float cell = MathF.Max(0.5f, FOOD_EAT_RADIUS * GRID_CELL_SCALE);
            var grid = new FoodGrid(cell);

            // Build grid of foods for this world
            foreach (var fid in w.FoodIds.Keys)
            {
                if (!gs.TryGetFood(fid, out var food))
                    continue;
                var p = new Vector2(food.Position.X, food.Position.Y);
                grid.Add(food.Id, p);
            }

            float eatR2 = FOOD_EAT_RADIUS * FOOD_EAT_RADIUS;
            var consumed = new HashSet<int>();

            // For each player, eat nearest food within radius (one per tick)
            foreach (var s in allSnakes)
            {
                if (s == null || !s.IsAlive) continue;

                var head = s.HeadPosition;
                var hp = new Vector2(head.X, head.Y);

                int bestId = 0;
                float bestD2 = float.MaxValue;

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
                    _ = OnPlayerAteFood(s.PlayerId, bestId);
                }
            }
        }

    }
}
