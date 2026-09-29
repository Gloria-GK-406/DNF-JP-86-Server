using Dfo.Server.Data.World;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.World;

/// <summary>
/// One clear's card face, end to end but without the archive.
/// </summary>
/// <remarks>
/// The tables here are hand-built so that a position's odds are either certain or impossible,
/// which makes "these positions held something and those did not" a statement about the
/// arrangement rather than about a seed. The archive-shaped versions of the same numbers are
/// asserted in <see cref="ClearRewardArchiveTests"/>.
/// </remarks>
public sealed class ClearRewardRuleServiceTests
{
    [Fact]
    public void Black_diamond_uses_pcroom_profiles_and_never_pays_extra_gold()
    {
        var table = ClearRewardFixture.Table() with
        {
            Profiles = [
                new(ClearRewardProfiles.Default, [new(1, 200, 20000)]),
                new(ClearRewardProfiles.PcRoomDefault, [new(1, 200, 9000)]),
                new(ClearRewardProfiles.PcRoomEvent, [new(1, 200, 8000)]),
                new(ClearRewardProfiles.PcRoomBonus, [new(1, 200, 7000)]),
            ],
        };
        var service = ClearRewardFixture.Service(table, OnePiece());
        var ordinary = ClearRewardFixture.Occasion(difficulty: 0);
        Assert.Equal(0, service.ChanceOf(ordinary).Rate);
        Assert.Equal(7000, service.ChanceOf(ordinary with { HasBlackDiamond = true }).Rate);
        Assert.Equal(8000, service.ChanceOf(ordinary with { Kind = ClearRewardCardKind.Paid, HasBlackDiamond = true }).Rate);
        var bonus = ordinary with { Kind = ClearRewardCardKind.BlackDiamond, HasBlackDiamond = true };
        Assert.Equal(9000, service.ChanceOf(bonus).Rate);
        Assert.Equal(0, service.FaceFor(bonus, Seed).Gold);
    }

    [Fact]
    public void Guaranteed_paid_equipment_does_not_change_free_cards()
    {
        var normal = ClearRewardFixture.Service(ClearRewardFixture.Table(), OnePiece());
        var guaranteed = ClearRewardFixture.Service(ClearRewardFixture.Table(), OnePiece(), guaranteePaidEquipment: true);
        for (ulong seed = 1; seed <= 50; seed++)
        {
            Assert.All(guaranteed.FaceFor(ClearRewardFixture.Occasion(ClearRewardCardKind.Paid), seed).Slots,
                slot => Assert.NotNull(slot.Item));
            Assert.Equal(normal.FaceFor(ClearRewardFixture.Occasion(), seed).Slots,
                guaranteed.FaceFor(ClearRewardFixture.Occasion(), seed).Slots);
        }
    }

    /// <summary>An arbitrary seed. Nothing about the assertions depends on which one.</summary>
    private const ulong Seed = 0x0BAD_F00D_C0FF_EE01;

    /// <summary>One position, certain, drawing the only piece the pool holds.</summary>
    private static DropItemPool OnePiece(int itemId = 114000088, ushort durability = 7700) =>
        ClearRewardFixture.Pool(
            new ClearRewardFixture.PoolRow(86, DropRarity.Common, itemId, 1000, durability));

    /// <summary>One call answers a whole face, and the face is four positions.</summary>
    /// <remarks>
    /// The shape that makes "the same position of the same clear holds the same thing" free: a
    /// caller that can only ask for the whole face cannot ask twice for one position and be told
    /// two different things.
    /// </remarks>
    [Fact]
    public void One_call_answers_four_positions()
    {
        var service = ClearRewardFixture.Service(ClearRewardFixture.Table(), OnePiece());

        var face = service.FaceFor(ClearRewardFixture.Occasion(), Seed);

        Assert.Equal(4, ClearRewardRules.CardSlotCount);
        Assert.Equal(4, face.Slots.Count);
        Assert.Equal([0, 1, 2, 3], face.Slots.Select(slot => slot.Index));
        Assert.Equal(ClearRewardCardKind.Free, face.Kind);
    }

    /// <summary>
    /// <b>The lowest difficulty adds nothing and empties nothing.</b>
    /// </summary>
    /// <remarks>
    /// The archive's difficulty table opens with a zero, and under an additive composition that
    /// zero leaves the maze's own base rate standing rather than cancelling it. Here the base is
    /// 6000 against a threshold of 10000, so 6000 of the ten thousand dice values carry a position
    /// and the rest do not.
    /// <para>
    /// Swept over many seeds rather than asserted on one, and both outcomes are required to appear:
    /// a chance of six in ten that only ever missed, or only ever hit, would not be one.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_lowest_difficulty_adds_nothing_and_empties_nothing()
    {
        var service = ClearRewardFixture.Service(ClearRewardFixture.Table(), OnePiece());
        var occasion = ClearRewardFixture.Occasion(
            difficulty: ClearRewardFixture.LowestDifficulty);

        Assert.Equal(ClearRewardFixture.LowestDifficultyRate, service.ChanceOf(occasion).Rate);
        Assert.False(service.ChanceOf(occasion).IsImpossible);

        var slots = new List<ClearRewardCardSlot>();
        for (var seed = 0UL; seed < 500; seed++)
        {
            var face = service.FaceFor(occasion, seed);
            Assert.Equal(4, face.Slots.Count);
            slots.AddRange(face.Slots);
        }

        Assert.Contains(slots, slot => !slot.IsEmpty);
        Assert.Contains(slots, slot => slot.IsEmpty);

        // And the same arrangement one difficulty up fills every one of them, so what is measured
        // above is the difficulty and not the fixture.
        Assert.All(
            service.FaceFor(ClearRewardFixture.Occasion(), Seed).Slots,
            slot => Assert.False(slot.IsEmpty));
    }

    /// <summary>The same occasion and the same seed produce the same face, position by position.</summary>
    [Fact]
    public void The_same_occasion_and_seed_produce_the_same_face()
    {
        var service = ClearRewardFixture.Service(
            ClearRewardFixture.Table(),
            ClearRewardFixture.Pool(
                new ClearRewardFixture.PoolRow(86, DropRarity.Common, 1001, 1000),
                new ClearRewardFixture.PoolRow(86, DropRarity.Common, 1002, 1000),
                new ClearRewardFixture.PoolRow(86, DropRarity.Common, 1003, 1000),
                new ClearRewardFixture.PoolRow(86, DropRarity.Common, 1004, 1000)));
        var occasion = ClearRewardFixture.Occasion();

        var first = service.FaceFor(occasion, Seed);
        var second = service.FaceFor(occasion, Seed);

        Assert.Equal(first.Slots, second.Slots);
        Assert.Equal(
            ClearRewardFixture.Fingerprint(first), ClearRewardFixture.Fingerprint(second));

        // A face that never varied would satisfy the above without meaning anything, so: these
        // seeds do not all agree, and where they disagree is inside the positions rather than in
        // how many there are.
        var faces = Enumerable.Range(1, 16)
            .Select(seed => service.FaceFor(occasion, (ulong)seed))
            .ToList();
        Assert.True(faces.Select(ClearRewardFixture.Fingerprint).Distinct().Count() > 1);
        Assert.All(faces, face => Assert.Equal(4, face.Slots.Count));
    }

    /// <summary>
    /// <b>Neither the kind of a face nor the dungeon's own level reaches the odds.</b>
    /// </summary>
    /// <remarks>
    /// Two absences, and an absence is only worth asserting where the thing is present to be read.
    /// The fixture declares an <c>event</c> profile at half the threshold and a
    /// <c>[gold card create rate]</c>, so a paid face drawn at either would come out a different
    /// number; and the occasion carries a dungeon basis level, which is a real key on this path --
    /// it decides the grade band and the price -- so a composition that had reached for it would
    /// be reaching for something that exists.
    /// <para>
    /// The lowest difficulty is where the profile shows up. Measured against the default
    /// threshold of 10000 a position carries 6000 times in ten thousand; measured against the
    /// event profile's 5000 the map base alone would clear the bar and no position could miss.
    /// </para>
    /// <para>
    /// Which level the threshold band <b>is</b> taken at is the other half of this, and is
    /// <see cref="The_threshold_band_is_the_characters_and_the_grade_band_the_dungeons"/>: this
    /// fixture's one band spans every level, so nothing here could tell the two apart on its own.
    /// </para>
    /// </remarks>
    [Fact]
    public void Neither_the_kind_nor_the_dungeons_level_reaches_the_odds()
    {
        var service = ClearRewardFixture.Service(ClearRewardFixture.Table(), OnePiece());

        foreach (var kind in new[] { ClearRewardCardKind.Free, ClearRewardCardKind.Paid })
        {
            Assert.Equal(
                ClearRewardFixture.Basis,
                service.ChanceOf(ClearRewardFixture.Occasion(kind)).Rate);

            // The bar a face is measured against is the default profile's, on both kinds.
            Assert.Equal(
                ClearRewardFixture.LowestDifficultyRate,
                service.ChanceOf(
                        ClearRewardFixture.Occasion(
                            kind, difficulty: ClearRewardFixture.LowestDifficulty))
                    .Rate);

            // And the dungeon's own level, moved right across the table the profile covers and
            // out past the end of it: the same answer throughout, on both kinds.
            foreach (var basisLevel in new[] { 1, 42, ClearRewardFixture.EndgameLevel, 201 })
            {
                Assert.Equal(
                    ClearRewardFixture.Basis,
                    service.ChanceOf(
                            ClearRewardFixture.Occasion(kind, dungeonBasisLevel: basisLevel))
                        .Rate);
            }
        }
    }

