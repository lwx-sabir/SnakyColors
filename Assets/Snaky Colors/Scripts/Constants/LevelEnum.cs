

namespace SnakyColors
{
    public enum GameType
    {
        FoodBuster,
        ObstacleMaster,
        ColorHunter
    }

    public enum ObjectiveType
    {
        CollectStars,
        MatchNObjects, // Collect N objects of the matching color
        DestroyNObstacles, // Destroy N matching obstacles
        SurviveTime,       // Survive for N seconds
        ReachSpeed,        // Reach a target player speed
        ScorePoints        // Achieve a target score
    }

    public enum PaletteType
    {
        EasyPalette,
        MediumPalette,
        HardPalette,
        NeonPalette,
        Seasonal
    }

    // Optional: Define Arena/Level Layouts if they are complex
    public enum ArenaLayout
    {
        BasicStraight,
        SwerveCorners,
        NarrowWalls,
        MovingObstacles
    }
}
