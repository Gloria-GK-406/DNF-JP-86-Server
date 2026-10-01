using Dfo.Pvf.Schema;
using System.Collections.Concurrent;
using Dfo.Pvf;
using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

/// <summary>
/// Reads the archive's clear-reward rate table, <c>etc/itemdropinfo_clearreward.etc</c>.
/// </summary>
/// <remarks>
/// Parsed once per file and held: it is small, it is read on every clear, and nothing rewrites it
/// while the process runs.
/// <para>
/// It is a separate reader from <c>PvfDropRateTableSource</c> rather than another path handed to
/// it, because the two files disagree about <c>[drop prob]</c>. There a band is two level bounds
/// followed by five rates, a gold rate and one per item slot; here the section is a run of
/// <b>named profiles</b>, each holding <c>[drop prob count]</c> bands of two bounds and a single
/// rate.
/// </para>
/// <para>
/// Handed this path, the sibling reader refuses rather than answers -- but not by anything that
/// noticed the shape. Measured, it throws <c>InvalidDataException</c> reading
/// <c>declares 5 numbers under '[dungeon difficulty drop bonusrate]', which is not a whole number
/// of groups for its 16 rates</c>, and that <b>16</b> is the misreading rather than the
/// complaint: 72 integers over the 4 bands this file declares leave bands of eighteen, and the
/// sixteen numbers after each pair of level bounds are taken for sixteen item-slot rates with
/// nothing said. What stops it is the difficulty bonus table's five numbers failing to divide
/// into the sixteen groups that misreading implies -- a section it read correctly, refusing on
/// behalf of one it did not. Worth having, and also the measure of how far a wrong shape travels
/// before anything objects.
/// </para>
/// <para>
/// Where a borrowed reading does pass in silence is <c>[item drop ref table]</c>: taking the
/// monster table's grade bands in place of this file's opens one extra item grade at levels 80 to
/// 89 and reports nothing at all. That one is held down by a test rather than by an exception --
/// see the remarks on <c>ReadGradeBands</c>.
/// </para>
/// <para>
/// Everything is checked against a count the file itself declares rather than transcribed: the
/// band count fixes how wide a band is, a band's width less its two level bounds has to leave
/// exactly one rate, and the pair and triplet tables have to divide evenly. A file whose shape is
/// not this one fails here by name.
/// </para>
/// <para>
/// Nothing is scaled or combined. These integers are numerators over a basis the archive never
/// states, so dividing here would be handing on an assumption dressed as a reading; the
/// combination, and the assumption, belong to the rule service above.
/// </para>
/// </remarks>
internal sealed class PvfClearRewardRateSource(PvfArchiveProvider provider) : IClearRewardRateSource
{
    /// <summary>How many bands each named profile holds.</summary>
    private static readonly PvfField<int> BandCountSection = PvfField.Int32("[drop prob count]");

    /// <summary>The named profiles and their bands. The one section unlike the sibling family's.</summary>
    private static readonly PvfField<IReadOnlyList<PvfToken>> ProfileSection = PvfField.Tokens("[drop prob]", optional: true);

    /// <summary>Two numbers whose positions are not ascertained. Carried verbatim.</summary>
    private static readonly PvfField<IReadOnlyList<int>> DropKindSection = PvfField.Int32List("[drop kind prob]", optional: true);

    /// <summary>Four numbers, as many as a sibling band pays item rates. Carried verbatim.</summary>
    private static readonly PvfField<IReadOnlyList<int>> ItemTypeSection = PvfField.Int32List("[drop item type prob]", optional: true);

    /// <summary>One bonus per difficulty, indexed from zero. Its first entry is zero.</summary>
    private static readonly PvfField<IReadOnlyList<int>> DifficultySection = PvfField.Int32List("[dungeon difficulty drop bonusrate]", optional: true);

    /// <summary>The cumulative rarity thresholds and the basis that closes them.</summary>
    private static readonly PvfField<IReadOnlyList<int>> RaritySection = PvfField.Int32List("[basis of rarity dicision]", optional: true);

