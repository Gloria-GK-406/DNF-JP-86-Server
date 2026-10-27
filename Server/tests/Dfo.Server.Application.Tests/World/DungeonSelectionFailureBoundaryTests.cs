using System.Buffers.Binary;
using System.Reflection;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Execution;
using Dfo.Server.Application.Execution.Outbox;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Game;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.DungeonSelection;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.World;

public sealed partial class BossTowerDomainServiceTests
{
    [Fact]
    public async Task Task42_released_boss_run_keeps_its_full_protection_scope_when_the_real_selection_encoder_fails()
    {
        await using var probe = new SelectionFailureProbe(selection: true, illusion: false, scopeFailure: false);
        using var f = Fixture(configure: services =>
        {
            probe.Configure(services);
            services.AddSingleton<ISelectedCharacterCurrentViewDomainService>(p => new SelectionFailureProbe.CompleteView(p));
            services.AddSingleton<ICharacterStateProjectionDomainService>(TownMovementProtocolCommandFlowTests.Proxy<ICharacterStateProjectionDomainService>.Create((_, args) => ValueTask.FromResult((SelectCharacterResult)args![0]!)));
            services.AddSingleton<IDungeonVisitDomainService>(TownMovementProtocolCommandFlowTests.Proxy<IDungeonVisitDomainService>.Create((name, _) => name == "DefersSelectionState" ? true : throw new NotSupportedException(name)));
        });
        SelectedSessionIdentity peer = null!;
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            f.SetLevel(65); await f.Seed(65, 2749110, 3); peer = await AddMember(f, 3);
            var tower = Tower(f);
            var run = (await tower.EnterAsync([new(f.Session, 0), new(peer, 1)], f.Session, 42, default)).Run!;
            await tower.LoadAsync(Context(run, f.Session), default); await tower.LoadAsync(Context(run, peer), default);
            await tower.StartAsync(Context(run, f.Session), default);
            await TestEffectFootprints.Run(new[] { f.Session, peer }, cap => tower.KillAsync(Context(run, f.Session), run.Room.Actors[0].Uid, cap, default));
            run = (await tower.NextLevelAsync(Context(run, f.Session), 0, 0, default)).Run!;
            await tower.LoadAsync(Context(run, f.Session), default); await tower.LoadAsync(Context(run, peer), default);
            await tower.StartAsync(Context(run, f.Session), default);
            await TestEffectFootprints.Run(new[] { f.Session, peer }, cap => tower.DieAsync(Context(run, peer), cap, default));
            run = (await TestEffectFootprints.Run(new[] { f.Session, peer }, cap => tower.DieAsync(Context(run, f.Session), cap, default))).Run!;
            Assert.True((await tower.SelectResultAsync(Context(run, f.Session), 0, default)).Changed);
        });
        var physical = probe.Sender.Add(f.Connection);
        var replacement = f.Connection with { };
        var next = probe.Sender.Add(replacement);
        f.Sender.Outbound.Encoder.Fail((connection, _) =>
        {
            Assert.Same(f.Connection, connection);
            Assert.Null(Tower(f).Find(f.Session)); Assert.Null(Tower(f).Find(peer));
            f.Sender.Outbound.Connections.Remove(f.Connection.ConnectionId);
            f.Sender.Outbound.Connections.Add(replacement);
            return probe.Cause;
        });
        var failure = await Record.ExceptionAsync(() => probe.Wire(f, 0xF, BitConverter.GetBytes((ushort)3505)).AsTask());
        await probe.AssertFailure(f, failure, physical, next, replacement, [f.Session, peer], scope: false);
        TestGameplay.Run(f.Services, () =>
        {
            Assert.Null(Tower(f).Find(f.Session)); Assert.Null(Tower(f).Find(peer));
            Assert.Equal(DungeonEntryIntent.Directed(3505), f.Services.GetRequiredService<IDungeonEntryIntentDomainService>().Find(f.Session));
        });
        Assert.Equal(2, f.Count(2749110));
    }

    [Fact]
    public async Task Task42_boss_back_scope_failure_keeps_committed_departure_and_all_original_members()
    {
        await using var probe = new SelectionFailureProbe(selection: false, illusion: false, scopeFailure: true);
        using var f = Fixture(configure: probe.Configure);
        SelectedSessionIdentity peer = null!;
        RemainingOpeningProtocolCommandFlowTests.PhysicalConnection? peerPhysical = null;
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            f.SetLevel(65); await f.Seed(65, 2749110, 3); peer = await AddMember(f, 1);
            var user = f.Services.GetRequiredService<IOnlineUserQueryDomainService>(); Assert.True(user.TryGetByConnection(peer.ConnectionId, out var online));
            f.Sender.Outbound.Connections.Add(online!.Connection);
            peerPhysical = probe.Sender.Add(online.Connection);
            Assert.NotNull((await Tower(f).EnterAsync([new(f.Session, 0), new(peer, 1)], f.Session, 42, default)).Run);
        });
        var physical = probe.Sender.Add(f.Connection); var replacement = f.Connection with { }; var next = probe.Sender.Add(replacement);
        probe.BeforeDispose = () => { f.Sender.Outbound.Connections.Remove(f.Connection.ConnectionId); f.Sender.Outbound.Connections.Add(replacement); };
        var failure = await Record.ExceptionAsync(() => probe.Wire(f, 0x84, []).AsTask());
        await probe.AssertFailure(f, failure, physical, next, replacement, [f.Session, peer], scope: true);
        TestGameplay.Run(f.Services, () => { Assert.Null(Tower(f).Find(f.Session)); Assert.Equal(peer, Assert.Single(Tower(f).Find(peer)!.Members).Session); });
        Assert.Equal(SelectionFailureProbe.Legacy ? 0 : 1, peerPhysical!.Sends.Count); Assert.Equal(0, peerPhysical.CloseCalls);
        if (!SelectionFailureProbe.Legacy)
        {
            var hostFrame = Assert.Single(peerPhysical.Sends);
            Assert.Equal((byte)0, hostFrame[0]);
            Assert.Equal((ushort)0x1A, BinaryPrimitives.ReadUInt16LittleEndian(hostFrame.AsSpan(1)));
        }
    }
}

