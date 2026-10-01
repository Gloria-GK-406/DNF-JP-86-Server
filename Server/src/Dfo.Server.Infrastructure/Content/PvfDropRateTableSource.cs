using Dfo.Pvf.Schema;
using System.Collections.Concurrent;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

/// <summary>
/// Reads one of the archive's <c>itemdropinfo_*.etc</c> rate tables.
/// </summary>
/// <remarks>
/// Parsed once per file and held: these are small, read on every kill, and rewritten by nothing
/// while the process runs.
/// <para>
/// The reading is held together by counts rather than by transcription. <c>[drop prob count]</c>
/// says how many bands the file declares, which fixes how wide a band is; a band's width less its
/// two level bounds fixes how many rates it pays; and that rate count is then what the three bonus
/// tables are split into. Every one of those is checked, so a file whose shape is not this one
/// fails here by name instead of arriving as a table of plausible wrong numbers.
/// </para>
/// </remarks>
internal sealed class PvfDropRateTableSource(PvfArchiveProvider provider)
    : IDropRateTableSource, IGoldDropTableSource
{
    private static readonly PvfField<int> BandCountSection = PvfField.Int32("[drop prob count]");
    private static readonly PvfField<IReadOnlyList<int>> BandSection = PvfField.Int32List("[drop prob]", optional: true);
    private static readonly PvfField<IReadOnlyList<int>> RaritySection = PvfField.Int32List("[basis of rarity dicision]", optional: true);
    private static readonly PvfField<IReadOnlyList<double>> MonsterRankSection = PvfField.Numbers("[monster type drop bonusrate]", optional: true);
    private static readonly PvfField<IReadOnlyList<double>> DifficultySection = PvfField.Numbers("[dungeon difficulty drop bonusrate]", optional: true);
    private static readonly PvfField<IReadOnlyList<double>> PartySection = PvfField.Numbers("[party member drop bonusrate]", optional: true);
    private static readonly PvfField<IReadOnlyList<int>> GradeBandSection = PvfField.Int32List("[item drop ref table]", optional: true);

    /// <summary>Where the whole world's per-level gold amounts live.</summary>
    private const string CommonPath = "etc/itemdropinfo_common.etc";
    private static readonly PvfField<IReadOnlyList<int>> GoldBaseSection = PvfField.Int32List("[gold drop ref table]", optional: true);
    private static readonly PvfField<IReadOnlyList<int>> GoldQuantitySection = PvfField.Int32List("[gold quantity]", optional: true);
    private static readonly PvfField<IReadOnlyList<int>> GoldVolumeSection = PvfField.Int32List("[gold volume]", optional: true);

    /// <summary>
    /// Level, the amount one drop of gold is worth there, and how far either way that amount may
    /// move as a percentage. All three are read; the third is banded rather than constant.
    /// </summary>
    private const int GoldBaseRowWidth = 3;

    /// <summary>The two level bounds every band opens with.</summary>
    private const int BandLevelBoundCount = 2;

    /// <summary>Level, how far below, how far above.</summary>
    private const int GradeBandRowWidth = 3;

    private readonly ConcurrentDictionary<string, DropRateTable> _tables = new(
        StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, GoldDropTable> _goldTables = new(
        StringComparer.OrdinalIgnoreCase);

    public DropRateTable Load(string archivePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        return _tables.GetOrAdd(archivePath, this.Read);
    }

    GoldDropTable IGoldDropTableSource.Load(string archivePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        return _goldTables.GetOrAdd(archivePath, this.ReadGold);
    }

    /// <summary>
    /// What a kill pays in gold, from the two files that between them say so.
    /// </summary>
    /// <remarks>
    /// The per-level amounts are the world's and live in the common file; the rank multiples and
    /// the variation band belong to whichever table is paying, and so are read from it. A rate
    /// file that declares neither leaves both at their neutral values rather than borrowing the
    /// monster table's.
    /// </remarks>
    private GoldDropTable ReadGold(string archivePath)
    {
        var common = this.ReadSections(CommonPath);
        var baseValues = Integers(common, GoldBaseSection);
        if (baseValues.Count == 0 || baseValues.Count % GoldBaseRowWidth != 0)
        {
            throw new InvalidDataException(
                $"'{CommonPath}' declares {baseValues.Count} gold numbers, which is not a whole "
                    + "number of rows.");
        }

        var amounts = new Dictionary<int, int>(baseValues.Count / GoldBaseRowWidth);
        var variances = new Dictionary<int, int>(baseValues.Count / GoldBaseRowWidth);
        for (var at = 0; at < baseValues.Count; at += GoldBaseRowWidth)
        {
            amounts[baseValues[at]] = baseValues[at + 1];
            variances[baseValues[at]] = baseValues[at + 2];
        }

        var own = this.ReadSections(archivePath);
        var quantities = Integers(own, GoldQuantitySection);
        var volume = Integers(own, GoldVolumeSection);

        // A band of exactly one hundred to one hundred is no variation at all, which is the
        // honest reading of a file that declares none -- not a band invented around the base.
        var low = volume.Count > 0 ? volume[0] : 100;
        var high = volume.Count > 1 ? volume[1] : low;

        return new GoldDropTable(
            amounts,
            variances,
            quantities.Count > 0 ? quantities : [1],
            (low, Math.Max(low, high)));
    }

    private DropRateTable Read(string archivePath)
    {
        var sections = this.ReadSections(archivePath);

        var declaredBands = Single(sections, BandCountSection, archivePath);
        var bandValues = Integers(sections, BandSection);
        if (declaredBands <= 0 || bandValues.Count == 0 || bandValues.Count % declaredBands != 0)
        {
            throw new InvalidDataException(
                $"'{archivePath}' declares {declaredBands} rate bands but "
                    + $"{bandValues.Count} numbers to build them from.");
        }

        var bandWidth = bandValues.Count / declaredBands;
        var rateCount = bandWidth - BandLevelBoundCount;
        if (rateCount <= 0)
        {
            throw new InvalidDataException(
                $"'{archivePath}' declares bands of {bandWidth} numbers, which leaves no rates "
                    + "once its two level bounds are taken off.");
        }

        var bands = new List<DropRateBand>(declaredBands);
        for (var at = 0; at < bandValues.Count; at += bandWidth)
        {
            bands.Add(new DropRateBand(
                bandValues[at],
                bandValues[at + 1],
                bandValues.GetRange(at + BandLevelBoundCount, rateCount)));
        }

        return new DropRateTable(
            archivePath,
            bands,
            ReadLadders(sections, archivePath),
            // Split into as many groups as a band has rates, which is what makes "one group to a
            // rate" a checked reading rather than a transcribed one.
            Groups(sections, MonsterRankSection, rateCount, archivePath),
            Groups(sections, DifficultySection, rateCount, archivePath),
            Groups(sections, PartySection, rateCount, archivePath),
            ReadGradeBands(sections, archivePath));
    }

    /// <summary>
    /// The rarity ladders, as groups of equal width closed by their own basis.
    /// </summary>
    /// <remarks>
    /// The group width is recovered rather than assumed, because the archive does not declare it
    /// and the number of bands differs between A21 and what the community documentation describes.
    /// What identifies a group is the archive's own invariant: a group's last number is one more
    /// than the number before it. The narrowest width that makes every group satisfy that is the
    /// width, and a file where no width does is a file this cannot read.
    /// <para>
    /// The abyss table is the one file that breaks the invariant, and it also declares a leading
    /// group count that none of the other four do. Neither is handled here: it is a different
    /// shape and belongs to a different reading.
    /// </para>
    /// </remarks>
    private IReadOnlyList<RarityLadder> ReadLadders(
        IReadOnlyDictionary<string, List<PvfToken>> sections, string archivePath)
    {
        var values = Integers(sections, RaritySection);
        if (values.Count == 0)
            return [];

        for (var width = 2; width <= values.Count; width++)
        {
            if (values.Count % width != 0)
                continue;

            var closes = true;
            for (var at = 0; at < values.Count && closes; at += width)
                closes = values[at + width - 1] == values[at + width - 2] + 1;

            if (!closes)
                continue;

            var ladders = new List<RarityLadder>(values.Count / width);
            for (var at = 0; at < values.Count; at += width)
                ladders.Add(new RarityLadder(values.GetRange(at, width)));
            return ladders;
        }

        throw new InvalidDataException(
            $"'{archivePath}' declares {values.Count} rarity numbers that do not divide into "
                + "groups whose last number closes the one before it.");
    }

    private IReadOnlyDictionary<int, ItemGradeBand> ReadGradeBands(
        IReadOnlyDictionary<string, List<PvfToken>> sections, string archivePath)
    {
        var values = Integers(sections, GradeBandSection);
        if (values.Count == 0)
            return new Dictionary<int, ItemGradeBand>();

        if (values.Count % GradeBandRowWidth != 0)
        {
            throw new InvalidDataException(
                $"'{archivePath}' declares {values.Count} grade band numbers, which is not a whole "
                    + "number of level/below/above rows.");
        }

        var bands = new Dictionary<int, ItemGradeBand>(values.Count / GradeBandRowWidth);
        for (var at = 0; at < values.Count; at += GradeBandRowWidth)
        {
            // The lower edge is clamped at zero rather than allowed to go negative: level one
            // declares a reach of three below it, and a band that started below grade zero would
            // be a span no item can be in rather than the smallest one that exists.
            bands[values[at]] = new ItemGradeBand(
                Math.Max(0, values[at] - values[at + 1]),
                values[at] + values[at + 2]);
        }

        return bands;
    }

    /// <summary>One bonus table, split into one group per rate.</summary>
    private IReadOnlyList<IReadOnlyList<double>> Groups(
        IReadOnlyDictionary<string, List<PvfToken>> sections,
        PvfField<IReadOnlyList<double>> section,
        int groupCount,
        string archivePath)
    {
        var values = Reals(sections, section);
        if (values.Count == 0)
            return [];

        if (values.Count % groupCount != 0)
        {
            throw new InvalidDataException(
                $"'{archivePath}' declares {values.Count} numbers under '{section}', which is not "
                    + $"a whole number of groups for its {groupCount} rates.");
        }

        var width = values.Count / groupCount;
        var groups = new List<IReadOnlyList<double>>(groupCount);
        for (var at = 0; at < values.Count; at += width)
            groups.Add(values.GetRange(at, width));
        return groups;
    }

    /// <summary>
    /// Every section of the file, each as the run of value tokens that followed its tag.
    /// </summary>
    /// <remarks>
    /// Collected by name rather than scanned for, so a section this reading does not know cannot
    /// arrive under a heading it does know -- which is the whole failure mode of a single forward
    /// pass over a file with a dozen numeric tables in it.
    /// <para>
    /// A repeated tag appends. Only the closing <c>[/…]</c> markers repeat in these files, and
    /// those carry no values.
    /// </para>
    /// </remarks>
    private Dictionary<string, List<PvfToken>> ReadSections(string archivePath)
    {
        var archive = provider.Archive;
        if (archive.FindFile(archivePath) < 0)
            throw new InvalidDataException($"The archive declares no '{archivePath}'.");

        var sections = new Dictionary<string, List<PvfToken>>(StringComparer.OrdinalIgnoreCase);
        List<PvfToken>? current = null;
        var reader = archive.OpenScript(archivePath);
        while (reader.Read(out var token))
        {
            if (token.Kind == PvfTokenKind.Identifier)
            {
                var name = archive.ResolveString(token.Value);
                if (!sections.TryGetValue(name, out current))
                    sections[name] = current = [];
                continue;
            }

            current?.Add(token);
        }

        return sections;
    }

    private List<int> Integers(
        IReadOnlyDictionary<string, List<PvfToken>> sections, PvfField<IReadOnlyList<int>> section) =>
        sections.TryGetValue(section.Name, out var tokens)
            ? [.. section.Decode(provider.Archive, tokens, section.Name)] : [];

    /// <remarks>
    /// Both numeric forms, because the files disagree: the monster table stores its multipliers as
    /// singles and the clear-reward table stores its difficulty group as plain integers. A reader
    /// that took only one would come away with an empty table, and an empty multiplier table is a
    /// parse failure wearing the costume of a multiplier of one.
    /// </remarks>
    private List<double> Reals(
        IReadOnlyDictionary<string, List<PvfToken>> sections, PvfField<IReadOnlyList<double>> section) =>
        sections.TryGetValue(section.Name, out var tokens)
            ? [.. section.Decode(provider.Archive, tokens, section.Name)] : [];

    private int Single(
        IReadOnlyDictionary<string, List<PvfToken>> sections, PvfField<int> section, string archivePath) =>
        sections.TryGetValue(section.Name, out var tokens)
            ? section.Decode(provider.Archive, tokens, archivePath)
            : throw new InvalidDataException($"'{archivePath}' declares no '{section}'.");
}
