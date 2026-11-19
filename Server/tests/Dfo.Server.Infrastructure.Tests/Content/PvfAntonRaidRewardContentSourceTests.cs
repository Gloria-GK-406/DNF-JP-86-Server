using Dfo.Server.Abstractions.Raids;
using Dfo.Server.Infrastructure.Content;
using Xunit;

namespace Dfo.Server.Infrastructure.Tests.Content;

public sealed class PvfAntonRaidRewardContentSourceTests
{
    [SkippableFact]
    public void DevArchiveExpandsBothPhasesAndReadsTheRetryCertificate()
    {
        Skip.IfNot(ContentArchive.Available, ContentArchive.SkipReason);
        using var provider = ContentArchive.CreateProvider();
        var rewards = new PvfAntonRaidRewardContentSource(provider).Read();

        Assert.Equal(new AntonRaidRewardItem(10099897, 1), rewards.RetryCertificate);
        Assert.Equal(2, rewards.Phases.Count);
        var first = rewards.Phases[0];
        var second = rewards.Phases[1];
        Assert.Equal([1, 5, 9, 14], first.Ranks.Select(rank => rank.Maximum));
        Assert.Equal(first.Ranks, second.Ranks);
        var stones = Assert.Single(first.Pools, pool => pool.Kind == AntonRaidRewardKind.PartyCard);
        Assert.Equal([3, 4], stones.Choices.Select(choice => choice.Item.Quantity));
        Assert.All(stones.Choices, choice => Assert.Equal(10093974, choice.Item.ItemId));
        var gold = Assert.Single(second.Pools, pool => pool.Kind == AntonRaidRewardKind.Gold && pool.State == 3);
        Assert.Equal(new AntonRaidRewardItem(0, 160000), Assert.Single(gold.Choices).Item);
        Assert.Equal([775, 145, 50, 25, 5],
            second.Pools.Where(pool => pool.Kind == AntonRaidRewardKind.SquadItem).Select(pool => pool.Weight));
    }
}
