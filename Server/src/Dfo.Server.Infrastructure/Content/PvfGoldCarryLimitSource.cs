using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

/// <summary>
/// Reads <c>etc/(r)goldlimitbylevel.etc</c>: how much gold a character may carry at a level.
/// </summary>
/// <remarks>
/// The file is one section, <c>[gold limit from level]</c>, holding a hundred pairs of a level and
/// a limit. It is read whole and once: it is a hundred numbers that do not change while the server
/// runs, and a lookup per gold change would reopen the archive on the pick-up path.
/// <para>
/// <b>An unreadable table fails rather than answering zero.</b> Zero is a limit this file can
/// legitimately declare -- level zero has it -- so a reader that fell back to it would clamp every
/// player's gold to nothing while looking like it had read something. The same reasoning the town
/// catalogue is read under.
/// </para>
/// </remarks>
internal sealed class PvfGoldCarryLimitSource(PvfArchiveProvider provider) : IGoldCarryLimitSource
{
    private const string TablePath = "etc/(r)goldlimitbylevel.etc";

    /// <summary>The archive declares a hundred levels; fewer means the file is not the one meant.</summary>
    private const int DeclaredLevels = 100;

    private readonly Lazy<IReadOnlyList<(int Level, int Limit)>> bands =
        new(() => Read(provider), LazyThreadSafetyMode.ExecutionAndPublication);

    public int BaseLimitFor(int characterLevel)
    {
        var declared = this.bands.Value;
        var limit = declared[0].Limit;

        // The nearest declared level at or below this one. Ascending, so the last that still
        // qualifies is the answer -- which is also what answers for a character above the last row.
        foreach (var (level, value) in declared)
        {
            if (level > characterLevel)
                break;

            limit = value;
        }

        return limit;
    }

    private static IReadOnlyList<(int Level, int Limit)> Read(PvfArchiveProvider provider)
    {
        var numbers = new List<int>();
        var reader = provider.Archive.OpenScript(TablePath);
        while (reader.Read(out var token))
        {
            if (token.Kind == PvfTokenKind.Int32)
                numbers.Add(token.Value);
        }

        if (numbers.Count < DeclaredLevels * 2 || numbers.Count % 2 != 0)
        {
            throw new InvalidDataException(
                $"'{TablePath}' declares {numbers.Count} numbers, which is not "
                + $"{DeclaredLevels} level-and-limit pairs.");
        }

        var bands = new List<(int Level, int Limit)>(numbers.Count / 2);
        for (var index = 0; index + 1 < numbers.Count; index += 2)
            bands.Add((numbers[index], Math.Max(0, numbers[index + 1])));

        bands.Sort((left, right) => left.Level.CompareTo(right.Level));
        return bands;
    }
}
