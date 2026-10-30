using System.Buffers.Binary;
using System.Reflection;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Parties;
using Dfo.Server.Data.Parties;
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
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.DungeonSelection;
using Dfo.Server.Protocol.Game.DungeonEntry;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.World;

public sealed partial class BossTowerDomainServiceTests
{
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task Task43_entry_commit_keeps_debits_and_exact_failure_boundary(bool scopeFailure, bool peerContextsReplaced)
    {
        await using var probe = new EntryFailureProbe(illusion: false, scopeFailure);
        using var f = Fixture(configure: services =>
        {
            probe.Configure(services);
            services.AddSingleton<ISelectedCharacterCurrentViewDomainService>(p => new EntryFailureProbe.CompleteView(p));
            services.AddSingleton<ICharacterStateProjectionDomainService>(TownMovementProtocolCommandFlowTests.Proxy<ICharacterStateProjectionDomainService>.Create((_, args) => ValueTask.FromResult((SelectCharacterResult)args![0]!)));
        });
        SelectedSessionIdentity peer = null!;
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            f.SetLevel(65); await f.Seed(65, 2749110, 3); peer = await AddMember(f, 3);
            var parties = f.Services.GetRequiredService<IPartyService>();
            PartyCharacterSnapshot Member(SelectedSessionIdentity x) => new(x.AccountId, x.CharacterId, x.WireUserId, new PartyDirectoryScope(1, 0, 0));
            Assert.True(parties.RequestInvite(Member(f.Session), Member(peer), new(0, 0, 0, 0)).Succeeded);
            Assert.True(parties.AcceptInvite(Member(peer), Member(f.Session)).Succeeded);
            var users = f.Services.GetRequiredService<IOnlineUserQueryDomainService>();
            Assert.True(users.TryGetByConnection(peer.ConnectionId, out var user));
            f.Sender.Outbound.Connections.Add(user!.Connection); probe.PeerContext = user.Connection; probe.Peer = probe.Sender.Add(user.Connection);
            var intents = f.Services.GetRequiredService<IDungeonEntryIntentDomainService>();
            intents.RestoreIfAbsent(f.Session, DungeonEntryIntent.Directed(3505));
            intents.RestoreIfAbsent(peer, DungeonEntryIntent.Directed(3505));
        });
        var physical = probe.Sender.Add(f.Connection);
        var replacement = f.Connection with { }; var next = probe.Sender.Add(replacement);
        void Replace()
        {
            f.Sender.Outbound.Connections.Remove(f.Connection.ConnectionId);
            f.Sender.Outbound.Connections.Add(replacement);
            probe.Replacements[f.Connection.ConnectionId] = replacement;
            if (probe.PeerReplacement is null)
            {
                var peerReplacement = probe.PeerContext! with { };
                probe.PeerReplacement = probe.Sender.Add(peerReplacement);
                if (peerContextsReplaced)
                {
                    f.Sender.Outbound.Connections.Remove(peerReplacement.ConnectionId);
                    f.Sender.Outbound.Connections.Add(peerReplacement);
                }
                probe.PeerContextsReplaced = peerContextsReplaced;
                probe.Replacements[peerReplacement.ConnectionId] = peerReplacement;
            }
        }
        if (scopeFailure) probe.BeforeDispose = Replace;
        else f.Sender.Outbound.Encoder.Fail((_, _) => { Replace(); return probe.Cause; });
        var body = new byte[15]; BinaryPrimitives.WriteInt32LittleEndian(body, 3505);
        var failure = await Record.ExceptionAsync(() => probe.Wire(f, 0x10, body).AsTask());
        await probe.AssertFailure(f, failure, physical, next, replacement, [f.Session, peer], scopeFailure);
        Assert.Equal(2, f.Count(2749110));
        TestGameplay.Run(f.Services, () =>
        {
            Assert.NotNull(Tower(f).Find(f.Session)); Assert.NotNull(Tower(f).Find(peer));
        });
    }
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task Task43_ordinary_entry_commit_keeps_debits_and_exact_failure_boundary(bool scopeFailure, bool peerContextsReplaced)
    {
        await using var probe = new EntryFailureProbe(illusion: false, scopeFailure, ordinary: true);
        using var f = Fixture(configure: services =>
        {
            probe.Configure(services);
            services.AddSingleton<IDungeonStructureRuleService, OrdinaryEntryStructures>();
            services.AddSingleton<Dfo.Server.Abstractions.Content.IDungeonAdmissionRuleService, OrdinaryEntryStructures>();
            services.AddSingleton<IDungeonCellFactory, OrdinaryEntryCells>();
            services.AddSingleton<ISelectedCharacterCurrentViewDomainService>(p => new EntryFailureProbe.CompleteView(p));
            services.AddSingleton<ICharacterStateProjectionDomainService>(TownMovementProtocolCommandFlowTests.Proxy<ICharacterStateProjectionDomainService>.Create((_, args) => ValueTask.FromResult((SelectCharacterResult)args![0]!)));
        });
        SelectedSessionIdentity peer = null!;
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            f.SetLevel(65); await f.Seed(65, 2749110, 3); peer = await AddMember(f, 3);
            var parties = f.Services.GetRequiredService<IPartyService>();
            PartyCharacterSnapshot Member(SelectedSessionIdentity x) => new(x.AccountId, x.CharacterId, x.WireUserId, new PartyDirectoryScope(1, 0, 0));
            Assert.True(parties.RequestInvite(Member(f.Session), Member(peer), new(0, 0, 0, 0)).Succeeded);
            Assert.True(parties.AcceptInvite(Member(peer), Member(f.Session)).Succeeded);
            var users = f.Services.GetRequiredService<IOnlineUserQueryDomainService>();
            Assert.True(users.TryGetByConnection(peer.ConnectionId, out var user));
            f.Sender.Outbound.Connections.Add(user!.Connection); probe.PeerContext = user.Connection; probe.Peer = probe.Sender.Add(user.Connection);
            var intents = f.Services.GetRequiredService<IDungeonEntryIntentDomainService>();
            intents.RestoreIfAbsent(f.Session, DungeonEntryIntent.Directed(150));
            intents.RestoreIfAbsent(peer, DungeonEntryIntent.Directed(150));
        });
        var physical = probe.Sender.Add(f.Connection);
        var replacement = f.Connection with { }; var next = probe.Sender.Add(replacement);
        void Replace()
        {
            f.Sender.Outbound.Connections.Remove(f.Connection.ConnectionId);
            f.Sender.Outbound.Connections.Add(replacement);
            probe.Replacements[f.Connection.ConnectionId] = replacement;
            if (probe.PeerReplacement is null)
            {
                var peerReplacement = probe.PeerContext! with { };
                probe.PeerReplacement = probe.Sender.Add(peerReplacement);
                if (peerContextsReplaced)
                {
                    f.Sender.Outbound.Connections.Remove(peerReplacement.ConnectionId);
                    f.Sender.Outbound.Connections.Add(peerReplacement);
                }
                probe.PeerContextsReplaced = peerContextsReplaced;
                probe.Replacements[peerReplacement.ConnectionId] = peerReplacement;
            }
        }
        if (scopeFailure) probe.BeforeDispose = Replace;
        else f.Sender.Outbound.Encoder.Fail((_, _) => { Replace(); return probe.Cause; });
        var body = new byte[15]; BinaryPrimitives.WriteInt32LittleEndian(body, 150);
        var failure = await Record.ExceptionAsync(() => probe.Wire(f, 0x10, body).AsTask());
        await probe.AssertFailure(f, failure, physical, next, replacement, [f.Session, peer], scopeFailure);
        Assert.Equal(3, f.Count(2749110));
        TestGameplay.Run(f.Services, () =>
        {
            Assert.Equal(scopeFailure, f.Services.DungeonRuns().Find(f.Session.CharacterId) is not null);
            Assert.Equal(1, f.Services.GetRequiredService<ICharacterFatigueDomainService>().BalanceOfAsync(f.Session.CharacterId, default).AsTask().GetAwaiter().GetResult().Used);
        });
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Task43_ordinary_normal_refusal_preserves_the_real_transaction_completion_and_intent(bool lateFailure)
    {
        await using var probe = new EntryFailureProbe(false, false, ordinary: true);
        using var f = Fixture(configure: services => {
            probe.Configure(services);
            var content = new OrdinaryEntryStructures(refuse: !lateFailure, lateFailure: lateFailure);
            services.AddSingleton<IDungeonStructureRuleService>(content);
            services.AddSingleton<IDungeonAdmissionRuleService>(content);
            services.AddSingleton<IDungeonCellFactory, OrdinaryEntryCells>();
            services.AddSingleton<ISelectedCharacterCurrentViewDomainService>(p => new EntryFailureProbe.CompleteView(p));
        });
        await TestGameplay.RunAsync(f.Services, () => {
            f.SetLevel(65);
            f.Services.GetRequiredService<IDungeonEntryIntentDomainService>().RestoreIfAbsent(f.Session,DungeonEntryIntent.Directed(150));
            return Task.CompletedTask;
        });
        var physical=probe.Sender.Add(f.Connection); var body=new byte[15]; BinaryPrimitives.WriteInt32LittleEndian(body,150);
        await probe.Wire(f,0x10,body); await probe.Sender.Settled();
        Assert.Equal(lateFailure ? DomainTransactionCompletion.ConfirmedRollback : DomainTransactionCompletion.Committed,Assert.Single(probe.Transactions).Completion);
        var frames=Assert.Single(physical.Sends); Assert.Equal((byte)1,frames[0]); Assert.Equal((ushort)0x10,BinaryPrimitives.ReadUInt16LittleEndian(frames.AsSpan(1)));
        Assert.Equal(lateFailure ? SelectDungeonRefusal.DungeonUnavailable : SelectDungeonRefusal.LevelTooLow,frames[16]); Assert.Equal(0,physical.CloseCalls);
        TestGameplay.Run(f.Services,()=> {
            Assert.Null(f.Services.DungeonRuns().Find(f.Session.CharacterId));
            Assert.Equal(DungeonEntryIntent.Directed(150),f.Services.GetRequiredService<IDungeonEntryIntentDomainService>().Find(f.Session));
        });
        Assert.Equal(0,(await f.Services.GetRequiredService<ICharacterFatigueDomainService>().BalanceOfAsync(f.Session.CharacterId,default)).Used);
    }

}
public sealed partial class IllusionTowerDomainServiceTests
{
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task Task43_entry_commit_keeps_debits_and_exact_failure_boundary(bool scopeFailure, bool peerContextsReplaced)
    {
        await using var probe = new EntryFailureProbe(illusion: true, scopeFailure);
        using var f = Fixture(configure: services =>
        {
            probe.Configure(services);
            services.AddSingleton<ISelectedCharacterCurrentViewDomainService>(p => new EntryFailureProbe.CompleteView(p));
            services.AddSingleton<ICharacterStateProjectionDomainService>(TownMovementProtocolCommandFlowTests.Proxy<ICharacterStateProjectionDomainService>.Create((_, args) => ValueTask.FromResult((SelectCharacterResult)args![0]!)));
        });
        SelectedSessionIdentity peer = null!;
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            f.SetLevel(50); await f.Seed(65, 4183, 3); peer = await AddMember(f, 3);
            var parties = f.Services.GetRequiredService<IPartyService>();
            PartyCharacterSnapshot Member(SelectedSessionIdentity x) => new(x.AccountId, x.CharacterId, x.WireUserId, new PartyDirectoryScope(1, 0, 0));
            Assert.True(parties.RequestInvite(Member(f.Session), Member(peer), new(0, 0, 0, 0)).Succeeded);
            Assert.True(parties.AcceptInvite(Member(peer), Member(f.Session)).Succeeded);
            var users = f.Services.GetRequiredService<IOnlineUserQueryDomainService>();
            Assert.True(users.TryGetByConnection(peer.ConnectionId, out var user));
            f.Sender.Outbound.Connections.Add(user!.Connection); probe.PeerContext = user.Connection; probe.Peer = probe.Sender.Add(user.Connection);
            var intents = f.Services.GetRequiredService<IDungeonEntryIntentDomainService>();
            intents.RestoreIfAbsent(f.Session, DungeonEntryIntent.Directed(11001));
            intents.RestoreIfAbsent(peer, DungeonEntryIntent.Directed(11001));
        });
        var physical = probe.Sender.Add(f.Connection);
        var replacement = f.Connection with { }; var next = probe.Sender.Add(replacement);
        void Replace()
        {
            f.Sender.Outbound.Connections.Remove(f.Connection.ConnectionId);
            f.Sender.Outbound.Connections.Add(replacement);
            probe.Replacements[f.Connection.ConnectionId] = replacement;
            if (probe.PeerReplacement is null)
            {
                var peerReplacement = probe.PeerContext! with { };
                probe.PeerReplacement = probe.Sender.Add(peerReplacement);
                if (peerContextsReplaced)
                {
                    f.Sender.Outbound.Connections.Remove(peerReplacement.ConnectionId);
                    f.Sender.Outbound.Connections.Add(peerReplacement);
                }
                probe.PeerContextsReplaced = peerContextsReplaced;
                probe.Replacements[peerReplacement.ConnectionId] = peerReplacement;
            }
        }
        if (scopeFailure) probe.BeforeDispose = Replace;
        else f.Sender.Outbound.Encoder.Fail((_, _) => { Replace(); return probe.Cause; });
        var body = new byte[15]; BinaryPrimitives.WriteInt32LittleEndian(body, 11001);
        var failure = await Record.ExceptionAsync(() => probe.Wire(f, 0x10, body).AsTask());
        await probe.AssertFailure(f, failure, physical, next, replacement, [], scopeFailure);
        Assert.Equal(2, f.Count(4183));
        TestGameplay.Run(f.Services, () =>
        {
            Assert.NotNull(Tower(f).Find(f.Session)); Assert.NotNull(Tower(f).Find(peer));
        });
    }
}
internal sealed class EntryFailureProbe(bool illusion, bool scopeFailure, bool ordinary = false, string? commandOverride = null) : IAsyncDisposable
{
    internal static bool Legacy => typeof(EnterSelectDungeonHandler).Assembly.GetType("Dfo.Server.Protocol.Game.DungeonEntry.GameSelectDungeonCommand") is null;
    internal readonly RemainingOpeningProtocolCommandFlowTests.SenderProbe Sender = new();
    internal readonly Exception Cause = new IOException("TASK43 committed entry boundary");
    internal readonly List<DomainTransaction> Transactions = [];
    internal readonly List<CommandExecutionContext> Executions = [];
    internal GameplayCallChain Chain;
    internal readonly List<CommandWorkUnitDeclaration> Declarations = [];
    internal Action? BeforeDispose;
    internal bool PeerContextsReplaced;
    internal NetworkConnectionContext? PeerContext;
    internal RemainingOpeningProtocolCommandFlowTests.PhysicalConnection? Peer, PeerReplacement;
    internal readonly Dictionary<NetworkConnectionId, NetworkConnectionContext> Replacements = [];

