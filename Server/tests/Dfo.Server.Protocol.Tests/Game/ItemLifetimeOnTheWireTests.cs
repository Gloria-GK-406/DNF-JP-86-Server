using Dfo.Server.Data.Inventory;
using Dfo.Server.Protocol.Game;

namespace Dfo.Server.Protocol.Tests.Game;

[Trait("Category", "Unit")]
public sealed class ItemLifetimeOnTheWireTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 27, 12, 0, 0, TimeSpan.FromHours(8));
    private static int Now => checked((int)At.ToUnixTimeSeconds());

    [Fact]
    public void A_permanent_lifetime_selects_the_unlimited_label()
    {
        Assert.Equal(0, ItemLifetimeOnTheWire.RemainingSeconds(ItemLifetime.Permanent, At));
    }

    [Fact]
    public void Int_max_counts_down_like_any_other_deadline()
    {
        Assert.Equal(int.MaxValue - Now, ItemLifetimeOnTheWire.RemainingSeconds(ItemLifetime.At(int.MaxValue), At));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void A_negative_deadline_selects_the_expired_label(int deadline)
    {
        Assert.Equal(-1, ItemLifetimeOnTheWire.RemainingSeconds(ItemLifetime.At(deadline), At));
    }

    [Theory]
    [InlineData(-3600, -1)]
    [InlineData(0, -1)]
    [InlineData(1, 1)]
    [InlineData(86400, 86400)]
    public void A_positive_deadline_sends_the_seconds_left_until_it_ends(int offset, int remaining)
    {
        Assert.Equal(remaining, ItemLifetimeOnTheWire.RemainingSeconds(ItemLifetime.At(Now + offset), At));
    }
}
