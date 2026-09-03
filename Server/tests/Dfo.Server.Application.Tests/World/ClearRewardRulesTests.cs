using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.Tests.World;

/// <summary>
/// Composing the archive's clear-reward numbers and one dice roll into a single chance.
/// </summary>
/// <remarks>
/// The numbers here are transcribed from <c>etc/itemdropinfo_clearreward.etc</c>, and
/// <see cref="ClearRewardArchiveTests"/> is what holds the transcription honest -- it asserts the
/// archive still declares these exact values and that the service composes the same answers out of
/// them. The arithmetic is tested here against the transcription rather than against the file, so
/// that a reading error and an arithmetic error cannot cover for each other.
/// <para>
/// Part of what is tested here is a reading and part of it is this server's own decision; which is
/// which is set out on <see cref="ClearRewardRules"/> -- five decisions -- and again at each of the
/// three places one of them is made. These assertions pin both, so that changing either is a
/// visible change rather than a silent one.
/// </para>
/// <para>
/// Every expected figure below is worked by hand from the transcribed numbers:
/// <c>threshold - map base - difficulty bonus</c> is how much the dice has to make up, and the
/// dice values that fail to make it up are the ones at or under that shortfall divided by the party
/// multiplier. None of them is read off the implementation.
/// </para>
/// </remarks>
public sealed class ClearRewardRulesTests
{
    /// <summary>The basis, which is also how many values the dice has.</summary>
    private const int Basis = 10000;

    /// <summary>What the <c>default</c> profile pays in every one of its level bands.</summary>
    private const int DefaultThreshold = 10000;

    /// <summary>The difficulty bonuses, indexed from zero. The first adds nothing.</summary>
    private static readonly int[] DifficultyBonuses = [0, 600, 1000, 1400, 1200];

    /// <summary>The map-count table, transcribed: one room through five.</summary>
    private static readonly Dictionary<int, int> MapCountRates = new()
    {
        [1] = 2000,
        [2] = 3000,
        [3] = 4000,
        [4] = 5000,
        [5] = 6000,
    };

    /// <summary>What the largest row of the map-count table declares.</summary>
    private const int LargestMapRate = 6000;

    /// <summary>What the smallest row of the map-count table declares.</summary>
    private const int SmallestMapRate = 2000;

    /// <summary>
    /// The party multipliers, held as the archive holds them: singles, widened.
    /// </summary>
    /// <remarks>
    /// Written as float literals on purpose. 1.6 arrives as 1.60000002384 and 1.4 as
    /// 1.39999997616, and it is exactly those values the composition has to be right about --
    /// see <see cref="A_party_multiplier_stored_as_a_single_does_not_decide_a_dice_value"/>.
    /// </remarks>
    private const double PartyOfOne = 1.0f;

    private const double PartyOfTwo = 1.2f;

    private const double PartyOfThree = 1.4f;

    private const double PartyOfFour = 1.6f;

