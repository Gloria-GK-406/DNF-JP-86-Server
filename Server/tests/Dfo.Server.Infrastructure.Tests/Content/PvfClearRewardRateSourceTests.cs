using Dfo.Pvf.Tokens;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

/// <summary>
/// The clear-reward rate table, against the archive that declares it.
/// </summary>
/// <remarks>
/// Every number asserted here is transcribed from <c>etc/itemdropinfo_clearreward.etc</c> as the
/// archive holds it, not taken from what this server's reading produced. That distinction is the
/// whole point of this file: the card flip has no second source anywhere -- no capture, no
/// reference server output -- so a reading that is wrong and self-consistent would look exactly
/// like a correct one.
/// <para>
/// <c>[drop kind prob]</c> and <c>[drop item type prob]</c> are asserted as lists rather than as
/// named quantities, because what their positions mean is not ascertained. Pinning a meaning here
/// would make a later identification look like a regression.
/// </para>
/// </remarks>
public sealed class PvfClearRewardRateSourceTests
{
    /// <summary>The band the endgame is fought in, and the level the cost table is sampled at.</summary>
    private const int EndgameLevel = 86;

    /// <summary>
    /// The abyss table. It has no <see cref="DropRateTables"/> constant because nothing reads it;
    /// it is named here because the remarks in this subject keep making claims about it.
    /// </summary>
    private const string AbyssTable = "etc/itemdropinfo_monster_hell.etc";

    /// <summary>The family file holding the world's per-level gold amounts.</summary>
    private const string CommonTable = "etc/itemdropinfo_common.etc";

    /// <summary>The family's control file.</summary>
    private const string ControlTable = "etc/(r)itemdropinfo_control.etc";

    /// <summary>The section whose shape this file does not share with its siblings.</summary>
    private const string DropProbTag = "[drop prob]";

    /// <summary>What the abyss table declares where the others declare <see cref="DropProbTag"/>.</summary>
    private const string DifficultyDropProbTag = "[dungeon difficulty drop prob]";

    /// <summary>How many bands a profile holds, or a sibling's table declares.</summary>
    private const string BandCountTag = "[drop prob count]";

    /// <summary>The rarity thresholds, spelled with the archive's own typo.</summary>
    private const string RarityTag = "[basis of rarity dicision]";

    /// <summary>Six cumulative thresholds and the basis that closes them.</summary>
    private const int RarityGroupWidth = 7;

    /// <summary>Two level bounds and five rates.</summary>
    private const int SiblingBandWidth = 7;

    /// <summary>A gold rate and one per item slot.</summary>
    private const int SiblingBandRateCount = 5;

    /// <summary>The two level bounds every band of either shape opens with.</summary>
    private const int ClearRewardBandLevelBoundCount = 2;

    /// <summary>What this file's <c>[drop prob count]</c> announces.</summary>
    private const int ClearRewardBandsPerProfile = 4;

    /// <summary>The levels both grade band tables cover.</summary>
    private const int GradeBandLevelCount = 200;

    /// <summary>What a path has to hold to be a file of this family.</summary>
    private const string FamilyNeedle = "itemdropinfo";

    /// <summary>
    /// Every file of the <c>itemdropinfo</c> family except the clear-reward table itself.
    /// </summary>
    /// <remarks>
    /// Six of them, so a claim of the form "no other file of the family declares this" is a claim
    /// about these six and can be counted rather than believed -- and that this list is the whole
    /// family is itself counted, out of the archive's file table, by
    /// <see cref="The_family_is_the_seven_files_the_archives_own_file_table_holds"/>.
    /// </remarks>
    private static readonly string[] Siblings =
    [
        DropRateTables.Monster,
        DropRateTables.MonsterExtra,
        DropRateTables.BreakableObject,
        AbyssTable,
        CommonTable,
        ControlTable,
    ];

    private static ClearRewardTable Table(PvfArchiveProvider provider) =>
        new PvfClearRewardRateSource(provider).Load(DropRateTables.ClearReward);

    /// <summary>
    /// The six named profiles, and every rate each of them declares.
    /// </summary>
    /// <remarks>
    /// This is the section whose shape differs from the rest of the family: the monster table
    /// declares bands of five parallel rates, and this one declares named profiles of four bands
    /// paying one rate each. Three of the six names are two words, so a reader that took a profile
    /// name to be a single word would fold <c>pcroom default</c>'s rates into a profile called
    /// <c>pcroom</c> and lose the two profiles behind it.
    /// </remarks>
    [SkippableFact]
    public void The_six_named_profiles_pay_the_rates_the_archive_declares()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();

        var table = Table(provider);

        Assert.Equal(
            [
                ClearRewardProfiles.Default,
                ClearRewardProfiles.Event,
                ClearRewardProfiles.SecondEvent,
                ClearRewardProfiles.PcRoomDefault,
                ClearRewardProfiles.PcRoomBonus,
                ClearRewardProfiles.PcRoomEvent,
            ],
            table.Profiles.Select(profile => profile.Name));