    /// <summary>
    /// <b>With every fallback switch off, the seed is the only thing that separates the two faces
    /// of one clear.</b>
    /// </summary>
    /// <remarks>
    /// Since the two kinds carry the same odds and share a ladder, a grade band and a pool, one
    /// seed asked for both answers the same four positions twice -- <b>with every fallback switch
    /// off</b>, which is this fixture's state and this server's. A switched-on paid card parts
    /// them, and that is
    /// <see cref="Blank_items_are_what_a_switched_on_paid_card_hands_out"/>. So the agreement here
    /// is a property a caller has to know about rather than one it may lean on -- the settlement
    /// seam hands the two faces different seeds, and this is why it must.
    /// <para>
    /// Asserted on drawn positions rather than on the odds, because what a caller sees is the face:
    /// the two come out equal position for position, and -- the switches being off -- only
    /// changing the seed moves them apart.
    /// </para>
    /// </remarks>
    [Fact]
    public void One_seed_answers_the_same_face_for_both_kinds()
    {
        var service = ClearRewardFixture.Service(
            ClearRewardFixture.Table(),
            ClearRewardFixture.Pool(
                new ClearRewardFixture.PoolRow(86, DropRarity.Common, 1001, 1000),
                new ClearRewardFixture.PoolRow(86, DropRarity.Common, 1002, 1000),
                new ClearRewardFixture.PoolRow(86, DropRarity.Common, 1003, 1000),
                new ClearRewardFixture.PoolRow(86, DropRarity.Common, 1004, 1000)));

        var free = service.FaceFor(ClearRewardFixture.Occasion(ClearRewardCardKind.Free), Seed);
        var paid = service.FaceFor(ClearRewardFixture.Occasion(ClearRewardCardKind.Paid), Seed);

        // The kind travels with the face, and with every fallback switch off decides nothing else
        // about it.
        Assert.Equal(ClearRewardCardKind.Free, free.Kind);
        Assert.Equal(ClearRewardCardKind.Paid, paid.Kind);
        Assert.Equal(free.Slots, paid.Slots);
        Assert.Equal(
            ClearRewardFixture.Fingerprint(free), ClearRewardFixture.Fingerprint(paid));

        // Over many seeds, and not because every face is alike: these faces do vary.
        Assert.All(
            Enumerable.Range(0, 64),
            seed => Assert.Equal(
                ClearRewardFixture.Fingerprint(
                    service.FaceFor(
                        ClearRewardFixture.Occasion(ClearRewardCardKind.Free), (ulong)seed)),
                ClearRewardFixture.Fingerprint(
                    service.FaceFor(
                        ClearRewardFixture.Occasion(ClearRewardCardKind.Paid), (ulong)seed))));

        // And the seed does separate two faces, which is what the caller's job rests on.
        Assert.NotEqual(
            ClearRewardFixture.Fingerprint(free),
            ClearRewardFixture.Fingerprint(
                service.FaceFor(ClearRewardFixture.Occasion(ClearRewardCardKind.Paid), Seed + 1)));
    }

    /// <summary>
    /// A drawn piece comes from the grade band the table declares, and only from it.
    /// </summary>
    /// <remarks>
    /// The pool holds four pieces of the rolled quality, one just under the band, two inside it
    /// and one just over. Only the two inside may ever come out, and both of them do -- which is
    /// what says the band is gathered across its grades rather than sampled at one of them.
    /// </remarks>
    [Fact]
    public void A_piece_comes_from_the_declared_grade_band()
    {
        var service = ClearRewardFixture.Service(
            ClearRewardFixture.Table(grades: new ItemGradeBand(79, 89)),
            ClearRewardFixture.Pool(
                new ClearRewardFixture.PoolRow(78, DropRarity.Common, 7800, 1000),
                new ClearRewardFixture.PoolRow(79, DropRarity.Common, 7900, 1000),
                new ClearRewardFixture.PoolRow(89, DropRarity.Common, 8900, 1000),
                new ClearRewardFixture.PoolRow(90, DropRarity.Common, 9000, 1000)));

        var drawn = Enumerable.Range(0, 64)
            .SelectMany(seed => service.FaceFor(ClearRewardFixture.Occasion(), (ulong)seed).Slots)
            .Select(slot => slot.Item!.Value.ItemId)
            .ToList();

        Assert.Equal(64 * 4, drawn.Count);
        Assert.Equal([7900, 8900], drawn.Distinct().Order());
    }

    /// <summary>
    /// A piece is drawn by its declared weight, so a weightless one never comes out.
    /// </summary>
    /// <remarks>
    /// The cheapest arrangement that separates a weighted draw from a uniform one: two of the
    /// three pieces declare no creation rate at all, and a draw that ignored weights would produce
    /// them two times in three.
    /// </remarks>
    [Fact]
    public void A_piece_is_drawn_by_its_declared_weight()
    {
        var service = ClearRewardFixture.Service(
            ClearRewardFixture.Table(),
            ClearRewardFixture.Pool(
                new ClearRewardFixture.PoolRow(86, DropRarity.Common, 1001, 0),
                new ClearRewardFixture.PoolRow(86, DropRarity.Common, 1002, 0),
                new ClearRewardFixture.PoolRow(86, DropRarity.Common, 1003, 5)));

        var drawn = Enumerable.Range(0, 64)
            .SelectMany(seed => service.FaceFor(ClearRewardFixture.Occasion(), (ulong)seed).Slots)
            .Select(slot => slot.Item!.Value.ItemId)
            .ToList();

        Assert.Equal(64 * 4, drawn.Count);
        Assert.Equal([1003], drawn.Distinct());
    }

    /// <summary>What a piece is created carrying travels with it.</summary>
    /// <remarks>
    /// The same reason a floor drop carries it: a piece created at zero durability is a broken
    /// one, and the pool already knows the number because the file was open when it was built.
    /// </remarks>
    [Fact]
    public void A_piece_carries_the_durability_it_is_created_with()
    {
        var service = ClearRewardFixture.Service(
            ClearRewardFixture.Table(), OnePiece(itemId: 114000088, durability: 7700));

        var item = service.FaceFor(ClearRewardFixture.Occasion(), Seed).Slots[0].Item;

        Assert.NotNull(item);
        Assert.Equal(114000088, item.Value.ItemId);
        Assert.Equal(1, item.Value.Quantity);
        Assert.Equal((ushort)7700, item.Value.Durability);
    }

    /// <summary>Only the equipment family is asked for; the avatar branch is not drawn.</summary>
    /// <remarks>
    /// The archive's item-type section declares four positions, one of which the specification
    /// reads as fashion. This phase draws the equipment branch and no other, and a rule service
    /// that quietly reached for a second family would be caught here rather than in play.
    /// </remarks>
    [Fact]
    public void Only_the_equipment_family_is_drawn_from()
    {
        var pools = new ClearRewardFixture.RecordingPools(OnePiece());
        var service = new ClearRewardRuleService(
            new ClearRewardFixture.StubRates(ClearRewardFixture.Table()),
            new ClearRewardFixture.StubGoldTables(),
            pools,
            new ClearRewardFixture.StubDungeons(),
            new ClearRewardRateOptions { RateBasis = ClearRewardFixture.Basis });

        service.FaceFor(ClearRewardFixture.Occasion(), Seed);

        Assert.Equal([DropItemFamily.Equipment], pools.Requested.Distinct());
    }

    /// <summary>
    /// An occasion the tables do not describe carries no chance at all.
    /// </summary>
    /// <remarks>
    /// The rate table hands back nothing for an unlisted difficulty, map count, party size or
    /// level, and it does so on purpose: its lowest difficulty declares zero, so "absent" and
    /// "neutral" are not interchangeable there. Answering an unlisted entry at some neighbour's
    /// odds would be indistinguishable from having read the archive's own zero one slot over.
    /// </remarks>
    [Theory]
    [InlineData(86, 9, 3, 1)]
    [InlineData(86, 1, 3, 0)]
    [InlineData(201, 1, 3, 1)]
    public void An_occasion_the_tables_do_not_describe_carries_no_chance(
        int level, int difficulty, int mapMaxCount, int partyMemberCount)
    {
        var service = ClearRewardFixture.Service(ClearRewardFixture.Table(), OnePiece());
        var occasion = ClearRewardFixture.Occasion(
            level: level,
            difficulty: difficulty,
            mapMaxCount: mapMaxCount,
            partyMemberCount: partyMemberCount);

        Assert.True(service.ChanceOf(occasion).IsImpossible);
        Assert.All(service.FaceFor(occasion, Seed).Slots, slot => Assert.True(slot.IsEmpty));
    }

