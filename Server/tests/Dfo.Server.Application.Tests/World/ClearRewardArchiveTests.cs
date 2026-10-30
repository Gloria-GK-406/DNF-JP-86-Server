using Dfo.Server.Data.World;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Characters;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Testing;

namespace Dfo.Server.Application.Tests.World;

/// <summary>
/// The card-face rules running against the operator-supplied archive.
/// </summary>
/// <remarks>
/// <see cref="ClearRewardRulesTests"/> proves the arithmetic against transcribed numbers. This
/// file is what keeps the transcription honest: it asserts that the archive still declares those
/// numbers and that the rule service composes the same answers out of them, so a reading error and
/// an arithmetic error cannot cover for each other.
/// <para>
/// Only the rate table and the grade bands come from the archive in most of these; the pool is
/// hand-built, because what is under test is which numbers reach the composition rather than what
/// the equipment family holds. The one test that does draw from the real pool says so.
/// </para>
/// </remarks>
public sealed class ClearRewardArchiveTests
{
    /// <summary>The level the endgame is fought at.</summary>
    private const int EndgameLevel = 86;

    /// <summary>
    /// A second character level, well away from <see cref="EndgameLevel"/> and from any dungeon's
    /// basis level, so that a table keyed on the character can be told from one that is not.
    /// </summary>
    private const int LowLevel = 20;

    /// <summary>The assumed basis, defaulted. See <see cref="ClearRewardRateOptions"/>.</summary>
    private const int Basis = 10000;

    /// <summary>The dungeon this subject's captures were taken in.</summary>
    private const int OrdinaryDungeonId = 144;

    /// <summary>How many rooms the largest of that dungeon's five mazes holds.</summary>
    private const int MazeRooms = 10;

    private static readonly CharacterId Character = new(31);

    private static ClearRewardTable Table(ServiceProvider provider) =>
        provider.GetRequiredService<IClearRewardRateSource>().Load(DropRateTables.ClearReward);

    /// <summary>The real rate table, a hand-built pool, and the defaulted basis.</summary>
    private static ClearRewardRuleService Service(ServiceProvider provider, DropItemPool pool) =>
        new(
            provider.GetRequiredService<IClearRewardRateSource>(),
            provider.GetRequiredService<IGoldDropTableSource>(),
            new ClearRewardFixture.RecordingPools(pool),
            provider.GetRequiredService<IDungeonDefinitionSource>(),
            new ClearRewardRateOptions { RateBasis = Basis });

    /// <summary>
    /// The numbers the composition is fed are the ones the archive declares.
    /// </summary>
    /// <remarks>
    /// Transcribed from <c>etc/itemdropinfo_clearreward.etc</c> as the archive holds it. Every
    /// claim the rules and the rule service make about this file in their own remarks is asserted
    /// here, because a claim about a file this code does not itself read is worth exactly as much
    /// as the test holding it down.
    /// </remarks>
    [SkippableFact]
    public void The_archive_declares_the_numbers_the_composition_is_built_from()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateContentProvider();
        var table = Table(provider);

        // Six named profiles, four level bands apiece. The band count is what the file's own count
        // section announces; it is not, and must not be read as, how many positions a face has.
        Assert.Equal(6, table.Profiles.Count);
        Assert.All(table.Profiles, profile => Assert.Equal(4, profile.Bands.Count));

        // The default profile pays the full rate in every band -- that is the threshold an
        // ordinary clear is measured against -- and the profiles named for occasions pay less,
        // which read as thresholds makes them the generous ones.
        var standard = table.ProfileNamed(ClearRewardProfiles.Default);
        Assert.NotNull(standard);
        Assert.All(standard.Bands, band => Assert.Equal(10000, band.Rate));
        Assert.Equal(9000, table.ProfileNamed(ClearRewardProfiles.Event)?.BandFor(EndgameLevel)?.Rate);
        Assert.Equal(
            8000, table.ProfileNamed(ClearRewardProfiles.PcRoomEvent)?.BandFor(EndgameLevel)?.Rate);

        // The file's own corroboration of the basis, which is the one part of the composition that
        // has any: two sections of it are shares that sum to ten thousand, and the full profile
        // rate is ten thousand as well. None of the three declares a basis; all three agree on one.
        Assert.Equal(Basis, table.DropKindRates.Sum());
        Assert.Equal(Basis, table.ItemTypeRates.Sum());

        // The bonus table, whose first entry adds nothing and whose fifth adds less than its
        // fourth. Under an additive composition the zero is an ordinary entry, not an off switch.
        Assert.Equal([0, 600, 1000, 1400, 1200], table.DifficultyRates);
        Assert.Equal(0, table.DifficultyRate(0)!.Value);
        Assert.True(table.DifficultyRate(4)!.Value < table.DifficultyRate(3)!.Value);
        Assert.Null(table.DifficultyRate(5));

