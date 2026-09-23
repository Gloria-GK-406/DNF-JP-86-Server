using Dfo.Server.Abstractions.Content;
using Dfo.Server.Infrastructure.Content;

namespace Dfo.Server.Infrastructure.Tests.Content;

/// <summary>
/// The monster rate table, against the archive that declares it.
/// </summary>
/// <remarks>
/// Every assertion here names a number the archive actually holds rather than a number this
/// server's own reading produced. That is the point: a reading can be wrong and self-consistent at
/// the same time, and only a transcribed archive value tells those two apart.
/// </remarks>
public sealed class PvfDropRateTableSourceTests
{
    /// <summary>The band the endgame is fought in.</summary>
    private const int EndgameLevel = 86;

    private static DropRateTable MonsterTable(PvfArchiveProvider provider) =>
        new PvfDropRateTableSource(provider).Load(DropRateTables.Monster);

    /// <summary>
    /// The five rates of the top band, transcribed from the archive.
    /// </summary>
    /// <remarks>
    /// <c>81 200 750 10 135 8 5</c> is the last row of <c>[drop prob]</c>. Reading a band as
    /// anything other than two level bounds followed by five rates puts different numbers here --
    /// a reader that took four rates would report the type-4 rate as another band's low level.
    /// </remarks>
    [SkippableFact]
    public void The_top_band_pays_the_five_rates_the_archive_declares()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();

        var band = MonsterTable(provider).BandFor(EndgameLevel);

