using Khela.Game.Slither.Items;
using System.Collections.Concurrent;
using System.Numerics;
using Khela.Game.Services.Redis;
using System.Linq;
using System;
using Microsoft.Extensions.Hosting;
using System.Threading;
using System.Threading.Tasks;
using Khela.Game.Models;

namespace Khela.Game.Slither
{
    public class GameEngine : BackgroundService
    {
        private readonly IRedisService _redis; 
        public event Action<string, int> OnFoodEaten; 
        public event Action<SlitherPlayerState, SlitherPlayerState> OnPlayerDied; 
        public event Action<SlitherPlayerState> OnPlayerJoined; 
        public event Action<string> OnPlayerLeft;
         
        private readonly ConcurrentDictionary<int, ServerFood> _food = new();
        private readonly int _tickRate = 20;
        private TimeSpan _tickInterval;
        private const string SNAKE_KEY_PREFIX = "snake:";
         
        public GameEngine(IRedisService redis)
        {
            _redis = redis;
            _tickInterval = TimeSpan.FromMilliseconds(1000.0 / _tickRate);
        }

        public async Task AddPlayer(string connectionId)
        {
            Vector2 startPos = new Vector2(Random.Shared.Next(0, 100), Random.Shared.Next(0, 100));
            SlitherPlayerState newSnake = new SlitherPlayerState(connectionId, startPos);
            await _redis.SetAsync(SNAKE_KEY_PREFIX + connectionId, newSnake);
            OnPlayerJoined?.Invoke(newSnake);
        }

        public async Task RemovePlayer(string connectionId)
        {
            await _redis.DeleteAsync(SNAKE_KEY_PREFIX + connectionId);
            OnPlayerLeft?.Invoke(connectionId); 
        }

        public async Task OnPlayerInput(string connectionId, Vector2 targetWorldPos)
        {
            string key = SNAKE_KEY_PREFIX + connectionId;
            var snake = await _redis.GetAsync<SlitherPlayerState>(key);
            if (snake != null && snake.IsAlive)
            {
                snake.TargetDirection = Vector2.Normalize(targetWorldPos - snake.HeadPosition);
                await _redis.SetAsync(key, snake);
            }
        }

        public async Task OnPlayerBoost(string connectionId, bool isBoosting)
        {
            string key = SNAKE_KEY_PREFIX + connectionId;
            var snake = await _redis.GetAsync<SlitherPlayerState>(key);
            if (snake != null && snake.IsAlive)
            {
                snake.IsBoosting = isBoosting;
                await _redis.SetAsync(key, snake);
            }
        }

        // --- MAIN GAME LOOP ---
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var startTime = DateTime.UtcNow;

                // 1. Run the simulation
                await UpdateGameWorld(stoppingToken); 

