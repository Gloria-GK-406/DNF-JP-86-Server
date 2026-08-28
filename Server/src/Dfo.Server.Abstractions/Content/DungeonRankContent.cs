namespace Dfo.Server.Abstractions.Content;

/// <summary>
/// The score a clear must reach to be each rank, best first.
/// </summary>
/// <remarks>
/// From <c>Etc/RankSystemInfo.etc</c>, where this archive lists eight: 99, 90, 80, 60, 50, 30, 20
/// and 10. The client works out a run's score for itself and reports it; the server's only use for
/// the table is deciding which rank bonus that score has earned.
/// </remarks>
/// <param name="Grades">The minimum score for each rank, in descending order.</param>
public sealed record DungeonRankGrades(IReadOnlyList<int> Grades)
{
    /// <summary>A table that grades nothing, so no clear earns a rank bonus.</summary>
    public static DungeonRankGrades None { get; } = new([]);

    /// <summary>
    /// Which of the top ranks this score reached, counting the best as the last.
    /// </summary>
    /// <remarks>
    /// Only as many ranks as there are bonus rates, because the two tables pair off from their
    /// opposite ends: the highest grade earns the highest rate. Below the lowest of those ranks the
    /// answer is nothing at all -- not the smallest rate, which is a rank in its own right and one
    /// this clear did not reach.
    /// </remarks>
    /// <param name="score">The score the client worked out for the run.</param>
    /// <param name="rankCount">How many ranks carry a bonus.</param>
    /// <returns>The rank's place counting from the bottom of those, or -1 for none of them.</returns>
    public int BandOf(int score, int rankCount)
    {
        var covered = Math.Min(rankCount, this.Grades.Count);
        for (var place = 0; place < covered; place++)
        {
            if (score >= this.Grades[place])
                return covered - 1 - place;
        }

        return -1;
    }
}

/// <summary>The archive's rank thresholds.</summary>
public interface IDungeonRankGradeSource
{
    DungeonRankGrades Load();
}
