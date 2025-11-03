using System.Numerics;
using System.Text.Json.Serialization;

namespace Khela.Game.Models
{
    public class SlitherPlayerState
    {
        public string ConnectionId { get; set; }
        public string PlayerName { get; set; } = "Snake";
        public string SkinID { get; set; }
        public bool IsAlive { get; set; } = true;
        public bool IsBoosting { get; set; } = false;

        public float Score { get; set; } = 0;
        public float Speed { get; set; } = 3f;
        public float BoostSpeed { get; set; } = 6f;
        public float MaxTurningAngle { get; set; } = 10f;
        public float PerSegmentDist { get; set; } = 0.5f;

        public List<Vector2> BodySegments { get; set; } = new List<Vector2>();

        [JsonIgnore]
        public Vector2 HeadPosition => BodySegments.Count > 0 ? BodySegments[^1] : Vector2.Zero;

        [JsonIgnore]
        public Vector2 TailPosition => BodySegments.Count > 0 ? BodySegments[0] : Vector2.Zero;

        [JsonIgnore] // This is also calculated
        public int TargetLength => 20 + (int)(Score / 10f); // 10f = scorePerSegment 

        public Vector2 CurrentDirection { get; set; } = Vector2.UnitY;
        public Vector2 TargetDirection { get; set; } = Vector2.UnitY;

        public SlitherPlayerState()
        {
            // Required by the JSON deserializer. 
        }

        // Your "logic" constructor, used by GameEngine.AddPlayer
        public SlitherPlayerState(string connectionId, Vector2 startPosition)
        {
            ConnectionId = connectionId;
            for (int i = 0; i < TargetLength; i++)
            {
                BodySegments.Add(startPosition - new Vector2(0, i * PerSegmentDist));
            }
        }
    }
}
