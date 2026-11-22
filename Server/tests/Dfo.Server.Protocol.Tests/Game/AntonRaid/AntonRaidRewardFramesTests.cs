using Dfo.Server.Abstractions.Raids;
using Dfo.Server.Protocol.Game.AntonRaid;
using Xunit;

namespace Dfo.Server.Protocol.Tests.Game.AntonRaid;

[Trait("Category", "Unit")]
public sealed class AntonRaidRewardFramesTests
{
    [Fact]
    public void CardsDisplayCappedGoldAndPreserveTheGrantedAmount()
    {
        var card = new AntonRaidRewardCard(new(42), AntonRaidRewardKind.Gold, 3, 1, new(0, 160000));
        var frame = AntonRaidRewardNotice.Cards(AntonRaidRewardKind.Gold, [(0x1234, card)]);
        Assert.Equal<byte>([0, 1, 0x34, 0x12, 3, 1, 0, 0, 0, 0, 0xFF, 0xFF], frame.Payload);
        Assert.Equal(160000, card.Item.Quantity);
    }

    [Fact]
    public void ResultUsesTheStageElapsedTimeAndSeparatesSuccessfulAndFailedRank()
    {
        var start = DateTimeOffset.UnixEpoch;
        var seed = new AntonRaidRewardSettlementSeed(Guid.NewGuid(), 4, 1, true, 65536,
            start, start.AddSeconds(513), [], []);
        var snapshot = new AntonRaidRewardSnapshot(seed, 2, AntonRaidRewardStage.Result, []);
        var result = AntonRaidRewardNotice.Result(snapshot, true);
        Assert.Equal<byte>([0, 1, 1, 2, 0, 0, 0xFF, 0xFF, 4, 0], result.Payload);
        var failure = AntonRaidRewardNotice.Result(snapshot with { Seed = seed with { Succeeded = false } }, false);
        Assert.Equal<byte>([1, 1, 1, 2, 0, 0, 0xFF, 0xFF, 0, 1], failure.Payload);
    }
}
