using Khela.Game.Models;

namespace Khela.Game.Dtos
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

    public class FoodStateDto
    {
        public int Id { get; set; }

        public float PosX { get; set; }

        public float PosY { get; set; }

        public string ItemKey { get; set; }
    }

    public class FoodDeltaDto
    {
        public List<FoodStateDto> Added { get; set; } = new();
        public List<int> Removed { get; set; } = new();
    }


    public class WorldUpdateDto
    {
        public SnakeKinematicsDto[] Snakes { get; set; }  
        public float WorldSize { get; set; } 
        public double ServerTimeSec { get; set; } 
        public System.DateTime ServerUtc { get; set; }
    }
}