        Assert.NotNull(band);
        Assert.Equal(81, band.LowLevel);
        Assert.Equal(200, band.HighLevel);
        Assert.Equal(750, band.RateOf(DropRateSlot.Gold));
        Assert.Equal(10, band.RateOf(DropRateSlot.ItemType1));
        Assert.Equal(135, band.RateOf(DropRateSlot.ItemType2));
        Assert.Equal(8, band.RateOf(DropRateSlot.ItemType3));
        Assert.Equal(5, band.RateOf(DropRateSlot.ItemType4));
    }

    /// <summary>
    /// The table declares as many bands as it says it does, and every one pays five rates.
    /// </summary>
    /// <remarks>
    /// The count is what fixes the band width, and the band width less its two level bounds is
    /// what fixes the number of rates -- which is in turn what the three bonus tables are split
    /// into. If this is wrong, every multiplier in the table belongs to the wrong rate.
    /// </remarks>
    [SkippableFact]
    public void The_declared_band_count_is_what_the_file_holds()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();

        var table = MonsterTable(provider);

        Assert.Equal(10, table.LevelBands.Count);
        Assert.All(table.LevelBands, band => Assert.Equal(5, band.Rates.Count));
        Assert.Equal(1, table.LevelBands[0].LowLevel);
        Assert.Equal(15, table.LevelBands[0].HighLevel);
    }

    /// <summary>
    /// The monster rank multipliers, and the one group that is not all ones.
    /// </summary>
    /// <remarks>
    /// <c>0.2 / 2 / 7 / 16.8</c> is the third group of <c>[monster type drop bonusrate]</c> -- the
    /// equipment slot -- and it is the steepest thing in this whole subject: a boss is eighty-four
    /// times as likely to drop a piece of gear as an ordinary monster. Taking the wrong group here
    /// is invisible in every other way, because the other four groups are the same tame
    /// <c>1 / 2 / 3 / 4</c> that a wrong group would quietly substitute.
    /// </remarks>
    [SkippableFact]
    public void The_equipment_slot_carries_the_steep_rank_multipliers()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();

        var table = MonsterTable(provider);

        // Four decimal places, because the archive stores these as 32-bit singles: 16.8 comes
        // back as 16.79999923, and a comparison tight enough to reject that would be rejecting
        // the archive rather than the reading.
        Assert.Equal(0.2, table.MonsterRankRate(DropRateSlot.ItemType2, 0), 4);
        Assert.Equal(2.0, table.MonsterRankRate(DropRateSlot.ItemType2, 1), 4);
        Assert.Equal(7.0, table.MonsterRankRate(DropRateSlot.ItemType2, 2), 4);
        Assert.Equal(16.8, table.MonsterRankRate(DropRateSlot.ItemType2, 3), 4);

        // Gold's own group, which is steep at the top and flat below it.
        Assert.Equal(1.0, table.MonsterRankRate(DropRateSlot.Gold, 0), 4);
        Assert.Equal(6.6, table.MonsterRankRate(DropRateSlot.Gold, 3), 4);
    }

    /// <summary>Each bonus table is split into one group per rate, with its own width.</summary>
    [SkippableFact]
    public void Each_bonus_table_holds_one_group_for_each_rate()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();

        var table = MonsterTable(provider);

        Assert.Equal(5, table.MonsterRankRates.Count);
        Assert.Equal(5, table.DifficultyRates.Count);
        Assert.Equal(5, table.PartyMemberRates.Count);
        Assert.All(table.MonsterRankRates, group => Assert.Equal(4, group.Count));
        Assert.All(table.DifficultyRates, group => Assert.Equal(5, group.Count));
        Assert.All(table.PartyMemberRates, group => Assert.Equal(4, group.Count));

        // The equipment slot's party group is the one that differs from every other file's --
        // 1 / 1.3 / 1.6 / 2 where its four neighbours all say 1 / 1.2 / 1.4 / 1.6. It is here
        // because it proves the groups were taken from this file rather than from a shape shared
        // across the family.
        Assert.Equal(1.3, table.PartyMemberRate(DropRateSlot.ItemType2, 2), 4);
        Assert.Equal(2.0, table.PartyMemberRate(DropRateSlot.ItemType2, 4), 4);

        // Gold does not scale with party size at all: its group is four ones.
        Assert.All(
            table.PartyMemberRates[(int)DropRateSlot.Gold],
            rate => Assert.Equal(1.0, rate, 4));
    }

    /// <summary>An unlisted rank, difficulty or party size multiplies by one.</summary>
    [SkippableFact]
    public void A_multiplier_the_table_does_not_list_is_one()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();

        var table = MonsterTable(provider);

        Assert.Equal(1.0, table.MonsterRankRate(DropRateSlot.ItemType2, 9), 6);
        Assert.Equal(1.0, table.DifficultyRate(DropRateSlot.ItemType2, 99), 6);
        Assert.Equal(1.0, table.PartyMemberRate(DropRateSlot.ItemType2, 0), 6);
    }

    /// <summary>
    /// The level to grade band table, and the invariant that identifies the reading.
    /// </summary>
    /// <remarks>
    /// The rows are level, how far below, how far above. What says so is not a declaration -- there
    /// is none -- but the shape: from level 86 upwards the archive raises the first number in step
    /// with the level, so that the lower edge stays at grade 79 for every level to 200. Seventy-nine
    /// is the floor of A21's endgame gear. No other reading of these three columns produces a
    /// constant at all, let alone that one.
    /// </remarks>
    [SkippableFact]
    public void The_grade_band_floor_holds_at_seventy_nine_across_the_endgame()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();

        var table = MonsterTable(provider);

        Assert.Equal(new ItemGradeBand(79, 90), table.GradeBandFor(EndgameLevel));
        for (var level = EndgameLevel; level <= 200; level++)
            Assert.Equal(79, table.GradeBandFor(level)?.Low);

        // And below the endgame it tracks the level rather than sitting still.
        Assert.Equal(new ItemGradeBand(2, 8), table.GradeBandFor(5));
        Assert.Equal(new ItemGradeBand(1, 4), table.GradeBandFor(1));
    }

    /// <summary>
    /// The four other files of this family read under the same rules.
    /// </summary>
    /// <remarks>
    /// The point of the file being a parameter. The clear-reward table is the one the card flip
    /// will want, and its shape differs in exactly one place -- its bands -- which is worth knowing
    /// now rather than discovering in the second phase.
    /// </remarks>
    [SkippableFact]
    public void The_family_reads_under_one_set_of_rules()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfDropRateTableSource(provider);

        var extra = source.Load(DropRateTables.MonsterExtra);
        Assert.Equal(7, extra.LevelBands.Count);
        Assert.All(extra.LevelBands, band => Assert.Equal(5, band.Rates.Count));
        Assert.Equal(4, extra.RarityLadders.Count);
        // Its rank multipliers are all tame, which is what makes taking the monster table's own
        // group from the monster table's own file matter.
        Assert.Equal(1.0, extra.MonsterRankRate(DropRateSlot.ItemType2, 0), 4);

        var breakable = source.Load(DropRateTables.BreakableObject);
        Assert.Equal(4, breakable.LevelBands.Count);
        Assert.Equal(4, breakable.RarityLadders.Count);
        // All four files declare their own grade band table, and they are not the same table: at
        // the endgame level a breakable object reaches one grade less far up than a monster does.
        // A reading that took any one file's copy for the family would be wrong here and nowhere
        // else -- the two differ in one number out of six hundred.
        Assert.Equal(new ItemGradeBand(79, 89), breakable.GradeBandFor(EndgameLevel));
        Assert.Equal(new ItemGradeBand(79, 90), extra.GradeBandFor(EndgameLevel));

        // A level no row covers has no band, which is an ordinary answer rather than a failure.
        Assert.Null(extra.GradeBandFor(0));
        Assert.Null(extra.GradeBandFor(201));
    }

    /// <summary>The same file asked for twice is parsed once.</summary>
    [SkippableFact]
    public void A_table_is_read_once_and_held()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfDropRateTableSource(provider);

        Assert.Same(
            source.Load(DropRateTables.Monster),
            source.Load(DropRateTables.Monster));
    }

    /// <summary>A file the archive does not hold is named rather than answered with an empty table.</summary>
    [SkippableFact]
    public void A_missing_file_is_refused_by_name()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfDropRateTableSource(provider);

        var thrown = Assert.Throws<InvalidDataException>(
            () => source.Load("etc/itemdropinfo_there_is_no_such_file.etc"));

        Assert.Contains("itemdropinfo_there_is_no_such_file", thrown.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The rarity ladders, transcribed.
    /// </summary>
    /// <remarks>
    /// This is what holds <c>RarityLadderTests</c> honest: that file works the arithmetic against
    /// these numbers as literals, and this one says the numbers are the archive's.
    /// <para>
    /// The group width is recovered rather than assumed. What identifies a group is that its last
    /// number is one more than the number before it -- the basis closing the thresholds -- and
    /// seven is the narrowest width for which all four groups satisfy that. A21 declares six bands
    /// where the community documentation describes five, so a transcribed width would have been
    /// wrong here and self-consistently so.
    /// </para>
    /// </remarks>
    [SkippableFact]
    public void The_ladders_are_the_groups_the_archive_declares()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();

        var table = MonsterTable(provider);
        var ladders = table.RarityLadders;

        Assert.Equal(4, ladders.Count);
        Assert.All(ladders, ladder => Assert.Equal(6, ladder.BandCount));
        Assert.Equal(
            [700000, 964900, 990000, 1000000, 1000001, 1000002, 1000003],
            ladders[0].Values);
        Assert.Equal(
            [545500, 899500, 999500, 1000000, 1000001, 1000002, 1000003],
            ladders[1].Values);

        // Gold has no ladder, and the first ladder belongs to the first item slot.
        Assert.Null(table.LadderFor(DropRateSlot.Gold));
        Assert.Equal(ladders[0], table.LadderFor(DropRateSlot.ItemType1));
        Assert.Equal(ladders[3], table.LadderFor(DropRateSlot.ItemType4));
    }

    /// <summary>
    /// The abyss table is a different shape and is refused rather than read as this one.
    /// </summary>
    /// <remarks>
    /// It declares a leading group count that no other file in the family does, and its groups do
    /// not close the way the other four do. Both would have to be guessed around, and a guess here
    /// produces a ladder of plausible wrong numbers rather than an error.
    /// </remarks>
    [SkippableFact]
    public void The_abyss_table_is_refused_rather_than_read_as_this_shape()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = new PvfDropRateTableSource(provider);

        Assert.Throws<InvalidDataException>(
            () => source.Load("etc/itemdropinfo_monster_hell.etc"));
    }

    /// <summary>
    /// What a kill pays in gold, against the two files that between them say so.
    /// </summary>
    /// <remarks>
    /// The gold reading was the one decisive claim in this change with nothing archive-facing
    /// behind it: four separate readings -- that a row of <c>[gold drop ref table]</c> is three
    /// numbers, that the amount is its second, that <c>[gold quantity]</c> is one multiple per
    /// monster rank, and that <c>[gold volume]</c> is a percentage band -- were all asserted from
    /// typed-in literals only. Gold is the highest rate in the table, so a wrong column here pays
    /// every kill in the game another level's money with nothing anywhere saying so.
    /// <para>
    /// The third column of a gold row is deliberately not asserted: it is 13, 11 and 9 across the
    /// level range and this reading has not identified it. Pinning a number nobody understands
    /// would only make a future correction look like a regression.
    /// </para>
    /// </remarks>
    [SkippableFact]
    public void The_gold_table_is_what_the_two_files_declare()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();

        var gold = ((IGoldDropTableSource)new PvfDropRateTableSource(provider))
            .Load(DropRateTables.Monster);

        // Transcribed from etc/itemdropinfo_common.etc: level 1 pays 17 and level 86 pays 1357.
        // Both ends, because reading the level out of the wrong column of a three-wide row would
        // still line one of them up by accident.
        Assert.Equal(17, gold.BaseFor(1));
        Assert.Equal(1357, gold.BaseFor(EndgameLevel));
        Assert.Equal(4877, gold.BaseFor(200));
        Assert.Equal(200, gold.BaseAmounts.Count);

        // And it is strictly the amount rather than the row's third number, which at level 86 is 9.
        Assert.NotEqual(9, gold.BaseFor(EndgameLevel));

        // From the rate table itself: [gold quantity] 1 2 4 6, one per monster rank.
        Assert.Equal([1, 2, 4, 6], gold.RankQuantities);
        Assert.Equal(1, gold.QuantityFor(0));
        Assert.Equal(6, gold.QuantityFor(3));
        Assert.Equal(1, gold.QuantityFor(9));

        // And [gold volume] 100 110 -- never less than the base, up to a tenth more.
        Assert.Equal((100, 110), gold.VolumeBand);

        // A level the table does not cover pays nothing rather than a number nobody declared.
        Assert.Equal(0, gold.BaseFor(0));
        Assert.Equal(0, gold.BaseFor(201));
    }

    /// <summary>
    /// The gold multiples belong to the file being read, not to the family.
    /// </summary>
    /// <remarks>
    /// A breakable object pays the same per-level base as a monster -- the base is the world's --
    /// but need not pay the same multiple for it. Reading the rank quantities from a fixed file
    /// would be invisible until the breakable-object path is built.
    /// </remarks>
    [SkippableFact]
    public void The_gold_multiples_come_from_the_file_being_read()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var source = (IGoldDropTableSource)new PvfDropRateTableSource(provider);

        var monster = source.Load(DropRateTables.Monster);
        var breakable = source.Load(DropRateTables.BreakableObject);

        // The per-level base is the world's, so both see the same one.
        Assert.Equal(monster.BaseFor(EndgameLevel), breakable.BaseFor(EndgameLevel));

        // The multiples are not. Only the monster table declares [gold quantity] and
        // [gold volume]; the breakable-object table declares neither, so it falls back to one
        // multiple and no variation. A reading that took these from a fixed file would hand back
        // the monster table's 1/2/4/6 here, which is the mutation this exists to catch.
        Assert.Equal([1, 2, 4, 6], monster.RankQuantities);
        Assert.Equal([1], breakable.RankQuantities);
        Assert.NotEqual(monster.RankQuantities, breakable.RankQuantities);
        Assert.Equal((100, 100), breakable.VolumeBand);
        Assert.NotEqual(monster.VolumeBand, breakable.VolumeBand);
    }

    /// <summary>
    /// The pool holds no avatar, which is why the avatar rate slot has nothing to draw.
    /// </summary>
    /// <remarks>
    /// Avatars live in <c>equipment.lst</c> beside ordinary gear, so "the avatar pool is empty" is
    /// a claim about the archive rather than about the code, and this is where it is checked.
    /// <c>MonsterDropDomainService</c> declines the avatar slot on the strength of it.
    /// <para>
    /// Named pieces here, but what pins the universal is the count in
    /// <c>PvfDropItemPoolSourceTests.The_equipment_pool_comes_from_the_equipment_files</c>: the
    /// pool holds 6062 of the list's 118479, and the ~61000 avatars are most of what the other
    /// 112417 are. A gate that let avatars through could not leave that count at 6062.
    /// </para>
    /// </remarks>
    [SkippableFact]
    public void No_avatar_declares_itself_creatable()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();

        var pool = new PvfDropItemPoolSource(provider).Load(DropItemFamily.Equipment);

        // Avatar caps and coats taken from equipment.lst. None declares a rarity or a creation
        // rate, which is what every avatar in the archive does.
        int[] avatars = [101550507, 101550511, 101550512];
        for (var grade = 0; grade <= 200; grade++)
        {
            foreach (var rarity in Enum.GetValues<Dfo.Server.Abstractions.World.DropRarity>())
            {
                Assert.DoesNotContain(
                    pool.Bucket(grade, rarity), entry => avatars.Contains(entry.ItemId));
            }
        }
    }
}