    /// <summary>
    /// <b>A maze the table prices nothing for is not one of those, and never was meant to be.</b>
    /// </summary>
    /// <remarks>
    /// A room count with no row used to be read as a gap and answered impossible, alongside a
    /// level outside every band and a party size the file does not price. It is not a gap: the
    /// archive prices one room through five and real mazes hold more -- dungeon 144's five hold
    /// ten, six, four, six and four -- so reading it as one emptied both faces of most real clears
    /// for nine tasks. Held down here in the small, and against the real archive and the real
    /// dungeon in <see cref="ClearRewardArchiveTests"/>.
    /// <para>
    /// What it falls back to is the largest row the table declares, so a maze past the table pays
    /// exactly what the largest maze the table prices pays and never less than a smaller one.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_maze_the_table_prices_nothing_for_still_carries_a_chance()
    {
        var service = ClearRewardFixture.Service(ClearRewardFixture.Table(), OnePiece());

        // The fixture prices one map count and no other, so every count here is one it has no row
        // for -- which is the same shape the archive has, four rows further along.
        Assert.Equal(
            [ClearRewardFixture.MapCount],
            ClearRewardFixture.Table().MapCountRates.Keys.Order());

        foreach (var rooms in new[] { 4, 6, 10 })
        {
            foreach (var kind in new[] { ClearRewardCardKind.Free, ClearRewardCardKind.Paid })
            {
                var occasion = ClearRewardFixture.Occasion(kind, mapMaxCount: rooms);
                Assert.False(
                    service.ChanceOf(occasion).IsImpossible,
                    $"{kind} at {rooms} rooms carried nothing");

                // And a face really comes out of it.
                Assert.Contains(
                    Enumerable.Range(0, 64)
                        .SelectMany(seed => service.FaceFor(occasion, (ulong)seed).Slots),
                    slot => !slot.IsEmpty);
            }
        }

        // An unpriced maze is priced at the one row the fixture declares -- so it comes to exactly
        // what a three-room maze comes to.
        Assert.Equal(
            ClearRewardFixture.Basis,
            service.ChanceOf(ClearRewardFixture.Occasion(mapMaxCount: 10)).Rate);
        Assert.Equal(
            ClearRewardFixture.Basis,
            service.ChanceOf(
                ClearRewardFixture.Occasion(mapMaxCount: ClearRewardFixture.MapCount)).Rate);

        // At the lowest difficulty, where the map base is what carries a position rather than
        // being swamped by the bonus, the two are still the same number.
        Assert.Equal(
            ClearRewardFixture.LowestDifficultyRate,
            service.ChanceOf(
                    ClearRewardFixture.Occasion(
                        mapMaxCount: 10, difficulty: ClearRewardFixture.LowestDifficulty))
                .Rate);
        Assert.Equal(
            ClearRewardFixture.LowestDifficultyRate,
            service.ChanceOf(
                    ClearRewardFixture.Occasion(
                        mapMaxCount: ClearRewardFixture.MapCount,
                        difficulty: ClearRewardFixture.LowestDifficulty))
                .Rate);
    }

    /// <summary>
    /// A dungeon whose grades the table does not declare draws nothing rather than throwing.
    /// </summary>
    /// <remarks>
    /// The odds and the grade band are keyed on different levels and shaped differently -- the
    /// profile covers bands of the character's level, the grade table covers single levels of the
    /// dungeon's -- so an occasion can carry odds and still have no band to draw from. That is a
    /// quiet nothing, not a clear that fails.
    /// <para>
    /// The character is left at the level the fixture does declare grades for, so the empty face
    /// below is the dungeon's level going unfound and not the character's.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_dungeon_level_with_odds_but_no_grade_band_draws_nothing()
    {
        var service = ClearRewardFixture.Service(ClearRewardFixture.Table(), OnePiece());
        var occasion = ClearRewardFixture.Occasion(dungeonBasisLevel: 42);

        Assert.NotNull(ClearRewardFixture.Table().GradeBandFor(ClearRewardFixture.EndgameLevel));
        Assert.Null(ClearRewardFixture.Table().GradeBandFor(42));
        Assert.False(service.ChanceOf(occasion).IsImpossible);
        Assert.All(service.FaceFor(occasion, Seed).Slots, slot => Assert.True(slot.IsEmpty));
    }

    /// <summary>
    /// The profile the archive calls the default is the bar a clear is measured against.
    /// </summary>
    /// <remarks>
    /// Six are declared and the archive says nothing about when each applies, so the one with no
    /// occasion attached is the only one with a caller. The fixture's other profile declares half
    /// the threshold and is declared first, so a service that reached for whichever profile came
    /// first would come away with it -- and at the lowest difficulty that shows: half the bar is
    /// one the map base clears on its own.
    /// </remarks>
    [Fact]
    public void A_clear_is_measured_against_the_default_profiles_threshold()
    {
        var service = ClearRewardFixture.Service(ClearRewardFixture.Table(), OnePiece());

        Assert.Equal(
            ClearRewardFixture.Basis, service.ChanceOf(ClearRewardFixture.Occasion()).Rate);
        Assert.Equal(
            ClearRewardFixture.LowestDifficultyRate,
            service.ChanceOf(
                    ClearRewardFixture.Occasion(difficulty: ClearRewardFixture.LowestDifficulty))
                .Rate);

        // A table whose default profile declares a lower bar carries more, which is what a
        // threshold is: the same occasion, the same tables, an easier number to pass.
        var lenient = ClearRewardFixture.Service(
            ClearRewardFixture.Table(threshold: ClearRewardFixture.Basis - 2000), OnePiece());
        Assert.Equal(
            8000,
            lenient.ChanceOf(
                    ClearRewardFixture.Occasion(difficulty: ClearRewardFixture.LowestDifficulty))
                .Rate);
    }

    /// <summary>
    /// <b>The rule service is handed nothing that could tell it about the world.</b>
    /// </summary>
    /// <remarks>
    /// Enumerated rather than reasoned about. A rule service that acquired a repository, a
    /// registry, a directory, a store or a domain service would become a second owner of whatever
    /// it acquired, and the failure is invisible from the outside -- the answers would still look
    /// like answers.
    /// </remarks>
    [Fact]
    public void The_rule_service_is_handed_no_reader_of_world_state()
    {
        var constructor = Assert.Single(typeof(ClearRewardRuleService).GetConstructors());
        var dependencies = constructor.GetParameters().Select(parameter => parameter.ParameterType);

        // Four content sources and the options, and nothing else. The dungeon list joined when a
        // dungeon became able to switch its own paid card off, and the gold table joined when a
        // card's gold became an archive reading rather than a figure this server chose; both are
        // content like the first two, read the same way and owned by the same service. What the
        // list may not grow is anything that could answer a question about the running world,
        // which is what the suffix
        // check below is and why it is a separate assertion rather than a comment on this one.
        Assert.Equal(
            [
                typeof(IClearRewardRateSource),
                typeof(IGoldDropTableSource),
                typeof(IDropItemPoolSource),
                typeof(IDungeonDefinitionSource),
                typeof(ClearRewardRateOptions),
            ],
            dependencies);

        string[] forbidden =
            ["Repository", "Registry", "Directory", "Store", "Persistence", "DomainService"];
        Assert.All(
            constructor.GetParameters(),
            parameter => Assert.DoesNotContain(
                forbidden,
                suffix => parameter.ParameterType.Name.EndsWith(suffix, StringComparison.Ordinal)));
    }

