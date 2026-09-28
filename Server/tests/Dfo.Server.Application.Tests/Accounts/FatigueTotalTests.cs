using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Configuration;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Application.Tests.Characters;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Accounts;

public sealed class FatigueTotalTests
{
    [Theory]
    [InlineData(true, 156, 32, 188)]
    [InlineData(false, 156, 32, 156)]
    [InlineData(true, 200, 50, 250)]
    [InlineData(false, 200, 50, 200)]
    [InlineData(true, 0, 0, 0)]
    public async Task Configured_total_and_entitlement_bonus_reach_login(bool enabled, ushort baseTotal, ushort extra, ushort expected)
    {
        await using var f = Create(enabled, baseTotal, extra);
        await f.SeedAsync();
        var premium = f.Services.GetRequiredService<IPremiumServiceDomainService>().For(new AccountId(1));
        Assert.Equal(enabled ? extra : (ushort)0, await premium.GetExtraFatigueAsync(default));
        var fatigue = f.Services.GetRequiredService<ICharacterFatigueDomainService>();
        var before = await fatigue.BalanceOfAsync(f.Session.CharacterId, default);
        Assert.Equal(new CharacterFatigueBalance(expected, 0), before);
        await fatigue.SpendAsync(f.Session.CharacterId, 7, default);
        await fatigue.RefundAsync(f.Session.CharacterId, 3, default);
        Assert.Equal(before, await fatigue.BalanceOfAsync(f.Session.CharacterId, default));
        var character = await f.Services.GetRequiredService<ICharacterRepository>().FindByAccountAndSlotAsync(new(1), 0, default);
        Assert.NotNull(character);
        Assert.Equal(new CharacterFatigueState(expected, 0), character.Fatigue);
    }

    [Fact]
    public async Task Unknown_character_and_unrepresentable_total_are_rejected()
    {
        await using var f = Create(true, ushort.MaxValue, 1);
        await f.SeedAsync();
        var fatigue = f.Services.GetRequiredService<ICharacterFatigueDomainService>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => fatigue.BalanceOfAsync(new(999), default).AsTask());
        await Assert.ThrowsAsync<OverflowException>(() => fatigue.BalanceOfAsync(f.Session.CharacterId, default).AsTask());
    }

    private static LearningFixture Create(bool enabled, ushort baseTotal, ushort extra) => new(configure: services =>
        services.AddSingleton<IServerConfigService>(new ServerConfigService(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Premium:BlackDiamondEnabled"] = enabled.ToString(),
                ["Premium:BlackDiamondExtraFatigue"] = extra.ToString(),
                ["Fatigue:BaseTotal"] = baseTotal.ToString(),
            }).Build())));
}