    /// <summary>
    /// <b>The numbers add, and the dice makes up the rest or does not.</b>
    /// </summary>
    /// <remarks>
    /// The whole composition in one table. Each row is the archive's map base and difficulty bonus
    /// against the <c>default</c> profile's threshold of 10000, with the party multiplier on the
    /// dice; the expectation is how many of the ten thousand dice values carry the sum past the
    /// threshold.
    /// <para>
    /// Worked by hand, row by row. A five-room maze at the third difficulty leaves
    /// <c>10000 - 6000 - 1400 = 2600</c> for the dice to find: at a party of one the 2600 values
    /// from one to 2600 fall short and 7400 pass; at a party of four each value is worth 1.6 of
    /// itself, so the values up to <c>2600 / 1.6 = 1625</c> fall short and 8375 pass; at a party of
    /// three the crossing is <c>2600 / 1.4 = 1857.14...</c>, so 1857 values fall short and 8143
    /// pass; at a party of two it is <c>2600 / 1.2 = 2166.66...</c>, so 2166 fall short and 7834
    /// pass.
    /// </para>
    /// <para>
    /// Exact expectations rather than approximate ones, because every one of them is a different
    /// answer under a different composition: multiplying the four instead of adding them would put
    /// the first row at 840 rather than 7400, and applying the party multiplier to the sum rather
    /// than to the dice would carry every position of the fourth row outright.
    /// </para>
    /// </remarks>
    [Theory]
    // map base, difficulty bonus, party multiplier, how many of the dice's values pass
    [InlineData(LargestMapRate, 1400, PartyOfOne, 7400)]
    [InlineData(LargestMapRate, 1000, PartyOfOne, 7000)]
    [InlineData(LargestMapRate, 600, PartyOfOne, 6600)]
    [InlineData(LargestMapRate, 1200, PartyOfOne, 7200)]
    [InlineData(LargestMapRate, 0, PartyOfOne, 6000)]
    [InlineData(SmallestMapRate, 0, PartyOfOne, 2000)]
    [InlineData(SmallestMapRate, 1400, PartyOfOne, 3400)]
    [InlineData(5000, 1400, PartyOfOne, 6400)]
    [InlineData(4000, 1400, PartyOfOne, 5400)]
    [InlineData(3000, 1400, PartyOfOne, 4400)]
    [InlineData(LargestMapRate, 1400, PartyOfTwo, 7834)]
    [InlineData(LargestMapRate, 1400, PartyOfThree, 8143)]
    [InlineData(LargestMapRate, 1400, PartyOfFour, 8375)]
    // A one-room maze at the second difficulty leaves 7400: the crossing is 7400 / 1.4 =
    // 5285.71..., so 5285 values fall short and 4715 pass. The row is here because it is the one
    // that separates taking the stored 1.4 to a whole 14000 from letting it fall to 13999, which
    // would move the crossing to 5286 and the answer to 4714.
    [InlineData(SmallestMapRate, 600, PartyOfThree, 4715)]
    public void The_numbers_add_and_the_dice_makes_up_the_shortfall(
        int mapBaseRate, int difficultyBonus, double partyRate, int expected)
    {
        var chance = ClearRewardRules.ChanceOf(
            mapBaseRate, difficultyBonus, partyRate, DefaultThreshold, Basis);

        Assert.Equal(expected, chance.Rate);
        Assert.Equal(Basis, chance.Basis);
        Assert.Equal((double)expected / Basis, chance.Probability, 6);
        Assert.False(chance.IsImpossible);
    }

    /// <summary>
    /// <b>The lowest difficulty adds nothing, and adding nothing is not paying nothing.</b>
    /// </summary>
    /// <remarks>
    /// The one assertion that tells an additive composition from a multiplicative one on this
    /// archive. The difficulty table opens with a zero; multiplied in, that zero emptied every
    /// position of every face of the easiest run in the game, which is what this server did until
    /// the composition was settled. Added in, it leaves the maze's own base rate standing: a
    /// five-room maze at a party of one still needs the dice to find 4000, and 6000 of its values
    /// do.
    /// </remarks>
    [Fact]
    public void The_lowest_difficulty_adds_nothing_and_leaves_the_map_base_standing()
    {
        var chance = ClearRewardRules.ChanceOf(
            LargestMapRate, DifficultyBonuses[0], PartyOfOne, DefaultThreshold, Basis);

        Assert.Equal(0, DifficultyBonuses[0]);
        Assert.Equal(6000, chance.Rate);
        Assert.Equal(0.6, chance.Probability, 6);
        Assert.False(chance.IsImpossible);

        // And every difficulty the table declares carries something, at the smallest maze the map
        // table prices as well as at the largest.
        foreach (var bonus in DifficultyBonuses)
        {
            foreach (var mapBase in new[] { SmallestMapRate, LargestMapRate })
            {
                Assert.False(
                    ClearRewardRules
                        .ChanceOf(mapBase, bonus, PartyOfOne, DefaultThreshold, Basis)
                        .IsImpossible,
                    $"a map base of {mapBase} at a bonus of {bonus} carried nothing");
            }
        }
    }

    /// <summary>
    /// The fourth difficulty pays less than the third, and the composition passes that through.
    /// </summary>
    /// <remarks>
    /// The archive's bonus table is not a ladder -- its fifth entry is smaller than its fourth --
    /// and a composition that quietly sorted or interpolated would hide that. Nothing here is
    /// allowed to be monotonic on the server's behalf.
    /// </remarks>
    [Fact]
    public void The_bonus_table_is_passed_through_unsorted()
    {
        int Rate(int bonus) => ClearRewardRules
            .ChanceOf(LargestMapRate, bonus, PartyOfOne, DefaultThreshold, Basis).Rate;

        Assert.True(DifficultyBonuses[4] < DifficultyBonuses[3]);
        Assert.Equal(7400, Rate(DifficultyBonuses[3]));
        Assert.Equal(7200, Rate(DifficultyBonuses[4]));
        Assert.True(Rate(DifficultyBonuses[4]) < Rate(DifficultyBonuses[3]));
    }

