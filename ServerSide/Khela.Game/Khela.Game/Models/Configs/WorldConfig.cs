namespace Khela.Game.Models.Configs
{
    public class WorldConfig
    {
        public float WorldSize { get; set; } = 300;
        public int WorldGridCount { get; set; } = 50;
        public int MaxPlayers { get; set; } = 500;
        public int TargetFoodCount { get; set; } = 50;
        public int TickRate { get; set; } = 20; 
        public int TargetAISnakeCount { get; set; } = 7;
    }
}