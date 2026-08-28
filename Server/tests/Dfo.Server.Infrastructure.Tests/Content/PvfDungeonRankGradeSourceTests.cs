using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

/// <summary>
/// The rank thresholds, read from the archive.
/// </summary>
/// <remarks>
/// The client works out a run's score for itself and reports it; the only use this server has for
/// the table is deciding which rank bonus that score earned. Reading it wrong pays the wrong rate
/// for the right run, which looks from any single clear like the bonus simply being small.
/// </remarks>
public sealed class PvfDungeonRankGradeSourceTests
{
    [SkippableFact]
    public void TheGradesAreReadAsTheArchiveWritesThem()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();

        var grades = new PvfDungeonRankGradeSource(provider).Load().Grades;

        Assert.Equal([99, 90, 80, 60, 50, 30, 20, 10], grades);
    }

    /// <summary>
    /// Only the named section is collected.
    /// </summary>
    /// <remarks>
    /// The file's other tables are long -- clear times per dungeon run to hundreds of entries --
    /// and a reader that took every integer would return them under this heading and grade every
    /// run at the top.
    /// </remarks>
    [SkippableFact]
    public void TheTablesAroundItAreNotCollectedUnderThisHeading()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();

        var grades = new PvfDungeonRankGradeSource(provider).Load().Grades;

        Assert.Equal(8, grades.Count);
    }
}
