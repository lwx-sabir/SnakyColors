using Khela.Game.Models;
using Khela.Game.Models.Configs;
using Khela.Game.Models.States;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Numerics;
using System.Linq;

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
            await Task.Delay(5000, stoppingToken); // Let other systems boot
            _logger.LogInformation("AI Service started (20Hz).");

            try { await Task.Delay(Random.Shared.Next(10, 25), stoppingToken); } catch { }

            while (!stoppingToken.IsCancellationRequested)
            {
                var tickStart = DateTime.UtcNow;

                try
                {
                    var worlds = GameState.Instance.Worlds.Values.ToArray();
                    foreach (var world in worlds)
                    {
                        try
                        {
                            await ManageAIForWorld(world, stoppingToken);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "AI world loop error for {WorldId}", world.WorldId);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "AI main loop error.");
                }

                var elapsed = DateTime.UtcNow - tickStart;
                var delay = _aiTickInterval - elapsed;
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, stoppingToken);
            }
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
                _logger.LogInformation("World {WorldId} needs {Needed} AI snakes. Spawning...",
                    world.WorldId, aiNeeded);

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

                Vector2 head = segments[^1];
                Vector2 prevDir = Vector2.Zero;

                if (segments.Count >= 2)
                {
                    var tail2 = segments[^1] - segments[^2];
                    if (tail2.LengthSquared() > 0.0001f)
                        prevDir = Vector2.Normalize(tail2);
                }

                // --- Target food selection (same logic) ---
                FoodState? nearest = null;

                if (_targetFood.TryGetValue(aiId, out var targetId))
                    nearest = foods.FirstOrDefault(f => f.Id == targetId);

                if (nearest == null)
                {
                    var nearby = foods
                        .OrderBy(f => Vector2.DistanceSquared(head, f.Position))
                        .Take(8)
                        .ToList();

                    if (nearby.Count > 0)
                    {
                        int pick = Math.Abs(StableHash(aiId) + world.Tick) % nearby.Count;
                        nearest = nearby[pick];
                        _targetFood[aiId] = nearest.Id;
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
                segments.Add(new SerializableVector2(newHead));

                int targetLen = snake.TargetLength;
                while (segments.Count > targetLen && segments.Count > 1)
                    segments.RemoveAt(0);

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
