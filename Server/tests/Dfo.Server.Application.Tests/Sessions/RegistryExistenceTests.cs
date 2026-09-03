using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.World;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Sessions;

/// <summary>
/// Proves that an empty runtime view means "asked and nobody was there", not "there was
/// nothing to ask".
/// </summary>
/// <remarks>
/// This is the distinction that decided the previous attempt at this outcome. Its views
/// also reported empty, but nothing existed behind them, and no test could tell the
/// difference. These resolve the real production graph and consult it with nothing written.
/// </remarks>
public sealed class RegistryExistenceTests
{
    [Fact]
    public void Every_runtime_registry_resolves_from_the_production_graph()
    {
        using var provider = BuildProductionGraph();

        Assert.NotNull(provider.GetRequiredService<ISessionPresenceDomainService>());
        Assert.NotNull(provider.GetRequiredService<IExpertStoreDomainService>());
        Assert.NotNull(provider.GetRequiredService<IPvpLobbyDomainService>());
        Assert.NotNull(provider.GetRequiredService<ISessionAdjacentEntryDomainService>());
    }

    [Fact]
    public async Task Every_view_answers_explicitly_when_nothing_has_been_written()
    {
        using var provider = BuildProductionGraph();
        var service = provider.GetRequiredService<ISessionAdjacentEntryDomainService>();

        var result = await service.LoadAsync(
            new SessionAdjacentSubject(
                new CharacterId(1),
                new AccountId(1),
                StubChannelEntryPolicy.FreeDuelChannel,
                TownId: 1,
                AreaId: 0),
            CancellationToken.None);

        Assert.Empty(result.TownPresence.Others);
        Assert.False(result.ExpertStore.OwnsOpenStore);
        Assert.NotNull(result.PvpLobby);
        Assert.Empty(result.PvpLobby!.Participants);
        Assert.Empty(result.PvpLobby.Rooms);
    }

    /// <remarks>
    /// The registries are single instances for the server's lifetime. Were they resolved
    /// per call, a write would vanish before the next read and every view would report
    /// empty forever while still looking correct.
    /// </remarks>
    [Fact]
    public async Task A_registry_written_to_is_the_same_one_the_view_consults()
    {
        using var provider = BuildProductionGraph();
        provider.GetRequiredService<ISessionPresenceDomainService>().Admit(
            new SessionPresence(new CharacterId(2), 10011, 1, 0, StandingInTown: true));

        var result = await provider.GetRequiredService<ISessionAdjacentEntryDomainService>().LoadAsync(
            new SessionAdjacentSubject(
                new CharacterId(1),
                new AccountId(1),
                StubChannelEntryPolicy.GeneralChannel,
                TownId: 1,
                AreaId: 0),
            CancellationToken.None);

        Assert.Equal([2], result.TownPresence.Others.Select(other => other.CharacterId.Value));
    }

    private static ServiceProvider BuildProductionGraph()
    {
        var services = new ServiceCollection();
        new ApplicationModule().ConfigureServices(
            services,
            new ConfigurationBuilder().Build());
        return services.BuildServiceProvider();
    }
}
