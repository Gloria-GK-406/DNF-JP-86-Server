using Dfo.Server.Data.World;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.World;

namespace Dfo.Server.Application.Tests.World;

/// <summary>
/// The gate, the draw and the reproducible source they both run on.
/// </summary>
/// <remarks>
/// Built from transcribed archive numbers rather than from the archive, so that these fail on the
/// arithmetic and not on the reading. What holds the transcription honest is
/// <c>PvfDropRateTableSourceTests</c>.
/// </remarks>
public sealed class DropRulesTests
{
    private const int Basis = 10000;

    /// <summary>The endgame band and the monster table's multipliers, as the archive declares them.</summary>
    private static DropRateTable Table() => new(
        DropRateTables.Monster,
        [new DropRateBand(81, 200, [750, 10, 135, 8, 5])],
        [
            new RarityLadder([700000, 964900, 990000, 1000000, 1000001, 1000002, 1000003]),
            new RarityLadder([545500, 899500, 999500, 1000000, 1000001, 1000002, 1000003]),
            new RarityLadder([500000, 944900, 999900, 1000000, 1000001, 1000002, 1000003]),
            new RarityLadder([700000, 944900, 999000, 1000000, 1000001, 1000002, 1000003]),
        ],
        [[1, 2, 3, 6.6], [1, 2, 3, 4], [0.2, 2, 7, 16.8], [1, 2, 3, 4], [1, 2, 3, 4]],
        [
            [1, 1, 1, 1.4, 1.1], [1, 1.4, 1.6, 2.2, 2], [1, 1.4, 1.6, 2.2, 2],
            [1, 1.4, 1.6, 2.2, 2], [1, 1.4, 1.6, 2.2, 2],
        ],
        [[1, 1, 1, 1], [1, 1.2, 1.4, 1.6], [1, 1.3, 1.6, 2], [1, 1.2, 1.4, 1.6], [1, 1.2, 1.4, 1.6]],
        new Dictionary<int, ItemGradeBand> { [86] = new(79, 90) });

