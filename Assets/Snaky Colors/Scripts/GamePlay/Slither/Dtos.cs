using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json.Serialization;

namespace SnakyColors
{
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

    //public class PlayerState
    //{
    //    public string PlayerId { get; set; }
    //    public float HeadX { get; set; }
    //    public float HeadY { get; set; }
    //    public float Score { get; set; }
    //    public string SkinID { get; set; }
    //    public int Length { get; set; }
    //    public int Mass { get; set; }
    //    public float CurrentSpeed { get; set; }
    //    public bool IsAI { get; set; }
    //    public SerializableVector2 HeadPosition { get; set; }
    //    public float MaxTurningAngle { get; set; }
    //    public int TargetLength { get; set; }
    //}

    public class FoodStateDto
    {
        public int Id { get; set; }

        public float PosX { get; set; }

        public float PosY { get; set; }

        public string ItemKey { get; set; }
    }  

    public class WorldUpdateDto
    {
        public SnakeKinematicsDto[] Snakes { get; set; }
        public FoodStateDto[] Food { get; set; }

        public float WorldSize { get; set; }
        public int Tick { get; set; }
        public int TickRate { get; set; }
        public DateTime ServerUtc { get; set; }
    }

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
        public int Mass { get; set; } 
        public float BaseSpeed { get; set; } = 7f;
        public float CurrentSpeed { get; set; } = 7f; // Client will report this
        public float BoostSpeed { get; set; } = 17f;
        public float MaxTurningAngle { get; set; } = 10f; // Client will use this
        public float PerSegmentDist { get; set; } = 0.5f;

        public List<SerializableVector2> BodySegments { get; set; } = new List<SerializableVector2>();


        [JsonIgnore]
        public Vector2 HeadPosition => BodySegments.Count > 0 ? BodySegments[^1] : Vector2.Zero;
        [JsonIgnore]
        public Vector2 TailPosition => BodySegments.Count > 0 ? BodySegments[0] : Vector2.Zero;
        [JsonIgnore]
        public int TargetLength => 20 + (int)(Score / 10f);

        public PlayerStateDto() { }

        public PlayerStateDto(string connectionId, Vector2 startPosition)
        {
            ConnectionId = connectionId;  
            BodySegments.Add(startPosition);
        }
    }
}
