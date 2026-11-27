using Dfo.Server.Application.Guilds;
using Dfo.Server.Data.Guilds;

namespace Dfo.Server.Application.Tests.Guilds;

[Trait("Category", "Unit")]
public sealed class GuildRankRuleTests
{
    [Theory]
    [InlineData(true, GuildRankSettings.Member, true)]
    [InlineData(false, GuildRankSettings.ViceLeader, true)]
    [InlineData(false, 3, true)]
    [InlineData(false, GuildRankSettings.Member, false)]
    [InlineData(false, GuildRankSettings.LastRank, false)]
    [InlineData(false, 0, false)]
    public void Warehouse_transfers_use_the_members_effective_rank(bool leader, byte storedRank, bool allowed)
    {
        var member = new GuildMember(new(1), new(2), leader, DateTimeOffset.UnixEpoch, MemberRank: storedRank);
        Assert.Equal(allowed, GuildRankRuleService.CanTransferWarehouseItems(member));
    }
}