    private static DropItemPool Pool(
        params (int Grade, DropRarity Rarity, int ItemId, int Weight)[] entries) =>
        new(
            DropItemFamily.Equipment,
            entries
                .GroupBy(entry => (entry.Grade, (int)entry.Rarity))
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyList<DropPoolEntry>)
                        [.. group.Select(entry => new DropPoolEntry(entry.ItemId, entry.Weight))]));

    /// <summary>
    /// An ordinary monster's rates against a boss's, from the same band.
    /// </summary>
    /// <remarks>
    /// This is the shape the whole subject turns on: the same row of the table pays an ordinary
    /// monster 0.27% for a piece of gear and a boss 22.68% -- eighty-four times as much -- while
    /// its gold rate only rises sixfold. Reading the rank multipliers off the wrong group flattens
    /// exactly this and nothing else would show it.
    /// </remarks>
    [Fact]
    public void A_boss_and_an_ordinary_monster_are_paid_from_one_row()
    {
        var table = Table();
        var band = table.BandFor(86)!;

        var ordinary = DropRules.RatesFor(table, band, 0, 0, 1, Basis);
        var boss = DropRules.RatesFor(table, band, 3, 0, 1, Basis);

        Assert.Equal(750, ordinary.Of(DropRateSlot.Gold), 4);
        Assert.Equal(27, ordinary.Of(DropRateSlot.ItemType2), 4);
        Assert.Equal(4950, boss.Of(DropRateSlot.Gold), 4);
        Assert.Equal(2268, boss.Of(DropRateSlot.ItemType2), 4);
    }

    /// <summary>A product that passes the basis is held at it.</summary>
    [Fact]
    public void A_rate_that_would_pass_the_basis_is_held_at_it()
    {
        var table = Table();
        var band = new DropRateBand(81, 200, [9000, 0, 0, 0, 0]);

        // Boss, hardest difficulty, full party: 9000 x 6.6 x 1.1, well past the basis.
        var rates = DropRules.RatesFor(table, band, 3, (DungeonDifficulty)4, 4, Basis);

        Assert.Equal(Basis, rates.Of(DropRateSlot.Gold), 4);
    }

    /// <summary>Every slot is rolled on its own, so nothing at all is an ordinary outcome.</summary>
    [Fact]
    public void No_slot_hits_when_every_rate_is_zero()
    {
        var rates = new DropRates(
            [.. Enum.GetValues<DropRateSlot>().Select(slot => new SlotRate(slot, 0))],
            Basis);

        Assert.Empty(DropRules.Gate(rates, new DropRandom(1)));
    }

    /// <summary>A rate at the basis always hits, and every slot at it hits together.</summary>
    /// <remarks>
    /// The half of independence that a single mutually-exclusive distribution cannot produce: five
    /// certainties would still yield one drop under a distribution and yield five here.
    /// </remarks>
    [Fact]
    public void Every_certain_slot_hits_in_the_same_occasion()
    {
        var rates = new DropRates(
            [.. Enum.GetValues<DropRateSlot>().Select(slot => new SlotRate(slot, Basis))],
            Basis);

        Assert.Equal(Enum.GetValues<DropRateSlot>(), DropRules.Gate(rates, new DropRandom(7)));
    }

    /// <summary>Gold has no ladder, and each item slot draws its own.</summary>
    [Fact]
    public void Each_item_slot_rolls_its_own_ladder()
    {
        var table = Table();

        Assert.Equal(DropRarity.Common, DropRules.RollRarity(table, DropRateSlot.Gold, new(3)));

        // The second slot's ladder puts unique at one roll in twenty thousand and the third's at
        // one in ten thousand; a slot reading the wrong group is off by that much and by nothing
        // else visible.
        Assert.Equal(0.0251, table.LadderFor(DropRateSlot.ItemType1)!.ProbabilityOf(2), 4);
        Assert.Equal(0.1000, table.LadderFor(DropRateSlot.ItemType2)!.ProbabilityOf(2), 4);
    }

    /// <summary>A draw takes something whose grade is inside the band.</summary>
    [Fact]
    public void A_draw_takes_a_piece_from_inside_the_band()
    {
        var pool = Pool(
            (78, DropRarity.Rare, 1, 100),
            (85, DropRarity.Rare, 2, 100),
            (90, DropRarity.Rare, 3, 100),
            (95, DropRarity.Rare, 4, 100));

        var drawn = new HashSet<int>();
        for (var seed = 0; seed < 200; seed++)
        {
            if (DropRules.Pick(pool, new ItemGradeBand(79, 90), DropRarity.Rare, new((ulong)seed))
                is { } entry)
            {
                drawn.Add(entry.ItemId);
            }
        }

        Assert.Equal([2, 3], drawn.Order());
    }

    /// <summary>Weight decides how often, and a weight of zero never comes up.</summary>
    [Fact]
    public void A_draw_is_weighted_by_what_each_piece_declares()
    {
        var pool = Pool(
            (85, DropRarity.Rare, 1, 1),
            (85, DropRarity.Rare, 2, 999),
            (85, DropRarity.Rare, 3, 0));

        var counts = new Dictionary<int, int>();
        for (var seed = 0; seed < 500; seed++)
        {
            var drawn = DropRules.Pick(
                pool, new ItemGradeBand(85, 85), DropRarity.Rare, new((ulong)seed))!;
            counts[drawn.Value.ItemId] = counts.GetValueOrDefault(drawn.Value.ItemId) + 1;
        }

        Assert.False(counts.ContainsKey(3));
        Assert.True(counts[2] > counts.GetValueOrDefault(1) * 10);
    }

    /// <summary>An empty band draws nothing and does not throw.</summary>
    /// <remarks>
    /// The archive fills five of the ladder's six qualities. A monster that rolls the sixth has to
    /// come away empty-handed rather than failing the kill.
    /// </remarks>
    [Fact]
    public void An_empty_band_draws_nothing()
    {
        var pool = Pool((85, DropRarity.Rare, 1, 100));

        Assert.Null(DropRules.Pick(pool, new ItemGradeBand(79, 90), DropRarity.Chronicle, new(1)));
        Assert.Null(DropRules.Pick(pool, new ItemGradeBand(10, 20), DropRarity.Rare, new(1)));
    }

    /// <summary>Gold is the level's base, the rank's multiple, inside the declared band.</summary>
    [Fact]
    public void Gold_is_the_level_base_times_the_rank_inside_the_declared_band()
    {
        // The archive's own numbers: level 86 pays 1357, the four ranks multiply by 1/2/4/6, and
        // the amount varies between 100% and 110%.
        var gold = new GoldDropTable(
            new Dictionary<int, int> { [86] = 1357 },
            new Dictionary<int, int> { [86] = 9 },
            [1, 2, 4, 6],
            (100, 110));

        for (var seed = 0; seed < 100; seed++)
        {
            Assert.InRange(DropRules.GoldAmount(gold, 86, 0, new((ulong)seed)), 1357, 1492);
            Assert.InRange(DropRules.GoldAmount(gold, 86, 3, new((ulong)seed)), 8142, 8956);
        }

        // A level the table does not list pays nothing rather than a number nobody declared.
        Assert.Equal(0, DropRules.GoldAmount(gold, 200, 0, new(1)));
    }

    /// <summary>
    /// The basis is the one it was handed, and the rules hold none of their own.
    /// </summary>
    /// <remarks>
    /// <b>The guard against the one constant that could hide here.</b> The archive declares the
    /// five rates as numerators and says nowhere what they are over; the reference server writes
    /// 10000 into its own arithmetic. A rule that did the same would agree with every other test
    /// in this file, because they all use 10000 -- so this one deliberately does not.
    /// </remarks>
    [Fact]
    public void The_basis_is_whatever_it_was_handed()
    {
        var table = Table();
        var band = table.BandFor(86)!;

        // A boss's gold comes to 4950. Under a basis of 1000 that is past the cap and held there;
        // under 100000 it is nowhere near it. A hard-coded 10000 answers 4950 to both.
        Assert.Equal(1000, DropRules.RatesFor(table, band, 3, 0, 1, 1000).Of(DropRateSlot.Gold), 4);
        Assert.Equal(
            4950, DropRules.RatesFor(table, band, 3, 0, 1, 100000).Of(DropRateSlot.Gold), 4);
    }

    /// <summary>And the gate rolls against that basis rather than one of its own.</summary>
    [Fact]
    public void The_gate_rolls_against_the_basis_it_was_given()
    {
        var seeds = Enumerable.Range(1, 40).ToArray();
        var certain = new DropRates([new SlotRate(DropRateSlot.Gold, 5000)], 5000);
        var evens = new DropRates([new SlotRate(DropRateSlot.Gold, 5000)], 10000);

        Assert.All(seeds, seed => Assert.Single(DropRules.Gate(certain, new DropRandom((ulong)seed))));
        Assert.Contains(seeds, seed => DropRules.Gate(evens, new DropRandom((ulong)seed)).Count == 0);
    }
}

