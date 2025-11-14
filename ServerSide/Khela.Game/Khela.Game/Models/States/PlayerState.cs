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

        public PlayerState() { }

        public PlayerState(string connectionId, Vector2 startPosition)
        {
            ConnectionId = connectionId;
            BodySegments.Add(startPosition);
        }
    }
}
