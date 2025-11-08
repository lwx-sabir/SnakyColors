using Khela.Game.Models;

namespace Khela.Game.Dtos
{
    //public class SnakeStateDto
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
        public PlayerState[] Snakes { get; set; }
        public FoodStateDto[] Food { get; set; }

        public float WorldSize { get; set; }
    }
}