        // Five rows, for one room through five, and nothing beyond -- which is the ordinary case
        // rather than an edge: dungeon 144's smallest maze holds four rooms and its largest ten.
        Assert.Equal(2000, table.MapCountRate(1)!.Value);
        Assert.Equal(6000, table.MapCountRate(5)!.Value);
        Assert.Null(table.MapCountRate(6));
        Assert.Equal(Enumerable.Range(1, 5), table.MapCountRates.Keys.Order());

        // The rate the paid card is given a section of its own for. Nothing on this path reads it:
        // the two faces carry the same odds, see ClearRewardCardKind.
        Assert.Equal(1.0, table.GoldCardCreateRate);

        // Stored as singles, which is why the composition takes them to whole parts of the basis
        // rather than comparing them as reals.
        Assert.Equal(4, table.PartyMemberRates.Count);
        Assert.Equal(1.0, table.PartyMemberRate(1)!.Value);
        Assert.Equal((double)1.4f, table.PartyMemberRate(3)!.Value);
        Assert.NotEqual(1.4, table.PartyMemberRate(3)!.Value);
        Assert.Equal((double)1.6f, table.PartyMemberRate(4)!.Value);
        Assert.NotEqual(1.6, table.PartyMemberRate(4)!.Value);
        Assert.Null(table.PartyMemberRate(5));

        // The grade band a card of the endgame level draws from.
        Assert.Equal(new ItemGradeBand(79, 89), table.GradeBandFor(EndgameLevel));

