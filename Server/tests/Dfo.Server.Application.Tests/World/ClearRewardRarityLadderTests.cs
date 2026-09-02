using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.World;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.World;

/// <summary>
/// The quality a card's piece is drawn at: the archive's ladder, and the two steps that walk it.
/// </summary>
/// <remarks>
/// Two halves that must not be allowed to cover for each other. The first asserts what the archive
/// declares -- that its clear-reward ladder has six bands and that every one of them, the two
/// rarest included, holds at least one roll. The second asserts that the rule service reaches a
/// band by walking that same ladder: a roll of one up to the ladder's own highest, then the band
/// that roll lands in.
/// <para>
/// <b>The failure being guarded against does not look like a failure.</b> A roll bound written as
/// a round million against a ladder whose basis is 1000003 leaves every other number in the table
/// correct, throws nothing, and simply never produces the top two qualities. That is why the bound
/// is asserted as a number here and why the service's two steps are replayed rather than sampled.
/// </para>
/// </remarks>
public sealed class ClearRewardRarityLadderTests
{
    private const ulong Seed = 0x00C0_FFEE_0BAD_1DEA;

    /// <summary>
    /// A ladder of six bands one roll wide each, so a roll and the band it lands in are the same
    /// number less one.
    /// </summary>
    /// <remarks>
    /// Deliberately tiny. Against the archive's own ladder the two rarest bands are one roll in a
    /// million apiece, so no amount of sampling could show them; against this one every band is
    /// equally likely and a wrong roll bound changes which band comes out rather than changing a
    /// probability in the sixth decimal place.
    /// </remarks>
    private static RarityLadder EvenLadder() => new([1, 2, 3, 4, 5, 6, 7]);

    /// <summary>One piece at each of the six qualities, each the only thing in its bucket.</summary>
    /// <remarks>
    /// Every weight is one, so the draw itself consumes no roll -- <c>DropRules.Pick</c> short
    /// circuits a bound of one -- which leaves the generator's sequence made of exactly the two
    /// steps under test.
    /// </remarks>
    private static DropItemPool OnePieceEachQuality() =>
        ClearRewardFixture.Pool(
            [.. Enumerable.Range(0, 6).Select(
                band => new ClearRewardFixture.PoolRow(
                    ClearRewardFixture.EndgameLevel, (DropRarity)band, 5000 + band, 1))]);

    /// <summary>
    /// The archive's clear-reward ladder declares six bands and every one of them is reachable.
    /// </summary>
    /// <remarks>
    /// Asserted against the ladder's own roll range rather than against probabilities, because a
    /// probability computed from a wrong denominator can still look plausible while a roll of
    /// 1000002 either exists or does not.
    /// </remarks>
    [SkippableFact]
    public void Every_Rarity_band_the_archive_declares_is_reachable_including_the_two_rarest()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateContentProvider();

        var ladder = provider.GetRequiredService<IClearRewardRateSource>()
            .Load(DropRateTables.ClearReward).Rarity;

        Assert.NotNull(ladder);
        Assert.Equal([500000, 799900, 999900, 1000000, 1000001, 1000002, 1000003], ladder.Values);
        Assert.Equal(1000003, ladder.Basis);

        // The roll bound is the basis less one, which is the whole of this subject in one line.
        Assert.Equal(ladder.Basis - 1, ladder.HighestRoll);
        Assert.Equal(1000002, ladder.HighestRoll);

        Assert.Equal(6, ladder.BandCount);
        Assert.Equal(500000, ladder.WeightOf((int)DropRarity.Common));
        Assert.Equal(299900, ladder.WeightOf((int)DropRarity.Uncommon));
        Assert.Equal(200000, ladder.WeightOf((int)DropRarity.Rare));
        Assert.Equal(100, ladder.WeightOf((int)DropRarity.Unique));
        Assert.Equal(1, ladder.WeightOf((int)DropRarity.Epic));
        Assert.Equal(1, ladder.WeightOf((int)DropRarity.Chronicle));

        for (var band = 0; band < ladder.BandCount; band++)
        {
            Assert.True(ladder.WeightOf(band) > 0, $"band {band} holds no roll");
            Assert.True(ladder.ProbabilityOf(band) > 0, $"band {band} has no share");
        }