        // [drop prob count] says four, and every profile holds exactly that many bands.
        Assert.All(table.Profiles, profile => Assert.Equal(4, profile.Bands.Count));

        // The level bounds are the same four in every profile: 1-15, 16-30, 31-40, 41-200.
        Assert.All(table.Profiles, profile => Assert.Equal(
            [(1, 15), (16, 30), (31, 40), (41, 200)],
            profile.Bands.Select(band => (band.LowLevel, band.HighLevel))));

        // And the rates, profile by profile, transcribed.
        Assert.Equal([10000, 10000, 10000, 10000], Rates(table, ClearRewardProfiles.Default));
        Assert.Equal([9000, 9000, 9000, 9000], Rates(table, ClearRewardProfiles.Event));
        Assert.Equal([9000, 9000, 9000, 9000], Rates(table, ClearRewardProfiles.SecondEvent));
        Assert.Equal([10000, 10000, 10000, 10000], Rates(table, ClearRewardProfiles.PcRoomDefault));
        Assert.Equal([9000, 9000, 9000, 9000], Rates(table, ClearRewardProfiles.PcRoomBonus));

        // The only profile whose four bands are not all one number. It is what shows the bands
        // were taken per profile rather than copied from the first one.
        Assert.Equal([9000, 8000, 8000, 8000], Rates(table, ClearRewardProfiles.PcRoomEvent));
    }

    /// <summary>A level is answered out of the profile that was asked for.</summary>
    [SkippableFact]
    public void A_level_lands_in_the_band_of_the_profile_it_was_asked_of()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();

        var table = Table(provider);

        Assert.Equal(
            10000, table.ProfileNamed(ClearRewardProfiles.Default)?.BandFor(EndgameLevel)?.Rate);
        Assert.Equal(
            8000, table.ProfileNamed(ClearRewardProfiles.PcRoomEvent)?.BandFor(EndgameLevel)?.Rate);
        Assert.Equal(9000, table.ProfileNamed(ClearRewardProfiles.PcRoomEvent)?.BandFor(15)?.Rate);
        Assert.Equal(8000, table.ProfileNamed(ClearRewardProfiles.PcRoomEvent)?.BandFor(30)?.Rate);

        // A level outside every band, and a profile the file does not declare, are both ordinary
        // answers rather than a substituted neighbour.
        Assert.Null(table.ProfileNamed(ClearRewardProfiles.Default)?.BandFor(201));
        Assert.Null(table.ProfileNamed("pcroom"));
    }

    /// <summary>
    /// The three bonus tables, transcribed.
    /// </summary>
    /// <remarks>
    /// The difficulty table's first entry is zero, and that zero is what RQ-008 rests on: the
    /// lowest difficulty adds nothing at all, so nothing can be on a card there. A reading that
    /// answered an unlisted difficulty with a neutral one would turn that zero into a full-rate
    /// card the moment an index slipped.
    /// </remarks>
    [SkippableFact]
    public void The_bonus_tables_are_the_numbers_the_archive_declares()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();

        var table = Table(provider);

        // [dungeon difficulty drop bonusrate] 0 600 1000 1400 1200 -- and it is not monotonic:
        // the fifth difficulty adds less than the fourth.
        Assert.Equal([0, 600, 1000, 1400, 1200], table.DifficultyRates);
        Assert.Equal(0, table.DifficultyRate(0));
        Assert.Equal(1400, table.DifficultyRate(3));
        Assert.Null(table.DifficultyRate(5));
        Assert.Null(table.DifficultyRate(-1));

        // [party member drop bonusrate] 1 1.2 1.4 1.6, stored as singles. Four decimal places,
        // because 1.2 comes back as 1.20000004 and a tighter comparison would be rejecting the
        // archive rather than the reading.
        Assert.Equal(4, table.PartyMemberRates.Count);
        Assert.Equal(1.0, table.PartyMemberRate(1)!.Value, 4);
        Assert.Equal(1.2, table.PartyMemberRate(2)!.Value, 4);
        Assert.Equal(1.4, table.PartyMemberRate(3)!.Value, 4);
        Assert.Equal(1.6, table.PartyMemberRate(4)!.Value, 4);
        Assert.Null(table.PartyMemberRate(5));
        Assert.Null(table.PartyMemberRate(0));

        // [reward item rate per map max count], read as count/rate pairs.
        Assert.Equal(5, table.MapCountRates.Count);
        Assert.Equal(2000, table.MapCountRate(1));
        Assert.Equal(3000, table.MapCountRate(2));
        Assert.Equal(4000, table.MapCountRate(3));
        Assert.Equal(5000, table.MapCountRate(4));
        Assert.Equal(6000, table.MapCountRate(5));
        Assert.Null(table.MapCountRate(6));
    }

    /// <summary>
    /// The paid-flip cost table, at both ends and everywhere between.
    /// </summary>
    /// <remarks>
    /// Two hundred level/price pairs in one arithmetic progression: a hundred at level one and a
    /// hundred and twenty more at every level after it. Checking the whole progression is what
    /// catches a reading that took the price out of the wrong half of a pair -- such a reading
    /// would report the level as the price and line up nowhere.
    /// </remarks>
    [SkippableFact]
    public void The_paid_flip_cost_table_is_what_the_archive_declares()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();

        var table = Table(provider);

        Assert.Equal(200, table.GoldCardCosts.Count);
        Assert.Equal(100, table.GoldCardCost(1));
        Assert.Equal(580, table.GoldCardCost(5));
        Assert.Equal(10300, table.GoldCardCost(EndgameLevel));
        Assert.Equal(23980, table.GoldCardCost(200));

        for (var level = 1; level <= 200; level++)
            Assert.Equal(100 + ((level - 1) * 120), table.GoldCardCost(level));

        // A level the table does not price is unpriced rather than free.
        Assert.Null(table.GoldCardCost(0));
        Assert.Null(table.GoldCardCost(201));
    }

    /// <summary>
    /// The rarity ladder, and the two top bands that must stay reachable.
    /// </summary>
    /// <remarks>
    /// This file declares one group of seven, where the three siblings that declare bands of
    /// parallel rates each declare four such groups and the abyss table declares a leading count
    /// of two followed by two groups. This file's group closes the way the monster table's do --
    /// its last number is one more than the one before it, so 1000003 is the basis and rolls run
    /// one to 1000002. That is not a family-wide invariant: neither of the abyss table's two
    /// groups satisfies it. RQ-009 is why this is asserted rather than assumed: a
    /// roll range hard-coded to a million would put the top two bands one and two rolls out of
    /// reach with nothing anywhere saying so.
    /// </remarks>
    [SkippableFact]
    public void The_rarity_ladder_is_the_group_the_archive_declares()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();

        var ladder = Table(provider).Rarity;

        Assert.NotNull(ladder);
        Assert.Equal([500000, 799900, 999900, 1000000, 1000001, 1000002, 1000003], ladder.Values);
        Assert.Equal(1000003, ladder.Basis);
        Assert.Equal(1000002, ladder.HighestRoll);
        Assert.Equal(6, ladder.BandCount);

        // Every declared band is reachable, the top two included.
        for (var band = 0; band < ladder.BandCount; band++)
            Assert.True(ladder.WeightOf(band) > 0, $"band {band} is unreachable");
    }

    /// <summary>
    /// The two sections whose reading is transcription only, left that way.
    /// </summary>
    /// <remarks>
    /// <c>[drop kind prob]</c> is two numbers whose positions this reading has not identified, and
    /// <c>[drop item type prob]</c> is four whose order is only assumed to parallel the family's
    /// four item rates. Both are asserted as the archive's own lists and nothing more.
    /// </remarks>
    [SkippableFact]
    public void The_kind_and_item_type_sections_are_transcribed_as_declared()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();

        var table = Table(provider);

        Assert.Equal([10000, 0], table.DropKindRates);
        Assert.Equal([0, 9500, 0, 500], table.ItemTypeRates);
    }

    /// <summary>
    /// The two fallback tables, as weight/item/quantity triplets.
    /// </summary>
    /// <remarks>
    /// All six paid rows name the same item -- 10002120, a 2013 event piece -- and differ only in
    /// weight and quantity. That repetition is what makes the triplet reading checkable: any other
    /// column order would scatter one repeated id across three different positions.
    /// </remarks>
    [SkippableFact]
    public void The_blank_card_tables_are_the_weighted_rows_the_archive_declares()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();

        var table = Table(provider);

        Assert.Equal(
            [
                new ClearRewardBlankItem(3000, 10002120, 1),
                new ClearRewardBlankItem(3000, 10002120, 2),
                new ClearRewardBlankItem(2000, 10002120, 3),
                new ClearRewardBlankItem(1000, 10002120, 4),
                new ClearRewardBlankItem(500, 10002120, 5),
                new ClearRewardBlankItem(500, 10002120, 10),
            ],
            table.GoldCardBlankItems);

        Assert.Equal([new ClearRewardBlankItem(50, 7454, 1)], table.PcRoomCardBlankItems);
    }

    /// <summary>
    /// The level to grade band table, which this file declares for itself.
    /// </summary>
    /// <remarks>
    /// Same three columns as the monster family -- level, how far below, how far above -- and the
    /// same invariant identifies the reading: from level 86 upwards the archive raises the first
    /// number in step with the level so the lower edge holds at grade 79, the floor of A21's
    /// endgame gear. It is not the monster table's copy: at the endgame level a card reaches one
    /// grade less far up than a monster kill does.
    /// </remarks>
    [SkippableFact]
    public void The_grade_bands_are_this_own_files_rows()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();

        var table = Table(provider);

        Assert.Equal(new ItemGradeBand(1, 4), table.GradeBandFor(1));
        Assert.Equal(new ItemGradeBand(2, 8), table.GradeBandFor(5));
        Assert.Equal(new ItemGradeBand(79, 89), table.GradeBandFor(EndgameLevel));
        for (var level = EndgameLevel; level <= 200; level++)
            Assert.Equal(79, table.GradeBandFor(level)?.Low);

        // A level no row covers has no band.
        Assert.Null(table.GradeBandFor(0));
        Assert.Null(table.GradeBandFor(201));
    }

    /// <summary>The same file asked for twice is parsed once.</summary>
    [SkippableFact]
    public void A_table_is_read_once_and_held()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfClearRewardRateSource(provider);

        Assert.Same(
            source.Load(DropRateTables.ClearReward),
            source.Load(DropRateTables.ClearReward));
    }

    /// <summary>A file the archive does not hold is named rather than answered emptily.</summary>
    [SkippableFact]
    public void A_missing_file_is_refused_by_name()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfClearRewardRateSource(provider);

        var thrown = Assert.Throws<InvalidDataException>(
            () => source.Load("etc/itemdropinfo_there_is_no_such_file.etc"));

        Assert.Contains(
            "itemdropinfo_there_is_no_such_file", thrown.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A rate table of the family's other shape is refused rather than read as this one.
    /// </summary>
    /// <remarks>
    /// The monster table's <c>[drop prob]</c> names no profiles at all: it is seventy bare
    /// integers. Read leniently that becomes one nameless profile of plausible-looking bands,
    /// which is precisely the failure this reading exists to avoid, so it is refused instead.
    /// </remarks>
    [SkippableFact]
    public void A_table_whose_bands_name_no_profile_is_refused()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfClearRewardRateSource(provider);

        var thrown = Assert.Throws<InvalidDataException>(
            () => source.Load(DropRateTables.Monster));

        Assert.Contains("[drop prob]", thrown.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The sections this reading deliberately skips are named, and the archive still has them.
    /// </summary>
    /// <remarks>
    /// The reader lists three sections it does not read and says why for each. A misspelled name
    /// in that list would be worse than no list: it would document a section that does not exist
    /// while the real one goes unmentioned. This checks the three names against the file's own
    /// tags, and checks that they are genuinely absent from the reading rather than absent from
    /// the file.
    /// <para>
    /// There were four until TASK-09 of this subject read the A21 reference server and found
    /// <c>[gold card create rate]</c> used there. It is read now, and is asserted from the other
    /// side by <see cref="The_paid_faces_own_rate_is_read_from_the_file"/> -- so a reading that
    /// quietly stopped reading it would fail there rather than pass here.
    /// </para>
    /// </remarks>
    [SkippableFact]
    public void The_sections_this_reading_skips_are_named_and_still_in_the_file()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();

        var archive = provider.Archive;
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var reader = archive.OpenScript(DropRateTables.ClearReward);
        while (reader.Read(out var token))
        {
            if (token.Kind == PvfTokenKind.Identifier)
                tags.Add(archive.ResolveString(token.Value));
        }

        Assert.Contains(PvfClearRewardRateSource.UnreadSections.RarityControl, tags);
        Assert.Contains(PvfClearRewardRateSource.UnreadSections.ChronicleSetRewardRate, tags);
        Assert.Contains(PvfClearRewardRateSource.UnreadSections.DifficultyGoldBonusRate, tags);

        // And the reading really does leave them out: none of the table's numeric members holds
        // the five numbers of [item drop rarity control] or the three pairs of
        // [chronicle set reward rate].
        var table = Table(provider);
        Assert.DoesNotContain(-5, table.DifficultyRates);
        Assert.DoesNotContain(-5, table.ItemTypeRates);
        Assert.DoesNotContain(-5, table.DropKindRates);
    }

    /// <summary>
    /// <b>The paid face's own rate is read from the file, and read as a real.</b>
    /// </summary>
    /// <remarks>
    /// <c>[gold card create rate]</c> is a single number stored as a float where every
    /// neighbouring rate in the file is an integer. Both halves are asserted, because a reader
    /// that took integers only would come away with nothing at all and could not tell that from a
    /// file which declares no such section -- and this server empties the paid face when it is
    /// null.
    /// </remarks>
    [SkippableFact]
    public void The_paid_faces_own_rate_is_read_from_the_file()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();

        Assert.Equal(1.0, Table(provider).GoldCardCreateRate);

        // Stored as a single, which is what makes the reader's choice of Reals load-bearing: the
        // sections either side of it in the file are plain integers.
        var archive = provider.Archive;
        var reader = archive.OpenScript(DropRateTables.ClearReward);
        var armed = false;
        var kind = default(PvfTokenKind?);
        while (reader.Read(out var token))
        {
            if (armed && token.Kind != PvfTokenKind.Identifier)
            {
                kind = token.Kind;
                break;
            }

            armed = token.Kind == PvfTokenKind.Identifier
                && archive.ResolveString(token.Value) == "[gold card create rate]";
        }

        Assert.Equal(PvfTokenKind.Single, kind);
    }

    /// <summary>
    /// This file's grade band rows against the monster table's, row for row.
    /// </summary>
    /// <remarks>
    /// The remarks on <c>PvfClearRewardRateSource.ReadGradeBands</c> assert something about a file
    /// that reader never opens: that the two tables agree on the <c>below</c> column at every one
    /// of the two hundred levels and part company on <c>above</c> at exactly ten of them, 80 to
    /// 89. Nothing checked that, and it was written wrongly twice before it was written correctly.
    /// <para>
    /// It is also the claim the decision to read this section at all rests on. Those ten levels
    /// are where A21's endgame sits, so a reader that borrowed the sibling's rows would open one
    /// extra item grade to every endgame flip and produce no error at all while doing it.
    /// </para>
    /// </remarks>
    [SkippableFact]
    public void The_grade_bands_share_the_monster_tables_below_column_and_differ_above_at_80_to_89()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();

        var card = Table(provider).GradeBands;
        var monster = new PvfDropRateTableSource(provider).Load(DropRateTables.Monster).GradeBands;

        // Both cover levels one to two hundred and nothing else, so the comparison below is of
        // the same rows rather than of whichever rows each file happens to hold.
        Assert.Equal(Enumerable.Range(1, GradeBandLevelCount), card.Keys.Order());
        Assert.Equal(Enumerable.Range(1, GradeBandLevelCount), monster.Keys.Order());

        // The archive writes a row as level, how far below, how far above; a band holds the two
        // edges those produce, so the columns come back by subtracting the level again.
        var aboveDiffers = new List<int>();
        for (var level = 1; level <= GradeBandLevelCount; level++)
        {
            Assert.True(
                card[level].Low == monster[level].Low,
                $"level {level}: this file reaches {level - card[level].Low} grades below and the "
                    + $"monster table {level - monster[level].Low}");

            if (card[level].High != monster[level].High)
                aboveDiffers.Add(level);
        }

        Assert.Equal(Enumerable.Range(80, 10), aboveDiffers);
        foreach (var level in aboveDiffers)
        {
            Assert.Equal(3, card[level].High - level);
            Assert.Equal(4, monster[level].High - level);
        }

        // Outside that window it is three in both, which is what makes those ten levels the whole
        // of the difference rather than the part somebody happened to sample.
        Assert.Equal([3], card.Select(row => row.Value.High - row.Key).Distinct().Order());
        Assert.Equal([3, 4], monster.Select(row => row.Value.High - row.Key).Distinct().Order());
    }

    /// <summary>
    /// Which files of the family declare <c>[drop prob]</c>, and what the one that does not
    /// declares in its place.
    /// </summary>
    /// <remarks>
    /// Three siblings declare it; the abyss table declares a difficulty-keyed section instead,
    /// which is a different thing from declaring none. Both halves are stated in the reader's
    /// remarks, in the contract's and in this file's, and not one of those opens any of the files
    /// concerned, so this counts them.
    /// </remarks>
    [SkippableFact]
    public void Three_siblings_declare_drop_prob_and_the_abyss_table_a_difficulty_keyed_one()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();

        Assert.Equal(
            [DropRateTables.Monster, DropRateTables.MonsterExtra, DropRateTables.BreakableObject],
            Siblings.Where(file => OpeningTags(provider, file).Contains(DropProbTag)));

        Assert.Equal(
            [AbyssTable],
            Siblings.Where(file => OpeningTags(provider, file).Contains(DifficultyDropProbTag)));

        // This file declares the first and not the second: what it and the three siblings disagree
        // about is the shape of that section, not which section they keep it in.
        var own = OpeningTags(provider, DropRateTables.ClearReward);
        Assert.Contains(DropProbTag, own);
        Assert.DoesNotContain(DifficultyDropProbTag, own);

        // And the sibling reader is no stand-in on the abyss table: with no [drop prob] to build
        // bands from it refuses by name rather than handing back an empty table.
        Assert.Throws<InvalidDataException>(
            () => new PvfDropRateTableSource(provider).Load(AbyssTable));
    }

    /// <summary>
    /// How much of this file's section list is shared with the family, and how much is its own.
    /// </summary>
    /// <remarks>
    /// <c>ClearRewardTable</c>'s remarks say sixteen tags, seven of them shared and nine declared
    /// by no other file of the family. Both halves of that sentence have been wrong here before:
    /// the count of its own sections was written as five, and once that was corrected to nine the
    /// same sentence still called the shared ones "most" of them. Naming the two sets rather than
    /// only counting them means a tag that moves between the sets is named too.
    /// </remarks>
    [SkippableFact]
    public void Seven_of_this_files_sixteen_sections_are_shared_and_nine_are_declared_nowhere_else()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();

        var own = OpeningTags(provider, DropRateTables.ClearReward);
        Assert.Equal(16, own.Count);

        var elsewhere = new HashSet<string>(
            Siblings.SelectMany(file => OpeningTags(provider, file)), StringComparer.Ordinal);

        Assert.Equal(
            [
                "[basis of rarity dicision]",
                "[drop prob count]",
                "[drop prob]",
                "[dungeon difficulty drop bonusrate]",
                "[item drop rarity control]",
                "[item drop ref table]",
                "[party member drop bonusrate]",
            ],
            own.Where(elsewhere.Contains).Order(StringComparer.Ordinal));

        Assert.Equal(
            [
                "[chronicle set reward rate]",
                "[drop item type prob]",
                "[drop kind prob]",
                "[dungeon difficulty gold drop bonusrate]",
                "[gold card blank item]",
                "[gold card cost table]",
                "[gold card create rate]",
                "[pcroom card blank item]",
                "[reward item rate per map max count]",
            ],
            own.Where(tag => !elsewhere.Contains(tag)).Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// A sibling band is two level bounds and five rates, where a band of this file is two and one.
    /// </summary>
    /// <remarks>
    /// That contrast is stated in four places across this subject and is the whole reason the two
    /// files need two readers. What fixes the sibling width is each file's own
    /// <c>[drop prob count]</c> divided into its numbers, so the width is recovered here rather
    /// than transcribed, and recovered on all three siblings rather than on the monster table
    /// alone.
    /// </remarks>
    [SkippableFact]
    public void A_sibling_band_is_two_bounds_and_five_rates_where_a_band_here_is_two_and_one()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();

        // The archive names none of the five. That they are a gold rate and one per item slot is
        // what DropRateSlot models, and it holds exactly five members with gold first.
        Assert.Equal(SiblingBandRateCount, Enum.GetValues<DropRateSlot>().Length);
        Assert.Equal(0, (int)DropRateSlot.Gold);

        var rates = new PvfDropRateTableSource(provider);
        (string File, int Bands, int Numbers)[] siblings =
        [
            (DropRateTables.Monster, 10, 70),
            (DropRateTables.MonsterExtra, 7, 49),
            (DropRateTables.BreakableObject, 4, 28),
        ];

        foreach (var (file, bands, numbers) in siblings)
        {
            Assert.Equal([bands], Numbers(provider, file, BandCountTag));
            Assert.Equal(numbers, Numbers(provider, file, DropProbTag).Count);
            Assert.Equal(SiblingBandWidth, numbers / bands);
            Assert.Equal(
                SiblingBandRateCount, SiblingBandWidth - ClearRewardBandLevelBoundCount);

            var table = rates.Load(file);
            Assert.Equal(bands, table.LevelBands.Count);
            Assert.All(
                table.LevelBands,
                band => Assert.Equal(SiblingBandRateCount, band.Rates.Count));
        }

        // And this file, for the contrast: four bands to a profile, six profiles and seventy-two
        // numbers, so three numbers to a band -- two level bounds and the single rate.
        var own = Table(provider);
        Assert.Equal([ClearRewardBandsPerProfile], Numbers(provider, DropRateTables.ClearReward, BandCountTag));
        Assert.Equal(72, Numbers(provider, DropRateTables.ClearReward, DropProbTag).Count);
        Assert.Equal(6, own.Profiles.Count);
        Assert.Equal(
            ClearRewardBandLevelBoundCount + 1,
            72 / (own.Profiles.Count * ClearRewardBandsPerProfile));
    }

    /// <summary>
    /// One rarity group here, four in each of three siblings, and a counted two in the abyss table.
    /// </summary>
    /// <remarks>
    /// Stated on <c>ReadLadder</c>, on <c>ClearRewardTable.Rarity</c> and in this file's own
    /// remarks. The half that matters is the leading count: the abyss table opens the section with
    /// the number of groups that follow it and this file does not, so a reader that expected one
    /// here would drop the first threshold and close the ladder on the wrong basis.
    /// </remarks>
    [SkippableFact]
    public void One_rarity_group_here_four_in_three_siblings_and_a_counted_two_in_the_abyss_table()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();

        var rates = new PvfDropRateTableSource(provider);
        foreach (var file in new[]
                 {
                     DropRateTables.Monster,
                     DropRateTables.MonsterExtra,
                     DropRateTables.BreakableObject,
                 })
        {
            var values = Numbers(provider, file, RarityTag);
            Assert.Equal(4 * RarityGroupWidth, values.Count);
            Assert.Equal(0, values.Count % RarityGroupWidth);

            // And that is what the sibling reader recovers from them: four ladders of seven.
            var ladders = rates.Load(file).RarityLadders;
            Assert.Equal(4, ladders.Count);
            Assert.All(ladders, ladder => Assert.Equal(RarityGroupWidth, ladder.Values.Count));
        }

        // The abyss table opens with the number of groups it then declares, so it has one number
        // left over when cut into groups of seven.
        var abyss = Numbers(provider, AbyssTable, RarityTag);
        Assert.Equal(1 + (2 * RarityGroupWidth), abyss.Count);
        Assert.Equal(2, abyss[0]);
        Assert.Equal(abyss[0], (abyss.Count - 1) / RarityGroupWidth);
        Assert.Equal(1, abyss.Count % RarityGroupWidth);

        // This file declares a group and no count: seven numbers exactly, the first of them a
        // threshold in the range the siblings' first thresholds are in rather than a small count.
        var card = Numbers(provider, DropRateTables.ClearReward, RarityTag);
        Assert.Equal(RarityGroupWidth, card.Count);
        Assert.Equal(0, card.Count % RarityGroupWidth);
        Assert.Equal(500000, card[0]);

        // And the reading takes all seven of them.
        var ladder = Table(provider).Rarity;
        Assert.NotNull(ladder);
        Assert.Equal(card, ladder.Values);
    }

    /// <summary>
    /// "A group's last number is one more than the one before it" is this file's rule and the
    /// monster table's, not the family's.
    /// </summary>
    /// <remarks>
    /// It is the invariant <c>ReadLadder</c> checks, and the one the sibling reader recovers a
    /// group width by, so calling it a family invariant would make the abyss table look like a
    /// malformed member of a shape it never had. Neither of its two groups satisfies it: one
    /// closes 1000001 after 1000001, the other 1000001 after 1000002.
    /// </remarks>
    [SkippableFact]
    public void The_closing_invariant_holds_here_and_in_the_monster_table_but_in_no_abyss_group()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();

        var abyss = Numbers(provider, AbyssTable, RarityTag);
        Assert.Equal(
            [
                2,
                0, 599000, 993000, 993000, 1000000, 1000001, 1000001,
                0, 800000, 1000000, 1000000, 1000001, 1000002, 1000001,
            ],
            abyss);

        for (var at = 1; at + RarityGroupWidth <= abyss.Count; at += RarityGroupWidth)
        {
            Assert.False(
                abyss[at + RarityGroupWidth - 1] == abyss[at + RarityGroupWidth - 2] + 1,
                $"the abyss group starting at {at} closes after all");
        }

        // This file's group closes, and so does every one of the monster table's four.
        var card = Numbers(provider, DropRateTables.ClearReward, RarityTag);
        Assert.Equal(card[^2] + 1, card[^1]);

        var monster = Numbers(provider, DropRateTables.Monster, RarityTag);
        for (var at = 0; at + RarityGroupWidth <= monster.Count; at += RarityGroupWidth)
        {
            Assert.Equal(
                monster[at + RarityGroupWidth - 2] + 1, monster[at + RarityGroupWidth - 1]);
        }
    }

    /// <summary>
    /// The family is seven files, counted out of the archive's own file table.
    /// </summary>
    /// <remarks>
    /// <c>DropRateTable</c>'s remarks say seven, and every "no other file of the family declares
    /// this" in this class is a claim about <see cref="Siblings"/> plus this one. Both rested on a
    /// list typed out by hand: nothing had ever asked the archive how many files there are, so an
    /// eighth would have left every other test here green and the word "seven" quietly wrong.
    /// <para>
    /// The needle is the family's name in the path, which is how the archive itself distinguishes
    /// them -- there is no index of the family anywhere in the file to read instead.
    /// </para>
    /// </remarks>
    [SkippableFact]
    public void The_family_is_the_seven_files_the_archives_own_file_table_holds()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();

        var family = FamilyFiles(provider);

        // Named rather than only counted, so that a file swapped for another is caught too.
        Assert.Equal(
            [
                ControlTable,
                DropRateTables.ClearReward,
                CommonTable,
                DropRateTables.Monster,
                DropRateTables.MonsterExtra,
                AbyssTable,
                DropRateTables.BreakableObject,
            ],
            family);

        // And the hand-written list is exactly that family less this file.
        Assert.Equal(
            Siblings.Order(StringComparer.Ordinal),
            family.Where(file => !string.Equals(
                file, DropRateTables.ClearReward, StringComparison.Ordinal)));
    }

    /// <summary>
    /// What the sibling reader really does when this file is handed to it.
    /// </summary>
    /// <remarks>
    /// <c>PvfClearRewardRateSource</c>'s own remarks said for seven rounds that it would hand back
    /// a full table without complaining once. It does not: it throws. But it throws over a section
    /// it read correctly, so both halves are pinned here -- that the file is refused, and that the
    /// refusal is not the shape being noticed. The rate count named in the message is the
    /// misreading itself: sixteen, from bands of eighteen less their two level bounds.
    /// </remarks>
    [SkippableFact]
    public void The_sibling_reader_refuses_this_file_over_a_section_it_read_correctly()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();

        var thrown = Assert.Throws<InvalidDataException>(
            () => new PvfDropRateTableSource(provider).Load(DropRateTables.ClearReward));

        Assert.Contains(
            "[dungeon difficulty drop bonusrate]", thrown.Message, StringComparison.Ordinal);
        Assert.Contains("16 rates", thrown.Message, StringComparison.Ordinal);

        // The section it actually misread is not what it complains about.
        Assert.DoesNotContain(DropProbTag, thrown.Message, StringComparison.Ordinal);

        // Because that section gave it no trouble: seventy-two numbers over the four bands this
        // file declares divide evenly into bands of eighteen, sixteen of them past the bounds.
        var declared = Numbers(provider, DropRateTables.ClearReward, BandCountTag);
        var numbers = Numbers(provider, DropRateTables.ClearReward, DropProbTag).Count;
        Assert.Equal([ClearRewardBandsPerProfile], declared);
        Assert.Equal(0, numbers % ClearRewardBandsPerProfile);
        Assert.Equal(18, numbers / ClearRewardBandsPerProfile);
        Assert.Equal(
            16, (numbers / ClearRewardBandsPerProfile) - ClearRewardBandLevelBoundCount);

        // And the five it could not divide by that sixteen are the difficulty bonuses.
        Assert.Equal(
            5,
            Numbers(provider, DropRateTables.ClearReward, "[dungeon difficulty drop bonusrate]")
                .Count);
    }

    /// <summary>
    /// The opening section tags of one archive file, in declaration order and without repeats.
    /// </summary>
    /// <remarks>
    /// The closing <c>[/…]</c> markers arrive as identifiers too and carry no values, so they are
    /// dropped: a claim about how many sections a file declares is a claim about the opening tags.
    /// </remarks>
    private static IReadOnlyList<string> OpeningTags(
        PvfArchiveProvider provider, string archivePath)
    {
        var archive = provider.Archive;
        var tags = new List<string>();
        var reader = archive.OpenScript(archivePath);
        while (reader.Read(out var token))
        {
            if (token.Kind != PvfTokenKind.Identifier)
                continue;

            var tag = archive.ResolveString(token.Value);
            if (!tag.StartsWith("[/", StringComparison.Ordinal)
                && !tags.Contains(tag, StringComparer.Ordinal))
            {
                tags.Add(tag);
            }
        }

        return tags;
    }

    /// <summary>
    /// Every file of the family the archive's file table holds, by path, in ordinal order.
    /// </summary>
    /// <remarks>
    /// Walked rather than looked up: a path can only find a file something already names, and the
    /// question here is whether anything is named that this class does not know about.
    /// </remarks>
    private static IReadOnlyList<string> FamilyFiles(PvfArchiveProvider provider)
    {
        var archive = provider.Archive;
        var files = new List<string>();
        for (var index = 0; index < archive.FileCount; index++)
        {
            var entry = archive.GetEntry(index);
            var folder = archive.ResolveString(entry.PathOffset);
            var name = archive.ResolveString(entry.NameOffset);
            var path = folder.Length > 0 ? $"{folder}/{name}" : name;
            if (path.Contains(FamilyNeedle, StringComparison.OrdinalIgnoreCase))
                files.Add(path);
        }

        files.Sort(StringComparer.Ordinal);
        return files;
    }

    /// <summary>Every integer one section of one archive file declares, in order.</summary>
    private static IReadOnlyList<int> Numbers(
        PvfArchiveProvider provider, string archivePath, string section)
    {
        var archive = provider.Archive;
        var values = new List<int>();
        var inside = false;
        var reader = archive.OpenScript(archivePath);
        while (reader.Read(out var token))
        {
            if (token.Kind == PvfTokenKind.Identifier)
            {
                inside = string.Equals(
                    archive.ResolveString(token.Value), section, StringComparison.Ordinal);
                continue;
            }

            if (inside && token.Kind == PvfTokenKind.Int32)
                values.Add(token.Value);
        }

        return values;
    }

    private static IReadOnlyList<int> Rates(ClearRewardTable table, string profileName) =>
        [.. table.ProfileNamed(profileName)!.Bands.Select(band => band.Rate)];
}
