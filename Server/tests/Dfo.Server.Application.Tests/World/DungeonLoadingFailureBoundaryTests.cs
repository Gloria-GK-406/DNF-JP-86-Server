using System.Buffers.Binary;
using Dfo.Server.Abstractions.Execution;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Execution;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.FinishLoading;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;
namespace Dfo.Server.Application.Tests.World;

public sealed partial class BossTowerDomainServiceTests
{
    // Real decoder/dispatcher, real tower transaction and encoders, production outbox,
    // controlled physical transport. Domain setup is separate from the inbound job.
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Task44_boss_last_peer_loading_preserves_two_units_and_publication_boundary(int fault)
    {
        var whole = typeof(FinishLoadingHandler).Assembly.GetType("Dfo.Server.Protocol.Game.FinishLoading.GameFinishLoadingCommand") is not null;
        await using var probe = new EntryFailureProbe(false, fault == 1,
            commandOverride: whole ? "GameFinishLoadingCommand" : "BossLoading");
        var startedBeforeFailure = false;
        using var f = Fixture(configure: services =>
        {
            probe.Configure(services);
            if (fault == 3)
            {
                var registration = services.Last(x => x.ServiceType == typeof(IBossTowerDomainService));
                services.Remove(registration);
                services.AddSingleton<IBossTowerDomainService>(p =>
                {
                    var actual = (IBossTowerDomainService)(registration.ImplementationInstance ?? registration.ImplementationFactory?.Invoke(p)
                        ?? ActivatorUtilities.CreateInstance(p, registration.ImplementationType!));
                    async ValueTask<BossTowerActionResult> FailAfterActualStart(ValueTask<BossTowerActionResult> action)
                    {
                        var changed = await action;
                        Assert.Equal(TowerRoomPhase.Fighting, changed.Run!.Room.Phase);
                        startedBeforeFailure = true;
                        throw probe.Cause;
                    }
                    return Dfo.Server.Application.Tests.Inventory.TownMovementProtocolCommandFlowTests.Proxy<IBossTowerDomainService>.Create((method, args) =>
                        method == nameof(IBossTowerDomainService.StartAsync)
                            ? FailAfterActualStart((ValueTask<BossTowerActionResult>)typeof(IBossTowerDomainService).GetMethod(method)!.Invoke(actual, args)!)
                            : typeof(IBossTowerDomainService).GetMethod(method)!.Invoke(actual, args)!);
                });
            }
        });
        SelectedSessionIdentity peer = null!;
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            f.SetLevel(65); await f.Seed(65, 2749110, 3); peer = await AddMember(f, 3);
            var run = (await Tower(f).EnterAsync([new(f.Session, 0), new(peer, 1)], f.Session, 42, default)).Run!;
            Assert.NotNull(run);
            await Tower(f).LoadAsync(Context(run, f.Session), default);
            var users = f.Services.GetRequiredService<IOnlineUserQueryDomainService>();
            Assert.True(users.TryGetByConnection(peer.ConnectionId, out var user));
            f.Sender.Outbound.Connections.Add(user!.Connection);
            probe.PeerContext = user.Connection;
        });
        var source = probe.Sender.Add(probe.PeerContext!);
        var leader = probe.Sender.Add(f.Connection);
        if (fault == 2) f.Sender.Outbound.Encoder.Fail((_, _) => probe.Cause);
        var metadata = new Dfo.Server.Data.Networking.GameMessageMetadata(1, 0x25, 0, 7) { InboundOrdinal = 7, SequenceIsFresh = true };
        var registration = f.Services.GetServices<GameMessageDecoderRegistration>().Single(x => x.Selector == 1 && x.Type == 0x25);
        var message = ((IGameMessageDecoder)f.Services.GetRequiredService(registration.DecoderType)).Decode(metadata, new byte[8])!;
        var failure = await Record.ExceptionAsync(() => f.Services.GetRequiredService<INetworkMessageDispatcher>().DispatchAsync(new(probe.PeerContext!), message, default).AsTask());
        if (fault is 0 or 2) Assert.Null(failure);
        else if (whole)
        {
            var typed = Assert.IsType<TransactionOutcomeUncertainException>(failure);
            Assert.Same(probe.Cause, typed.InnerException);
        }
        else Assert.Same(probe.Cause, failure);
        await probe.Sender.Settled();
        Assert.Equal(fault == 3, startedBeforeFailure);
        Assert.Equal(!whole && fault == 1 ? 1 : 2, probe.Transactions.Count);
        if (fault == 3)
        {
            Assert.Equal(DomainTransactionCompletion.ConfirmedRollback, probe.Transactions[^1].Completion);
            Assert.Equal(DomainTransactionCompletion.Committed, probe.Transactions[0].Completion);
        }
        else Assert.All(probe.Transactions, tx => Assert.Equal(DomainTransactionCompletion.Committed, tx.Completion));
        if (whole)
        {
            Assert.Equal(new[] { "load-boss-tower", "start-boss-tower" }, probe.Declarations.Select(d => d.Name));
            Assert.All(probe.Declarations, d => { Assert.Equal(CommandWorkUnitKind.Ordinary, d.Kind); Assert.Equal(2, d.Participants.Count); Assert.Equal(CommandFootprintMode.None, d.Footprint); });
            Assert.Null(probe.Declarations[0].Participants.Single(p => p.Session == f.Session).Connection);
            Assert.Same(f.Connection, probe.Declarations[1].Participants.Single(p => p.Session == f.Session).Connection);
        }
        TestGameplay.Run(f.Services, () =>
        {
            var run = Tower(f).Find(peer)!;
            Assert.Equal(fault == 3 || !whole && fault == 1 ? TowerRoomPhase.Ready : TowerRoomPhase.Fighting, run.Room.Phase);
            Assert.Equal(whole && fault is 1 or 3, f.Services.GetRequiredService<ISelectedSessionDomainService>().StandingOf(f.Session)!.Fenced);
            Assert.Equal(whole && fault is 1 or 3, f.Services.GetRequiredService<ISelectedSessionDomainService>().StandingOf(peer)!.Fenced);
        });
        var delivered = fault == 0 || whole && fault == 1;
        Assert.Equal(delivered ? 1 : 0, leader.Sends.Count);
        Assert.Equal(delivered ? 1 : 0, source.Sends.Count);
        if (delivered)
            foreach (var bytes in leader.Sends.Concat(source.Sends))
            {
                Assert.Equal(20, bytes.Length); Assert.Equal(0, bytes[0]);
                Assert.Equal(0x1E, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(1)));
                Assert.Equal(new byte[5], bytes[15..]);
            }
        if (fault is 1 or 3)
        {
            await source.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(1, source.CloseCalls);
            Assert.True(f.Services.GetRequiredService<GameplayCloseCauses>().TryTake(probe.PeerContext!, out var chain));
            Assert.Equal(probe.Chain.RootId, chain.RootId); Assert.Equal(probe.PeerContext!.ConnectionId, chain.SourceConnection);
        }
        if (fault == 2) await source.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(fault != 0 ? 1 : 0, source.CloseCalls);
        if (whole && fault == 1) await leader.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(fault == 2 || whole && fault == 1 ? 1 : 0, leader.CloseCalls);
    }
}