public sealed partial class IllusionTowerDomainServiceTests
{
    [Fact]
    public async Task Task42_illusion_back_scope_failure_keeps_the_original_empty_selected_protection()
    {
        await using var probe = new SelectionFailureProbe(selection: false, illusion: true, scopeFailure: true);
        using var f = Fixture(configure: probe.Configure);
        SelectedSessionIdentity peer = null!;
        RemainingOpeningProtocolCommandFlowTests.PhysicalConnection? peerPhysical = null;
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            f.SetLevel(50); await f.Seed(65, 4183, 3); peer = await AddMember(f, 1);
            var user = f.Services.GetRequiredService<IOnlineUserQueryDomainService>(); Assert.True(user.TryGetByConnection(peer.ConnectionId, out var online));
            f.Sender.Outbound.Connections.Add(online!.Connection);
            peerPhysical = probe.Sender.Add(online.Connection);
            Assert.NotNull((await Tower(f).EnterAsync([new(f.Session, 0), new(peer, 1)], f.Session, 42, default)).Run);
        });
        var physical = probe.Sender.Add(f.Connection); var replacement = f.Connection with { }; var next = probe.Sender.Add(replacement);
        probe.BeforeDispose = () => { f.Sender.Outbound.Connections.Remove(f.Connection.ConnectionId); f.Sender.Outbound.Connections.Add(replacement); };
        var failure = await Record.ExceptionAsync(() => probe.Wire(f, 0x84, []).AsTask());
        await probe.AssertFailure(f, failure, physical, next, replacement, [], scope: true);
        TestGameplay.Run(f.Services, () => { Assert.Null(Tower(f).Find(f.Session)); Assert.Equal(peer, Assert.Single(Tower(f).Find(peer)!.Members).Session); });
        Assert.Equal(SelectionFailureProbe.Legacy ? 0 : 1, peerPhysical!.Sends.Count); Assert.Equal(0, peerPhysical.CloseCalls);
        if (!SelectionFailureProbe.Legacy)
        {
            var hostFrame = Assert.Single(peerPhysical.Sends);
            Assert.Equal((byte)0, hostFrame[0]);
            Assert.Equal((ushort)0x1A, BinaryPrimitives.ReadUInt16LittleEndian(hostFrame.AsSpan(1)));
        }
        Assert.False(f.Services.GetRequiredService<ISelectedSessionDomainService>().StandingOf(peer)!.Fenced);
        Assert.False(f.Services.GetRequiredService<ISelectedSessionDomainService>().StandingOf(f.Session)!.Fenced);
    }
}