    /// <summary>
    /// <b>The threshold band is the character's level, and the grade band is the dungeon's.</b>
    /// </summary>
    /// <remarks>
    /// The split this server keeps between the two tables of one file, held down where it can be
    /// seen. It cannot be seen against the archive: A21's <c>default</c> profile pays 10000 in all
    /// four of its bands, so on that file the two readings answer alike whatever the levels are --
    /// which is exactly the condition under which a wrong key survives forever. The table here
    /// declares a profile whose two bands pay differently and grades for two levels, so each key
    /// moves an answer the other cannot.
    /// <para>
    /// Why the split rather than one level for both is argued on
    /// <see cref="ClearRewardOccasion.CharacterLevel"/>: <c>[drop prob]</c> is six profiles named
    /// for the player's circumstance, where <c>[item drop ref table]</c> and
    /// <c>[gold card cost table]</c> are what a clear is worth and what it costs. Against the real
    /// archive and a real dungeon the second half of that is
    /// <see cref="ClearRewardArchiveTests.One_dungeon_bands_and_prices_alike_for_every_character_level"/>.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_threshold_band_is_the_characters_and_the_grade_band_the_dungeons()
    {
        const int LowBandLevel = 10;
        const int HighBandLevel = 90;
        const int LowGradeLevel = 4;

        // A profile whose two bands pay different rates, and grades declared at two levels. Both
        // keys therefore have somewhere to move to, which the shared fixture's single band and
        // single grade row do not give them.
        var table = ClearRewardFixture.Table() with
        {
            Profiles =
            [
                new ClearRewardProfile(
                    ClearRewardProfiles.Default,
                    [
                        new ClearRewardBand(1, 50, ClearRewardFixture.Basis),
                        new ClearRewardBand(51, 200, ClearRewardFixture.Basis / 2),
                    ]),
            ],
            GradeBands = new Dictionary<int, ItemGradeBand>
            {
                [LowGradeLevel] = new ItemGradeBand(1, 1),
                [ClearRewardFixture.EndgameLevel] = new ItemGradeBand(79, 89),
            },
        };
        var service = ClearRewardFixture.Service(
            table,
            ClearRewardFixture.Pool(
                new ClearRewardFixture.PoolRow(1, DropRarity.Common, 11, 1000),
                new ClearRewardFixture.PoolRow(
                    ClearRewardFixture.EndgameLevel, DropRarity.Common, 86_86, 1000)));

        // The odds move with the character and stand still for the dungeon. Read at the
        // difficulty that adds nothing, because that is where the threshold is what decides: the
        // sum of 6000 has to beat the bar, and the two bands set the bar at 10000 and at 5000.
        int Rate(int characterLevel, int dungeonBasisLevel) => service.ChanceOf(
            ClearRewardFixture.Occasion(
                level: characterLevel,
                difficulty: ClearRewardFixture.LowestDifficulty,
                dungeonBasisLevel: dungeonBasisLevel)).Rate;

        Assert.Equal(ClearRewardFixture.LowestDifficultyRate, Rate(LowBandLevel, LowGradeLevel));
        Assert.Equal(ClearRewardFixture.Basis, Rate(HighBandLevel, LowGradeLevel));
        Assert.Equal(
            Rate(LowBandLevel, LowGradeLevel),
            Rate(LowBandLevel, ClearRewardFixture.EndgameLevel));
        Assert.Equal(
            Rate(HighBandLevel, LowGradeLevel),
            Rate(HighBandLevel, ClearRewardFixture.EndgameLevel));

        // And the pieces move with the dungeon and stand still for the character. The two grade
        // rows hold one piece each, so which of the two comes out names which level was read.
        // Drawn at the difficulty that makes every position certain, so an empty slot cannot be
        // mistaken for a band that held nothing.
        int? Drawn(int characterLevel, int dungeonBasisLevel) => service
            .FaceFor(
                ClearRewardFixture.Occasion(
                    level: characterLevel, dungeonBasisLevel: dungeonBasisLevel),
                Seed)
            .Slots[0].Item?.ItemId;

        Assert.Equal(11, Drawn(LowBandLevel, LowGradeLevel));
        Assert.Equal(11, Drawn(HighBandLevel, LowGradeLevel));
        Assert.Equal(86_86, Drawn(LowBandLevel, ClearRewardFixture.EndgameLevel));
        Assert.Equal(86_86, Drawn(HighBandLevel, ClearRewardFixture.EndgameLevel));
    }

    /// <summary>
    /// What a paid flip costs is the table's own price, and an unpriced level stays unpriced.
    /// </summary>
    /// <remarks>
    /// Both halves. The price is passed through rather than composed -- none of the four bonus
    /// tables is keyed on it, so a service that multiplied it by a difficulty or a party size
    /// would be charging a tariff no file declares -- and a level the table has no row for comes
    /// back as nothing rather than as a free flip, which is the distinction the whole of the frame
    /// side rests on.
    /// <para>
    /// The level asked about is the dungeon's own <c>[basis level]</c>; see
    /// <see cref="IClearRewardRuleService.PaidFlipCost"/>. That this signature takes one number
    /// and no occasion is asserted below, which is what says no difficulty, maze or party size can
    /// reach a price at all -- and so is what that one number is called, because a number named
    /// for the wrong level is how the two readings get mixed up again.
    /// </para>
    /// </remarks>
    [Fact]
    public void What_a_paid_flip_costs_is_the_tables_own_price()
    {
        var service = ClearRewardFixture.Service(ClearRewardFixture.Table(), OnePiece());

        Assert.Equal(
            ClearRewardFixture.PaidFlipCost,
            service.PaidFlipCost(ClearRewardFixture.EndgameLevel));
        Assert.NotEqual(0, service.PaidFlipCost(ClearRewardFixture.EndgameLevel));

        // The fixture prices one level and no other, so every one of these is the table declining
        // to price a flip rather than pricing it at nothing.
        Assert.Null(service.PaidFlipCost(ClearRewardFixture.EndgameLevel + 1));
        Assert.Null(service.PaidFlipCost(ClearRewardFixture.EndgameLevel - 1));
        Assert.Null(service.PaidFlipCost(0));

        // And the price is the level's alone: nothing about the occasion a face is drawn for is
        // asked about, which is what this signature is. The name is pinned beside the count,
        // because at the interface the name is the only thing that says which of the two levels a
        // caller is meant to hand in -- and a stub in the protocol tests had gone on calling it
        // the character's for two revisions after the reading moved to the dungeon's.
        var priced = Assert.Single(
            typeof(IClearRewardRuleService)
                .GetMethod(nameof(IClearRewardRuleService.PaidFlipCost))!
                .GetParameters());
        Assert.Equal("dungeonBasisLevel", priced.Name);
    }