/// <summary>
/// The source the rolls come from.
/// </summary>
/// <remarks>
/// It is not the game's generator and cannot be checked against one. What is required of it is
/// only what these assert.
/// </remarks>
public sealed class DropRandomTests
{
    private static IReadOnlyList<int> Sequence(ulong seed, int count = 24)
    {
        var random = new DropRandom((ulong)seed);
        return [.. Enumerable.Range(0, count).Select(_ => random.Next(1_000_000))];
    }

    /// <summary>The same occasion asked twice answers twice the same.</summary>
    [Fact]
    public void One_seed_gives_one_sequence()
    {
        var seed = DropRandom.SeedFor(12345, 8153, 1, 2, 30000);

        Assert.Equal(Sequence(seed), Sequence(seed));
    }

    /// <summary>
    /// Occasions that differ by one differ completely.
    /// </summary>
    /// <remarks>
    /// The room next door, the next actor in the room, and the same maze re-entered: all three
    /// have to fall out unrelated, or neighbouring monsters would drop visibly related things.
    /// </remarks>
    [Fact]
    public void Neighbouring_occasions_do_not_march_in_step()
    {
        var here = Sequence(DropRandom.SeedFor(12345, 8153, 1, 2, 30000));

        foreach (var neighbour in new[]
                 {
                     DropRandom.SeedFor(12345, 8153, 1, 2, 30001),
                     DropRandom.SeedFor(12345, 8154, 1, 2, 30000),
                     DropRandom.SeedFor(12345, 8153, 2, 2, 30000),
                     DropRandom.SeedFor(12345, 8153, 1, 3, 30000),
                     DropRandom.SeedFor(12346, 8153, 1, 2, 30000),
                 })
        {
            var there = Sequence(neighbour);
            Assert.NotEqual(here, there);
            Assert.Empty(here.Intersect(there));
        }
    }