    internal void Configure(IServiceCollection services)
    {
        services.AddSingleton<IOnlineUserQueryDomainService>(p => new Query(p.GetRequiredService<IOnlineUserDomainService>(), this));
        services.AddSingleton<IOutboundSender>(Sender.Recorder);
        services.AddSingleton(p => new GameplayOutboxes(p.GetRequiredService<IOutboundSender>(), p.GetRequiredService<GameplayCloseCauses>(), p.GetRequiredService<Microsoft.Extensions.Logging.ILogger<GameplayOutbox>>()));
        services.AddSingleton<ISelectedCharacterCurrentViewDomainService, Task39NoReturnView>();
        var command = commandOverride ?? (Legacy ? ordinary ? "EnterDungeonCommand" : illusion ? "EnterIllusionTowerCommand" : "EnterBossTowerCommand" : "GameSelectDungeonCommand");
        if (command == "LegacyMove")
            services.AddScoped<Microsoft.Extensions.Logging.ILogger<Dfo.Server.Protocol.Game.DungeonEntry.MoveMapHandler>>(_ => new MovementScopeLogger(this, scopeFailure));
        else
        {
        var descriptors = services.Where(value => value.ServiceType.IsGenericType && value.ServiceType.GetGenericTypeDefinition() == typeof(ICommandHandler<,>)
            && (command == "BossLoading" ? value.ServiceType.GenericTypeArguments[0].Name is "LoadBossTowerCommand" or "StartBossTowerCommand" : command.Split('|').Contains(value.ServiceType.GenericTypeArguments[0].Name))).ToArray();
        Assert.NotEmpty(descriptors);
        foreach (var descriptor in descriptors)
        {
            services.Remove(descriptor);
            var disposeFailure = scopeFailure && (command != "BossLoading" || descriptor.ServiceType.GenericTypeArguments[0].Name == "LoadBossTowerCommand");
            services.AddScoped(descriptor.ServiceType, p => Activator.CreateInstance(typeof(Scope<,>).MakeGenericType(descriptor.ServiceType.GenericTypeArguments), ActivatorUtilities.CreateInstance(p, descriptor.ImplementationType!), p, this, disposeFailure)!);
        }
        }
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
        Assert.Equal(1, physical.CloseCalls); Assert.Equal(Legacy && !scope && !ordinary ? 1 : 0, next.CloseCalls);
        if (!Legacy || scope) Assert.Empty(next.Events);
        Assert.True(f.Services.GetRequiredService<GameplayCloseCauses>().TryTake(f.Connection, out var chain));
        Assert.Equal(Chain.Id, chain.Id); Assert.Equal(Chain.RootId, chain.RootId); Assert.Equal(f.Connection.ConnectionId, chain.SourceConnection);
        foreach (var member in protectedMembers)
            Assert.Equal(!Legacy, f.Services.GetRequiredService<ISelectedSessionDomainService>().StandingOf(member)!.Fenced);
        Assert.Equal(!Legacy && protectedMembers.Count > 0, f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
        Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(replacement));
        Assert.NotNull(Peer); Assert.NotNull(PeerReplacement);
        Assert.Empty(PeerReplacement.Sends);
        Assert.Equal(Legacy && !scope && PeerContextsReplaced ? 1 : 0, PeerReplacement.CloseCalls);
        Assert.Equal(!scope && !PeerContextsReplaced ? 1 : 0, Peer.CloseCalls);
        if (scope && !Legacy)
        {
            var bytes = Assert.Single(Peer.Sends);
            var types = FrameTypes(bytes);
            var ack = Array.IndexOf(types, (ushort)0xF);
            Assert.True(ack >= 0); Assert.Equal((ushort)3, types[ack + 1]); Assert.Equal((ushort)0x1A, types[ack + 2]);
            Assert.Equal(ordinary ? new ushort[] { 0x1C, 0x1D } : illusion ? new ushort[] { 0x8E, 0x8F } : new ushort[] { 0x333, 0x334 }, types.Where(t => t is 0x1C or 0x1D or 0x8E or 0x8F or 0x333 or 0x334));
            Assert.Equal(types, FrameTypes(Assert.Single(physical.Sends)));
        }
        else { Assert.Empty(Peer.Sends); Assert.Empty(physical.Sends); }
        if (illusion)
        {
            Assert.False(f.Services.GetRequiredService<ISelectedSessionDomainService>().StandingOf(f.Session)!.Fenced);
            var peerSession = f.Services.GetRequiredService<ISelectedSessionDomainService>().FindByConnection(PeerContext!.ConnectionId);
            Assert.NotNull(peerSession);
            Assert.False(f.Services.GetRequiredService<ISelectedSessionDomainService>().StandingOf(peerSession)!.Fenced);
        }
    }