                // 3. Wait for next tick
                var endTime = DateTime.UtcNow;
                var timeToWait = _tickInterval - (endTime - startTime);
                if (timeToWait > TimeSpan.Zero)
                {
                    await Task.Delay(timeToWait, stoppingToken);
                }
            }
        }

        private async Task UpdateGameWorld(CancellationToken token)
        {
            SpawnFood();
            var snakeKeys = await _redis.GetKeysByPatternAsync(SNAKE_KEY_PREFIX + "*");
            var processingTasks = snakeKeys.Select(key => ProcessSnakeTick(key, token)).ToList();
            await Task.WhenAll(processingTasks);
        }

        private async Task ProcessSnakeTick(string snakeKey, CancellationToken token)
        {
            if (token.IsCancellationRequested) return;
            var snake = await _redis.GetAsync<SlitherPlayerState>(snakeKey);
            if (snake == null || !snake.IsAlive) return;

            MoveSnake(snake);
            await CheckCollisionsAsync(snake);

            await _redis.SetAsync(snakeKey, snake);
        }

        private void MoveSnake(SlitherPlayerState snake)
        {
            // 1. Calculate Steering
            float angle = MathF.Atan2(snake.TargetDirection.Y, snake.TargetDirection.X) - MathF.Atan2(snake.CurrentDirection.Y, snake.CurrentDirection.X);
            if (angle > MathF.PI) angle -= 2 * MathF.PI;
            if (angle < -MathF.PI) angle += 2 * MathF.PI;
            float maxTurnAngleRad = snake.MaxTurningAngle * (MathF.PI / 180f);
            float steerAngle = Math.Max(-maxTurnAngleRad, Math.Min(maxTurnAngleRad, angle));
            float currentAngle = MathF.Atan2(snake.CurrentDirection.Y, snake.CurrentDirection.X);
            float newAngle = currentAngle + steerAngle;
            snake.CurrentDirection = new Vector2(MathF.Cos(newAngle), MathF.Sin(newAngle));

            // 2. Move Head
            float currentSpeed = snake.IsBoosting ? snake.BoostSpeed : snake.Speed;
            float moveDistance = currentSpeed * (float)_tickInterval.TotalSeconds;
            Vector2 newHeadPos = snake.HeadPosition + (snake.CurrentDirection * moveDistance);
            snake.BodySegments[^1] = newHeadPos; // Update head position

            // 3. Update Body Segments (Sliding Chain)
            for (int i = snake.BodySegments.Count - 2; i >= 0; i--)
            {
                Vector2 dir = snake.BodySegments[i] - snake.BodySegments[i + 1];
                float dist = dir.Length();
                float scaledSegmentDist = snake.PerSegmentDist;
                if (dist > scaledSegmentDist)
                {
                    snake.BodySegments[i] = snake.BodySegments[i + 1] + (Vector2.Normalize(dir) * scaledSegmentDist);
                }
            }
             
            if (snake.BodySegments.Count < snake.TargetLength)
            { 
                snake.BodySegments.Insert(0, snake.TailPosition);
            } 

            // --- 5.Handle Boost Cost Commented for now---
            //if (snake.IsBoosting && snake.Score > 0) // Check if score is > 0
            //{
            //    // Example: Cost 10 score per second while boosting
            //    float scoreCost = 10f * (float)_tickInterval.TotalSeconds;
            //    snake.Score = MathF.Max(0, snake.Score - scoreCost);

            //    // If length is greater than minimum, shrink the tail
            //    if (snake.BodySegments.Count > snake.TargetLength)
            //    {
            //        Vector2 foodPos = snake.TailPosition; // Get tail pos before removing
            //        snake.BodySegments.RemoveAt(0);  
            //    }
            //}
        }

        private async Task CheckCollisionsAsync(SlitherPlayerState snake)
        {
            // 1. Check against food
            foreach (var food in _food.Values)
            {
                if (Vector2.Distance(snake.HeadPosition, food.Position) < 1.0f)
                {
                    snake.Score += 10;
                    if (_food.TryRemove(food.Id, out _))
                    {
                        // --- FIRE EVENT (don't send message) ---
                        OnFoodEaten?.Invoke(snake.ConnectionId, food.Id);
                    }
                }
            }

            // 2. Check against other snakes
            var allSnakeKeys = await _redis.GetKeysByPatternAsync(SNAKE_KEY_PREFIX + "*");
            foreach (var key in allSnakeKeys)
            {
                if (key == SNAKE_KEY_PREFIX + snake.ConnectionId) continue;
                var otherSnake = await _redis.GetAsync<SlitherPlayerState>(key);
                if (otherSnake == null || !otherSnake.IsAlive) continue;

                foreach (var segment in otherSnake.BodySegments)
                {
                    if (Vector2.Distance(snake.HeadPosition, segment) < 0.5f)
                    {
                        snake.IsAlive = false;
                        // --- FIRE EVENT ---
                        OnPlayerDied?.Invoke(snake, otherSnake);
                        return;
                    }
                }
            }
        }

        private void SpawnFood()
        {
            if (_food.Count < 10000)
            {
                int id = Random.Shared.Next(int.MaxValue);
                Vector2 pos = new Vector2(Random.Shared.Next(0, 100), Random.Shared.Next(0, 100));
                _food.TryAdd(id, new ServerFood(id, pos));
            }
        }

        // --- BROADCAST METHODS ARE REMOVED ---
    }
}