internal sealed class SelectionFailureProbe(bool selection, bool illusion, bool scopeFailure) : IAsyncDisposable
{
    internal static bool Legacy => typeof(EnterSelectDungeonHandler).Assembly.GetType("Dfo.Server.Protocol.Game.DungeonSelection.Task42OldEnterSelectDungeonHandler") is not null;
    internal readonly RemainingOpeningProtocolCommandFlowTests.SenderProbe Sender = new();
    internal readonly Exception Cause = new IOException("TASK42 committed selection boundary");
    internal readonly List<DomainTransaction> Transactions = [];
    internal GameplayCallChain Chain;
    internal Action? BeforeDispose;

    internal void Configure(IServiceCollection services)
    {
        services.AddSingleton<IOutboundSender>(Sender.Recorder);
        services.AddSingleton(p => new GameplayOutboxes(p.GetRequiredService<IOutboundSender>(), p.GetRequiredService<GameplayCloseCauses>(), p.GetRequiredService<Microsoft.Extensions.Logging.ILogger<GameplayOutbox>>()));
        services.AddSingleton<ISelectedCharacterCurrentViewDomainService, Task39NoReturnView>();
        var command = Legacy ? selection ? "ReleaseBossTowerSelectionCommand" : illusion ? "LeaveIllusionTowerCommand" : "LeaveBossTowerCommand"
            : selection ? "GameEnterSelectDungeonCommand" : "GameBackToVillageCommand";
        var descriptor = services.Single(value => value.ServiceType.IsGenericType && value.ServiceType.GetGenericTypeDefinition() == typeof(ICommandHandler<,>) && value.ServiceType.GenericTypeArguments[0].Name == command);
        services.Remove(descriptor);
        services.AddScoped(descriptor.ServiceType, p => Activator.CreateInstance(typeof(Scope<,>).MakeGenericType(descriptor.ServiceType.GenericTypeArguments), ActivatorUtilities.CreateInstance(p, descriptor.ImplementationType!), p, this, scopeFailure)!);
        var units = services.Last(value => value.ServiceType == typeof(ICommandWorkUnits));
        services.Remove(units);
        services.AddScoped<ICommandWorkUnits>(p => new Units((ICommandWorkUnits)(units.ImplementationFactory?.Invoke(p) ?? ActivatorUtilities.CreateInstance(p, units.ImplementationType!)), p, this));
    }

    internal ValueTask Wire(ShopFixture f, ushort type, byte[] body)
    {
        var metadata = new GameMessageMetadata(1, type, 0, 7) { InboundOrdinal = 7, SequenceIsFresh = true }.WithRawBody(body);
        var registration = f.Services.GetServices<GameMessageDecoderRegistration>().Single(value => value.Selector == 1 && value.Type == type);
        var message = ((IGameMessageDecoder)f.Services.GetRequiredService(registration.DecoderType)).Decode(metadata, body)!;
        return f.Services.GetRequiredService<INetworkMessageDispatcher>().DispatchAsync(new(f.Connection), message, default);
    }

