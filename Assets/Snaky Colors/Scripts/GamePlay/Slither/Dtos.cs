using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json.Serialization;
using UnityEngine.Scripting;

namespace SnakyColors
{
    [Preserve]
    [Serializable]
    public class SnakeKinematicsDto
    {
        public string PlayerId { get; set; }
        public string SkinID { get; set; }
        public bool IsAI { get; set; }
        public int Mass { get; set; }

        public SerializableVector2 HeadPosition { get; set; }

        public float BaseSpeed { get; set; }
        public float CurrentSpeed { get; set; }
        public float MaxTurningAngle { get; set; }

        public int TargetLength { get; set; }
    }

    [Preserve]
    [Serializable]
    public class FoodStateDto
    {
        public int Id { get; set; }

        public float PosX { get; set; }

        public float PosY { get; set; }

        public string ItemKey { get; set; }
    }

    [Preserve]
    [Serializable]
    public class WorldUpdateDto
    {
        public SnakeKinematicsDto[] Snakes { get; set; }  
        public float WorldSize { get; set; } 
        public double ServerTimeSec { get; set; }
        public DateTime ServerUtc { get; set; }
    }

    [Preserve]
    [Serializable]
    public class FoodDeltaDto
    {
        public List<FoodStateDto> Added { get; set; } = new();
        public List<int> Removed { get; set; } = new();
    }


    [Preserve]
    [Serializable]
    public class PlayerStateDto
    {
        public string PlayerId { get; set; }
        public string ConnectionId { get; set; }
        public string PlayerName { get; set; } = "Snake";
        public string CurrentWorldId { get; set; }
        public string SkinID { get; set; }
        public bool IsAlive { get; set; } = true;
        public bool IsBoosting { get; set; } = false;
        public bool IsAI { get; set; }
        public float Score { get; set; } = 0;
        public int Mass { get; set; } = 20;
        public float BaseSpeed { get; set; } = 7f;
        public float CurrentSpeed { get; set; } = 7f;
        public float BoostSpeed { get; set; } = 10f;
        public float MaxTurningAngle { get; set; } = 1000f;
        public float PerSegmentDist { get; set; } = 0.5f;

        public List<SerializableVector2> BodySegments { get; set; } = new List<SerializableVector2>();

        [JsonIgnore]
        public Vector2 HeadPosition => BodySegments.Count > 0
            ? new Vector2(BodySegments[^1].X, BodySegments[^1].Y)
            : Vector2.Zero;

        [JsonIgnore]
        public Vector2 TailPosition => BodySegments.Count > 0
            ? new Vector2(BodySegments[0].X, BodySegments[0].Y)
            : Vector2.Zero;

        [JsonIgnore]
        public int TargetLength => 20 + (int)(Score / 10f);

        // === NEW: server-side runtime state ===

        /// <summary>
        /// Latest input direction from client (unit vector or zero).
        /// </summary>
        [JsonIgnore]
        public Vector2 PendingInputDir { get; set; } = Vector2.Zero;

        /// <summary>
        /// Current forward direction used by the simulator.
        /// </summary>
        [JsonIgnore]
        public Vector2 ForwardDir { get; set; } = Vector2.UnitY;

        /// <summary>
        /// Internal wobble phase (for future server-side wobble).
        /// </summary>
        [JsonIgnore]
        public float WobblePhase { get; set; } = 0f;

        public PlayerStateDto() { }

        public PlayerStateDto(string connectionId, Vector2 startPosition)
        {
            ConnectionId = connectionId;
            BodySegments.Add(startPosition);
        }
    }
}