public sealed partial class BossTowerDomainServiceTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    public async Task Task44_ordinary_ready_is_direct_and_move_uses_real_sqlite_atomic_boundary(bool move, int fault)
    {
        var whole = typeof(FinishLoadingHandler).Assembly.GetType("Dfo.Server.Protocol.Game.DungeonEntry.GameMoveMapCommand") is not null;
        await using var probe = new EntryFailureProbe(false, fault == 1, ordinary: true,
            commandOverride: whole ? move ? "GameMoveMapCommand" : "GameFinishLoadingCommand" : move ? "LegacyMove" : "MarkDungeonParticipantReadyCommand");
        using var f = Fixture(configure: services =>
        {
            probe.Configure(services);
            services.AddSingleton<Dfo.Server.Abstractions.Content.IDungeonAdmissionRuleService, OrdinaryEntryStructures>();
            services.AddSingleton<Dfo.Server.Abstractions.World.IDungeonStructureRuleService, OrdinaryEntryStructures>();
            services.AddSingleton<Dfo.Server.Abstractions.World.IDungeonCellFactory>(p => new Task44Cells(p, probe, whole));
        });
        SelectedSessionIdentity peer = null!;
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            f.SetLevel(65); peer = await AddMember(f, 0);
            var parties = f.Services.GetRequiredService<Dfo.Server.Abstractions.Parties.IPartyService>();
            Dfo.Server.Data.Parties.PartyCharacterSnapshot Member(SelectedSessionIdentity x) => new(x.AccountId, x.CharacterId, x.WireUserId, new(1, 0, 0));
            Assert.True(parties.RequestInvite(Member(f.Session), Member(peer), new(0, 0, 0, 0)).Succeeded);
            Assert.True(parties.AcceptInvite(Member(peer), Member(f.Session)).Succeeded);
            var layout = new Dfo.Server.Abstractions.World.DungeonLayout(150, 0, 3, 1, new(0, 0), new(2, 0), 800, 42,
                new Dictionary<Dfo.Server.Abstractions.Content.MazeCell, Dfo.Server.Abstractions.Content.DungeonCellDefinition>
                { [new(0, 0)] = Dfo.Server.Abstractions.Content.DungeonCellDefinition.Fixed(800), [new(1, 0)] = Dfo.Server.Abstractions.Content.DungeonCellDefinition.Fixed(801), [new(2, 0)] = Dfo.Server.Abstractions.Content.DungeonCellDefinition.Fixed(802) });
            var visits = Dfo.Server.Application.Tests.Support.UseCaseDungeons.Visits(f.Services);
            var entered = await visits.TryEnterPreparedPartyAsync([new(f.Session.AccountId, f.Session.CharacterId, 0, true), new(peer.AccountId, peer.CharacterId, 1, false)], layout,
                Dfo.Server.Abstractions.World.DungeonEntryFacts.None, default);
            Assert.NotNull(entered.Entry);
            var runs = f.Services.DungeonRuns();
            var run = runs.Find(f.Session.CharacterId)!;
            f.Services.GetRequiredService<Dfo.Server.Protocol.Game.Dungeons.DungeonRunDeliveryRegistry>().Register(run.RunId, [f.Session, peer]);
            if (move)
            { _ = runs.TryMarkParticipantReady(f.Session.CharacterId, run.RunId, run.RoomVersion); _ = runs.TryMarkParticipantReady(peer.CharacterId, run.RunId, run.RoomVersion); }
            var online = f.Services.GetRequiredService<IOnlineUserQueryDomainService>();
            Assert.True(online.TryGetByConnection(peer.ConnectionId, out var user));
            f.Sender.Outbound.Connections.Add(user!.Connection); probe.PeerContext = user.Connection;
        });
        probe.Transactions.Clear(); probe.Declarations.Clear();
        var source = probe.Sender.Add(f.Connection); var peerPhysical = probe.Sender.Add(probe.PeerContext!);
        if (fault == 2) f.Sender.Outbound.Encoder.Fail((_, _) => probe.Cause);
        var body = move ? new byte[100] : new byte[8]; if (move) body[0] = 1;
        var error = await Record.ExceptionAsync(() => probe.Wire(f, move ? (ushort)0x2D : (ushort)0x25, body).AsTask());
        if (fault == 0) Assert.Null(error);
        else if (whole && move)
        { var typed = Assert.IsType<TransactionOutcomeUncertainException>(error); Assert.Same(probe.Cause, typed.InnerException); }
        else Assert.Same(probe.Cause, error);
        await probe.Sender.Settled();
        if (move)
        {
            Assert.Single(probe.Transactions); Assert.Equal(DomainTransactionCompletion.Committed, probe.Transactions[0].Completion);
            if (whole) { var unit = Assert.Single(probe.Declarations); Assert.Empty(unit.Participants); Assert.Equal(CommandFootprintMode.None, unit.Footprint); }
        }
        else Assert.Empty(probe.Transactions);
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            var runs = f.Services.DungeonRuns();
            var run = runs.Find(f.Session.CharacterId)!;
            Assert.Equal(move ? 1 : 0, run.CurrentCell.X);
            Assert.Contains(f.Session.CharacterId, move ? new[] { f.Session.CharacterId } : run.ReadyParticipants);
            var fatigue = f.Services.GetRequiredService<Dfo.Server.Abstractions.Characters.ICharacterFatigueDomainService>();
            Assert.Equal(move ? 2 : 1, (await fatigue.BalanceOfAsync(f.Session.CharacterId, default)).Used);
            Assert.Equal(move ? 2 : 1, (await fatigue.BalanceOfAsync(peer.CharacterId, default)).Used);
            Assert.False(f.Services.GetRequiredService<ISelectedSessionDomainService>().StandingOf(f.Session)!.Fenced);
            Assert.False(f.Services.GetRequiredService<ISelectedSessionDomainService>().StandingOf(peer)!.Fenced);
        });
        var delivered = fault == 0 || fault == 1 && (whole || move);
        Assert.Equal(delivered ? 2 : 0, source.Sends.Count);
        Assert.Equal(delivered && move ? 2 : 0, peerPhysical.Sends.Count);
        if (delivered)
        {
            var bytes = source.Sends.First();
            Assert.Equal(move ? 0x1D : 0x1E, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(1)));
            Assert.Equal(0, bytes[0]);
        }
        if (fault != 0)
        {
            await source.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10)); Assert.Equal(1, source.CloseCalls);
            Assert.True(f.Services.GetRequiredService<GameplayCloseCauses>().TryTake(f.Connection, out var chain));
            Assert.Equal(probe.Chain.RootId, chain.RootId); Assert.Equal(f.Connection.ConnectionId, chain.SourceConnection);
        }
        Assert.Equal(0, peerPhysical.CloseCalls);
    }
    private sealed class Task44Cells(IServiceProvider provider, EntryFailureProbe probe, bool whole) : Dfo.Server.Abstractions.World.IDungeonCellFactory
    {
        public ValueTask<Dfo.Server.Abstractions.World.DungeonCellSeed> CreateAsync(Dfo.Server.Abstractions.World.DungeonCellCreationContext context, CancellationToken token)
        {
            if (!whole && provider.GetRequiredService<IDomainTransactionAccessor>().Current is DomainTransaction tx) probe.Transactions.Add(tx);
            return ValueTask.FromResult(Dfo.Server.Abstractions.World.DungeonCellSeed.CompleteEmpty(context.Cell, context.MapId));
        }
    }
}

public sealed partial class IllusionTowerDomainServiceTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Task44_illusion_loading_keeps_empty_protection_and_start_belongs_only_to_stage_mode_one(int fault)
    {
        var whole = typeof(FinishLoadingHandler).Assembly.GetType("Dfo.Server.Protocol.Game.FinishLoading.GameFinishLoadingCommand") is not null;
        await using var probe = new EntryFailureProbe(true, fault == 1,
            commandOverride: whole ? "GameFinishLoadingCommand" : "LoadIllusionTowerCommand");
        using var f = Fixture(configure: probe.Configure);
        SelectedSessionIdentity peer = null!;
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            await f.Seed(65, 4183, 1); peer = await AddMember(f, 1);
            var entered = await Tower(f).EnterAsync([new(f.Session, 2), new(peer, 0)], f.Session, 42, default, 11001);
            Assert.NotNull(entered.Run);
            await Tower(f).LoadAsync(Context(entered.Run, peer), default);
            var online = f.Services.GetRequiredService<IOnlineUserQueryDomainService>();
            Assert.True(online.TryGetByConnection(peer.ConnectionId, out var user));
            probe.PeerContext = user!.Connection; f.Sender.Outbound.Connections.Add(user.Connection);
        });
        var source = probe.Sender.Add(f.Connection); var peerPhysical = probe.Sender.Add(probe.PeerContext!);
        if (fault == 2) f.Sender.Outbound.Encoder.Fail((_, _) => probe.Cause);
        var error = await Record.ExceptionAsync(() => probe.Wire(f, 0x25, new byte[8]).AsTask());
        if (fault is 0 or 2) Assert.Null(error);
        else if (whole) { var typed = Assert.IsType<TransactionOutcomeUncertainException>(error); Assert.Same(probe.Cause, typed.InnerException); }
        else Assert.Same(probe.Cause, error);
        Assert.Equal(DomainTransactionCompletion.Committed, Assert.Single(probe.Transactions).Completion);
        if (whole) { var unit = Assert.Single(probe.Declarations); Assert.Empty(unit.Participants); Assert.Equal(CommandFootprintMode.None, unit.Footprint); }
        TestGameplay.Run(f.Services, () =>
        {
            Assert.Equal(TowerRoomPhase.Ready, Tower(f).Find(f.Session)!.Room.Phase);
            Assert.False(f.Services.GetRequiredService<ISelectedSessionDomainService>().StandingOf(f.Session)!.Fenced);
            Assert.False(f.Services.GetRequiredService<ISelectedSessionDomainService>().StandingOf(peer)!.Fenced);
            Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
            Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(probe.PeerContext!));
        });
        await probe.Sender.Settled();
        var delivered = fault == 0 || fault == 1 && whole;
        Assert.Equal(delivered ? 1 : 0, source.Sends.Count); Assert.Equal(delivered ? 1 : 0, peerPhysical.Sends.Count);
        if (delivered)
            foreach (var bytes in source.Sends.Concat(peerPhysical.Sends))
            { Assert.Equal(20, bytes.Length); Assert.Equal(0x1E, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(1))); }
        if (fault != 0)
        { await source.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10)); Assert.Equal(1, source.CloseCalls); }
        Assert.Equal(fault == 2 ? 1 : 0, peerPhysical.CloseCalls);
        if (fault == 0)
        {
            var before = source.Sends.Count;
            var clearReport = new byte[17]; clearReport[0] = 2;
            await probe.Wire(f, 0x9F, clearReport);
            TestGameplay.Run(f.Services, () => Assert.Equal(TowerRoomPhase.Ready, Tower(f).Find(f.Session)!.Room.Phase));
            await probe.Wire(f, 0x9F, [1]);
            TestGameplay.Run(f.Services, () => Assert.Equal(TowerRoomPhase.Fighting, Tower(f).Find(f.Session)!.Room.Phase));
            await probe.Sender.Settled(); Assert.Equal(before, source.Sends.Count);
            if (whole) { Assert.Equal(2, probe.Transactions.Count); Assert.All(probe.Declarations, unit => Assert.Empty(unit.Participants)); }
        }
    }
}