    /// <summary>
    /// <c>1.02 1.38 1.6 1.9 2</c> in A21: one multiplier per difficulty, the same five
    /// difficulties as <c>[dungeon difficulty drop bonusrate]</c>.
    /// <para>
    /// <b>Read since the card's gold became an archive reading.</b> It used to be recorded
    /// here as inapplicable, because this server paid a flat figure its owner had chosen and
    /// a multiplier with nothing to multiply would only have put an archive number beside a
    /// value that did not come from one. That is no longer the case: a free card's gold is
    /// now the level's base from <c>[gold drop ref table]</c> scaled by this entry, so the
    /// multiplier has its multiplicand.
    /// </para>
    /// </summary>
    private static readonly PvfField<IReadOnlyList<double>> DifficultyGoldSection = PvfField.Numbers("[dungeon difficulty gold drop bonusrate]", optional: true);

    /// <summary>One multiplier per party size, stored as singles.</summary>
    private static readonly PvfField<IReadOnlyList<double>> PartySection = PvfField.Numbers("[party member drop bonusrate]", optional: true);

    /// <summary>Map count and the bonus that count carries, in pairs.</summary>
    private static readonly PvfField<IReadOnlyList<int>> MapCountSection = PvfField.Int32List("[reward item rate per map max count]", optional: true);

    /// <summary>Level and what a paid flip costs at it, in pairs.</summary>
    private static readonly PvfField<IReadOnlyList<int>> CostSection = PvfField.Int32List("[gold card cost table]", optional: true);

    /// <summary>
    /// One number, stored as a float. <b>Read here and applied by nothing</b>: both faces are
    /// composed from the map-count table, the difficulty bonus, the dice and the threshold, and
    /// neither reads this. What it could be for, and what another server does with it, is on
    /// <see cref="ClearRewardTable.GoldCardCreateRate"/>.
    /// </summary>
    private static readonly PvfField<double> CreateRateSection = PvfField.Single("[gold card create rate]", optional: true);

    /// <summary>The paid flip's fallback rows: weight, item, quantity.</summary>
    private static readonly PvfField<IReadOnlyList<int>> GoldBlankItemSection = PvfField.Int32List("[gold card blank item]", optional: true);

    /// <summary>The PC-room flip's fallback rows, in the same three columns.</summary>
    private static readonly PvfField<IReadOnlyList<int>> PcRoomBlankItemSection = PvfField.Int32List("[pcroom card blank item]", optional: true);

    /// <summary>Level, how far below, how far above: the grades a level draws from.</summary>
    private static readonly PvfField<IReadOnlyList<int>> GradeBandSection = PvfField.Int32List("[item drop ref table]", optional: true);

    /// <summary>The two level bounds every band opens with.</summary>
    private const int BandLevelBoundCount = 2;

    /// <summary>A band of this file: two level bounds and one rate.</summary>
    private const int ProfileBandRateCount = 1;

    /// <summary>Key and value, which is how the cost and map-count tables are laid out.</summary>
    private const int PairWidth = 2;

    /// <summary>Weight, item, quantity.</summary>
    private const int BlankItemRowWidth = 3;

    /// <summary>Level, how far below, how far above.</summary>
    private const int GradeBandRowWidth = 3;

