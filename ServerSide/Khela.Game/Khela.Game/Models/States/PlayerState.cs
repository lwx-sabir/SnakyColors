using System.Numerics;
using System.Text.Json.Serialization;
using System.Collections.Generic;

namespace Khela.Game.Models.States
{
    public class PlayerState
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
        public int Mass { get; set; } = 35;
        public float BaseSpeed { get; set; } = 7f;
        public float CurrentSpeed { get; set; } = 7f;
        public float BoostSpeed { get; set; } = 10f;
        public float MaxTurningAngle { get; set; } = 1000f;
        public float PerSegmentDist { get; set; } = 0.5f;

        public List<SerializableVector2> BodySegments { get; set; } = new();

        // HEAD = index 0
        [JsonIgnore]
        public Vector2 HeadPosition
        {
            get => BodySegments.Count > 0 ? BodySegments[0] : Vector2.Zero;
            set
            {
                if (BodySegments.Count == 0)
                    BodySegments.Add(value);
                else
                    BodySegments[0] = value;
            }
        }

        // TAIL = last index
        [JsonIgnore]
        public Vector2 TailPosition =>
            BodySegments.Count > 0 ? BodySegments[^1] : Vector2.Zero;

        [JsonIgnore]
        public int TargetLength => 15 + (int)(Score / 10f);

        public PlayerState() { }

        public PlayerState(string connectionId, Vector2 startPosition)
        {
            ConnectionId = connectionId;
            BodySegments.Add(startPosition);
        }
    }
}