    /// <summary>
    /// <b>A party multiplier stored as a single does not get to decide a dice value.</b>
    /// </summary>
    /// <remarks>
    /// The assertion that catches an off-by-one nobody would ever see in play. The archive stores
    /// the party table as singles, so 1.6 comes back as 1.60000002384: compared as reals, the dice
    /// value 1625 at a shortfall of 2600 contributes 2600.0000387 and squeaks past a threshold it
    /// exactly meets in decimal. One position in ten thousand would then turn on the eighth decimal
    /// place of a number the file wrote as "1.6".
    /// <para>
    /// So the multiplier is taken to whole parts of the basis once and the comparison is whole
    /// numbers from there. The first two assertions are what makes the third about something: they
    /// show the artefact is really there before showing it does not reach the answer.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_party_multiplier_stored_as_a_single_does_not_decide_a_dice_value()
    {
        // The stored multiplier is above the decimal it was written as, and the offending dice
        // value really does clear the threshold when the two are compared as reals.
        Assert.True(PartyOfFour > 1.6);
        Assert.True(6000 + 1400 + (1625 * PartyOfFour) > DefaultThreshold);

        // And it does not get through: 8375, not 8376.
        Assert.Equal(
            8375,
            ClearRewardRules
                .ChanceOf(LargestMapRate, 1400, PartyOfFour, DefaultThreshold, Basis).Rate);

        // The other two fractional multipliers are stored the same way -- one above its decimal,
        // one below -- and neither moves its answer either.
        Assert.True(PartyOfTwo > 1.2);
        Assert.True(PartyOfThree < 1.4);
        Assert.Equal(
            7834,
            ClearRewardRules
                .ChanceOf(LargestMapRate, 1400, PartyOfTwo, DefaultThreshold, Basis).Rate);
        Assert.Equal(
            8143,
            ClearRewardRules
                .ChanceOf(LargestMapRate, 1400, PartyOfThree, DefaultThreshold, Basis).Rate);
    }

    /// <summary>
    /// A sum that clears the threshold before the dice is rolled carries every position.
    /// </summary>
    /// <remarks>
    /// No occasion this archive describes reaches it -- the largest sum without the dice is 7400
    /// against a threshold of 10000 -- so it guards a future table rather than correcting this one.
    /// The equal case is asserted beside it because it is the boundary the comparison is written
    /// on: a sum that exactly meets the threshold has not passed it, and needs the dice to add
    /// something, which every dice value does.
    /// </remarks>
    [Fact]
    public void A_sum_that_clears_the_threshold_without_the_dice_carries_every_position()
    {
        var clear = ClearRewardRules.ChanceOf(LargestMapRate, 1400, PartyOfOne, 7000, Basis);
        Assert.Equal(Basis, clear.Rate);
        Assert.Equal(1.0, clear.Probability);

        var exact = ClearRewardRules.ChanceOf(LargestMapRate, 1400, PartyOfOne, 7400, Basis);
        Assert.Equal(Basis, exact.Rate);
    }

    /// <summary>A shortfall the dice cannot make up carries nothing.</summary>
    /// <remarks>
    /// The other end of the same comparison, and the only way a position can be impossible once the
    /// tables have answered at all. A map base and a difficulty bonus of nothing against the full
    /// threshold leaves the dice needing more than it has.
    /// </remarks>
    [Theory]
    [InlineData(0, 0, Basis)]
    [InlineData(0, 0, Basis + 1)]
    [InlineData(1, 0, Basis + 2)]
    public void A_shortfall_the_dice_cannot_make_up_carries_nothing(
        int mapBaseRate, int difficultyBonus, int threshold)
    {
        var chance = ClearRewardRules.ChanceOf(
            mapBaseRate, difficultyBonus, PartyOfOne, threshold, Basis);

        Assert.Equal(0, chance.Rate);
        Assert.Equal(Basis, chance.Basis);
        Assert.True(chance.IsImpossible);
    }