    private static ushort[] FrameTypes(byte[] bytes)
    {
        List<ushort> types = []; var offset = 0;
        while (offset < bytes.Length) { types.Add(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + 1))); offset += checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 3))); }
        Assert.Equal(bytes.Length, offset); return types.ToArray();
    }
    private sealed class Query(IOnlineUserQueryDomainService actual, EntryFailureProbe probe) : IOnlineUserQueryDomainService
    {
        public OnlineUserSnapshot GetRequired(OnlineUserId id) => actual.GetRequired(id);
        public bool TryGetByConnection(NetworkConnectionId id, out OnlineUserSnapshot? user)
        {
            var found = actual.TryGetByConnection(id, out user);
            if (user is not null && probe.Replacements.TryGetValue(id, out var connection)) user = user with { Connection = connection };
            return found;
        }
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

    private sealed class MovementScopeLogger(EntryFailureProbe trace, bool fail) : Microsoft.Extensions.Logging.ILogger<Dfo.Server.Protocol.Game.DungeonEntry.MoveMapHandler>, IDisposable
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel level) => true;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel level, Microsoft.Extensions.Logging.EventId id, TState state, Exception? e, Func<TState, Exception?, string> formatter) => trace.Chain = GameplayJob.Current!.Value;
        public void Dispose() { if (fail) throw trace.Cause; }
    }
    private sealed class Scope<TCommand, TResult>(object actual, IServiceProvider provider, EntryFailureProbe trace, bool fail) : ICommandHandler<TCommand, TResult>, IDisposable
        where TCommand : class, ICommand<TResult> where TResult : notnull
    {
        public ValueTask<TResult> HandleAsync(CommandExecutionContext context, TCommand command, CancellationToken token)
        {
            trace.Chain = GameplayJob.Current!.Value;
            trace.Executions.Add(context);
            if (provider.GetRequiredService<IDomainTransactionAccessor>().Current is DomainTransaction transaction) trace.Transactions.Add(transaction);
            return ((ICommandHandler<TCommand, TResult>)actual).HandleAsync(context, command, token);
        }
        public void Dispose() { if (fail) { trace.BeforeDispose?.Invoke(); throw trace.Cause; } }
    }
    private sealed class Units(ICommandWorkUnits actual, IServiceProvider provider, EntryFailureProbe trace) : ICommandWorkUnits
    {
        public ValueTask<T> WriteAsync<T>(CommandExecutionContext context, CommandWorkUnitDeclaration declaration, Func<CommandWorkUnit, CancellationToken, ValueTask<T>> action, CancellationToken token = default) where T : notnull =>
            actual.WriteAsync(context, declaration, (unit, ct) => { trace.Transactions.Add(Assert.IsType<DomainTransaction>(provider.GetRequiredService<IDomainTransactionAccessor>().Current)); trace.Declarations.Add(declaration); return action(unit, ct); }, token);
        public ValueTask<CommandWriteResult<T>> WriteResultAsync<T>(CommandExecutionContext c, CommandWorkUnitDeclaration d, Func<CommandWorkUnit, CancellationToken, ValueTask<T>> a, CancellationToken ct = default) where T : notnull => actual.WriteResultAsync(c, d, a, ct);
        public ValueTask<T> ReadOnlyAsync<T>(CommandExecutionContext c, CommandWorkUnitDeclaration d, Func<CancellationToken, ValueTask<T>> a, CancellationToken ct = default) where T : notnull => actual.ReadOnlyAsync(c, d, a, ct);
    }
    public ValueTask DisposeAsync() => Sender.DisposeAsync();
}

