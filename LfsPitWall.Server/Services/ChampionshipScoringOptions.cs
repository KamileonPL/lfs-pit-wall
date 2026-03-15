namespace LfsPitWall.Server.Services;

public class ChampionshipScoringOptions
{
    public const string SectionName = "ChampionshipScoring";

    public List<int> PositionPoints { get; set; } =
    [
        50, 45, 40, 36, 32,
        29, 26, 23, 20, 18,
        16, 14, 13, 12, 11,
        10, 9, 8, 7, 6,
        5, 4, 3, 2, 1
    ];

    public ChampionshipBonusPointsOptions Bonuses { get; set; } = new();

    public int GetPointsForPosition(int finishingPosition)
    {
        return finishingPosition <= 0 || finishingPosition > PositionPoints.Count
            ? 0
            : PositionPoints[finishingPosition - 1];
    }

    public bool HasValidConfiguration()
    {
        return PositionPoints.Count > 0
            && PositionPoints.All(points => points >= 0)
            && Bonuses.AreValid();
    }
}

public class ChampionshipBonusPointsOptions
{
    public int PolePosition { get; set; } = 5;
    public int FastestLap { get; set; } = 5;
    public int HighestClimber { get; set; } = 2;

    public bool AreValid()
    {
        return PolePosition >= 0
            && FastestLap >= 0
            && HighestClimber >= 0;
    }
}
