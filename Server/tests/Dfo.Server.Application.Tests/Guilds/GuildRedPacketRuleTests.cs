using Dfo.Server.Application.Guilds;

namespace Dfo.Server.Application.Tests.Guilds;

[Trait("Category", "Unit")]
public sealed class GuildRedPacketRuleTests
{
    [Theory]
    [InlineData(100, 100, 5000, false, 100, 0)]
    [InlineData(100, 25, 5000, false, 25, 37)]
    [InlineData(100, 0, 5000, false, 0, 50)]
    [InlineData(100, 25, 5000, true, 0, 0)]
    [InlineData(int.MaxValue, 0, 10_000, false, 0, int.MaxValue)]
    public void Settlement_refunds_unclaimed_tokens_and_rewards_claimed_amount_unless_sender_departed(
        int amount, int remaining, int rate, bool departed, int refund, int points)
    {
        Assert.Equal(new GuildRedPacketSettlement(refund, points),
            GuildRedPacketRuleService.CalculateSettlement(amount, remaining, rate, departed));
    }

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
