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
        private const int SEGMENT_SAMPLE_STRIDE = 5;

        private const float EAT_RADIUS = 0.75f;
        private const float COLLISION_RADIUS = 0.75f;
         
        // Persistent per-AI target food
        private readonly ConcurrentDictionary<string, int> _targetFood = new();

        // Persistent per-AI movement memory (for the unused random test helper / can be reused)
        private readonly ConcurrentDictionary<string, (Vector2 dir, int ticksLeft)> _aiMoveMemory = new();

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
                    await _worldManager.AddPlayerToWorldAsync(aiConnId, world.WorldId, aiPlayerId, "greenskin", isAi: true);
                }

                // Refresh AI list after spawn
                aiSnakes = world.AISnakeIds.Keys
                    .Select(id => gs.TryGetPlayer(id, out var s) ? s : null)
                    .Where(s => s != null && s.IsAlive)
                    .ToList();
            }

            if (world.AISnakeIds.Count > 0 && aiSnakes.Count > 0)
                MoveAIs(world, aiSnakes);

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
            float colR2 = COLLISION_RADIUS * COLLISION_RADIUS;

            // --- Food snapshot ---
            var foods = world.FoodIds.Keys
                .Select(id => gs.TryGetFood(id, out var f) ? f : null)
                .Where(f => f != null)
                .ToList();

            if (foods.Count == 0)
                return;

            // --- Cached snake map for collisions ---
            var allSnakesMap = world.SnakeIds.Keys
                .Concat(world.AISnakeIds.Keys)
                .Distinct()
                .Select(id => new { Id = id, Snake = gs.TryGetPlayer(id, out var s) ? s : null })
                .Where(x => x.Snake != null && x.Snake.IsAlive)
                .ToDictionary(x => x.Id, x => x.Snake);

            foreach (var snake in aiSnakes)
            {
                string aiId = snake.PlayerId;

                var segments = snake.BodySegments ?? new List<SerializableVector2>();
                if (segments.Count == 0)
                {
                    // Ensure at least one segment exists
                    segments.Add(new SerializableVector2(0, 0));
                }

                Vector2 head = segments[0];
                Vector2 prevDir = Vector2.Zero;

                if (segments.Count >= 2)
                {
                    var hd2 = segments[0] - segments[1]; 
                    prevDir = hd2.LengthSquared() > 0.0001f ? NormalizeSafe(hd2) : Vector2.Zero;
                }

                // --- Target food selection (same logic) ---
                FoodState? nearest = null;

                if (_targetFood.TryGetValue(aiId, out var targetId))
                    nearest = foods.FirstOrDefault(f => f.Id == targetId);

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
                    nearest = top.FirstOrDefault(f => f != null);
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

                // --- Boundary kill (same behavior) ---
                if (MathF.Abs(newHead.X) >= worldHalf || MathF.Abs(newHead.Y) >= worldHalf)
                {
                    _ = _gameEngine.OnPlayerDied(aiId, null);
                    continue;
                }
                 
                bool collided = false;
                string? killerId = null;

                foreach (var kv in allSnakesMap)
                {
                    var other = kv.Value;
                    if (other.PlayerId == aiId || !other.IsAlive)
                        continue;

                    var segs = other.BodySegments;
                    if (segs == null || segs.Count == 0)
                        continue;

                    for (int i = 0; i < segs.Count; i += SEGMENT_SAMPLE_STRIDE)
                    {
                        float dx = segs[i].X - newHead.X;
                        float dy = segs[i].Y - newHead.Y;
                        if (dx * dx + dy * dy <= colR2)
                        {
                            collided = true;
                            killerId = other.PlayerId;
                            break;
                        }
                    }

                    if (collided)
                        break;
                }

                if (collided)
                {
                    _ = _gameEngine.OnPlayerDied(aiId, killerId);
                    continue;
                }

                // --- Update body (same growth & trim logic) ---
                segments.Insert(0, new SerializableVector2(newHead));

                int targetLen = snake.TargetLength;
                while (segments.Count > targetLen && segments.Count > 1)
                    segments.RemoveAt(segments.Count - 1);

                snake.BodySegments = segments;
                snake.CurrentSpeed = speed;
                snake.IsBoosting = false;
                 
                gs.AddOrUpdatePlayer(snake);
                 
                if (nearest != null)
                {
                    float ex = nearest.Position.X - newHead.X;
                    float ey = nearest.Position.Y - newHead.Y;
                    if (ex * ex + ey * ey <= EAT_RADIUS * EAT_RADIUS)
                    {
                        _ = _gameEngine.OnPlayerAteFood(aiId, nearest.Id);
                        _targetFood.TryRemove(aiId, out _);
                    }
                }
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

        private static int StableHash(string s)
        {
            unchecked
            {
                int h = 23;
                foreach (char c in s)
                    h = h * 31 + c;
                return h;
            }
        }
    }
}