    /// <summary>The service is registered, and its basis comes from configuration.</summary>
    /// <remarks>
    /// The registration is what makes the four content sources reach it and nothing else does;
    /// the configured basis is what makes the named assumption an operator's dial rather than a
    /// constant hidden in the rules. Four, as
    /// <see cref="The_rule_service_is_handed_no_reader_of_world_state"/> pins by reflection and the
    /// <c>Build</c> below registers: the dungeon list joined the rate table and the pool when a
    /// dungeon became able to switch its own paid card off, and the gold table joined when a
    /// card's gold became an archive reading.
    /// </remarks>
    [Fact]
    public void The_registration_wires_the_service_and_reads_the_basis()
    {
        using var defaulted = Build(null);
        Assert.IsType<ClearRewardRuleService>(
            defaulted.GetRequiredService<IClearRewardRuleService>());
        Assert.Equal(10000, defaulted.GetRequiredService<ClearRewardRateOptions>().RateBasis);

        using var configured = Build("2500");
        Assert.Equal(2500, configured.GetRequiredService<ClearRewardRateOptions>().RateBasis);

        // A basis that is not a positive number is an operator's typo, and the server boots on the
        // documented default rather than on a division by zero.
        using var mistyped = Build("nought");
        Assert.Equal(10000, mistyped.GetRequiredService<ClearRewardRateOptions>().RateBasis);

        static ServiceProvider Build(string? basis)
        {
            var services = new ServiceCollection();
            new ApplicationModule().ConfigureServices(
                services,
                new ConfigurationBuilder()
                    .AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["ClearRewardRates:RateBasis"] = basis,
                        })
                    .Build());
            services.AddSingleton<IClearRewardRateSource>(
                new ClearRewardFixture.StubRates(ClearRewardFixture.Table()));
            services.AddSingleton<IDropItemPoolSource>(
                new ClearRewardFixture.RecordingPools(
                    ClearRewardFixture.Pool(
                        new ClearRewardFixture.PoolRow(86, DropRarity.Common, 1, 1))));
            services.AddSingleton<IDungeonDefinitionSource>(new ClearRewardFixture.StubDungeons());
            services.AddSingleton<IGoldDropTableSource>(new ClearRewardFixture.StubGoldTables());
            return services.BuildServiceProvider();
        }
    }

    /// <summary>
    /// A pool that holds nothing, so a position whose odds come in still comes up empty.
    /// </summary>
    /// <remarks>
    /// The shape the fallback exists for, produced on purpose: the odds carry, the ladder rolls, a
    /// grade band is found, and the draw finds no piece at that quality. It is an ordinary outcome
    /// on the archive too -- the ladder's rarest bands are one roll wide and a band can hold
    /// nothing at the quality rolled.
    /// </remarks>
    private static DropItemPool NoPiece() => ClearRewardFixture.Pool();

    /// <summary>
    /// <b>Every one of the three fallback switches is off, and each is its own switch.</b>
    /// </summary>
    /// <remarks>
    /// Off is what the server is deployed as, so this asserts the registered options and not only
    /// a freshly built pair of braces. What each switch is off for is on
    /// <see cref="ClearRewardBlankItemSwitches"/>, one reason per card.
    /// <para>
    /// The three are read back through <see cref="ClearRewardBlankItemSwitches.Allows"/> rather
    /// than off their properties, so a mapping that answered the paid card with the free card's
    /// switch would fail here rather than quietly hand out the wrong table.
    /// </para>
    /// </remarks>
    [Fact]
    public void Blank_item_switches_are_all_off_by_default()
    {
        Assert.Equal([false, false, false], AsRead(new ClearRewardBlankItemSwitches()));
        Assert.Equal([false, false, false], AsRead(new ClearRewardRateOptions().BlankItems));

        // Each moves on its own, and moves the card it is named for.
        Assert.Equal(
            [true, false, false],
            AsRead(new ClearRewardBlankItemSwitches { FreeCard = true }));
        Assert.Equal(
            [false, true, false],
            AsRead(new ClearRewardBlankItemSwitches { GoldCard = true }));
        Assert.Equal(
            [false, false, true],
            AsRead(new ClearRewardBlankItemSwitches { PcRoomCard = true }));

        // And the server as it registers itself has all three off: nothing in configuration is
        // read for them, so the deployed default is the one written in the code.
        var services = new ServiceCollection();
        new ApplicationModule().ConfigureServices(services, new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        Assert.Equal(
            [false, false, false],
            AsRead(provider.GetRequiredService<ClearRewardRateOptions>().BlankItems));

        static bool[] AsRead(ClearRewardBlankItemSwitches switches) =>
        [
            switches.Allows(ClearRewardBlankCard.Free),
            switches.Allows(ClearRewardBlankCard.GoldCard),
            switches.Allows(ClearRewardBlankCard.PcRoom),
        ];
    }

    /// <summary>
    /// <b>Switched on, a paid position that came up empty is handed a row of
    /// <c>[gold card blank item]</c>, drawn by the archive's weights.</b>
    /// </summary>
    /// <remarks>
    /// The arrangement makes every position come up empty for a reason the fallback is for: the
    /// odds are certain, so every position is drawn, and the pool holds no piece to draw. What
    /// fills them is therefore the fallback and nothing else.
    /// <para>
    /// All six of the archive's quantities have to appear across the sweep. A rule that handed out
    /// the first row every time would satisfy every assertion about the item and the count and
    /// fail that one, which is the difference between a fallback that is drawn and one that is
    /// taken off the top.
    /// </para>
    /// </remarks>
    [Fact]
    public void Blank_items_are_what_a_switched_on_paid_card_hands_out()
    {
        var service = ClearRewardFixture.Service(
            ClearRewardFixture.Table(
                goldCardBlankItems: ClearRewardFixture.GoldCardBlankItems,
                pcRoomCardBlankItems: ClearRewardFixture.PcRoomCardBlankItems),
            NoPiece(),
            goldCardBlankItems: true);
        var paid = ClearRewardFixture.Occasion(ClearRewardCardKind.Paid);

        var quantities = new SortedSet<int>();
        for (var seed = 0UL; seed < 200; seed++)
        {
            var face = service.FaceFor(paid, seed);
            Assert.Equal(4, face.Slots.Count);
            foreach (var slot in face.Slots)
            {
                Assert.False(slot.IsEmpty);
                var item = slot.Item!.Value;
                Assert.Equal(10002120, item.ItemId);

                // A stackable, which is what the archive's row names: no ladder was rolled for it
                // and it is created carrying no durability.
                Assert.Equal(DropRarity.Common, item.Rarity);
                Assert.Equal(0, item.Durability);
                quantities.Add(item.Quantity);
            }
        }

        Assert.Equal([1, 2, 3, 4, 5, 10], quantities);

        // Reproducible from the seed, as every other face on this path is.
        Assert.Equal(
            ClearRewardFixture.Fingerprint(service.FaceFor(paid, Seed)),
            ClearRewardFixture.Fingerprint(service.FaceFor(paid, Seed)));

        // And the paid card's switch moved the paid card only: the free face of the same
        // arrangement still holds nothing.
        Assert.All(
            service.FaceFor(ClearRewardFixture.Occasion(), Seed).Slots,
            slot => Assert.True(slot.IsEmpty));
    }

    /// <summary>
    /// <b>Switched off, a position that came up empty is handed nothing -- and the face is the one
    /// a table with no fallback rows at all produces, position by position.</b>
    /// </summary>
    /// <remarks>
    /// This is what "off" has to mean: not a smaller payout but the face this server dealt before
    /// the switches existed. The two services differ only in whether their table declares fallback
    /// rows, and their faces are compared as faces -- so a switched-off table that was read, or a
    /// roll spent on one and discarded, would move every position after it and show up here.
    /// <para>
    /// Drawn at the difficulty that leaves the dice something to do, so the sweep holds both
    /// filled and empty positions. A sweep whose positions were all empty, or all filled, would
    /// make the comparison say nothing.
    /// </para>
    /// </remarks>
    [Fact]
    public void Blank_items_are_handed_out_by_nothing_while_the_switches_are_off()
    {
        var stocked = ClearRewardFixture.Service(
            ClearRewardFixture.Table(
                goldCardBlankItems: ClearRewardFixture.GoldCardBlankItems,
                pcRoomCardBlankItems: ClearRewardFixture.PcRoomCardBlankItems),
            OnePiece());
        var bare = ClearRewardFixture.Service(ClearRewardFixture.Table(), OnePiece());

        var slots = new List<ClearRewardCardSlot>();
        foreach (var kind in Enum.GetValues<ClearRewardCardKind>())
        {
            var occasion = ClearRewardFixture.Occasion(
                kind, difficulty: ClearRewardFixture.LowestDifficulty);
            for (var seed = 0UL; seed < 200; seed++)
            {
                var face = stocked.FaceFor(occasion, seed);
                Assert.Equal(
                    ClearRewardFixture.Fingerprint(bare.FaceFor(occasion, seed)),
                    ClearRewardFixture.Fingerprint(face));
                slots.AddRange(face.Slots);
            }
        }

        // The sweep saw both outcomes, and no position of it was ever handed a fallback row.
        Assert.Contains(slots, slot => slot.IsEmpty);
        Assert.Contains(slots, slot => !slot.IsEmpty);
        Assert.DoesNotContain(slots, slot => slot.Item?.ItemId is 10002120 or 7454);
    }

    /// <summary>
    /// <b>The free card has no fallback table in the archive, so its switch hands out nothing.</b>
    /// </summary>
    /// <remarks>
    /// The asymmetry is the file's: <c>etc/itemdropinfo_clearreward.etc</c> declares
    /// <c>[gold card blank item]</c> and <c>[pcroom card blank item]</c> and no third section of
    /// that shape. Lending the free card the paid card's rows would be this server declaring a
    /// fallback the archive does not, so switching the free card on changes nothing here -- see
    /// <see cref="ClearRewardBlankCard.Free"/>.
    /// <para>
    /// Asserted against an arrangement where the paid card's rows <i>are</i> present and the paid
    /// switch <i>would</i> fill the same face, so what is measured is the free card's own absence
    /// of a table rather than a fixture with nothing in it.
    /// </para>
    /// </remarks>
    [Fact]
    public void Blank_items_have_no_free_card_table_to_come_from()
    {
        var table = ClearRewardFixture.Table(
            goldCardBlankItems: ClearRewardFixture.GoldCardBlankItems,
            pcRoomCardBlankItems: ClearRewardFixture.PcRoomCardBlankItems);
        var freeSwitchedOn = ClearRewardFixture.Service(
            table, NoPiece(), freeCardBlankItems: true);

        for (var seed = 0UL; seed < 50; seed++)
        {
            Assert.All(
                freeSwitchedOn.FaceFor(ClearRewardFixture.Occasion(), seed).Slots,
                slot => Assert.True(slot.IsEmpty));

            // And the free switch is the free card's alone: the paid face beside it is untouched.
            Assert.All(
                freeSwitchedOn
                    .FaceFor(ClearRewardFixture.Occasion(ClearRewardCardKind.Paid), seed).Slots,
                slot => Assert.True(slot.IsEmpty));
        }

        // The same table and the same empty pool, with the paid card switched on instead: the
        // rows are there and they do fill a face, which is what makes the emptiness above the free
        // card's missing section rather than an arrangement that could fill nothing.
        Assert.All(
            ClearRewardFixture.Service(table, NoPiece(), goldCardBlankItems: true)
                .FaceFor(ClearRewardFixture.Occasion(ClearRewardCardKind.Paid), Seed).Slots,
            slot => Assert.False(slot.IsEmpty));
    }

    /// <summary>
    /// <b>A face the tables could not answer hands out nothing, switched on or not.</b>
    /// </summary>
    /// <remarks>
    /// <b>The archive's fallback answers "the dice was thrown and this position is still empty",
    /// and throwing the dice presupposes that every table it is thrown against answered.</b> Every
    /// route to an undrawn face is the other thing -- a table that did not answer -- so paying a
    /// fallback there would hand the player four items exactly where this server failed to read
    /// the archive, turning a visible defect into a silent payout. That is the opposite of what
    /// the rest of this path does with a table that says nothing.
    /// <para>
    /// All four routes are taken with the paid card switched on and its rows present. The contrast
    /// at the end is what says the switch really was on: the same service, the same empty pool, on
    /// a table that does answer, fills all four positions.
    /// </para>
    /// </remarks>
    [Fact]
    public void Blank_items_are_not_handed_out_where_the_tables_could_not_answer()
    {
        var table = ClearRewardFixture.Table(
            goldCardBlankItems: ClearRewardFixture.GoldCardBlankItems,
            pcRoomCardBlankItems: ClearRewardFixture.PcRoomCardBlankItems);
        var paid = ClearRewardFixture.Occasion(ClearRewardCardKind.Paid);

        // The file has lost the rarity section altogether.
        AllEmpty(Switched(table with { Rarity = null }), paid);

        // The section is there and declares a ladder no roll can land in.
        AllEmpty(Switched(table with { Rarity = new RarityLadder([0, 0, 0, 0, 0, 0, 1]) }), paid);

        // The grade table declares no row for the dungeon's own level.
        AllEmpty(
            Switched(table),
            ClearRewardFixture.Occasion(
                ClearRewardCardKind.Paid,
                dungeonBasisLevel: ClearRewardFixture.EndgameLevel + 1));

        // The occasion is outside the rate tables, so no odds were composed for it at all.
        AllEmpty(
            Switched(table),
            ClearRewardFixture.Occasion(ClearRewardCardKind.Paid, difficulty: 9));

        // And the switch really was on.
        Assert.All(
            Switched(table).FaceFor(paid, Seed).Slots, slot => Assert.False(slot.IsEmpty));

        static ClearRewardRuleService Switched(ClearRewardTable table) =>
            ClearRewardFixture.Service(table, NoPiece(), goldCardBlankItems: true);

        static void AllEmpty(ClearRewardRuleService service, ClearRewardOccasion occasion)
        {
            for (var seed = 0UL; seed < 50; seed++)
            {
                var face = service.FaceFor(occasion, seed);
                Assert.Equal(ClearRewardRules.CardSlotCount, face.Slots.Count);
                Assert.All(face.Slots, slot => Assert.True(slot.IsEmpty));
            }
        }
    }

    /// <summary>PC-room fallback belongs only to the extra reward, not a selectable card.</summary>
    [Fact]
    public void Pcroom_blank_items_never_leak_into_ordinary_selectable_cards()
    {
        var table = ClearRewardFixture.Table(
            goldCardBlankItems: ClearRewardFixture.GoldCardBlankItems,
            pcRoomCardBlankItems: ClearRewardFixture.PcRoomCardBlankItems);

        // Read: the one row the archive declares, and drawable from.
        Assert.Equal(
            new ClearRewardBlankItem(50, 7454, 1),
            ClearRewardRules.BlankItemOf(table.PcRoomCardBlankItems, new DropRandom(Seed)));

        // Switched: its own switch, which no other card's answer moves.
        Assert.True(
            new ClearRewardBlankItemSwitches { PcRoomCard = true }
                .Allows(ClearRewardBlankCard.PcRoom));

        // And unreachable: a flip carries one of two kinds, and with all three switched on
        // neither of them ever hands out the PC-room card's item.
        ClearRewardCardKind[] flippable = [ClearRewardCardKind.Free, ClearRewardCardKind.Paid];
        Assert.Contains(ClearRewardCardKind.BlackDiamond, Enum.GetValues<ClearRewardCardKind>());

        var everythingOn = ClearRewardFixture.Service(
            table,
            NoPiece(),
            freeCardBlankItems: true,
            goldCardBlankItems: true,
            pcRoomCardBlankItems: true);
        foreach (var kind in flippable)
        {
            for (var seed = 0UL; seed < 50; seed++)
            {
                Assert.DoesNotContain(
                    everythingOn.FaceFor(ClearRewardFixture.Occasion(kind), seed).Slots,
                    slot => slot.Item?.ItemId == 7454);
            }
        }
    }

    /// <summary>
    /// A settled face carries its gold whether or not any of its positions holds anything.
    /// </summary>
    /// <remarks>
    /// <b>The two are decided apart, and this is what says so.</b> A face drawn at no chance at
    /// all comes back with every position empty, and it still has to pay: a free card is a floor,
    /// and paying only when something was drawn is what used to leave an empty flip silent.
    /// </remarks>
    [Theory]
    [InlineData(ClearRewardFixture.Basis)]
    [InlineData(0)]
    public void A_settled_face_carries_its_gold_whether_or_not_it_drew_anything(int threshold)
    {
        var service = ClearRewardFixture.Service(
            ClearRewardFixture.Table(threshold: threshold), ClearRewardFixture.Pool());

        var face = service.FaceFor(ClearRewardFixture.Occasion(), Seed);

        Assert.All(face.Slots, slot => Assert.Null(slot.Item));
        Assert.True(face.Gold > 0, $"a settled face paid {face.Gold}");
    }

    /// <summary>
    /// The gold is the level's base scaled by the difficulty, inside the band the level declares.
    /// </summary>
    [Fact]
    public void The_faces_gold_is_the_levels_base_scaled_by_the_difficulty()
    {
        var service = ClearRewardFixture.Service(
            ClearRewardFixture.Table(), ClearRewardFixture.Pool());

        var unmoved = ClearRewardFixture.GoldBase * ClearRewardFixture.DifficultyGoldRate;
        var percent = ClearRewardFixture.GoldVariancePercent / 100.0;

        for (var seed = 0UL; seed < 50; seed++)
        {
            var face = service.FaceFor(ClearRewardFixture.Occasion(), seed);
            Assert.InRange(
                face.Gold,
                (int)Math.Floor(unmoved * (1 - percent)),
                (int)Math.Ceiling(unmoved * (1 + percent)));
        }
    }

    /// <summary>The same clear settles the same gold every time it is asked.</summary>
    /// <remarks>
    /// Same seed, same figure -- which is what lets a reconnecting client be told the same number
    /// the first settlement announced. Two different seeds are asked as well, so a constant could
    /// not pass this by standing still.
    /// </remarks>
    [Fact]
    public void The_same_clear_settles_the_same_gold_every_time()
    {
        var service = ClearRewardFixture.Service(
            ClearRewardFixture.Table(), ClearRewardFixture.Pool());
        var occasion = ClearRewardFixture.Occasion();

        Assert.Equal(
            service.FaceFor(occasion, Seed).Gold, service.FaceFor(occasion, Seed).Gold);

        var spread = new HashSet<int>();
        for (var seed = 0UL; seed < 50; seed++)
            spread.Add(service.FaceFor(occasion, seed).Gold);

        Assert.True(spread.Count > 1, "every seed settled the same gold");
    }

    /// <summary>
    /// What the gold spends of the generator never reaches the positions.
    /// </summary>
    /// <remarks>
    /// <b>Two faces from one seed, differing only in how much randomness the gold needs.</b> A
    /// level with no band spends nothing on its gold and a level with one spends a value; if the
    /// two shared a generator with the draw, the second face's positions would all be shifted by
    /// that value and this server would have silently redealt every face it has ever dealt.
    /// Asking the same seed twice would not have noticed -- both answers would have been shifted
    /// alike -- so the difference has to be in the gold's own appetite.
    /// </remarks>
    [Fact]
    public void What_the_gold_spends_of_the_generator_never_reaches_the_positions()
    {
        // Several candidates at the same grade and quality, so a generator that had been shifted
        // would pick a different one. A pool of one could not tell the two apart.
        var pool = ClearRewardFixture.Pool(
            [.. Enumerable.Range(0, 8).Select(
                index => new ClearRewardFixture.PoolRow(
                    86, DropRarity.Common, 40_000 + index, 12))]);
        var steady = ClearRewardFixture.Service(
            ClearRewardFixture.Table(),
            pool,
            gold: ClearRewardFixture.GoldTable(variancePercent: 0));
        var moving = ClearRewardFixture.Service(
            ClearRewardFixture.Table(),
            pool,
            gold: ClearRewardFixture.GoldTable(variancePercent: 13));

        for (var seed = 0UL; seed < 20; seed++)
        {
            Assert.Equal(
                steady.FaceFor(ClearRewardFixture.Occasion(), seed)
                    .Slots.Select(slot => slot.Item?.ItemId),
                moving.FaceFor(ClearRewardFixture.Occasion(), seed)
                    .Slots.Select(slot => slot.Item?.ItemId));
        }
    }

    /// <summary>
    /// The gold's base is taken at the dungeon's basis level, not at the character's.
    /// </summary>
    /// <remarks>
    /// <b>The two are the same number in most of this file's cases, which is exactly why this one
    /// names them apart.</b> A character-keyed base is the same defect the grade band and the paid
    /// flip's price were both found to have: an eighty-sixth-level character farming a dungeon
    /// whose basis level is five would be paid the endgame figure -- on this archive 1357 against
    /// 44, some thirty times over -- and nothing on the screen would look wrong.
    /// </remarks>
    [Fact]
    public void The_golds_base_is_taken_at_the_dungeons_level_not_the_characters()
    {
        const int DungeonLevel = 5;
        const int DungeonBase = 44;
        var service = ClearRewardFixture.Service(
            ClearRewardFixture.Table(),
            ClearRewardFixture.Pool(),
            gold: new GoldDropTable(
                new Dictionary<int, int>
                {
                    [DungeonLevel] = DungeonBase,
                    [ClearRewardFixture.EndgameLevel] = 1357,
                },
                new Dictionary<int, int>
                {
                    // The dungeon's level moves the amount not at all and the character's moves it
                    // a long way, so a band read at the wrong level cannot land on the same figure.
                    [DungeonLevel] = 0,
                    [ClearRewardFixture.EndgameLevel] = 50,
                },
                [1],
                (100, 100)));

        var occasion = ClearRewardFixture.Occasion(dungeonBasisLevel: DungeonLevel);
        var expected = (int)Math.Round(DungeonBase * ClearRewardFixture.DifficultyGoldRate);

        // Every seed, because a band read at the character's level would still land on the
        // dungeon's figure for some of them.
        for (var seed = 0UL; seed < 50; seed++)
            Assert.Equal(expected, service.FaceFor(occasion, seed).Gold);
    }

    /// <summary>
    /// The floor is the free card's; a paid flip is not given it on top of the price it paid.
    /// </summary>
    /// <remarks>
    /// The archive prices a paid flip, and a card the player has already bought does not also
    /// carry the figure that exists so that turning a free card over is never silent.
    /// </remarks>
    [Fact]
    public void The_floor_belongs_to_the_free_card_and_not_to_the_paid_one()
    {
        var service = ClearRewardFixture.Service(
            ClearRewardFixture.Table(), ClearRewardFixture.Pool());

        Assert.True(
            service.FaceFor(ClearRewardFixture.Occasion(), Seed).Gold > 0,
            "the free card paid nothing");
        Assert.Equal(
            0,
            service.FaceFor(
                ClearRewardFixture.Occasion(kind: ClearRewardCardKind.Paid), Seed).Gold);
    }

    /// <summary>
    /// Each difficulty scales the gold by its own entry, not by the first one.
    /// </summary>
    /// <remarks>
    /// <b>Every difficulty, because one of them proves nothing.</b> A lookup that always reached
    /// the first entry would pass any case built on a single difficulty, and the amount it
    /// produced would still be a plausible figure that moved with the seed -- the same shape as a
    /// base read at the wrong level. On the archive the five entries run 1.02 to 2, so getting
    /// this wrong halves what the hardest difficulty pays and nothing on the screen says so.
    /// <para>
    /// The band is switched off here so the scaling can be read on its own; that the band itself
    /// is applied is <see cref="The_faces_gold_is_the_levels_base_scaled_by_the_difficulty"/>'s.
    /// </para>
    /// </remarks>
    [Fact]
    public void Each_difficulty_scales_the_gold_by_its_own_entry()
    {
        var service = ClearRewardFixture.Service(
            ClearRewardFixture.Table(),
            ClearRewardFixture.Pool(),
            gold: ClearRewardFixture.GoldTable(variancePercent: 0));

        var paid = new List<int>();
        for (var difficulty = 0; difficulty < ClearRewardFixture.DifficultyGoldRates.Length; difficulty++)
        {
            var face = service.FaceFor(
                ClearRewardFixture.Occasion(difficulty: difficulty), Seed);

            Assert.Equal(
                (int)Math.Round(
                    ClearRewardFixture.GoldBase * ClearRewardFixture.DifficultyGoldRates[difficulty],
                    MidpointRounding.AwayFromZero),
                face.Gold);
            paid.Add(face.Gold);
        }

        // And the five are not all the same figure, which is what makes the case above a check on
        // the lookup rather than on the multiplication alone.
        Assert.Equal(paid.Count, paid.Distinct().Count());
    }

    /// <summary>A difficulty the file does not list pays nothing rather than paying unscaled.</summary>
    [Fact]
    public void A_difficulty_the_file_does_not_list_pays_nothing()
    {
        var service = ClearRewardFixture.Service(
            ClearRewardFixture.Table(), ClearRewardFixture.Pool());

        var face = service.FaceFor(
            ClearRewardFixture.Occasion() with { Difficulty = (DungeonDifficulty)9 }, Seed);

        Assert.Equal(0, face.Gold);
    }
}