        // Every roll belongs to exactly one band, which is what makes the two rarest one roll each
        // rather than nothing rounded up.
        var total = 0;
        for (var band = 0; band < ladder.BandCount; band++)
            total += ladder.WeightOf(band);
        Assert.Equal(ladder.HighestRoll, total);

        Assert.Equal((int)DropRarity.Epic, ladder.BandOf(1000001));
        Assert.Equal((int)DropRarity.Chronicle, ladder.BandOf(1000002));

        // And the arithmetic of the line this is all about: a bound of a round million stops one
        // roll short of epic and two short of chronicle.
        Assert.True(ladder.HighestRoll > 1_000_000);
        Assert.Equal((int)DropRarity.Unique, ladder.BandOf(1_000_000));
    }

    /// <summary>
    /// The rule service reaches a quality by walking the table's own ladder, and by nothing else.
    /// </summary>
    /// <remarks>
    /// Replayed rather than sampled. The generator is rebuilt from the same seed and driven by
    /// hand through the two steps the existing drop parts use -- the odds, then a roll of one up
    /// to the ladder's highest -- and the qualities that come out must be the ones the service
    /// produced, position for position. Any second ladder semantics, any other roll bound, and any
    /// extra or missing draw makes this diverge.
    /// </remarks>
    [Fact]
    public void The_service_rolls_a_Rarity_by_walking_the_tables_own_ladder()
    {
        var ladder = EvenLadder();
        var service = ClearRewardFixture.Service(
            ClearRewardFixture.Table(ladder: ladder), OnePieceEachQuality());
        var occasion = ClearRewardFixture.Occasion();
        var chance = service.ChanceOf(occasion);

        var face = service.FaceFor(occasion, Seed);

        var replay = new DropRandom(Seed);
        foreach (var slot in face.Slots)
        {
            Assert.True(replay.Hits(chance.Rate, chance.Basis));
            var expected = (DropRarity)ladder.BandOf(replay.Next(ladder.HighestRoll));

            Assert.NotNull(slot.Item);
            Assert.Equal(expected, slot.Item.Value.Rarity);
            Assert.Equal(5000 + (int)expected, slot.Item.Value.ItemId);
        }
    }

    /// <summary>
    /// Every band of the ladder the service is given is actually produced by it.
    /// </summary>
    /// <remarks>
    /// The reachability assertion carried through the service rather than stopped at the ladder:
    /// six bands are declared, and over these seeds all six come out, the top two included. It is
    /// the same statement as the archive assertion above, made where a wrong roll bound would
    /// actually bite.
    /// </remarks>
    [Fact]
    public void The_service_produces_every_Rarity_band_its_ladder_declares()
    {
        var service = ClearRewardFixture.Service(
            ClearRewardFixture.Table(ladder: EvenLadder()), OnePieceEachQuality());

        var produced = Enumerable.Range(0, 64)
            .SelectMany(seed => service.FaceFor(ClearRewardFixture.Occasion(), (ulong)seed).Slots)
            .Select(slot => slot.Item!.Value.Rarity)
            .Distinct()
            .Order()
            .ToList();

        Assert.Equal(
            [
                DropRarity.Common,
                DropRarity.Uncommon,
                DropRarity.Rare,
                DropRarity.Unique,
                DropRarity.Epic,
                DropRarity.Chronicle,
            ],
            produced);
    }

    /// <summary>
    /// A quality whose bucket holds nothing leaves the position empty rather than failing.
    /// </summary>
    /// <remarks>
    /// The archive fills only some of the ladder's qualities at any one grade band, so a position
    /// whose odds came in can still draw nothing. Refusing here would turn a quiet miss into a
    /// clear that fails.
    /// </remarks>
    [Fact]
    public void A_Rarity_whose_bucket_is_empty_leaves_the_position_empty()
    {
        var service = ClearRewardFixture.Service(
            ClearRewardFixture.Table(ladder: ClearRewardFixture.PinnedTo(DropRarity.Chronicle)),
            ClearRewardFixture.Pool(
                new ClearRewardFixture.PoolRow(
                    ClearRewardFixture.EndgameLevel, DropRarity.Common, 1001, 1000)));
        var occasion = ClearRewardFixture.Occasion();

        Assert.False(service.ChanceOf(occasion).IsImpossible);
        Assert.All(service.FaceFor(occasion, Seed).Slots, slot => Assert.True(slot.IsEmpty));
    }
}