    /// <summary>A roll runs from one to the bound, both included.</summary>
    /// <remarks>
    /// One-based because the ladders are: a band covers the rolls up to its threshold, so a zero
    /// would belong to no band.
    /// </remarks>
    [Fact]
    public void A_roll_starts_at_one()
    {
        var random = new DropRandom(99);
        for (var index = 0; index < 2000; index++)
            Assert.InRange(random.Next(3), 1, 3);

        Assert.Equal(1, new DropRandom(1).Next(1));
        Assert.Equal(0, new DropRandom(1).Next(0));
    }
}

/// <summary>The compensation seam, in the phase where it compensates nothing.</summary>
public sealed class DropRateAdjusterTests
{
    /// <summary>
    /// The first phase's implementation gives back what it was given, field for field.
    /// </summary>
    /// <remarks>
    /// Asserted rather than assumed, because everything upstream of the seam is written as though
    /// something were behind it. A no-op that quietly rounded or reordered would make every drop
    /// in this phase different from the drop the rules describe, with nothing saying so.
    /// </remarks>
    [Fact]
    public void The_first_phase_adjustment_changes_nothing()
    {
        var rates = new DropRates(
            [
                new SlotRate(DropRateSlot.Gold, 750),
                new SlotRate(DropRateSlot.ItemType1, 10),
                new SlotRate(DropRateSlot.ItemType2, 27),
                new SlotRate(DropRateSlot.ItemType3, 8),
                new SlotRate(DropRateSlot.ItemType4, 5),
            ],
            10000);

        var adjusted = Adjuster().Adjust(new(7), rates);

        Assert.Equal(rates, adjusted);
        Assert.Equal(rates.Basis, adjusted.Basis);
        foreach (var slot in Enum.GetValues<DropRateSlot>())
            Assert.Equal(rates.Of(slot), adjusted.Of(slot), 6);
    }

    /// <summary>And a raised rate reaches the gate, which is what the seam is for.</summary>
    [Fact]
    public void A_raised_rate_is_what_the_gate_rolls_against()
    {
        var rates = new DropRates([new SlotRate(DropRateSlot.ItemType2, 0)], 10000);

        var raised = rates.With(DropRateSlot.ItemType2, 10000);

        Assert.Empty(DropRules.Gate(rates, new DropRandom(5)));
        Assert.Equal([DropRateSlot.ItemType2], DropRules.Gate(raised, new DropRandom(5)));
    }

    private static IDropRateAdjuster Adjuster() => new NoDropRateAdjustment();
}