    /// <summary>
    /// A party multiplier at or below zero leaves the dice contributing nothing.
    /// </summary>
    /// <remarks>
    /// The archive declares none -- its smallest is one -- so this is the composition applied
    /// honestly rather than a case that occurs. What it must not do is quietly become a neutral
    /// multiplier: a party size the file does not price is answered null by the table and never
    /// reaches here at all.
    /// </remarks>
    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    public void A_party_multiplier_at_or_below_zero_leaves_the_dice_contributing_nothing(
        double partyRate)
    {
        Assert.True(
            ClearRewardRules
                .ChanceOf(LargestMapRate, 1400, partyRate, DefaultThreshold, Basis)
                .IsImpossible);

        // And the sum without the dice still stands on its own: a threshold it already clears is
        // cleared whatever the dice is worth.
        Assert.Equal(
            Basis,
            ClearRewardRules.ChanceOf(LargestMapRate, 1400, partyRate, 7000, Basis).Rate);
    }

    /// <summary>
    /// The threshold and the basis are both read from the arguments rather than held inside.
    /// </summary>
    /// <remarks>
    /// The point of the basis being configuration rather than a constant: it can be configured
    /// wrongly, but the rules cannot be wrong about it of their own accord. The same shape of
    /// occasion at a quarter of the basis, with every number scaled to match, comes to the same
    /// share -- 1850 of 2500 is the same 74 in a hundred as 7400 of 10000 -- which is what says the
    /// basis is a scale everywhere it is used and not a constant in one of them.
    /// </remarks>
    [Fact]
    public void The_threshold_and_the_basis_are_read_from_the_arguments()
    {
        var tenThousand = ClearRewardRules.ChanceOf(6000, 1400, PartyOfOne, 10000, 10000);
        var twentyFiveHundred = ClearRewardRules.ChanceOf(1500, 350, PartyOfOne, 2500, 2500);

        Assert.Equal(7400, tenThousand.Rate);
        Assert.Equal(1850, twentyFiveHundred.Rate);
        Assert.Equal(2500, twentyFiveHundred.Basis);
        Assert.Equal(tenThousand.Probability, twentyFiveHundred.Probability, 6);

        // A lower threshold is a bonus, which is the whole of why [drop prob] is read as one.
        Assert.True(
            ClearRewardRules.ChanceOf(6000, 1400, PartyOfOne, 9000, Basis).Rate
            > tenThousand.Rate);
        Assert.Equal(8400, ClearRewardRules.ChanceOf(6000, 1400, PartyOfOne, 9000, Basis).Rate);
    }