internal sealed class OrdinaryEntryStructures(bool refuse = false, bool lateFailure = false) : IDungeonStructureRuleService, Dfo.Server.Abstractions.Content.IDungeonAdmissionRuleService
{
    private static readonly DungeonLayout Layout = new(150, 0, 1, 1, new(0, 0), new(0, 0), 800, 42,
        new Dictionary<MazeCell, DungeonCellDefinition> { [new(0, 0)] = DungeonCellDefinition.Fixed(800) });
    public ValueTask<Dfo.Server.Abstractions.Content.DungeonDefinition?> GetDefinitionAsync(int id, CancellationToken token) => ValueTask.FromResult<Dfo.Server.Abstractions.Content.DungeonDefinition?>(new(150, "test", refuse ? (byte)80 : (byte)1, true, [], 86));
    public async ValueTask<DungeonAdmissionDefinition?> GetAsync(int id, CancellationToken token) => DungeonAdmissionDefinition.From((await GetDefinitionAsync(id, token))!);
    public ValueTask<DungeonLayout?> ResolveAsync(int id, DungeonEntryFacts facts, int seed, CancellationToken token) => lateFailure ? throw new InvalidDataException("TASK43 layout unavailable after admission") : ValueTask.FromResult<DungeonLayout?>(Layout);
    public ValueTask<DungeonLayout> PrepareLayoutAsync(DungeonLayout layout, CancellationToken token) => ValueTask.FromResult(layout);
    public ValueTask<DungeonMapSelection> SelectMapAsync(DungeonLayout layout, MazeCell? from, MazeCell target, CancellationToken token) => ValueTask.FromResult(DungeonMapSelection.Selected(800));
}
internal sealed class OrdinaryEntryCells : IDungeonCellFactory
{
    public ValueTask<DungeonCellSeed> CreateAsync(DungeonCellCreationContext context, CancellationToken token) => ValueTask.FromResult(DungeonCellSeed.CompleteEmpty(context.Cell, context.MapId));
}
