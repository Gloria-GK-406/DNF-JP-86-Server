using Dfo.Pvf.Schema;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

/// <summary>
/// Reads the rank thresholds out of <c>Etc/RankSystemInfo.etc</c>.
/// </summary>
/// <remarks>
/// One short section of a long file, taken the same way the reward rates beside it are: only the
/// named section is collected, so the clear-time tables around it cannot arrive under this heading.
/// <para>
/// A missing table is answered with no grades rather than an exception. Nothing else in a clear
/// depends on it -- a run whose rank cannot be graded simply earns no rank bonus, and the clear
/// still pays what it is worth.
/// </para>
/// </remarks>
internal sealed class PvfDungeonRankGradeSource(PvfArchiveProvider provider)
    : IDungeonRankGradeSource
{
    private const string ArchivePath = "Etc/RankSystemInfo.etc";
    private static readonly PvfField<IReadOnlyList<int>> GradeSection = PvfField.Int32List("[rank grade]", optional: true);

    private readonly Lock _gate = new();
    private DungeonRankGrades? _grades;

    public DungeonRankGrades Load()
    {
        lock (this._gate)
        {
            return this._grades ??= this.Read();
        }
    }

    private DungeonRankGrades Read()
    {
        var archive = provider.Archive;
        var grades = PvfDocument.Open(archive, ArchivePath).Sections
            .Where(section => section.Name.Equals(GradeSection.Name, StringComparison.OrdinalIgnoreCase))
            .SelectMany(section => GradeSection.Decode(archive, section.Values, ArchivePath)).ToArray();

        return grades.Length > 0 ? new DungeonRankGrades(grades) : DungeonRankGrades.None;
    }
}
