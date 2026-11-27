using Dfo.Server.Application.Guilds;

namespace Dfo.Server.Application.Tests.Guilds;

[Trait("Category", "Unit")]
public sealed class GuildRedPacketRuleTests
{
    [Theory]
    [InlineData(30, 30)]
    [InlineData(100, 7)]
    [InlineData(int.MaxValue, 2)]
    public void Seeded_claims_reserve_each_share_and_distribute_the_full_balance(int total, int shares)
    {
        var remaining = total;
        for (var left = shares; left > 0; left--)
        {
            var seed = left * 17;
            var payout = GuildRedPacketRuleService.RollClaimAmount(remaining, left, seed);
            Assert.Equal(payout, GuildRedPacketRuleService.RollClaimAmount(remaining, left, seed));
            if (left == 1)
                Assert.Equal(remaining, payout);
            else
                Assert.InRange(payout, 1, Math.Min(remaining - left + 1, 2 * (remaining / left)));
            remaining -= payout;
        }
        Assert.Equal(0, remaining);
    }
}