    private readonly ConcurrentDictionary<string, ClearRewardTable> _tables = new(
        StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The sections of this file that are deliberately not read, and why.
    /// </summary>
    /// <remarks>
    /// Named rather than skipped in silence. A section that no code mentions is
    /// indistinguishable from a section nobody noticed, and this file is the only source there
    /// is for the whole subject.
    /// <para>
    /// The first two are the ones whose purpose is still not ascertained -- and "not
    /// ascertained" now means looked for and not found, the A21 reference server included, rather
    /// than merely not read. A reading invented for them would become a rule nobody can trace.
    /// They are left here as names so that whoever ascertains one knows where it goes.
    /// </para>
    /// <para>
    /// <c>[gold card create rate]</c> used to stand here as a third. The reference server reads it
    /// -- as the paid face's own scale -- so it is read here now;
    /// see <see cref="ClearRewardTable.GoldCardCreateRate"/>. What it cost to leave it here was
    /// that "unascertained" and "not looked for" are written the same way and are not the same
    /// thing.
    /// </para>
    /// </remarks>
    internal static class UnreadSections
    {
        /// <summary>
        /// <c>2 13 17 0 -5</c> in A21. Five numbers, no declared column meanings, and the only
        /// negative number anywhere in the file. Purpose not ascertained; not read.
        /// </summary>
        internal static readonly PvfField<IReadOnlyList<PvfToken>> RarityControl = PvfField.Tokens("[item drop rarity control]", optional: true);

        /// <summary>
        /// <c>0 50 1 75 2 100</c> in A21. Three pairs, and what the first of each indexes is
        /// not ascertained -- a set count, a tier, a slot, all fit the numbers equally well.
        /// Nothing on the card path asks for it; not read.
        /// </summary>
        internal static readonly PvfField<IReadOnlyList<PvfToken>> ChronicleSetRewardRate = PvfField.Tokens("[chronicle set reward rate]", optional: true);

    }

    public ClearRewardTable Load(string archivePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        return _tables.GetOrAdd(archivePath, this.Read);
    }

    private ClearRewardTable Read(string archivePath)
    {
        var archive = provider.Archive;
        if (archive.FindFile(archivePath) < 0)
            throw new InvalidDataException($"The archive declares no '{archivePath}'.");

        var sections = ReadSections(archive, archivePath);
        var declaredBands = Single(sections, BandCountSection, archivePath);

        return new ClearRewardTable(
            archivePath,
            ReadProfiles(archive, sections, declaredBands, archivePath),
            Integers(sections, DropKindSection),
            Integers(sections, ItemTypeSection),
            Integers(sections, DifficultySection),
            Reals(sections, DifficultyGoldSection),
            Reals(sections, PartySection),
            Pairs(sections, MapCountSection, archivePath),
            ReadLadder(sections, archivePath),
            Pairs(sections, CostSection, archivePath),
            FirstReal(sections, CreateRateSection),
            ReadBlankItems(sections, GoldBlankItemSection, archivePath),
            ReadBlankItems(sections, PcRoomBlankItemSection, archivePath),
            ReadGradeBands(sections, archivePath));
    }

    /// <summary>
    /// The named profiles of <c>[drop prob]</c>, in declaration order.
    /// </summary>
    /// <remarks>
    /// A string token opens a profile and the integers after it are its bands, so the section
    /// splits where the archive says it splits rather than at a stride this reader picked.
    /// <para>
    /// Three of A21's six names are two words. Each arrives as one quoted string with the space
    /// inside it, but nothing in the token format requires that, so consecutive string tokens
    /// before the first number are joined into one name -- which is the same rule stated in the
    /// form that does not depend on how the archive happened to quote it.
    /// </para>
    /// <para>
    /// Numbers arriving before any name are refused rather than collected into an anonymous
    /// profile. That is precisely what the three siblings declaring this section look like, and
    /// reading one of
    /// them here would produce bands that divide evenly and mean nothing.
    /// </para>
    /// </remarks>
    private IReadOnlyList<ClearRewardProfile> ReadProfiles(
        PvfArchive archive,
        IReadOnlyDictionary<string, List<PvfToken>> sections,
        int declaredBands,
        string archivePath)
    {
        if (declaredBands <= 0)
        {
            throw new InvalidDataException(
                $"'{archivePath}' declares {declaredBands} bands to a profile under "
                    + $"'{BandCountSection}'.");
        }

        if (!sections.TryGetValue(ProfileSection.Name, out var tokens) || tokens.Count == 0)
            throw new InvalidDataException($"'{archivePath}' declares no '{ProfileSection}'.");

        var profiles = new List<ClearRewardProfile>();
        string? name = null;
        var values = new List<int>();

        foreach (var token in tokens)
        {
            if (token.IsString)
            {
                if (values.Count > 0)
                {
                    profiles.Add(CloseProfile(name, values, declaredBands, archivePath));
                    name = null;
                    values.Clear();
                }

                var word = archive.ResolveString(token.Value);
                name = name is null ? word : $"{name} {word}";
                continue;
            }

            if (token.Kind == PvfTokenKind.Int32)
                values.Add(token.Value);
        }

        if (values.Count > 0)
            profiles.Add(CloseProfile(name, values, declaredBands, archivePath));

        return profiles.Count > 0
            ? profiles
            : throw new InvalidDataException(
                $"'{archivePath}' declares '{ProfileSection}' with no profile in it.");
    }

    /// <summary>One profile's collected numbers, cut into the bands the file declared.</summary>
    private ClearRewardProfile CloseProfile(
        string? name, List<int> values, int declaredBands, string archivePath)
    {
        if (name is null)
        {
            throw new InvalidDataException(
                $"'{archivePath}' declares {values.Count} numbers under '{ProfileSection}' before "
                    + "naming any profile, which is the sibling rate tables' shape and not this "
                    + "one.");
        }

        if (values.Count % declaredBands != 0)
        {
            throw new InvalidDataException(
                $"'{archivePath}' gives profile '{name}' {values.Count} numbers under "
                    + $"'{ProfileSection}', which is not a whole number of bands for the "
                    + $"{declaredBands} it declares.");
        }

        var width = values.Count / declaredBands;
        if (width - BandLevelBoundCount != ProfileBandRateCount)
        {
            throw new InvalidDataException(
                $"'{archivePath}' gives profile '{name}' bands of {width} numbers under "
                    + $"'{ProfileSection}', which is not two level bounds and one rate.");
        }

        var bands = new List<ClearRewardBand>(declaredBands);
        for (var at = 0; at < values.Count; at += width)
            bands.Add(new ClearRewardBand(values[at], values[at + 1], values[at + 2]));

        return new ClearRewardProfile(name, bands);
    }

    /// <summary>
    /// The rarity ladder of <c>[basis of rarity dicision]</c>.
    /// </summary>
    /// <remarks>
    /// One group of seven here. The three siblings that declare bands of parallel rates each
    /// declare four such groups, one per item slot, and the abyss table declares a leading count
    /// of two followed by two groups. Why this file declares a single group and no count is not
    /// ascertained; what is checked is
    /// the archive's own invariant, that the group's last number is one more than the one before
    /// it. That last number is the basis, and reading it rather than assuming a round million is
    /// the point: see <see cref="RarityLadder"/> for what assuming it costs.
    /// </remarks>
    private RarityLadder? ReadLadder(
        IReadOnlyDictionary<string, List<PvfToken>> sections, string archivePath)
    {
        var values = Integers(sections, RaritySection);
        if (values.Count == 0)
            return null;

        if (values.Count < 2 || values[^1] != values[^2] + 1)
        {
            throw new InvalidDataException(
                $"'{archivePath}' declares {values.Count} numbers under '{RaritySection}' whose "
                    + "last does not close the one before it, so no basis can be read from them.");
        }

        return new RarityLadder(values);
    }

    /// <summary>A weighted fallback table, as weight/item/quantity rows.</summary>
    private IReadOnlyList<ClearRewardBlankItem> ReadBlankItems(
        IReadOnlyDictionary<string, List<PvfToken>> sections, PvfField<IReadOnlyList<int>> section, string archivePath)
    {
        var values = Integers(sections, section);
        if (values.Count == 0)
            return [];

        if (values.Count % BlankItemRowWidth != 0)
        {
            throw new InvalidDataException(
                $"'{archivePath}' declares {values.Count} numbers under '{section}', which is not "
                    + "a whole number of weight/item/quantity rows.");
        }

        var rows = new List<ClearRewardBlankItem>(values.Count / BlankItemRowWidth);
        for (var at = 0; at < values.Count; at += BlankItemRowWidth)
            rows.Add(new ClearRewardBlankItem(values[at], values[at + 1], values[at + 2]));
        return rows;
    }

    /// <summary>
    /// The grades each level draws from, out of <c>[item drop ref table]</c>.
    /// </summary>
    /// <remarks>
    /// Rows of level, how far below, how far above -- the same three columns the sibling family
    /// uses, and this file declares its own rather than sharing theirs.
    /// <para>
    /// <b>This file's rows are not the sibling family's rows.</b> Compared row by row against
    /// <c>etc/itemdropinfo_monseter.etc</c> across all two hundred levels: the <c>below</c> column
    /// is identical everywhere, and the <c>above</c> column differs at exactly ten levels, 80 to
    /// 89, where this file says three and the monster table says four. Every other level agrees.
    /// A reader that borrowed the sibling's band would quietly open one extra grade to flips in
    /// that ten-level window -- which is where A21's endgame sits -- with nothing anywhere to say
    /// it had.
    /// </para>
    /// <para>
    /// The lower edge is clamped at zero defensively, not because this archive needs it: across
    /// all two hundred rows the smallest <c>level - below</c> is one (level one declares zero
    /// below, not three), so the clamp never fires here. It is kept so that an archive whose rows
    /// did reach under grade zero could not produce a span no item can be in.
    /// </para>
    /// </remarks>
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
            bands[values[at]] = new ItemGradeBand(
                Math.Max(0, values[at] - values[at + 1]),
                values[at] + values[at + 2]);
        }