public sealed partial class BossTowerDomainServiceTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Task44_tower_encoder_cleanup_captures_original_physical_and_respects_contexts_current_instance(bool replaceContexts)
    {
        var whole = typeof(FinishLoadingHandler).Assembly.GetType("Dfo.Server.Protocol.Game.FinishLoading.GameFinishLoadingCommand") is not null;
        await using var probe = new EntryFailureProbe(false, false, commandOverride: whole ? "GameFinishLoadingCommand" : "BossLoading");
        using var f = Fixture(configure: probe.Configure);
        SelectedSessionIdentity peer = null!;
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            f.SetLevel(65); await f.Seed(65, 2749110, 3); peer = await AddMember(f, 3);
            var run = (await Tower(f).EnterAsync([new(f.Session, 0), new(peer, 1)], f.Session, 42, default)).Run!;
            await Tower(f).LoadAsync(Context(run, peer), default);
            Assert.True(f.Services.GetRequiredService<IOnlineUserQueryDomainService>().TryGetByConnection(peer.ConnectionId, out var user));
            probe.PeerContext = user!.Connection; f.Sender.Outbound.Connections.Add(user.Connection);
        });
        var source = probe.Sender.Add(f.Connection); var peerPhysical = probe.Sender.Add(probe.PeerContext!);
        var nextContext = f.Connection with { }; var next = probe.Sender.Add(nextContext);
        var nextPeerContext = probe.PeerContext! with { }; var nextPeer = probe.Sender.Add(nextPeerContext);
        f.Sender.Outbound.Encoder.Fail((_, _) =>
        {
            probe.Replacements[f.Connection.ConnectionId] = nextContext;
            probe.Replacements[peer.ConnectionId] = nextPeerContext;
            if (replaceContexts)
            {
                f.Sender.Outbound.Connections.Remove(f.Connection.ConnectionId); f.Sender.Outbound.Connections.Add(nextContext);
                f.Sender.Outbound.Connections.Remove(peer.ConnectionId); f.Sender.Outbound.Connections.Add(nextPeerContext);
            }
            return probe.Cause;
        });
        await probe.Wire(f, 0x25, new byte[8]);
        await probe.Sender.Settled();
        Assert.Empty(source.Sends); Assert.Empty(peerPhysical.Sends); Assert.Empty(next.Sends); Assert.Empty(nextPeer.Sends);
        Assert.Equal(!replaceContexts ? 1 : 0, source.CloseCalls); Assert.Equal(!replaceContexts ? 1 : 0, peerPhysical.CloseCalls);
        Assert.Equal(!whole && replaceContexts ? 1 : 0, next.CloseCalls); Assert.Equal(!whole && replaceContexts ? 1 : 0, nextPeer.CloseCalls);
        Assert.Equal(2, probe.Transactions.Count); Assert.All(probe.Transactions, tx => Assert.Equal(DomainTransactionCompletion.Committed, tx.Completion));
    }
}