    internal async Task AssertFailure(ShopFixture f, Exception? failure,
        RemainingOpeningProtocolCommandFlowTests.PhysicalConnection physical,
        RemainingOpeningProtocolCommandFlowTests.PhysicalConnection next,
        NetworkConnectionContext replacement, IReadOnlyList<SelectedSessionIdentity> protectedMembers, bool scope)
    {
        if (Legacy) Assert.Same(Cause, failure);
        else
        {
            var typed = Assert.IsType<TransactionOutcomeUncertainException>(failure);
            Assert.Same(Cause, typed.InnerException);
            Assert.Equal(TransactionUncertaintyReason.PostCommitPublicationFailure, typed.Reason);
        }
        Assert.NotEmpty(Transactions);
        Assert.All(Transactions, transaction => Assert.Equal(DomainTransactionCompletion.Committed, transaction.Completion));
        await physical.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10)); await Sender.Settled();
        Assert.Equal(1, physical.CloseCalls); Assert.Equal(0, next.CloseCalls); Assert.Empty(next.Events);
        Assert.True(f.Services.GetRequiredService<GameplayCloseCauses>().TryTake(f.Connection, out var chain));
        Assert.Equal(Chain.Id, chain.Id); Assert.Equal(Chain.RootId, chain.RootId); Assert.Equal(f.Connection.ConnectionId, chain.SourceConnection);
        foreach (var member in protectedMembers)
            Assert.Equal(!Legacy, f.Services.GetRequiredService<ISelectedSessionDomainService>().StandingOf(member)!.Fenced);
        Assert.Equal(!Legacy && protectedMembers.Count > 0, f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
        Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(replacement));
        if (!scope) Assert.Empty(physical.Sends);
    }

    internal sealed class CompleteView(IServiceProvider provider) : ISelectedCharacterCurrentViewDomainService
    {
        public async ValueTask<SelectCharacterResult?> TryLoadAsync(SelectedSessionIdentity session, CancellationToken token)
        {
            var baseline = await Characters.CharacterEntryInventoryTests.CreateLifecycleResultAsync(session.AccountId, session.CharacterId.Value, 0);
            var inventory = await provider.GetRequiredService<ICharacterInventoryEntryDomainService>().ReadCurrentAsync(session.AccountId, session.CharacterId, token);
            return baseline with { CharacterInventory = inventory, CharacterEntry = baseline.CharacterEntry with { Skills = baseline.CharacterEntry.Skills with { ProtocolMirror = new(0, 0, 0, 0) }, Public = baseline.CharacterEntry.Public with { Identity = new(0, 0, 0) } } };
        }
    }

    private sealed class Scope<TCommand, TResult>(object actual, IServiceProvider provider, SelectionFailureProbe trace, bool fail) : ICommandHandler<TCommand, TResult>, IDisposable
        where TCommand : class, ICommand<TResult> where TResult : notnull
    {
        public ValueTask<TResult> HandleAsync(CommandExecutionContext context, TCommand command, CancellationToken token)
        {
            trace.Chain = GameplayJob.Current!.Value;
            if (provider.GetRequiredService<IDomainTransactionAccessor>().Current is DomainTransaction transaction) trace.Transactions.Add(transaction);
            return ((ICommandHandler<TCommand, TResult>)actual).HandleAsync(context, command, token);
        }
        public void Dispose() { if (fail) { trace.BeforeDispose?.Invoke(); throw trace.Cause; } }
    }
    private sealed class Units(ICommandWorkUnits actual, IServiceProvider provider, SelectionFailureProbe trace) : ICommandWorkUnits
    {
        public ValueTask<T> WriteAsync<T>(CommandExecutionContext context, CommandWorkUnitDeclaration declaration, Func<CommandWorkUnit, CancellationToken, ValueTask<T>> action, CancellationToken token = default) where T : notnull =>
            actual.WriteAsync(context, declaration, (unit, ct) => { trace.Transactions.Add(Assert.IsType<DomainTransaction>(provider.GetRequiredService<IDomainTransactionAccessor>().Current)); return action(unit, ct); }, token);
        public ValueTask<CommandWriteResult<T>> WriteResultAsync<T>(CommandExecutionContext c, CommandWorkUnitDeclaration d, Func<CommandWorkUnit, CancellationToken, ValueTask<T>> a, CancellationToken ct = default) where T : notnull => actual.WriteResultAsync(c, d, a, ct);
        public ValueTask<T> ReadOnlyAsync<T>(CommandExecutionContext c, CommandWorkUnitDeclaration d, Func<CancellationToken, ValueTask<T>> a, CancellationToken ct = default) where T : notnull => actual.ReadOnlyAsync(c, d, a, ct);
    }
    public ValueTask DisposeAsync() => Sender.DisposeAsync();
}
