using Khela.Game.Models;
using Khela.Game.Models.States;
using System.Numerics;

namespace Khela.Game.Services.Simulators
{
    public static class SnakeSimulation
    {
        public static void StepPlayerSnake(PlayerState snake, float deltaTime)
        {
            if (!snake.IsAlive)
                return;

            // Ensure we have a body
            if (snake.BodySegments == null)
                snake.BodySegments = new List<SerializableVector2>();

            if (snake.BodySegments.Count == 0)
            {
                // Initialize at some reasonable starting spot
                snake.BodySegments.Add(new SerializableVector2(0, 0));
            }

            int targetLength = Math.Max(2, snake.TargetLength);

            // Resize body to target length (keep tail, add at head)
            while (snake.BodySegments.Count < targetLength)
            {
                var tail = snake.BodySegments[0];
                snake.BodySegments.Insert(0, tail);
            }

            while (snake.BodySegments.Count > targetLength)
            {
                snake.BodySegments.RemoveAt(0);
            }

            // Current speed
            float speed = snake.IsBoosting ? snake.BoostSpeed : snake.BaseSpeed;
            snake.CurrentSpeed = speed;

            // Head & direction
            var headSV = snake.BodySegments[^1];
            var head = new Vector2(headSV.X, headSV.Y);

            var fwd = snake.ForwardDir;
            if (fwd.LengthSquared() < 0.0001f)
                fwd = Vector2.UnitY;

            var targetDir = snake.PendingInputDir;
            if (targetDir.LengthSquared() < 0.0001f)
                targetDir = fwd; // keep going same way if no input
            else
                targetDir = Vector2.Normalize(targetDir);

            // === TURN CAP (MoveToTarget style) ===
            float maxTurnRad = snake.MaxTurningAngle * (MathF.PI / 180f) * deltaTime;

            float dot = Vector2.Dot(fwd, targetDir);
            dot = Math.Clamp(dot, -1f, 1f);
            float angle = MathF.Acos(dot);

            float cross = fwd.X * targetDir.Y - fwd.Y * targetDir.X;
            float sign = cross < 0 ? -1f : 1f;

            float signedAngle = angle * sign;
            float clampedAngle = Math.Clamp(signedAngle, -maxTurnRad, maxTurnRad);

            float cos = MathF.Cos(clampedAngle);
            float sin = MathF.Sin(clampedAngle);

            var newDir = new Vector2(
                fwd.X * cos - fwd.Y * sin,
                fwd.X * sin + fwd.Y * cos
            );

            if (newDir.LengthSquared() < 0.0001f)
                newDir = fwd;

            newDir = Vector2.Normalize(newDir);
            snake.ForwardDir = newDir;

            // Advance head (no wobble in simulation – wobble stays a visual effect for now)
            head += newDir * (speed * deltaTime);

            // Write back head
            snake.BodySegments[^1] = new SerializableVector2(head.X, head.Y);

            // === SlidingChain copy ===
            float segmentDist = snake.PerSegmentDist > 0f ? snake.PerSegmentDist : 0.87f;

            for (int i = snake.BodySegments.Count - 2; i >= 0; i--)
            {
                var svA = snake.BodySegments[i];
                var svB = snake.BodySegments[i + 1];

                var a = new Vector2(svA.X, svA.Y);
                var b = new Vector2(svB.X, svB.Y);

                var diff = a - b;
                float dist = diff.Length();

                if (dist > segmentDist && dist > 0.0001f)
                {
                    var dir = diff / dist;
                    a = b + dir * segmentDist;

                    snake.BodySegments[i] = new SerializableVector2(a.X, a.Y);
                }
            }
        }
    }
}
