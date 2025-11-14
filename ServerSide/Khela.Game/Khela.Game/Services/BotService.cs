using Khela.Game.Models;
using Khela.Game.Models.Configs;
using Khela.Game.Models.States;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Numerics;
using System.Linq;
using System.Diagnostics;

namespace Khela.Game.Services
{
    public class AIService : BackgroundService
    {
        private readonly ILogger<AIService> _logger;
        private readonly WorldManagerService _worldManager;
        private readonly GameEngine _gameEngine;

        // === PERFORMANCE CONSTANTS ===
        private readonly TimeSpan _aiTickInterval = TimeSpan.FromMilliseconds(50); // 20 Hz movement

        // === Steering tunables ===
        private const float FOOD_ATTRACT_WEIGHT = 1.0f;
        private const float INWARD_WEIGHT = 0.35f;
        private const float PREV_DIR_WEIGHT = 0.3f;
        
        // === Avoidance tuning ===
        private const float DANGER_AVOID_WEIGHT = 1.1f; // repulsion weight
        private const float MAX_AVOID_DIST = 2.0f;      // meters
        private const float MIN_AVOID_DIST = 0.6f;      // clamp singularity
        private const int MAX_AVOID_SAMPLES = 8;        // limit checks per AI
         
        // Persistent per-AI target food
        private readonly ConcurrentDictionary<string, int> _targetFood = new();

        // Persistent per-AI movement memory (unused) — removed for GC hygiene
        // private readonly ConcurrentDictionary<string, (Vector2 dir, int ticksLeft)> _aiMoveMemory = new();

        // Soft food claim map to reduce dogpiling (foodId -> (aiId, d2, ts))
        private readonly ConcurrentDictionary<int, (string owner, float d2, long ts)> _foodClaims = new();

        public AIService(
            ILogger<AIService> logger,
            WorldManagerService worldManager,
            GameEngine gameEngine)
        {
            _logger = logger;
            _worldManager = worldManager;
            _gameEngine = gameEngine;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try { await Task.Delay(Random.Shared.Next(10, 40), stoppingToken); } catch { }

            _logger.LogInformation("AIService started at {Hz}Hz", 1.0 / _aiTickInterval.TotalSeconds);

            var stopwatch = Stopwatch.StartNew();
            var nextTick = stopwatch.Elapsed;
            long tickCount = 0;

            while (!stoppingToken.IsCancellationRequested)
            {
                var now = stopwatch.Elapsed;

                // Run tick precisely when expected
                if (now >= nextTick)
                {
                    var tickStart = stopwatch.Elapsed;

                    try
                    {
                        // Snapshot worlds once per tick (safe)
                        var worlds = GameState.Instance.Worlds.Values.ToArray();

                        foreach (var world in worlds)
                        {
                            try
                            {
                                await ManageAIForWorld(world, stoppingToken);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "AI error in world {WorldId}", world.WorldId);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "AI main tick loop failure");
                    }

                    tickCount++;
                    nextTick += _aiTickInterval;     // schedule the next tick

                    // Catch-up logic — but safe and non-spiraling
                    while (stopwatch.Elapsed - nextTick > _aiTickInterval)
                        nextTick += _aiTickInterval;

                    // OPTIONAL: Tick performance monitoring every 100 cycles
                    if (tickCount % 100 == 0)
                    {
                        var elapsedSec = stopwatch.Elapsed.TotalSeconds;
                        if (elapsedSec > 0)
                        {
                            var eff = tickCount / elapsedSec;
                            _logger.LogInformation("AI: {Ticks} ticks in {Sec:F1}s → {Rate:F2}Hz",
                                tickCount, elapsedSec, eff);
                        }
                    }
                }

                // Compute remaining time before next tick
                var sleep = nextTick - stopwatch.Elapsed;

                if (sleep > TimeSpan.Zero)
                {
                    // AI sleeps lightly — DOES NOT flood CPU
                    await Task.Delay(sleep, stoppingToken);
                }
                else
                {
                    // Behind schedule, yield control but don't spam
                    await Task.Yield();
                }
            }

            _logger.LogInformation("AIService stopped.");
        }

        // =====================================================
        // PER-WORLD AI MANAGEMENT (same semantics)
        // =====================================================