/// <summary>
/// The hand-built clear-reward table and pool the rule-service tests draw against.
/// </summary>
/// <remarks>
/// Shared between the service tests and the rarity-ladder tests so that both are talking about the
/// same arrangement: certain odds, one profile that pays and one that pays less, and a ladder
/// whose every roll lands where the test asked it to.
/// </remarks>
internal static class ClearRewardFixture
{
    /// <summary>The assumed basis these tests work at.</summary>
    public const int Basis = 10000;

    /// <summary>The level the endgame is fought at, and the one the fixture declares grades for.</summary>
    public const int EndgameLevel = 86;

    /// <summary>
    /// The fixture's difficulty that adds nothing, as the archive's first one does.
    /// </summary>
    /// <remarks>
    /// It no longer empties a face, and that is the point of keeping it: added to the fixture's
    /// map base of 6000 against a threshold of 10000 it leaves 4000 for the dice, so
    /// <see cref="LowestDifficultyRate"/> of the ten thousand values pass.
    /// </remarks>
    public const int LowestDifficulty = 0;

    /// <summary>What a position at <see cref="LowestDifficulty"/> comes to, by hand.</summary>
    public const int LowestDifficultyRate = 6000;

    /// <summary>The fixture's difficulty that makes every position certain.</summary>
    /// <remarks>
    /// It adds 4000, which with the map base of 6000 exactly meets the threshold of 10000 -- so
    /// every dice value carries the sum past it and no position can miss.
    /// </remarks>
    public const int CertainDifficulty = 1;

