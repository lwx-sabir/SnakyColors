namespace SnakyColors
{
    public class SnakeStateDto
    {
        public string Id { get; set; }
        public System.Numerics.Vector2 HeadPosition { get; set; } // Server-side Vector2
        public float Score { get; set; }
        public string SkinID { get; set; }

        public int Length { get; set; }
    }

    public class FoodStateDto
    {
        public int Id { get; set; }
        public System.Numerics.Vector2 Pos { get; set; } // Server-side Vector2  
    }

    public class WorldUpdateDto
    {
        public SnakeStateDto[] Snakes { get; set; }
        public FoodStateDto[] Food { get; set; }
    }
}