        private async Task ManageAIForWorld(WorldState world, CancellationToken token)
        {
            if (token.IsCancellationRequested)
                return;

            if (world == null || world.Config == null || world.CurrentStatus != GameStatus.Running)
                return;

            var gs = GameState.Instance;

            // Get AI snakes from in-memory state
            var aiSnakes = world.AISnakeIds.Keys
                .Select(id => gs.TryGetPlayer(id, out var s) ? s : null)
                .Where(s => s != null && s.IsAlive)
                .ToList();

            int aliveAICount = aiSnakes.Count;
            int aiNeeded = world.Config.TargetAISnakeCount - aliveAICount;

            if (aiNeeded > 0)
            {
                //_logger.LogInformation("World {WorldId} needs {Needed} AI snakes. Spawning...",
                //    world.WorldId, aiNeeded);

                for (int i = 0; i < aiNeeded; i++)
                {
                    string aiPlayerId = $"ai-pid-{Guid.NewGuid():N}";
                    string aiConnId = $"ai-conn-{Guid.NewGuid():N}";

                    // Uses in-memory WorldManager; it already adds to AISnakeIds
                    await _worldManager.AddPlayerToWorldAsync(aiConnId, world.WorldId, aiPlayerId, 1f, "greenskin", isAi: true);
                }

                // Refresh AI list after spawn
                aiSnakes = world.AISnakeIds.Keys
                    .Select(id => gs.TryGetPlayer(id, out var s) ? s : null)
                    .Where(s => s != null && s.IsAlive)
                    .ToList();
            }

            if (world.AISnakeIds.Count > 0 && aiSnakes.Count > 0)
            {
                // GC stale claims (foods removed globally) without building a hash set
                foreach (var fid in _foodClaims.Keys)
                {
                    if (!gs.Foods.ContainsKey(fid))
                        _foodClaims.TryRemove(fid, out _);
                }

                MoveAIs(world, aiSnakes);
            }

            return;
        }

        // =====================================================
        // AI MOVEMENT & COLLISION (behavior preserved)
        // =====================================================

        private void MoveAIs(WorldState world, List<PlayerState> aiSnakes)
        { 
            var gs = GameState.Instance;
            float worldHalf = world.Config.WorldSize / 2f;
            float dt = (float)_aiTickInterval.TotalSeconds;

            // --- Food snapshot (allocation‑light) ---
            var foods = new List<FoodState>(world.FoodIds.Count);
            foreach (var fid in world.FoodIds.Keys)
            {
                if (gs.TryGetFood(fid, out var f) && f != null)
                    foods.Add(f);
            }

            if (foods.Count == 0)
                return;

            // Heads snapshot for avoidance (allocation‑light)
            var headCount = world.SnakeIds.Count + world.AISnakeIds.Count;
            var tmpHeads = new List<(string id, Vector2 pos)>(headCount);
            foreach (var id in world.SnakeIds.Keys)
            {
                if (gs.TryGetPlayer(id, out var s) && s != null && s.IsAlive)
                    tmpHeads.Add((s.PlayerId, (Vector2)s.HeadPosition));
            }
            foreach (var id in world.AISnakeIds.Keys)
            {
                if (gs.TryGetPlayer(id, out var s) && s != null && s.IsAlive)
                    tmpHeads.Add((s.PlayerId, (Vector2)s.HeadPosition));
            }
            var allHeads = tmpHeads.ToArray();

            foreach (var snake in aiSnakes)
            {
                string aiId = snake.PlayerId;

                var segments = snake.BodySegments ?? new List<SerializableVector2>();
                if (segments.Count == 0)
                {
                    // Ensure at least one segment exists
                    segments.Add(new SerializableVector2(0, 0));
                }

                int last = segments.Count - 1;

                // HEAD = last element
                Vector2 head = segments[last];

                // PREVIOUS HEAD = second last
                Vector2 prevDir = Vector2.Zero;
                if (segments.Count >= 2)
                {
                    var from = segments[last - 1];
                    var to = segments[last];
                    var d = to - from;
                    prevDir = d.LengthSquared() > 0.0001f ? NormalizeSafe(d) : Vector2.Zero;
                }

                // --- Target food selection with soft claims ---
                FoodState? nearest = null;
                float nearestD2 = float.MaxValue;

                if (_targetFood.TryGetValue(aiId, out var targetId))
                {
                    var cur = foods.FirstOrDefault(f => f.Id == targetId);
                    if (cur != null)
                    {
                        nearest = cur;
                        nearestD2 = Vector2.DistanceSquared(head, cur.Position);
                    }
                }

                if (nearest == null)
                { 
                    FoodState[] top = new FoodState[8];
                    float[] best = new float[8];

                    for (int i = 0; i < 8; i++)
                        best[i] = float.MaxValue;

                    foreach (var f in foods)
                    {
                        float d2 = Vector2.DistanceSquared(head, f.Position);

                        // Check if it's better than the worst (slot 7)
                        if (d2 < best[7])
                        {
                            // Insert sorted
                            int j = 7;
                            while (j > 0 && d2 < best[j - 1])
                            {
                                best[j] = best[j - 1];
                                top[j] = top[j - 1];
                                j--;
                            }
                            best[j] = d2;
                            top[j] = f;
                        }
                    }

                    for (int i = 0; i < 8; i++)
                    {
                        var cand = top[i];
                        if (cand == null) continue;
                        float d2 = best[i];
                        if (TryClaimFood(cand.Id, aiId, d2))
                        {
                            nearest = cand; nearestD2 = d2; break;
                        }
                    }

                    if (nearest == null)
                    {
                        nearest = top.FirstOrDefault(f => f != null);
                        if (nearest != null) nearestD2 = Vector2.DistanceSquared(head, nearest.Position);
                    }
                }

                Vector2 desired = Vector2.Zero;

                if (nearest != null)
                    desired += NormalizeSafe(nearest.Position - (SerializableVector2)head) * FOOD_ATTRACT_WEIGHT;

                // Inward bias near walls (same)
                float margin = MathF.Max(2f, worldHalf * 0.02f);
                float distEdgeX = worldHalf - MathF.Abs(head.X);
                float distEdgeY = worldHalf - MathF.Abs(head.Y);
                if (distEdgeX < margin || distEdgeY < margin)
                    desired += NormalizeSafe(new Vector2(-head.X, -head.Y)) * INWARD_WEIGHT;

                // Repulsion from nearby heads (danger avoidance)
                Vector2 avoid = Vector2.Zero;
                float maxR2 = MAX_AVOID_DIST * MAX_AVOID_DIST;
                int samples = 0;
                for (int i = 0; i < allHeads.Length && samples < MAX_AVOID_SAMPLES; i++)
                {
                    var (oid, hpos) = allHeads[i];
                    if (oid == aiId) continue;
                    float dx = hpos.X - head.X;
                    float dy = hpos.Y - head.Y;
                    float d2 = dx * dx + dy * dy;
                    if (d2 <= maxR2)
                    {
                        float d = MathF.Sqrt(MathF.Max(d2, MIN_AVOID_DIST * MIN_AVOID_DIST));
                        float inv = 1.0f / d;
                        avoid += new Vector2(-dx * inv, -dy * inv);
                        samples++;
                    }
                }
                if (avoid.LengthSquared() > 0.0001f)
                    desired += NormalizeSafe(avoid) * DANGER_AVOID_WEIGHT;

                // Keep momentum
                if (prevDir.LengthSquared() > 0.0001f)
                    desired += prevDir * PREV_DIR_WEIGHT;

                desired = NormalizeSafe(desired);

                float speed = snake.IsBoosting
                    ? snake.BoostSpeed
                    : (snake.BaseSpeed > 0 ? snake.BaseSpeed : snake.CurrentSpeed);

                float turnDeg = snake.MaxTurningAngle > 0 ? snake.MaxTurningAngle : 360f;
                float maxRad = turnDeg * (MathF.PI / 180f) * dt;

                Vector2 moveDir = prevDir.LengthSquared() > 0.0001f
                    ? RotateTowards(prevDir, desired, maxRad)
                    : desired;

                Vector2 newHead = head + moveDir * speed * dt;

                int targetLen = snake.TargetLength;
                // ADD NEW HEAD AT END
                segments.Add(new SerializableVector2(newHead));

                // TRIM FROM FRONT (oldest)
                while (segments.Count > targetLen && segments.Count > 1)
                    segments.RemoveAt(0);

                snake.BodySegments = segments;
                snake.CurrentSpeed = speed;
                snake.IsBoosting = false;
                 
                gs.AddOrUpdatePlayer(snake);
                 
                // Food eats/collisions are resolved centrally in GameEngine.
            }
        }