    /// <summary>A basis of zero is a misconfiguration and is refused rather than divided by.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_basis_that_is_not_positive_is_refused(int basis) =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ClearRewardRules.ChanceOf(
                LargestMapRate, 1400, PartyOfOne, DefaultThreshold, basis));

    /// <summary>A maze the table prices has its own row and nothing else's.</summary>
    /// <remarks>
    /// Five rows, transcribed. The point of asserting all five rather than the ends is that the
    /// fallback below must not reach a count that has a row of its own.
    /// </remarks>
    [Theory]
    [InlineData(1, 2000)]
    [InlineData(2, 3000)]
    [InlineData(3, 4000)]
    [InlineData(4, 5000)]
    [InlineData(5, 6000)]
    public void A_maze_the_table_prices_takes_its_own_row(int rooms, int expected) =>
        Assert.Equal(expected, ClearRewardRules.MapBaseRate(MapCountRates, rooms));

    /// <summary>
    /// <b>A maze the table prices nothing for takes the largest row the table declares.</b>
    /// </summary>
    /// <remarks>
    /// This server's decision, and the archive states nothing about it -- see
    /// <see cref="ClearRewardRules.MapBaseRate"/>. The case it exists for is the ordinary one:
    /// the table stops at five rooms and dungeon 144's five mazes hold ten, six, four, six and
    /// four, so three of them have no row. Answering nothing for them, which this server did until
    /// this revision, emptied both faces of every clear of those three.
    /// <para>
    /// Swept far past any maze the archive lays out, because the rule is "beyond the table" and not
    /// "one past the table". The largest row is found by the largest room count rather than by the
    /// largest rate, and the rows are handed over unordered so that a lookup which took whichever
    /// came first would come away with a different number.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_maze_the_table_prices_nothing_for_takes_the_largest_row()
    {
        foreach (var rooms in new[] { 6, 7, 10, 40, 400 })
            Assert.Equal(LargestMapRate, ClearRewardRules.MapBaseRate(MapCountRates, rooms));

        // Never nothing, and never less than a smaller maze: a maze past the table pays what the
        // largest maze it prices pays.
        for (var rooms = 1; rooms <= 40; rooms++)
        {
            var rate = ClearRewardRules.MapBaseRate(MapCountRates, rooms);
            Assert.NotNull(rate);
            Assert.InRange(rate.Value, SmallestMapRate, LargestMapRate);
        }

        // Out of declaration order, and with the largest rate not on the largest count, so that a
        // lookup taking the first row or the biggest number comes away with 900 rather than 500.
        var jumbled = new Dictionary<int, int> { [3] = 900, [1] = 100, [7] = 500 };
        Assert.Equal(500, ClearRewardRules.MapBaseRate(jumbled, 8));
        Assert.Equal(900, ClearRewardRules.MapBaseRate(jumbled, 3));
    }

    /// <summary>A table with no rows at all is a gap, and answers nothing.</summary>
    /// <remarks>
    /// The one place the map-count reading does say "absent": a file that declares the section
    /// nowhere is a file that has lost it, not a maze the section does not cover. The caller turns
    /// that into an impossible position rather than into a base rate of zero.
    /// </remarks>
    [Fact]
    public void A_table_with_no_rows_at_all_answers_nothing() =>
        Assert.Null(ClearRewardRules.MapBaseRate(new Dictionary<int, int>(), 5));

    /// <summary>
    /// <b>A fallback row is drawn by the archive's own weights, not taken from the top.</b>
    /// </summary>
    /// <remarks>
    /// The six rows of <c>[gold card blank item]</c> differ only in weight and quantity, so which
    /// quantity comes out is the whole of what a weighted draw decides here. A draw that always
    /// answered the first row would pass every assertion about the item and none of these. The
    /// rows are <see cref="ClearRewardFixture.GoldCardBlankItems"/>, transcribed there once.
    /// <para>
    /// Swept over twenty thousand seeds and checked against each row's share of the ten thousand:
    /// a tolerance of one and a half points in the hundred is some five standard deviations out at
    /// this count, so a sweep that drifts past it is a changed rule rather than an unlucky run.
    /// </para>
    /// </remarks>
    [Fact]
    public void Blank_item_draws_are_weighted_by_the_archives_own_weights()
    {
        const int Sweep = 20000;
        var rows = ClearRewardFixture.GoldCardBlankItems;

        // The archive's six weights come to the same ten thousand the rest of this file is
        // counted against, which is what makes each row's share readable straight off its weight.
        Assert.Equal(Basis, rows.Sum(row => row.Weight));

        var drawn = new List<ClearRewardBlankItem>(Sweep);
        for (var seed = 0UL; seed < Sweep; seed++)
        {
            var row = ClearRewardRules.BlankItemOf(rows, new DropRandom(seed));
            Assert.NotNull(row);
            drawn.Add(row);
        }

        // Every row is reachable, and each comes up about as often as the archive weights it.
        Assert.All(drawn, row => Assert.Equal(10002120, row.ItemId));
        foreach (var row in rows)
        {
            var share = (double)drawn.Count(hit => hit.Quantity == row.Quantity) / Sweep;
            var declared = row.Weight / (double)Basis;
            Assert.InRange(share, declared - 0.015, declared + 0.015);
        }

        // And the order is the weights' order rather than the rows': the heaviest quantity comes
        // up several times as often as the lightest, which is what a draw off the top could not do.
        Assert.True(
            drawn.Count(hit => hit.Quantity == 1) > drawn.Count(hit => hit.Quantity == 10) * 3);
    }

    /// <summary>
    /// <b>A fallback table with nothing to draw from draws nothing, and spends no roll on it.</b>
    /// </summary>
    /// <remarks>
    /// Both halves matter. Nothing drawn is what a card whose switch is off must come to, and no
    /// roll spent is what makes the switched-off state <i>the same face</i> as before the switches
    /// existed rather than a face drawn from a generator one step along.
    /// <para>
    /// A row weighted at zero is declared and unreachable, the same way a rarity band of width
    /// zero is: the archive turns a row off by weighting it out, and a draw that could still land
    /// on it would be reading the weights as an order rather than as weights.
    /// </para>
    /// </remarks>
    [Fact]
    public void Blank_item_draws_with_no_rows_answer_nothing()
    {
        ClearRewardBlankItem[] weightedOut = [new(0, 7454, 1)];
        Assert.Null(ClearRewardRules.BlankItemOf([], new DropRandom(1)));
        Assert.Null(ClearRewardRules.BlankItemOf(weightedOut, new DropRandom(1)));

        // The generator handed an empty table is the generator that was handed in.
        var asked = new DropRandom(0xC0FF_EE);
        var untouched = new DropRandom(0xC0FF_EE);
        Assert.Null(ClearRewardRules.BlankItemOf([], asked));
        Assert.Equal(untouched.Next(1000), asked.Next(1000));

        // One row is drawn whatever the roll; a row weighted at zero beside it never is.
        ClearRewardBlankItem[] rows = [new(0, 10002120, 5), new(50, 7454, 1)];
        for (var seed = 0UL; seed < 200; seed++)
        {
            Assert.Equal(
                new ClearRewardBlankItem(50, 7454, 1),
                ClearRewardRules.BlankItemOf(rows, new DropRandom(seed)));
        }
    }

    /// <summary>
    /// A card's gold is the level's base scaled by the difficulty, with the variance disabled.
    /// </summary>
    /// <remarks>
    /// The band is zero here so that the scaling can be read on its own. What the three numbers
    /// mean and where each comes from is on <see cref="ClearRewardRules.CardGoldOf"/>; that they
    /// are the archive's is pinned in <c>ClearRewardArchiveTests</c>.
    /// </remarks>
    [Theory]
    [InlineData(44, 1.02, 45)]
    [InlineData(44, 2.0, 88)]
    [InlineData(77, 1.6, 123)]
    [InlineData(17, 1.02, 17)]
    public void A_cards_gold_is_the_levels_base_scaled_by_the_difficulty(
        int baseAmount, double rate, int expected) =>
        Assert.Equal(
            expected,
            ClearRewardRules.CardGoldOf(baseAmount, rate, variancePercent: 0, new DropRandom(1)));

    /// <summary>
    /// The variance moves the amount either way and never past the band the level declares.
    /// </summary>
    /// <remarks>
    /// <b>Both halves matter.</b> A rule that only ever moved one way, or that moved further than
    /// the archive allows, would satisfy a bound check alone; so the seeds are swept and the span
    /// actually produced is asserted to reach both sides of the unmoved figure.
    /// </remarks>
    [Fact]
    public void The_variance_moves_the_amount_both_ways_and_stays_inside_the_band()
    {
        const int Base = 200;
        const double Rate = 1.5;
        const int Percent = 13;
        var unmoved = Base * Rate;
        var low = (int)Math.Floor(unmoved * (1 - (Percent / 100.0)));
        var high = (int)Math.Ceiling(unmoved * (1 + (Percent / 100.0)));

        var seen = new List<int>();
        for (var seed = 0; seed < 200; seed++)
            seen.Add(ClearRewardRules.CardGoldOf(Base, Rate, Percent, new DropRandom((ulong)seed)));

        Assert.All(seen, amount => Assert.InRange(amount, low, high));
        Assert.Contains(seen, amount => amount < unmoved);
        Assert.Contains(seen, amount => amount > unmoved);
    }

    /// <summary>A level the archive gives no base for pays nothing, because there is no figure.</summary>
    /// <remarks>
    /// The one case that pays nothing. A card that was turned over pays whatever its level is
    /// worth, and a level the file does not list is not worth zero -- it is not worth anything
    /// this server can name, which is a different statement and the honest one.
    /// </remarks>
    [Theory]
    [InlineData(0, 1.5)]
    [InlineData(-1, 1.5)]
    [InlineData(200, 0)]
    [InlineData(200, double.NaN)]
    public void A_figure_the_archive_does_not_give_pays_nothing(int baseAmount, double rate) =>
        Assert.Equal(
            0,
            ClearRewardRules.CardGoldOf(baseAmount, rate, variancePercent: 13, new DropRandom(1)));

    /// <summary>An amount that would round away comes back as one rather than as nothing.</summary>
    /// <remarks>
    /// The floor is what makes this a floor. A base and a multiplier that between them come to
    /// less than half a coin still describe a card that was turned over, and a card that was
    /// turned over pays.
    /// </remarks>
    [Fact]
    public void An_amount_that_would_round_away_still_pays_one() =>
        Assert.Equal(
            1,
            ClearRewardRules.CardGoldOf(1, 0.01, variancePercent: 0, new DropRandom(1)));

}