        // Four item-type positions, which is what the service's remarks say it is declining to
        // apply as a second gate.
        Assert.Equal(4, table.ItemTypeRates.Count);
    }

    /// <summary>
    /// <b>What the archive charges for a paid flip, and what it charges for a level it has no row
    /// for.</b>
    /// </summary>
    /// <remarks>
    /// Read from <c>[gold card cost table]</c>, whose rows are a level and a price. The endgame
    /// level's own row is asserted because it is the one this server's clears are fought at, and
    /// it is asserted through the rule service as well as through the table so that a price which
    /// went missing on the way out shows up here rather than on a result screen.
    /// <para>
    /// The three properties the frame side rests on are pinned with it. No price in this table is
    /// zero, so a frame writing zero cannot be passing an archive price off as itself; no price in
    /// it is <see cref="int.MaxValue"/>, which is what the frame writes for a level it could not
    /// price, so that marker cannot be mistaken for a reading; and a level outside the table is
    /// answered with nothing rather than with a free flip.
    /// </para>
    /// </remarks>
    [SkippableFact]
    public void The_archive_prices_a_paid_flip_by_level()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateContentProvider();
        var table = Table(provider);
        var service = Service(provider, ClearRewardFixture.Pool());

        Assert.Equal(10300, table.GoldCardCost(EndgameLevel));
        Assert.Equal(10300, service.PaidFlipCost(EndgameLevel));

        // A second row, so that the one above is a lookup rather than a table of one value.
        Assert.Equal(100, service.PaidFlipCost(1));
        Assert.NotEqual(service.PaidFlipCost(1), service.PaidFlipCost(EndgameLevel));

        // Nothing in this table is free, and nothing in it is the marker the frame writes for a
        // level it could not price.
        Assert.NotEmpty(table.GoldCardCosts);
        Assert.DoesNotContain(0, table.GoldCardCosts.Values);
        Assert.DoesNotContain(int.MaxValue, table.GoldCardCosts.Values);

        // A level the file has no row for: nothing, which is not the same answer as zero. Taken
        // off the table's own keys rather than written down, so a file that priced further would
        // move this test with it instead of leaving it asserting a stale bound.
        var unpriced = table.GoldCardCosts.Keys.Max() + 1;
        Assert.Null(table.GoldCardCost(unpriced));
        Assert.Null(service.PaidFlipCost(unpriced));
        Assert.Null(service.PaidFlipCost(table.GoldCardCosts.Keys.Min() - 1));
    }

    /// <summary>
    /// <b>The gap the named assumption exists to fill, asserted as a gap.</b>
    /// </summary>
    /// <remarks>
    /// <see cref="ClearRewardRateOptions"/> says two things about this file that no positive
    /// assertion can pin: that it gives no basis for its rate numerators, and that it gives nothing
    /// joining its tables together. What can be pinned is the shape of the reading the file
    /// produces, which is what carries those two claims -- so it is pinned here rather than left as
    /// prose nobody can check.
    /// <para>
    /// Both remain claims about <b>the file</b> and both are still true of it. The basis has the
    /// file's own arithmetic behind it -- see the sums asserted in
    /// <see cref="The_archive_declares_the_numbers_the_composition_is_built_from"/> -- but
    /// arithmetic that agrees is not a declaration, and none of the five decisions the composition
    /// makes has even that much.
    /// </para>
    /// <para>
    /// A band is two level bounds and a rate, with nothing to divide the rate by. The table carries
    /// no member with a basis in it, though the same file's rarity ladder closes itself with one --
    /// which is what shows a basis is a thing this family does declare when it means to. And the
    /// four tables are reached by four separate lookups of one argument each, none of them keyed on
    /// a card position; nothing in the reading combines them.
    /// </para>
    /// </remarks>
    [SkippableFact]
    public void The_reading_of_this_file_gives_no_basis_and_nothing_that_combines()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateContentProvider();
        var table = Table(provider);

        Assert.Equal(
            ["LowLevel", "HighLevel", "Rate"],
            typeof(ClearRewardBand).GetProperties().Select(property => property.Name));

        var members = typeof(ClearRewardTable).GetProperties().Select(property => property.Name);
        Assert.DoesNotContain(
            members,
            name => name.Contains("Basis", StringComparison.OrdinalIgnoreCase));

        // The one basis this file does declare belongs to the rarity ladder and closes it.
        Assert.NotNull(table.Rarity);
        Assert.Equal(table.Rarity.Values[^1], table.Rarity.Basis);

        // Four lookups, one argument apiece, and not one of them asks which position a card is.
        foreach (var name in new[]
                 {
                     nameof(ClearRewardTable.ProfileNamed),
                     nameof(ClearRewardTable.DifficultyRate),
                     nameof(ClearRewardTable.MapCountRate),
                     nameof(ClearRewardTable.PartyMemberRate),
                 })
        {
            var lookup = typeof(ClearRewardTable).GetMethod(name);
            Assert.NotNull(lookup);
            Assert.Single(lookup.GetParameters());
        }
    }

    /// <summary>
    /// The composed chance of a free face, with every input taken from the archive.
    /// </summary>
    /// <remarks>
    /// The end of the path the whole task exists to build: a level, a difficulty, a maze and a
    /// party size go in, and the number that comes out is the archive's values put through this
    /// server's composition.
    /// <para>
    /// Every figure is worked by hand from the numbers asserted in
    /// <see cref="The_archive_declares_the_numbers_the_composition_is_built_from"/>. The
    /// <c>default</c> profile's threshold is 10000 in every band; the map base is the table's row
    /// for the maze's size, or its largest row where it has none; the difficulty bonus is added;
    /// and what is left is what the dice, scaled by the party multiplier, has to beat. A five-room
    /// maze at the third difficulty leaves 2600, so 7400 of the ten thousand dice values pass at a
    /// party of one, and 8375 at a party of four because each value is then worth 1.6 of itself.
    /// </para>
    /// <para>
    /// The level here is the character's, which is what the threshold profile's band is keyed on;
    /// the dungeon's own level reaches the grade band and the price instead and no part of this
    /// number. Three of the profile's four bands are visited by the rows below -- 15 and 20 fall
    /// in the first two, 86 in the last -- and all three answer 7400, because A21's
    /// <c>default</c> pays 10000 in every band. So these rows say the band was found, not that it
    /// mattered; that the odds move with the character at all, and only with the character, is
    /// <see cref="ClearRewardRuleServiceTests"/>'s
    /// <c>The_threshold_band_is_the_characters_and_the_grade_band_the_dungeons</c>, which declares
    /// a table whose bands differ.
    /// </para>
    /// </remarks>
    [SkippableTheory]
    // character level, difficulty, rooms, party, how many of the dice's values pass
    [InlineData(EndgameLevel, 3, 5, 1, 7400)]
    [InlineData(EndgameLevel, 3, 5, 2, 7834)]
    [InlineData(EndgameLevel, 3, 5, 3, 8143)]
    [InlineData(EndgameLevel, 3, 5, 4, 8375)]
    [InlineData(EndgameLevel, 1, 5, 1, 6600)]
    [InlineData(EndgameLevel, 4, 5, 1, 7200)]
    [InlineData(EndgameLevel, 0, 5, 1, 6000)]
    [InlineData(EndgameLevel, 3, 1, 1, 3400)]
    // A one-room maze at the second difficulty in a party of three: 10000 - 2000 - 600 leaves
    // 7400, and 7400 / 1.4 = 5285.71..., so 5285 dice values fall short and 4715 pass.
    [InlineData(EndgameLevel, 1, 1, 3, 4715)]
    // Two more of the profile's four bands, which all pay 10000 on this archive.
    [InlineData(15, 3, 5, 1, 7400)]
    [InlineData(LowLevel, 3, 5, 1, 7400)]
    // Dungeon 144's mazes with no row of their own: the table's largest row, 6000.
    [InlineData(EndgameLevel, 3, 10, 1, 7400)]
    [InlineData(EndgameLevel, 3, 6, 1, 7400)]
    // And one the table does declare.
    [InlineData(EndgameLevel, 3, 4, 1, 6400)]
    public void The_chance_a_position_carries_is_the_archives_numbers_combined(
        int level,
        int difficulty,
        int mapMaxCount,
        int partyMemberCount,
        int expected)
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateContentProvider();
        var service = Service(provider, ClearRewardFixture.Pool());

        var chance = service.ChanceOf(
            new ClearRewardOccasion(
                Kind: ClearRewardCardKind.Free,
                CharacterLevel: level,
                DungeonBasisLevel: EndgameLevel,
                Difficulty: (DungeonDifficulty)difficulty,
                MapMaxCount: mapMaxCount,
                PartyMemberCount: partyMemberCount));

        Assert.Equal(expected, chance.Rate);
        Assert.Equal(Basis, chance.Basis);
    }

    /// <summary>
    /// <b>The two faces of one clear carry the same odds, whatever the maze.</b>
    /// </summary>
    /// <remarks>
    /// The archive gives the paid card three sections of its own and none of them is a rate for a
    /// position, so nothing in the composition is keyed on the kind of face -- see
    /// <see cref="ClearRewardCardKind"/>. Asserted against the file rather than argued: the
    /// <c>event</c> profile and <c>[gold card create rate]</c> are both read off the table here, so
    /// a service that had reached for either would come away with a different number and be caught.
    /// </remarks>
    [SkippableFact]
    public void Both_faces_of_one_clear_carry_the_same_odds()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateContentProvider();
        var table = Table(provider);
        var service = Service(provider, ClearRewardFixture.Pool());

        // The two things a paid face could have been drawn differently by, both present in the
        // file and neither reaching the answer.
        Assert.Equal(
            9000, table.ProfileNamed(ClearRewardProfiles.Event)?.BandFor(EndgameLevel)?.Rate);
        Assert.Equal(1.0, table.GoldCardCreateRate);

        int Rate(ClearRewardCardKind kind, int rooms) => service.ChanceOf(
            new ClearRewardOccasion(kind, EndgameLevel, EndgameLevel, (DungeonDifficulty)3, rooms, 1)).Rate;

        // 6000 from the largest map row, 1400 from the third difficulty, against a threshold of
        // 10000. Drawn at the event profile's 9000 it would be 8400 instead.
        Assert.Equal(7400, Rate(ClearRewardCardKind.Free, 5));
        Assert.Equal(7400, Rate(ClearRewardCardKind.Paid, 5));

        // Every maze dungeon 144 can lay out, on both faces: one answer throughout.
        Assert.Equal(
            [7400],
            new[] { 10, 6, 4, 6, 4 }
                .Where(rooms => rooms > 5)
                .SelectMany(rooms => new[]
                {
                    Rate(ClearRewardCardKind.Free, rooms),
                    Rate(ClearRewardCardKind.Paid, rooms),
                })
                .Distinct());
        Assert.Equal(
            Rate(ClearRewardCardKind.Free, 4), Rate(ClearRewardCardKind.Paid, 4));
    }

    /// <summary>
    /// <b>The archive's lowest difficulty no longer empties a face.</b>
    /// </summary>
    /// <remarks>
    /// The defect the additive composition exists to remove, asserted against the real file. That
    /// difficulty declares a bonus of zero; multiplied in, the zero emptied every position of both
    /// faces of the easiest run in the game, and added in it leaves the maze's own base rate
    /// standing. A five-room maze at a party of one therefore needs the dice to find 4000 of the
    /// 10000 threshold, and 6000 of its values do.
    /// <para>
    /// Which entry of the table is the zero is read off the archive rather than written down, so a
    /// table that moved its zero would move this test with it rather than silently pass.
    /// </para>
    /// </remarks>
    [SkippableFact]
    public void The_archives_lowest_difficulty_no_longer_empties_a_face()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateContentProvider();
        var table = Table(provider);
        var service = Service(
            provider,
            ClearRewardFixture.Pool(
                new ClearRewardFixture.PoolRow(EndgameLevel, DropRarity.Common, 114000088, 1000)));

        var zeroed = table.DifficultyRates.Select((rate, index) => (rate, index))
            .Where(entry => entry.rate == 0)
            .Select(entry => entry.index)
            .ToList();
        Assert.Equal([0], zeroed);

        var occasion = new ClearRewardOccasion(
            ClearRewardCardKind.Free, EndgameLevel, EndgameLevel, (DungeonDifficulty)zeroed[0], 5, 1);
        Assert.Equal(6000, service.ChanceOf(occasion).Rate);
        Assert.False(service.ChanceOf(occasion).IsImpossible);

        // And a real face comes out of it, on both kinds. Swept over seeds because six positions
        // in ten carry: one seed says nothing either way.
        foreach (var kind in new[] { ClearRewardCardKind.Free, ClearRewardCardKind.Paid })
        {
            var slots = Enumerable.Range(0, 64)
                .SelectMany(seed => service.FaceFor(occasion with { Kind = kind }, (ulong)seed)
                    .Slots)
                .ToList();
            Assert.Equal(64 * 4, slots.Count);
            Assert.Contains(slots, slot => !slot.IsEmpty);
            Assert.Contains(slots, slot => slot.IsEmpty);
        }

        // Still less than the same run one difficulty up, which is what the bonus is for.
        Assert.Equal(6600, service.ChanceOf(occasion with { Difficulty = (DungeonDifficulty)1 }).Rate);
    }

    /// <summary>
    /// <b>No occasion this archive describes is either certain or impossible.</b>
    /// </summary>
    /// <remarks>
    /// The two ends the composition guards, swept over everything an ordinary clear can be: every
    /// map base the table can give -- its five rows, and the largest of them again for a maze past
    /// them -- against every difficulty bonus, against every party size, at the <c>default</c>
    /// profile's threshold.
    /// <para>
    /// Both bounds are worked by hand. The largest sum before the dice is 6000 from the biggest
    /// maze plus 1400 from the third difficulty, which leaves 2600 of the 10000 for the dice; at a
    /// party of four each dice value is worth 1.6 of itself, so the values up to 1625 fall short
    /// and 8375 pass -- the steepest anything here reaches, and still short of certainty. The
    /// shallowest is a one-room maze at the difficulty that adds nothing: 8000 left for a party of
    /// one, so 2000 values pass and the face is not empty either.
    /// </para>
    /// </remarks>
    [SkippableFact]
    public void No_occasion_the_archive_describes_is_certain_or_impossible()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateContentProvider();
        var table = Table(provider);

        // A maze of six rooms is one the table declares no row for -- dungeon 144 lays out three
        // such -- and it is taken from the table's own keys rather than written down, so a file
        // that priced further would move this with it.
        var unlisted = table.MapCountRates.Keys.Max() + 1;
        Assert.Null(table.MapCountRate(unlisted));

        var mapBases = table.MapCountRates.Keys
            .Append(unlisted)
            .Select(rooms => ClearRewardRules.MapBaseRate(table.MapCountRates, rooms))
            .ToList();
        Assert.All(mapBases, rate => Assert.NotNull(rate));
        Assert.Equal(6000, mapBases.Max());
        Assert.Equal(2000, mapBases.Min());

        var threshold = table.ProfileNamed(ClearRewardProfiles.Default)?.BandFor(EndgameLevel);
        Assert.NotNull(threshold);
        Assert.Equal(Basis, threshold.Rate);

        var steepest = 0;
        var shallowest = Basis;
        foreach (var mapBase in mapBases)
        {
            foreach (var difficulty in table.DifficultyRates)
            {
                foreach (var party in table.PartyMemberRates)
                {
                    var rate = ClearRewardRules
                        .ChanceOf(mapBase!.Value, difficulty, party, threshold.Rate, Basis).Rate;
                    Assert.InRange(rate, 1, Basis - 1);
                    steepest = Math.Max(steepest, rate);
                    shallowest = Math.Min(shallowest, rate);
                }
            }
        }

        Assert.Equal(8375, steepest);
        Assert.Equal(2000, shallowest);
    }

    /// <summary>
    /// <b>Every maze a real dungeon lays out still carries a chance, whatever its room count.</b>
    /// </summary>
    /// <remarks>
    /// Written against dungeon 144 -- the one this subject's captures were taken in -- and against
    /// the room counts the archive really gives its five mazes rather than a fixture's one or
    /// three. Three of the five hold more rooms than the map-count table declares, so this is the
    /// case that a fixture built at one to five rooms cannot reach.
    /// </remarks>
    [SkippableFact]
    public async Task Every_maze_of_an_ordinary_dungeon_carries_a_chance()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateContentProvider();
        var table = Table(provider);
        var service = Service(
            provider,
            ClearRewardFixture.Pool(
                new ClearRewardFixture.PoolRow(EndgameLevel, DropRarity.Common, 114000088, 1000)));

        var dungeon = await provider.GetRequiredService<IDungeonDefinitionSource>()
            .GetAsync(OrdinaryDungeonId, CancellationToken.None);
        Assert.NotNull(dungeon);

        var roomCounts = dungeon.Mazes.Select(maze => maze.Cells.Count).ToArray();
        Assert.Equal([10, 6, 4, 6, 4], roomCounts);

        // The two sides of the same path: the counts the table prices, and the counts it does not.
        Assert.Equal([4, 4], roomCounts.Where(c => table.MapCountRate(c) is not null).ToArray());
        Assert.Equal([10, 6, 6], roomCounts.Where(c => table.MapCountRate(c) is null).ToArray());

        // Every maze, on both faces: something can still come out. Until this revision the three
        // mazes the table has no row for answered nothing at all, on both faces, on every clear.
        foreach (var rooms in roomCounts)
        {
            foreach (var kind in new[] { ClearRewardCardKind.Free, ClearRewardCardKind.Paid })
            {
                var occasion =
                    new ClearRewardOccasion(kind, EndgameLevel, EndgameLevel, (DungeonDifficulty)3, rooms, 1);
                Assert.False(
                    service.ChanceOf(occasion).IsImpossible,
                    $"{kind} at {rooms} rooms carried nothing");
                Assert.Contains(
                    Enumerable.Range(0, 64)
                        .SelectMany(seed => service.FaceFor(occasion, (ulong)seed).Slots),
                    slot => !slot.IsEmpty);
            }
        }

        // The two sides differ rather than happening to agree: a maze with a row of its own is
        // priced by that row, and a maze past the table by the largest row there is.
        int Free(int rooms) => service.ChanceOf(
            new ClearRewardOccasion(
                ClearRewardCardKind.Free, EndgameLevel, EndgameLevel, (DungeonDifficulty)3, rooms, 1)).Rate;
        Assert.Equal(6400, Free(4));
        Assert.Equal(7400, Free(6));
        Assert.Equal(7400, Free(10));
    }

    /// <summary>
    /// <b>A maze larger than the map-count table declares is priced at the table's largest row.</b>
    /// </summary>
    /// <remarks>
    /// The ordinary case rather than an edge, and the one the whole revision turns on: the archive
    /// prices one room through five and real mazes hold more -- dungeon 144's five hold ten, six,
    /// four, six and four -- so three of its five mazes have no row of their own.
    /// <para>
    /// The figures are the archive's own numbers put through the composition by hand. The largest
    /// row the table declares pays 6000, the third difficulty adds 1400, and the <c>default</c>
    /// profile's threshold is 10000; a party of one leaves the dice unscaled, so the 2600 dice
    /// values from one to 2600 fall short and the remaining 7400 pass. A four-room maze has a row
    /// of its own paying 5000, which leaves 3600 short and 6400 through.
    /// </para>
    /// </remarks>
    [SkippableFact]
    public async Task A_maze_larger_than_the_table_declares_is_priced_at_its_largest_row()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateContentProvider();
        var table = Table(provider);
        var service = Service(provider, ClearRewardFixture.Pool());

        var dungeon = await provider.GetRequiredService<IDungeonDefinitionSource>()
            .GetAsync(OrdinaryDungeonId, CancellationToken.None);
        Assert.NotNull(dungeon);

        var roomCounts = dungeon.Mazes.Select(maze => maze.Cells.Count).ToArray();
        Assert.Equal([10, 6, 4, 6, 4], roomCounts);
        Assert.Equal([10, 6, 6], roomCounts.Where(c => table.MapCountRate(c) is null).ToArray());

        int Free(int rooms) => service.ChanceOf(
                new ClearRewardOccasion(
                    ClearRewardCardKind.Free, EndgameLevel, EndgameLevel, (DungeonDifficulty)3, rooms, 1))
            .Rate;

        Assert.Equal(7400, Free(10));
        Assert.Equal(7400, Free(6));
        Assert.Equal(7400, Free(5));
        Assert.Equal(6400, Free(4));
    }

    /// <summary>
    /// A face drawn from the real rate table and the real equipment pool.
    /// </summary>
    /// <remarks>
    /// The only test here that reads the equipment family, and the only one that proves the whole
    /// path: the archive's odds decide which positions carry, the archive's ladder decides at what
    /// quality, the archive's grade band bounds what may be drawn, and the pieces that come out
    /// are pieces the pool actually holds inside that band at that quality.
    /// <para>
    /// Swept over seeds because the odds are a fraction: one seed proves nothing about a chance of
    /// roughly three positions in four.
    /// </para>
    /// </remarks>
    [SkippableFact]
    public void A_face_drawn_from_the_real_pool_stays_inside_the_declared_band()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateContentProvider();
        var table = Table(provider);
        var pool = provider.GetRequiredService<IDropItemPoolSource>().Load(DropItemFamily.Equipment);
        var service = new ClearRewardRuleService(
            provider.GetRequiredService<IClearRewardRateSource>(),
            provider.GetRequiredService<IGoldDropTableSource>(),
            provider.GetRequiredService<IDropItemPoolSource>(),
            provider.GetRequiredService<IDungeonDefinitionSource>(),
            new ClearRewardRateOptions { RateBasis = Basis });

        var grades = table.GradeBandFor(EndgameLevel);
        Assert.NotNull(grades);

        var occasion = new ClearRewardOccasion(
            ClearRewardCardKind.Free, EndgameLevel, EndgameLevel, (DungeonDifficulty)3, 5, 1);
        var drawn = Enumerable.Range(0, 400)
            .SelectMany(seed => service.FaceFor(occasion, (ulong)seed).Slots)
            .Where(slot => !slot.IsEmpty)
            .Select(slot => slot.Item!.Value)
            .ToList();

        // Roughly the composed chance of 7400 in 10000, over 1600 positions -- some 1184 -- less
        // whatever the ladder rolls into a band the pool holds nothing at. Wide enough that the
        // seeds are not being asserted, narrow enough that a composition off by a factor fails.
        Assert.Equal(7400, service.ChanceOf(occasion).Rate);
        Assert.InRange(drawn.Count, 900, 1300);

        Assert.All(
            drawn,
            item =>
            {
                Assert.Equal(1, item.Quantity);
                Assert.Contains(
                    pool.Band(grades.Value, item.Rarity),
                    entry => entry.ItemId == item.ItemId && entry.Durability == item.Durability);
            });

        // The same occasion asked twice answers the same face, archive and all.
        Assert.Equal(
            ClearRewardFixture.Fingerprint(service.FaceFor(occasion, 7)),
            ClearRewardFixture.Fingerprint(service.FaceFor(occasion, 7)));
    }

    /// <summary>
    /// <b>One dungeon gives one grade band and one price, whatever level the character is.</b>
    /// </summary>
    /// <remarks>
    /// The hole this closes is a gear tap rather than a rounding error: priced and banded by the
    /// character, a level 86 character clearing a dungeon the archive sets at level 5 came away
    /// with level 86 equipment for a level 5 clear, and was charged a level 86 price for it. Both
    /// tables are the dungeon's to key -- see <c>ClearRewardOccasion.DungeonBasisLevel</c> -- and
    /// this is what says so against the real file.
    /// <para>
    /// Dungeon 144 is the one this subject's captures were taken in, and the archive gives it
    /// <c>[basis level] 5</c>. Every figure below is read off the archive beside the assertion
    /// that uses it, and the two character levels are asserted to be priced and banded differently
    /// from each other first -- without that, a server that had keyed both tables on nothing at
    /// all would satisfy every equality here.
    /// </para>
    /// <para>
    /// The faces are compared position by position rather than only the price. Both settlements
    /// draw from the same seed -- it is the run's, and the run is the same -- so a band that moved
    /// with the character moves the pieces that come out, and a comparison of prices alone would
    /// miss the half of this that hands out equipment.
    /// </para>
    /// </remarks>
    [SkippableFact]
    public async Task One_dungeon_bands_and_prices_alike_for_every_character_level()
    {
        await TestGameplay.RunAsync(async () =>
        {
            Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
            using var provider = ContentArchive.CreateContentProvider();
            var table = Table(provider);
            var pool = provider.GetRequiredService<IDropItemPoolSource>()
                .Load(DropItemFamily.Equipment);
            var rules = new ClearRewardRuleService(
                provider.GetRequiredService<IClearRewardRateSource>(),
                provider.GetRequiredService<IGoldDropTableSource>(),
                provider.GetRequiredService<IDropItemPoolSource>(),
                provider.GetRequiredService<IDungeonDefinitionSource>(),
                new ClearRewardRateOptions { RateBasis = Basis });

            var dungeon = await provider.GetRequiredService<IDungeonDefinitionSource>()
                .GetAsync(OrdinaryDungeonId, CancellationToken.None);
            Assert.NotNull(dungeon);

            // What the archive declares about this dungeon and about that level of it.
            Assert.Equal(5, dungeon.BasisLevel);
            Assert.Equal(580, table.GoldCardCost(dungeon.BasisLevel));
            Assert.Equal(new ItemGradeBand(2, 8), table.GradeBandFor(dungeon.BasisLevel));

            // And that the two character levels below are neither priced nor banded like it, so the
            // equalities that follow have something to fail against.
            Assert.Equal(10300, table.GoldCardCost(EndgameLevel));
            Assert.Equal(2380, table.GoldCardCost(LowLevel));
            Assert.NotEqual(table.GradeBandFor(dungeon.BasisLevel), table.GradeBandFor(EndgameLevel));
            Assert.NotEqual(table.GradeBandFor(dungeon.BasisLevel), table.GradeBandFor(LowLevel));

            var band = table.GradeBandFor(dungeon.BasisLevel);
            Assert.NotNull(band);

            var drawn = 0;
            foreach (var seed in new[] { 1, 2, 3, 4, 5, 6, 7, 8 })
            {
                var settlements = new List<(int? Price, string Faces)>();
                foreach (var characterLevel in new[] { LowLevel, EndgameLevel })
                {
                    var runs = TestParties.Runs();
                    runs.Begin(Character, LayoutOf(dungeon, MazeRooms, seed), difficulty: (DungeonDifficulty)3);
                    var settled = await new CardRewardDomainService(rules, runs, new Dfo.Server.Application.Tests.Support.CardPremiums(), dimensions: TestRequired.Absent<Dfo.Server.Abstractions.World.IDimensionDungeonRuleService>()).SettleAsync(
                        new ClearRewardSettlement(Character, characterLevel), CancellationToken.None);

                    Assert.NotNull(settled);
                    settlements.Add(
                        (settled.PaidFlipCost,
                            ClearRewardFixture.Fingerprint(settled.Cards.Free)
                            + " || "
                            + ClearRewardFixture.Fingerprint(settled.Cards.Paid)));

                    // Whatever came out came out of the level 5 band, in the pool the archive holds.
                    foreach (var item in settled.Cards.Free.Slots.Concat(settled.Cards.Paid.Slots)
                                 .Where(slot => !slot.IsEmpty)
                                 .Select(slot => slot.Item!.Value))
                    {
                        drawn++;
                        Assert.Contains(
                            pool.Band(band.Value, item.Rarity),
                            entry => entry.ItemId == item.ItemId);
                    }
                }

                Assert.Equal(580, settlements[0].Price);
                Assert.Single(settlements.Select(settled => settled.Price).Distinct());
                Assert.Single(settlements.Select(settled => settled.Faces).Distinct());
            }

            // Not a comparison of two empty faces: the eight runs really handed pieces over.
            Assert.True(drawn > 0, "no position of any of these faces carried anything");
        });
    }

    /// <summary>
    /// A maze of that dungeon, laid out over its own basis level and its own identifier.
    /// </summary>
    /// <remarks>
    /// The rooms are strung in a line rather than taken from the archive's maze, because what is
    /// under test is which level the tables are keyed on and the shape of a maze has no bearing on
    /// that. What does come off the archive is the basis level, which is the fact the whole test
    /// is about.
    /// </remarks>
    private static DungeonLayout LayoutOf(DungeonDefinition dungeon, int rooms, int seed)
    {
        var cells = Enumerable.Range(0, rooms).Select(index => new MazeCell((byte)index, 0));
        return new DungeonLayout(
            DungeonId: dungeon.DungeonId,
            MazeIndex: 0,
            Width: (byte)rooms,
            Height: 1,
            StartCell: new MazeCell(0, 0),
            BossCell: new MazeCell((byte)(rooms - 1), 0),
            StartRoomMapId: 0,
            Seed: seed,
            Cells: cells.ToDictionary(cell => cell, _ => DungeonCellDefinition.Fixed(0)),
            BasisLevel: dungeon.BasisLevel,
            ExperienceWeight: dungeon.ExperienceWeight);
    }

    /// <summary>
    /// The three numbers a card's gold is built from are the ones the archive declares.
    /// </summary>
    /// <remarks>
    /// <b>Every factor in that figure has to be pointed at a line of the archive.</b> The base and
    /// the band come from <c>[gold drop ref table]</c> in the shared file; the multiplier comes
    /// from <c>[dungeon difficulty gold drop bonusrate]</c> in the clear-reward file itself. This
    /// case is what keeps that claim honest, and it is the case that would fail if a factor with
    /// no archive behind it were ever quietly folded in -- which is exactly how the A21 reference
    /// server writes the same figure, with a bare <c>175/1000</c> that appears nowhere in the file.
    /// </remarks>
    [SkippableFact]
    public void The_archive_declares_the_numbers_a_cards_gold_is_built_from()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateContentProvider();

        // The clear-reward file's own multipliers: one per difficulty, and the same five
        // difficulties its sibling section declares. They are scaled, not summed, which is what
        // the lowest entry being above one says.
        var table = Table(provider);
        Assert.Equal(
            [1.02, 1.38, 1.6, 1.9, 2.0],
            table.DifficultyGoldRates.Select(rate => Math.Round(rate, 4)));
        Assert.Equal(table.DifficultyRates.Count, table.DifficultyGoldRates.Count);

        // The shared file's per-level bases and the band each level moves inside. Three levels
        // apart from one another, so a reader that returned a constant or read the wrong column
        // could not pass.
        var gold = provider.GetRequiredService<IGoldDropTableSource>()
            .Load(DropRateTables.ClearReward);
        Assert.Equal(17, gold.BaseFor(1));
        Assert.Equal(44, gold.BaseFor(5));
        Assert.Equal(77, gold.BaseFor(9));
        // <b>The band is banded.</b> Reading this column as one figure passes every check taken
        // at a low level and is wrong for every endgame one, so all three of the archive's bands
        // are pinned here along with the two levels the value changes at.
        Assert.Equal(13, gold.VarianceFor(1));
        Assert.Equal(13, gold.VarianceFor(64));
        Assert.Equal(11, gold.VarianceFor(65));
        Assert.Equal(11, gold.VarianceFor(77));
        Assert.Equal(9, gold.VarianceFor(78));
        Assert.Equal(9, gold.VarianceFor(86));
        Assert.Equal(9, gold.VarianceFor(200));

        // The bases at those same levels, so a reader that returned the band where the base goes
        // -- or the other way about -- cannot pass either.
        Assert.Equal(977, gold.BaseFor(65));
        Assert.Equal(1196, gold.BaseFor(78));
        Assert.Equal(1357, gold.BaseFor(86));
        Assert.Equal(4877, gold.BaseFor(200));

        // A level the file does not reach carries neither, which is what makes the rule's own
        // refusal to pay for one reachable rather than theoretical.
        Assert.Equal(0, gold.BaseFor(0));
        Assert.Equal(0, gold.VarianceFor(0));
    }

}