        // =====================================================
        // Utility methods (unchanged)
        // =====================================================

        private static Vector2 NormalizeSafe(Vector2 v)
            => v.LengthSquared() < 0.0001f ? Vector2.Zero : Vector2.Normalize(v);

        private static Vector2 RotateTowards(Vector2 from, Vector2 to, float maxRadians)
        {
            var f = NormalizeSafe(from);
            var t = NormalizeSafe(to);

            if (f.LengthSquared() < 0.0001f)
                return t;

            float angle = MathF.Atan2(f.X * t.Y - f.Y * t.X, f.X * t.X + f.Y * t.Y);
            float clamped = Math.Clamp(angle, -maxRadians, maxRadians);
            float cos = MathF.Cos(clamped);
            float sin = MathF.Sin(clamped);

            return new Vector2(
                f.X * cos - f.Y * sin,
                f.X * sin + f.Y * cos
            );
        }

        // StableHash removed (unused)

        private bool TryClaimFood(int foodId, string aiId, float d2)
        {
            var now = DateTime.UtcNow.Ticks;
            if (_foodClaims.TryAdd(foodId, (aiId, d2, now)))
                return true;

            if (_foodClaims.TryGetValue(foodId, out var existing))
            {
                // If significantly closer, take over
                if (d2 < existing.d2 * 0.7f)
                {
                    return _foodClaims.TryUpdate(foodId, (aiId, d2, now), existing);
                }
            }
            return false;
        }
    }
}
