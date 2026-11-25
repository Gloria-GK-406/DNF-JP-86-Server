namespace Dfo.Server.Abstractions.Pvp;

/// <summary>The active season of the ordinary duel ladder.</summary>
public sealed record PvpRatingSeason(int Id)
{
    public static PvpRatingSeason First { get; } = new(1);
}

/// <summary>The official score stays hidden until the twentieth completed placement match.</summary>
public sealed record PvpRatingStanding(int PlacementGames, int Points, int PeakPoints)
{
    public const int PlacementLimit = 20;
    public const int MinimumPoints = 100;
    public static PvpRatingStanding Initial { get; } = new(0, 1000, 0);
    public int OfficialPoints => PlacementGames == PlacementLimit ? Points : 0;
}

/// <summary>One fighter's immutable entry in the rating journal.</summary>
public sealed record PvpRatingChange(int SeasonId, int PreviousPoints, PvpRatingStanding Standing);
