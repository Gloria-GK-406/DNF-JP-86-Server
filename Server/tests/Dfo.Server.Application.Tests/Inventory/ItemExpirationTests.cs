using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class ItemExpirationTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 14, 5, 59, 0, TimeSpan.FromHours(8));
    private static readonly ItemSnapshot Item = new() { ItemId = 10100300, LegacyKind = 2, IsPile = true };
    private static readonly ItemExpirationRuleService Rules = new(new Reset());
    private sealed class Reset : IDailyResetRuleService
    {
        public DateTimeOffset PeriodStart(DateTimeOffset now)
        {
            var local = now.ToOffset(TimeSpan.FromHours(8));
            var boundary = new DateTimeOffset(local.Date.AddHours(6), local.Offset);
            return boundary > now ? boundary.AddDays(-1) : boundary;
        }
        public bool IsCurrentPeriod(DateTimeOffset recordedAt, DateTimeOffset now) => recordedAt <= now && recordedAt >= PeriodStart(now);
    }

    [Fact]
    public void Fixed_expiration_is_not_rebased_even_when_already_expired()
    {
        var deadline = checked((int)At.AddYears(-10).ToUnixTimeSeconds());
        Assert.Equal(deadline, Rules.Prepare(Item, new(deadline, 0, false), At).ExpireTime);
    }

    [Theory]
    [InlineData(5, 59, 0)]
    [InlineData(6, 0, 1)]
    [InlineData(6, 1, 1)]
    public void Daily_expiration_is_the_next_configured_reset(int hour, int minute, int extraDays)
    {
        var now = new DateTimeOffset(2026, 12, 31, hour, minute, 0, TimeSpan.FromHours(8));
        var expected = new DateTimeOffset(2026, 12, 31, 6, 0, 0, now.Offset).AddDays(extraDays);
        Assert.Equal(expected.ToUnixTimeSeconds(), Rules.Prepare(Item, new(0, 0, true), now).ExpireTime);
    }

    [Fact]
    public void Period_days_start_at_acquisition_and_combined_limits_choose_earliest()
    {
        Assert.Equal(At.AddDays(7).ToUnixTimeSeconds(), Rules.Prepare(Item, new(0, 7, false), At).ExpireTime);
        Assert.Equal(At.AddMinutes(1).ToUnixTimeSeconds(), Rules.Prepare(Item, new(0, 7, true), At).ExpireTime);
        var sooner = (int)At.AddSeconds(10).ToUnixTimeSeconds();
        Assert.Equal(sooner, Rules.Prepare(Item, new(sooner, 7, true), At).ExpireTime);
    }

    [Theory]
    [InlineData(123456)]
    [InlineData(int.MaxValue)]
    public void Explicit_deadline_is_preserved(int deadline) =>
        Assert.Equal(deadline, Rules.Prepare(Item with { ExpireTime = deadline }, new(0, 7, true), At).ExpireTime);

    [Fact]
    public void Permanent_stays_zero_and_avatar_uses_its_existing_expiration_field()
    {
        Assert.Equal(0, Rules.Prepare(Item, default, At).ExpireTime);
        var avatar = Item with { LegacyKind = 8, AvatarDetail = new(0, 0, new byte[30], 0, 0, 0) };
        var prepared = Rules.Prepare(avatar, new(0, 7, false), At);
        Assert.Equal(At.AddDays(7).ToUnixTimeSeconds(), prepared.AvatarDetail!.ExpireDate);
        Assert.Equal(0, prepared.ExpireTime);
    }

    private sealed class OpeningDefinitions(int defaultDays) : IPackageOpeningDefinitionSource
    {
        public ValueTask<OpeningPackageDefinition?> GetAsync(int id, CancellationToken ct) => ValueTask.FromResult<OpeningPackageDefinition?>(null);
        public ValueTask<OpeningRewardItemDefinition?> GetRewardAsync(int id, CancellationToken ct) =>
            ValueTask.FromResult<OpeningRewardItemDefinition?>(new(id,
                new(3, 50, new(BusinessInventorySpace.Material, 0, 55)) { Lifetime = new(0, defaultDays, true) },
                0, false, [], null, true, defaultDays));
    }

    [Theory]
    [InlineData(7, 0)]
    [InlineData(0, 2)]
    public async Task Prepared_opening_takes_daily_limit_even_with_positive_template_or_package_period(int defaultDays, int rewardDays)
    {
        var opening = new PackageOpeningRuleService(new OpeningDefinitions(defaultDays), null!, Rules);
        var result = await opening.PrepareAsync([new(9001, 1, PeriodDays: rewardDays)], At, default);
        Assert.Equal(At.AddMinutes(1).ToUnixTimeSeconds(), Assert.Single(result).Expiration);
    }
}
