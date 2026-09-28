using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Application.Tests.Characters;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Accounts;

public sealed class BlackDiamondTests
{
    [Fact]
    public async Task Default_grant_is_stable_shared_by_accounts_and_never_persisted()
    {
        var clock = new Clock();
        await using var f = new LearningFixture(configure: services => services.AddSingleton<TimeProvider>(clock));
        await f.SeedAsync();
        var service = f.Services.GetRequiredService<IPremiumServiceDomainService>();
        var first = service.For(new(1));
        var grant = Assert.Single(await first.ReadAsync(default));
        Assert.Equal((int)PremiumServiceType.BlackDiamond, grant.PremiumType);
        Assert.Equal(clock.Now.AddDays(365).ToUnixTimeSeconds(), grant.ExpiresAtUnixSeconds);
        Assert.Equal(grant, Assert.Single(await first.ReadAsync(default)));
        Assert.Equal(grant, Assert.Single(await service.For(new(2)).ReadAsync(default)));
        clock.Now = clock.Now.AddDays(366);
        Assert.Equal(clock.Now.AddDays(365).ToUnixTimeSeconds(),
            Assert.Single(await first.ReadAsync(default)).ExpiresAtUnixSeconds);
        Assert.True(await first.IsActiveAsync(PremiumServiceType.BlackDiamond, default));
        Assert.Equal(0, f.Scalar("SELECT COUNT(*) FROM account_premiums"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => first.ExtendAsync(
            PremiumServiceType.BlackDiamond, TimeSpan.FromDays(30), default).AsTask());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Configuration_controls_legacy_black_diamond_without_changing_other_contracts(bool enabled)
    {
        await using var f = new LearningFixture(configure: services =>
            services.AddSingleton<IServerConfigService>(new Settings(enabled)));
        await f.SeedAsync();
        f.Sql("INSERT INTO account_premiums(account_id,premium_type,end_time) VALUES(1,56,4000000000),(1,27,4000000000)");
        var account = f.Services.GetRequiredService<IPremiumServiceDomainService>().For(new(1));
        var entries = await account.ReadAsync(default);
        Assert.Equal(enabled ? 2 : 1, entries.Count);
        Assert.Equal(enabled, await account.IsActiveAsync(PremiumServiceType.BlackDiamond, default));
        Assert.True(await account.IsActiveAsync(PremiumServiceType.ExpertSkillContract, default));
        Assert.Equal(4000000000L, Assert.Single(entries, x => x.PremiumType == 27).ExpiresAtUnixSeconds);
        Assert.Equal(2, f.Scalar("SELECT COUNT(*) FROM account_premiums"));
    }

    private sealed class Settings(bool enabled) : IServerConfigService
    {
        public T GetConfig<T>(string key) => key == "Premium:BlackDiamondEnabled" && typeof(T) == typeof(bool)
            ? (T)(object)enabled : throw new InvalidOperationException(key);
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.FromUnixTimeSeconds(1700000000);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
