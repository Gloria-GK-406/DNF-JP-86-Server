using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Tests.Inventory;

[Trait("Category", "Unit")]
public sealed class ItemLifetimeTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 27, 12, 0, 0, TimeSpan.FromHours(8));
    private static int Now => checked((int)At.ToUnixTimeSeconds());

    [Fact]
    public void Zero_is_the_only_permanent_value()
    {
        var lifetime = ItemLifetime.At(0);

        Assert.True(lifetime.IsPermanent);
        Assert.Equal(ItemLifetime.Permanent, lifetime);
        Assert.False(lifetime.IsExpired(At));
        Assert.False(lifetime.IsExpired(DateTimeOffset.MaxValue));
    }

    [Fact]
    public void Int_max_is_a_2038_deadline_not_permanent()
    {
        var lifetime = ItemLifetime.At(int.MaxValue);

        Assert.False(lifetime.IsPermanent);
        Assert.False(lifetime.IsExpired(At));
        Assert.True(lifetime.IsExpired(DateTimeOffset.FromUnixTimeSeconds(int.MaxValue)));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void A_negative_deadline_has_already_passed(int deadline)
    {
        var lifetime = ItemLifetime.At(deadline);

        Assert.False(lifetime.IsPermanent);
        Assert.True(lifetime.IsExpired(At));
    }

    [Theory]
    [InlineData(-3600, true)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(86400, false)]
    public void A_positive_deadline_ends_at_its_own_second(int offset, bool expired)
    {
        var lifetime = ItemLifetime.At(Now + offset);

        Assert.False(lifetime.IsPermanent);
        Assert.Equal(expired, lifetime.IsExpired(At));
    }

    [Fact]
    public void An_avatar_reads_its_detail_deadline_and_ignores_the_core()
    {
        var avatar = new ItemSnapshot { LegacyKind = 8, ExpireTime = Now - 1 } with
        {
            AvatarDetail = new(Now + 60, 0, new byte[30], 0, 0, 0),
        };

        Assert.Equal(Now + 60, avatar.Lifetime.Deadline);
        Assert.False(avatar.Lifetime.IsExpired(At));
    }

    [Fact]
    public void An_avatar_without_detail_is_permanent()
    {
        var avatar = new ItemSnapshot { LegacyKind = 8, ExpireTime = Now - 1 };

        Assert.True(avatar.Lifetime.IsPermanent);
    }

    [Fact]
    public void Any_other_kind_reads_the_core_deadline_and_ignores_avatar_detail()
    {
        var item = new ItemSnapshot { LegacyKind = 2, ExpireTime = Now - 1 } with
        {
            AvatarDetail = new(0, 0, new byte[30], 0, 0, 0),
        };

        Assert.Equal(Now - 1, item.Lifetime.Deadline);
        Assert.True(item.Lifetime.IsExpired(At));
    }
}