        return bands;
    }

    /// <summary>A section laid out as key/value pairs, as a lookup.</summary>
    /// <remarks>
    /// A repeated key keeps the last row, which is the archive's own last word on it. Neither of
    /// the two sections read this way repeats one in A21.
    /// </remarks>
    private IReadOnlyDictionary<int, int> Pairs(
        IReadOnlyDictionary<string, List<PvfToken>> sections, PvfField<IReadOnlyList<int>> section, string archivePath)
    {
        var values = Integers(sections, section);
        if (values.Count == 0)
            return new Dictionary<int, int>();

        if (values.Count % PairWidth != 0)
        {
            throw new InvalidDataException(
                $"'{archivePath}' declares {values.Count} numbers under '{section}', which is not "
                    + "a whole number of pairs.");
        }

        var pairs = new Dictionary<int, int>(values.Count / PairWidth);
        for (var at = 0; at < values.Count; at += PairWidth)
            pairs[values[at]] = values[at + 1];
        return pairs;
    }

    /// <summary>
    /// Every section of the file, each as the run of value tokens that followed its tag.
    /// </summary>
    /// <remarks>
    /// Collected by name rather than scanned for, so a section this reading does not know cannot
    /// arrive under a heading it does know. Quoted strings are kept alongside the numbers, because
    /// in this file they are the profile names and dropping them would leave one anonymous run of
    /// integers -- the very shape this reader refuses.
    /// <para>
    /// A repeated tag appends. Only the closing <c>[/…]</c> markers repeat in these files, and
    /// those carry no values.
    /// </para>
    /// </remarks>
    private Dictionary<string, List<PvfToken>> ReadSections(
        PvfArchive archive, string archivePath)
    {
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
    /// Both numeric forms, because this file mixes them: its party multipliers are singles and
    /// its difficulty bonuses are plain integers. A reader that took only one form would come
    /// away with an empty table, and an empty multiplier table is a parse failure wearing the
    /// costume of a multiplier of one.
    /// </remarks>
    private List<double> Reals(
        IReadOnlyDictionary<string, List<PvfToken>> sections, PvfField<IReadOnlyList<double>> section) =>
        sections.TryGetValue(section.Name, out var tokens)
            ? [.. section.Decode(provider.Archive, tokens, section.Name)] : [];

    private double? FirstReal(
        IReadOnlyDictionary<string, List<PvfToken>> sections, PvfField<double> section) =>
        sections.TryGetValue(section.Name, out var tokens)
            ? section.Decode(provider.Archive, tokens, section.Name) : null;

    private int Single(
        IReadOnlyDictionary<string, List<PvfToken>> sections, PvfField<int> section, string archivePath) =>
        sections.TryGetValue(section.Name, out var tokens)
            ? section.Decode(provider.Archive, tokens, archivePath)
            : throw new InvalidDataException($"'{archivePath}' declares no '{section}'.");
}
