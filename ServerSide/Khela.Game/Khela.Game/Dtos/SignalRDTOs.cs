namespace Khela.Game.Dtos
{
    public class SnakeStateDto
    {
        public string Id { get; set; }
        
        public float HeadX { get; set; }

        public float HeadY { get; set; }
        
        public float Score { get; set; }
        
        public string SkinID { get; set; }
        
        public int Length { get; set; }
    }

    public class FoodStateDto
    {
        public int Id { get; set; }

        public float PosX { get; set; }

        public float PosY { get; set; }

        public string ItemKey { get; set; }
    }

    public class WorldUpdateDto
    {
        public SnakeStateDto[] Snakes { get; set; }
        public FoodStateDto[] Food { get; set; }
    }
}