    /// <summary>The only map count the fixture prices, and what it prices it at.</summary>
    /// <remarks>
    /// One row, so every other room count is one the fixture has no row for -- the same shape the
    /// archive has, four rows further along. The rate is the archive's own largest row.
    /// </remarks>
    public const int MapCount = 3;

    public const int MapBase = 6000;

    /// <summary>
    /// What the fixture's <c>[gold card create rate]</c> declares, which is A21's own figure.
    /// </summary>
    /// <remarks>
    /// Declared and never read: the two faces carry the same odds, so nothing on this path asks
    /// for it. It is here so that a service which started reading it would have something to read
    /// and would be caught changing an answer.
    /// </remarks>
    public const double PaidFaceRate = 1.0;

    /// <summary>
    /// What the fixture's cost table charges for a paid flip, and at the one level it charges at.
    /// </summary>
    /// <remarks>
    /// The archive's own figure for <see cref="EndgameLevel"/>, so that a reading which lost its
    /// way between the file and this fixture would show up as two different numbers rather than
    /// one shared constant. What the archive actually declares is asserted against the archive in
    /// <see cref="ClearRewardArchiveTests"/>; here it is only a table's contents.
    /// </remarks>
    public const int PaidFlipCost = 10300;

    /// <summary>
    /// <c>[gold card blank item]</c>, transcribed: six rows, one item, six quantities.
    /// </summary>
    /// <remarks>
    /// The archive's own rows rather than hand-built ones, for the reason
    /// <see cref="PaidFlipCost"/> is: what a switched-on fallback hands out has to be the item the
    /// file declares, so a fixture that invented rows could not say that. The six weights come to
    /// exactly ten thousand. That the archive still holds these is asserted against the file
    /// itself in <c>PvfClearRewardRateSourceTests</c>; here they are a transcription.
    /// </remarks>
    public static readonly ClearRewardBlankItem[] GoldCardBlankItems =
    [
        new(3000, 10002120, 1),
        new(3000, 10002120, 2),
        new(2000, 10002120, 3),
        new(1000, 10002120, 4),
        new(500, 10002120, 5),
        new(500, 10002120, 10),
    ];

    /// <summary>
    /// <c>[pcroom card blank item]</c>, transcribed: one row, and the archive declares no more.
    /// </summary>
    /// <remarks>
    /// One row where the paid card has six, which is the asymmetry the file holds rather than one
    /// this fixture arranged. The free card has no such section at all and so has nothing here.
    /// </remarks>
    public static readonly ClearRewardBlankItem[] PcRoomCardBlankItems = [new(50, 7454, 1)];

    /// <summary>One row of a hand-built pool.</summary>
    public readonly record struct PoolRow(
        int Grade, DropRarity Rarity, int ItemId, int Weight, ushort Durability = 0);

    /// <summary>
    /// A face written out position by position, so two faces can be compared as faces.
    /// </summary>
    /// <remarks>
    /// Needed because a face's positions live in a list, and a record holding a list compares that
    /// list by reference: two faces built from the same seed are equal position by position and
    /// still not equal as records. Comparing what is written here is comparing what a player would
    /// see.
    /// </remarks>
    public static string Fingerprint(ClearRewardCardFace face) =>
        string.Join(
            " | ",
            face.Slots.Select(
                slot => slot.Item is { } item
                    ? $"{slot.Index}:{item.ItemId}x{item.Quantity}/{item.Rarity}/{item.Durability}"
                    : $"{slot.Index}:-"));

