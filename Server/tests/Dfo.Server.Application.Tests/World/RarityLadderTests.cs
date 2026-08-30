using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;

namespace Dfo.Server.Application.Tests.World;

/// <summary>
/// The cumulative-threshold ladder that decides what quality a drop is.
/// </summary>
/// <remarks>
/// The groups here are transcribed from the archive, and
/// <c>PvfDropRateTableSourceTests.The_ladders_are_the_groups_the_archive_declares</c> is what
/// holds the transcription honest -- it asserts the reader hands back these exact numbers. The
/// arithmetic is tested here against them rather than against the file, so that a reading error
/// and an arithmetic error cannot cover for each other.
/// </remarks>
public sealed class RarityLadderTests
{
    /// <summary>
    /// The monster table's first group: <c>700000 964900 990000 1000000 1000001 1000002 1000003</c>.
    /// </summary>
    private static RarityLadder FirstGroup() =>
        new([700000, 964900, 990000, 1000000, 1000001, 1000002, 1000003]);

    /// <summary>
    /// The abyss table's first group, which switches two of its own bands off.
    /// </summary>
    private static RarityLadder AbyssGroup() =>
        new([0, 599000, 993000, 993000, 1000000, 1000001, 1000001]);

    /// <summary>
    /// The six bands of the first group, as shares of its rolls.
    /// </summary>
    /// <remarks>
    /// The denominator is the ladder's own: 1000002 rolls, not a round million. Seventy per cent
    /// of 1000002 is 699 999 more one, and it is that last one which the reference server's
    /// hard-coded million throws away.
    /// </remarks>
    [Fact]
    public void The_first_group_pays_the_shares_its_thresholds_describe()
    {
        var ladder = FirstGroup();

        Assert.Equal(0.7000, ladder.ProbabilityOf((int)DropRarity.Common), 4);
        Assert.Equal(0.2649, ladder.ProbabilityOf((int)DropRarity.Uncommon), 4);
        Assert.Equal(0.0251, ladder.ProbabilityOf((int)DropRarity.Rare), 4);
        Assert.Equal(0.0100, ladder.ProbabilityOf((int)DropRarity.Unique), 4);
    }

    /// <summary>
    /// The two rarest bands are reachable, and this is the assertion that says so.
    /// </summary>
    /// <remarks>
    /// <b>This is the test that catches the reference server's most expensive line.</b> Its
    /// <c>RollRarity</c> draws from one to a million against a ladder whose basis is 1000003, which
    /// leaves its highest roll one below the epic threshold. Every other number in its table stays
    /// right, nothing throws, and the top two bands simply never occur.
    /// <para>
    /// Written against the ladder's own roll range rather than against a probability, because a
    /// probability computed from a wrong denominator can still look plausible. A roll of 1000002
    /// either exists or it does not.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_two_rarest_bands_are_reachable()
    {
        var ladder = FirstGroup();

        Assert.Equal(1000003, ladder.Basis);
        Assert.Equal(1000002, ladder.HighestRoll);
        Assert.Equal(6, ladder.BandCount);

        Assert.True(ladder.WeightOf((int)DropRarity.Epic) > 0);
        Assert.True(ladder.WeightOf((int)DropRarity.Chronicle) > 0);
        Assert.Equal((int)DropRarity.Epic, ladder.BandOf(1000001));
        Assert.Equal((int)DropRarity.Chronicle, ladder.BandOf(1000002));

        // And the bands account for every roll, which is what makes the two above one each rather
        // than nothing rounded up.
        var total = 0;
        for (var band = 0; band < ladder.BandCount; band++)
            total += ladder.WeightOf(band);
        Assert.Equal(ladder.HighestRoll, total);
    }

    /// <summary>Every roll from one to the highest lands in a band, and in the right one.</summary>
    [Fact]
    public void The_band_a_roll_lands_in_is_the_first_threshold_it_does_not_exceed()
    {
        var ladder = FirstGroup();

        Assert.Equal((int)DropRarity.Common, ladder.BandOf(1));
        Assert.Equal((int)DropRarity.Common, ladder.BandOf(700000));
        Assert.Equal((int)DropRarity.Uncommon, ladder.BandOf(700001));
        Assert.Equal((int)DropRarity.Uncommon, ladder.BandOf(964900));
        Assert.Equal((int)DropRarity.Rare, ladder.BandOf(964901));
        Assert.Equal((int)DropRarity.Rare, ladder.BandOf(990000));
        Assert.Equal((int)DropRarity.Unique, ladder.BandOf(990001));
        Assert.Equal((int)DropRarity.Unique, ladder.BandOf(1000000));
    }

    /// <summary>
    /// A threshold equal to the one before it is a band that never occurs.
    /// </summary>
    /// <remarks>
    /// That is how the archive turns a band off, and the abyss table uses it twice: white is off
    /// because its threshold is zero, and pink is off because its threshold repeats rare's. The
    /// same group's epic band comes to seven thousand rolls in a million, which is the 0.70% the
    /// abyss is known for and seven thousand times what an ordinary monster pays.
    /// </remarks>
    [Fact]
    public void A_repeated_threshold_is_a_band_that_never_occurs()
    {
        var ladder = AbyssGroup();

        Assert.Equal(0, ladder.WeightOf((int)DropRarity.Common));
        Assert.Equal(0, ladder.WeightOf((int)DropRarity.Unique));
        Assert.Equal(0.5990, ladder.ProbabilityOf((int)DropRarity.Uncommon), 4);
        Assert.Equal(0.3940, ladder.ProbabilityOf((int)DropRarity.Rare), 4);
        Assert.Equal(0.0070, ladder.ProbabilityOf((int)DropRarity.Epic), 4);

        // No roll can land in a band of width zero, however the ladder is walked.
        Assert.NotEqual((int)DropRarity.Common, ladder.BandOf(1));
        Assert.NotEqual((int)DropRarity.Unique, ladder.BandOf(993000));
        Assert.NotEqual((int)DropRarity.Unique, ladder.BandOf(993001));
    }

    /// <summary>A band the ladder does not declare weighs nothing rather than throwing.</summary>
    [Fact]
    public void A_band_outside_the_ladder_weighs_nothing()
    {
        var ladder = FirstGroup();

        Assert.Equal(0, ladder.WeightOf(-1));
        Assert.Equal(0, ladder.WeightOf((int)DropRarity.Legendary));
        Assert.Equal(0, ladder.ProbabilityOf((int)DropRarity.Legendary));
    }
}
