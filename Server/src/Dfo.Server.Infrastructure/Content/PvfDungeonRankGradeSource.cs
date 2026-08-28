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
    private const string GradeSection = "[rank grade]";

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
        var grades = new List<int>();
        string? section = null;

        var reader = provider.Archive.OpenScript(ArchivePath);
        while (reader.Read(out var token))
        {
            if (token.Kind == PvfTokenKind.Identifier)
            {
                section = provider.Archive.ResolveString(token.Value);
                continue;
            }

            if (token.Kind == PvfTokenKind.Int32
                && section is not null
                && section.Equals(GradeSection, StringComparison.OrdinalIgnoreCase))
            {
                grades.Add(token.Value);
            }
        }

        return grades.Count > 0 ? new DungeonRankGrades(grades) : DungeonRankGrades.None;
    }
}
