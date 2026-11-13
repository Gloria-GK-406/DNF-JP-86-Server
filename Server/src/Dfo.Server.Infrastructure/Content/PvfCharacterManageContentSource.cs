using Dfo.Pvf.Schema;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

/// <summary>
/// Reads <c>etc/linksystem/charactermanage.etc</c>: the adventure-group (冒险团) point, level and
/// bonus tables.
/// </summary>
/// <remarks>
/// <b>The file is not well formed, and is read as it is.</b> <c>[manage level max]</c> has no
/// closing tag, and the file ends on a second, unclosed <c>[contents revision]</c>. The 70 server
/// reads it the same way: each heading runs to the next one. Neither section's values spill into
/// another, so reading section by section loses nothing.
/// <para>
/// An unreadable table fails rather than answering zero, the way the gold carry table does: a
/// zero threshold would put every account at the top level.
/// </para>
/// </remarks>
internal sealed class PvfCharacterManageContentSource(PvfArchiveProvider provider)
    : ICharacterManageContentSource, Dfo.Server.Abstractions.Content.IContentWarmup
{
    internal const string TablePath = "etc/linksystem/charactermanage.etc";

    private static readonly PvfField<IReadOnlyList<int>> PointBonus = PvfField.Int32List("[point bonus]");
    private static readonly PvfField<IReadOnlyList<int>> LevelPoint = PvfField.Int32List("[manage level point]");
    private static readonly PvfField<int> LevelMax = PvfField.Int32("[manage level max]", minimum: 1);
    private static readonly PvfField<IReadOnlyList<int>> ExpBonus = PvfField.Int32List("[exp bonus]");
    private static readonly PvfField<IReadOnlyList<int>> GoldBonus = PvfField.Int32List("[gold bonus]", optional: true);

    private readonly Lazy<CharacterManageTable> table =
        new(() => Read(provider), LazyThreadSafetyMode.ExecutionAndPublication);

    public string Name => "character-manage";

    public CharacterManageTable Table => table.Value;

    public ValueTask<int> WarmAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(table.Value.LevelThresholds.Count);

    private static CharacterManageTable Read(PvfArchiveProvider provider)
    {
        var document = PvfDocument.Open(provider.Archive, TablePath);

        var bandNumbers = document.Read(PointBonus);
        Require(bandNumbers.Count > 0 && bandNumbers.Count % 3 == 0, "[point bonus] is not (min, max, point) triples");
        var bands = new List<CharacterManagePointBand>(bandNumbers.Count / 3);
        for (var index = 0; index < bandNumbers.Count; index += 3)
        {
            var band = new CharacterManagePointBand(bandNumbers[index], bandNumbers[index + 1], bandNumbers[index + 2]);
            Require(band.MinLevel >= 0 && band.MinLevel <= band.MaxLevel && band.Point >= 0,
                $"[point bonus] row {index / 3} is not a level range with a point");
            bands.Add(band);
        }

        var thresholds = document.Read(LevelPoint);
        Require(thresholds.Count > 0, "[manage level point] is empty");
        for (var index = 0; index < thresholds.Count; index++)
            Require(thresholds[index] > (index == 0 ? 0 : thresholds[index - 1]),
                "[manage level point] does not rise strictly");

        var max = document.Read(LevelMax);
        Require(max <= thresholds.Count, "[manage level max] names a level with no threshold");

        return new(
            bands.AsReadOnly(),
            thresholds,
            max,
            Pairs(document.Read(ExpBonus), "[exp bonus]"),
            Pairs(document.Read(GoldBonus) ?? [], "[gold bonus]"));
    }

    private static IReadOnlyDictionary<int, int> Pairs(IReadOnlyList<int> numbers, string section)
    {
        Require(numbers.Count % 2 == 0, $"{section} is not (level, percent) pairs");
        var pairs = new Dictionary<int, int>();
        for (var index = 0; index < numbers.Count; index += 2)
        {
            Require(numbers[index + 1] >= 0 && pairs.TryAdd(numbers[index], numbers[index + 1]),
                $"{section} repeats a level or declares a negative percent");
        }

        return pairs;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidDataException($"'{TablePath}': {message}.");
    }
}
