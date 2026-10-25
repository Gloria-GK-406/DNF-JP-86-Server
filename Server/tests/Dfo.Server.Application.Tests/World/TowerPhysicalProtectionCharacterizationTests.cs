using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.World;

public sealed partial class BossTowerDomainServiceTests
{
    [Fact]
    public async Task Current_whole_deduplication_keeps_an_earlier_session_only_entry_and_hides_a_later_declared_reporter_physical()
    {
        using var fixture = Fixture(configure: TowerPhysicalProtectionProbe.Install);
        var observation = await TowerPhysicalProtectionProbe.RunAsync(fixture);
        Assert.True(observation.FirstBlocked);
        Assert.False(observation.SecondBlocked);
        Assert.Equal([fixture.Session.ConnectionId], fixture.Sender.Outbound.Closes);
    }
}

internal sealed record TowerPhysicalProtectionObservation(bool FirstBlocked, bool SecondBlocked,
    SelectedSessionIdentity Second);

internal sealed class TowerPhysicalProtectionState
{
    internal IOException Cause { get; } = new("failure after both real explicit units committed");
    internal List<DomainTransaction> Transactions { get; } = [];
}

internal sealed record TowerPhysicalProtectionCommand(SelectedSessionIdentity First, NetworkConnectionContext FirstPhysical,
    SelectedSessionIdentity Second, NetworkConnectionContext SecondPhysical) : ICommand<int>;

internal sealed class TowerPhysicalProtectionHandler(ICommandWorkUnits units, ICharacterDomainService characters,
    IDomainTransactionAccessor transactions, TowerPhysicalProtectionState state) : ICommandHandler<TowerPhysicalProtectionCommand, int>
{
    public async ValueTask<int> HandleAsync(CommandExecutionContext context, TowerPhysicalProtectionCommand command,
        CancellationToken token)
    {
        await units.WriteAsync(context, new("first-member", [new(command.First, command.FirstPhysical), new(command.Second, null)]),
            async (_, ct) =>
            {
                state.Transactions.Add(Assert.IsType<DomainTransaction>(transactions.Current));
                return await characters.Get(command.First.CharacterId).AddGoldAsync(7, ct);
            }, token);
        await units.WriteAsync(context, new("second-member", [new(command.First, null), new(command.Second, command.SecondPhysical)]),
            async (_, ct) =>
            {
                state.Transactions.Add(Assert.IsType<DomainTransaction>(transactions.Current));
                return await characters.Get(command.Second.CharacterId).AddGoldAsync(7, ct);
            }, token);
        throw state.Cause;
    }
}

internal static class TowerPhysicalProtectionProbe
{
    internal static void Install(IServiceCollection services)
    {
        services.AddSingleton<TowerPhysicalProtectionState>();
        services.AddInternalCommandHandler<TowerPhysicalProtectionCommand, int, TowerPhysicalProtectionHandler>();
    }

    internal static async Task<TowerPhysicalProtectionObservation> RunAsync(ShopFixture fixture)
    {
        SelectedSessionIdentity peer = null!;
        NetworkConnectionContext physical = null!;
        await TestGameplay.RunAsync(fixture.Services, async () =>
        {
            peer = await TowerSourceMembers.AddAsync(fixture, 3, 2, 65, 2749110);
            Assert.True(fixture.Sender.Outbound.Connections.TryGet(peer.ConnectionId, out var found));
            physical = found!;
        });
        var state = fixture.Services.GetRequiredService<TowerPhysicalProtectionState>();
        var failure = await Assert.ThrowsAsync<TransactionOutcomeUncertainException>(() => fixture.Services.GetRequiredService<ICommandBus>()
            .SendInternalAsync(new TowerPhysicalProtectionCommand(fixture.Session, fixture.Connection, peer, physical),
                new(GameplayCallSource.Timer, GameplayCallChain.NewRoot(GameplayCallSource.Timer), "timer:physical-protection-control")).AsTask());
        Assert.Same(state.Cause, failure.InnerException);
        Assert.Equal(2, state.Transactions.Count);
        Assert.NotSame(state.Transactions[0], state.Transactions[1]);
        Assert.All(state.Transactions, transaction => Assert.Equal(DomainTransactionCompletion.Committed, transaction.Completion));
        Assert.Equal(100007, fixture.Gold);
        Assert.Equal(107, fixture.Scalar("SELECT carried_gold FROM character_stats WHERE character_id=3"));
        return TestGameplay.Run(fixture.Services, () =>
        {
            var selected = fixture.Services.GetRequiredService<ISelectedSessionDomainService>();
            Assert.True(selected.StandingOf(fixture.Session)!.Fenced);
            Assert.True(selected.StandingOf(peer)!.Fenced);
            var safety = fixture.Services.GetRequiredService<SelectedSessionSafety>();
            return new TowerPhysicalProtectionObservation(safety.IsBlocked(fixture.Connection), safety.IsBlocked(physical), peer);
        });
    }
}