    /// <summary>
    /// An occasion whose two levels agree unless a test parts them.
    /// </summary>
    /// <remarks>
    /// The character's level and the dungeon's are separate facts and separate lookups -- see
    /// <see cref="ClearRewardOccasion.CharacterLevel"/> and
    /// <see cref="ClearRewardOccasion.DungeonBasisLevel"/> -- but most tests here are about
    /// neither, so <paramref name="level"/> fills both and a test that cares about the difference
    /// names <paramref name="dungeonBasisLevel"/> as well.
    /// </remarks>
    public static ClearRewardOccasion Occasion(
        ClearRewardCardKind kind = ClearRewardCardKind.Free,
        int level = EndgameLevel,
        int difficulty = CertainDifficulty,
        int mapMaxCount = MapCount,
        int partyMemberCount = 1,
        int? dungeonBasisLevel = null) =>
        new(
            kind,
            level,
            dungeonBasisLevel ?? level,
            (DungeonDifficulty)difficulty,
            mapMaxCount,
            partyMemberCount);

    /// <summary>A ladder every one of whose rolls lands in the band asked for.</summary>
    /// <remarks>
    /// Built the way the archive turns a band off: a threshold equal to the one before it is a
    /// band of width zero. Every band below the one asked for is closed at zero and every band
    /// from it upwards is closed at the top, so the first threshold a roll does not exceed is
    /// always the one named.
    /// </remarks>
    public static RarityLadder PinnedTo(DropRarity rarity)
    {
        const int Top = 1000002;
        var band = (int)rarity;
        var values = new int[7];
        for (var index = 0; index < 6; index++)
            values[index] = index < band ? 0 : Top;
        values[6] = Top + 1;
        return new RarityLadder(values);
    }

    /// <summary>The gold bases and bands the fixture's dungeon level declares.</summary>
    /// <remarks>
    /// Level and figures of its own rather than the archive's, so a case that asserts an amount
    /// says which numbers produced it. The archive's own reading is pinned separately, against the
    /// real file.
    /// </remarks>
    public const int GoldBase = 200;

    /// <summary>How far either way the fixture's level lets a card's gold move.</summary>
    public const int GoldVariancePercent = 10;

    /// <summary>A gold table on the fixture's level, with a band a case may choose.</summary>
    public static GoldDropTable GoldTable(int variancePercent = GoldVariancePercent) =>
        new(
            new Dictionary<int, int> { [EndgameLevel] = GoldBase },
            new Dictionary<int, int> { [EndgameLevel] = variancePercent },
            [1],
            (100, 100));

    /// <summary>Hands out one gold table, whatever path it is asked for.</summary>
    public sealed class StubGoldTables(GoldDropTable? gold = null) : IGoldDropTableSource
    {
        public GoldDropTable Load(string archivePath) =>
            gold ?? new GoldDropTable(
                new Dictionary<int, int> { [EndgameLevel] = GoldBase },
                new Dictionary<int, int> { [EndgameLevel] = GoldVariancePercent },
                [1],
                (100, 100));
    }

    /// <summary>What the fixture's own difficulty multiplies a card's gold by.</summary>
    /// <remarks>
    /// The entry at <see cref="CertainDifficulty"/>. The other four differ from it and from each
    /// other -- see <see cref="DifficultyGoldRates"/> -- so a lookup that reached the wrong index
    /// cannot land on this figure.
    /// </remarks>
    public const double DifficultyGoldRate = 1.5;

    /// <summary>
    /// One gold multiplier per difficulty, every one of them different.
    /// </summary>
    /// <remarks>
    /// <b>Different on purpose.</b> A table whose entries repeated would let a service that always
    /// read the first of them pass every case that used any of them, which is how a wrong-index
    /// lookup stays invisible: the amount is still a plausible figure that moves with the seed.
    /// The archive's own five differ too.
    /// </remarks>
    public static readonly double[] DifficultyGoldRates = [1.25, DifficultyGoldRate, 1.75, 2.0, 2.25];

    /// <summary>
    /// A table whose second profile declares the full threshold and whose first declares half.
    /// </summary>
    /// <remarks>
    /// The default is declared second on purpose: a service that took whichever profile came first
    /// would be measuring against half the bar, and at the lowest difficulty that turns a position
    /// carrying six times in ten into one that never misses.
    /// <para>
    /// The <c>event</c> profile and the create rate are the two things the paid face could have
    /// been drawn differently by. Both are here and neither is read, so a service that started
    /// reading either would move an answer rather than pass unnoticed.
    /// </para>
    /// </remarks>
    public static ClearRewardTable Table(
        RarityLadder? ladder = null,
        int threshold = Basis,
        ItemGradeBand? grades = null,
        double? goldCardCreateRate = PaidFaceRate,
        IReadOnlyList<ClearRewardBlankItem>? goldCardBlankItems = null,
        IReadOnlyList<ClearRewardBlankItem>? pcRoomCardBlankItems = null) =>
        new(
            DropRateTables.ClearReward,
            [
                new ClearRewardProfile(
                    ClearRewardProfiles.Event, [new ClearRewardBand(1, 200, threshold / 2)]),
                new ClearRewardProfile(
                    ClearRewardProfiles.Default, [new ClearRewardBand(1, 200, threshold)]),
            ],
            [10000, 0],
            [0, 9500, 0, 500],
            [0, Basis - MapBase],
            DifficultyGoldRates,
            [1.0],
            new Dictionary<int, int> { [MapCount] = MapBase },
            ladder ?? PinnedTo(DropRarity.Common),
            new Dictionary<int, int> { [EndgameLevel] = PaidFlipCost },
            goldCardCreateRate,
            goldCardBlankItems ?? [],
            pcRoomCardBlankItems ?? [],
            new Dictionary<int, ItemGradeBand>
            {
                [EndgameLevel] = grades ?? new ItemGradeBand(79, 89),
            });

    public static DropItemPool Pool(params PoolRow[] rows) =>
        new(
            DropItemFamily.Equipment,
            rows.GroupBy(row => (row.Grade, (int)row.Rarity))
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<DropPoolEntry>)
                    [
                        .. group.Select(
                            row => new DropPoolEntry(row.ItemId, row.Weight, row.Durability)),
                    ]));

    /// <summary>
    /// The service, with the three fallback switches off unless a test turns one on.
    /// </summary>
    /// <remarks>
    /// Off is the shipped default -- see <see cref="ClearRewardBlankItemSwitches"/> -- so a test
    /// that says nothing about them is asking about the server as it is deployed.
    /// </remarks>
    public static ClearRewardRuleService Service(
        ClearRewardTable table,
        DropItemPool pool,
        int basis = Basis,
        IDungeonDefinitionSource? dungeons = null,
        GoldDropTable? gold = null,
        bool freeCardBlankItems = false,
        bool goldCardBlankItems = false,
        bool pcRoomCardBlankItems = false,
        bool guaranteePaidEquipment = false) =>
        new(
            new StubRates(table),
            new StubGoldTables(gold),
            new RecordingPools(pool),
            dungeons ?? new StubDungeons(),
            new ClearRewardRateOptions
            {
                RateBasis = basis,
                DebugGuaranteePaidEquipment = guaranteePaidEquipment,
                BlankItems =
                {
                    FreeCard = freeCardBlankItems,
                    GoldCard = goldCardBlankItems,
                    PcRoomCard = pcRoomCardBlankItems,
                },
            });

    /// <summary>
    /// A dungeon list that declares whichever dungeons the test names and nothing else.
    /// </summary>
    /// <remarks>
    /// Absent by default, which is the archive's ordinary case: 1042 of its 1047 dungeons say
    /// nothing about the paid card. A test that wants one switched off names it.
    /// </remarks>
    public sealed class StubDungeons(params int[] switchedOff) : IDungeonDefinitionSource
    {
        /// <summary>Every dungeon this source was asked for, in the order it was asked.</summary>
        public List<int> Asked { get; } = [];

        public ValueTask<DungeonDefinition?> GetAsync(
            int dungeonId, CancellationToken cancellationToken)
        {
            this.Asked.Add(dungeonId);
            return ValueTask.FromResult<DungeonDefinition?>(
                switchedOff.Contains(dungeonId)
                    ? new DungeonDefinition(
                        dungeonId, $"dungeon/{dungeonId}.dgn", 1, true, [], OffersPaidCard: false)
                    : null);
        }
    }

    internal sealed class StubRates(ClearRewardTable table) : IClearRewardRateSource
    {
        public ClearRewardTable Load(string archivePath) => table;
    }

    /// <summary>A pool source that remembers which families were asked for.</summary>
    internal sealed class RecordingPools(DropItemPool pool) : IDropItemPoolSource
    {
        private readonly List<DropItemFamily> _requested = [];

        public IReadOnlyList<DropItemFamily> Requested => this._requested;

        public DropItemPool Load(DropItemFamily family)
        {
            this._requested.Add(family);
            return pool;
        }
    }

}
