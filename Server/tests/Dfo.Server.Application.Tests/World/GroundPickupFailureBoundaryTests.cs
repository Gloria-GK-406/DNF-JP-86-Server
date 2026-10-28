using System.Buffers.Binary;
using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Execution;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.GroundPickup;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;
namespace Dfo.Server.Application.Tests.World;

internal static class GroundPickupOracle
{
    internal static bool Whole => typeof(GetItemHandler).Assembly.GetType("Dfo.Server.Protocol.Game.GroundPickup.GameGetItemCommand") is not null;
    internal static async Task Check(ShopFixture f, EntryFailureProbe probe, SelectedSessionIdentity? peer,
        ushort ground, int fault, bool boss, Action assertCommitted)
    {
        var source = probe.Sender.Add(f.Connection);
        RemainingOpeningProtocolCommandFlowTests.PhysicalConnection? physicalPeer = null;
        if (peer is not null)
        {
            Assert.True(f.Services.GetRequiredService<IOnlineUserQueryDomainService>().TryGetByConnection(peer.ConnectionId, out var user));
            probe.PeerContext = user!.Connection; f.Sender.Outbound.Connections.Add(user.Connection);
            physicalPeer = probe.Sender.Add(user.Connection);
        }
        RemainingOpeningProtocolCommandFlowTests.PhysicalConnection? nextSource = null, nextPeer = null;
        if (fault >= 2 && fault != 5) f.Sender.Outbound.Encoder.Fail((_, _) =>
        {
            if (fault >= 3 && nextSource is null)
            {
                var replacement = f.Connection with { }; nextSource = probe.Sender.Add(replacement); probe.Replacements[f.Connection.ConnectionId] = replacement;
                if (fault == 4) { f.Sender.Outbound.Connections.Remove(f.Connection.ConnectionId); f.Sender.Outbound.Connections.Add(replacement); }
                if (probe.PeerContext is { } original)
                {
                    var next = original with { }; nextPeer = probe.Sender.Add(next); probe.Replacements[original.ConnectionId] = next;
                    if (fault == 4) { f.Sender.Outbound.Connections.Remove(original.ConnectionId); f.Sender.Outbound.Connections.Add(next); }
                }
            }
            return probe.Cause;
        });
        if (fault == 5) physicalPeer!.SendFailure = probe.Cause;
        var body = new byte[2]; BinaryPrimitives.WriteUInt16LittleEndian(body, ground);
        var error = await Record.ExceptionAsync(() => probe.Wire(f, 0x2B, body).AsTask());
        if (fault == 1)
        {
            if (Whole) { var typed = Assert.IsType<TransactionOutcomeUncertainException>(error); Assert.Same(probe.Cause, typed.InnerException); Assert.Equal(TransactionUncertaintyReason.PostCommitPublicationFailure, typed.Reason); }
            else Assert.Same(probe.Cause, error);
        }
        else Assert.Null(error);
        Assert.Equal(DomainTransactionCompletion.Committed, Assert.Single(probe.Transactions).Completion);
        if (Whole)
        {
            var declaration = Assert.Single(probe.Declarations);
            Assert.Equal(CommandWorkUnitKind.Ordinary, declaration.Kind); Assert.Equal(CommandFootprintMode.None, declaration.Footprint);
            Assert.Null(declaration.FootprintOwners); Assert.Equal(boss ? peer is null ? 1 : 2 : 0, declaration.Participants.Count);
            if (boss)
            {
                Assert.Same(f.Connection, declaration.Participants.Single(x => x.Session == f.Session).Connection);
                Assert.All(declaration.Participants, x => Assert.Equal((ushort)7, x.Metadata!.Sequence));
                if (peer is not null) Assert.Null(declaration.Participants.Single(x => x.Session == peer).Connection);
            }
        }
        TestGameplay.Run(f.Services, () =>
        {
            assertCommitted();
            foreach (var member in peer is null ? new[] { f.Session } : new[] { f.Session, peer })
                Assert.Equal(Whole && boss && fault == 1, f.Services.GetRequiredService<ISelectedSessionDomainService>().StandingOf(member)!.Fenced);
        });
        Assert.Equal(Whole && boss && fault == 1, f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
        await probe.Sender.Settled();
        if (fault is 1 or 2 or 3) await source.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        if (fault == 5) await physicalPeer!.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(fault is 1 or 2 or 3 ? 1 : 0, source.CloseCalls);
        if (fault == 1)
        {
            Assert.True(f.Services.GetRequiredService<GameplayCloseCauses>().TryTake(f.Connection,out var chain));
            Assert.Equal(probe.Chain.RootId,chain.RootId);Assert.Equal(f.Connection.ConnectionId,chain.SourceConnection);
        }
        if (physicalPeer is not null) Assert.Equal(fault is 2 or 3 or 5 ? 1 : 0, physicalPeer.CloseCalls);
        if (fault >= 3 && fault != 5)
        {
            Assert.Empty(nextSource!.Sends); Assert.Equal(!Whole && fault == 4 ? 1 : 0, nextSource.CloseCalls);
            if (nextPeer is not null) { Assert.Empty(nextPeer.Sends); Assert.Equal(!Whole && fault == 4 ? 1 : 0, nextPeer.CloseCalls); }
        }
        var delivered = fault is 0 or 5 || Whole && fault == 1;
        Assert.Equal(delivered ? 1 : 0, source.Sends.Count);
        if (physicalPeer is not null) Assert.Equal(delivered && fault != 5 ? 1 : 0, physicalPeer.Sends.Count);
        if (delivered)
        {
            var frames = MonsterKillOracle.Types(Assert.Single(source.Sends));
            Assert.Equal((ushort)0x2B, frames[0]); Assert.Equal((ushort)0x27, frames[1]);
            Assert.Equal((byte)1, source.Sends.Single()[15]);
            if (physicalPeer is not null && fault != 5) Assert.Equal(new ushort[] { 0x27 }, MonsterKillOracle.Types(Assert.Single(physicalPeer.Sends)));
        }
        if (fault is 0 or 1)
        {
            var gold = f.Gold; var count = f.Scalar("SELECT COUNT(*) FROM items");
            var retry = await Record.ExceptionAsync(() => probe.Wire(f, 0x2B, body).AsTask());
            if (fault == 0 || Whole && boss) Assert.Null(retry); else Assert.NotNull(retry);
            if (Whole && boss && fault == 1) Assert.Single(probe.Transactions);
            Assert.Equal(gold, f.Gold); Assert.Equal(count, f.Scalar("SELECT COUNT(*) FROM items"));
        }
    }
}
public sealed partial class BossTowerDomainServiceTests
{
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public async Task Task46_boss_pickup_real_wire_commit_scope_and_encoder_boundary(int fault)
    {
        await using var probe = new EntryFailureProbe(false, fault == 1, commandOverride: GroundPickupOracle.Whole ? "GameGetItemCommand" : "PickupBossTowerItemCommand");
        using var f = Fixture(drops: new Drops([new(3000, 1, 0, DropItemKind.Equipment)]), configure: probe.Configure);
        SelectedSessionIdentity peer = null!; ushort ground = 0;
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            f.SetLevel(65); await f.Seed(65, 2749110, 3); peer = await AddMember(f, 3);
            var tower = Tower(f); var run = (await tower.EnterAsync([new(f.Session, 0), new(peer, 1)], f.Session, 42, default)).Run!;
            await tower.LoadAsync(Context(run, f.Session), default); await tower.LoadAsync(Context(run, peer), default); await tower.StartAsync(Context(run, f.Session), default);
            var result = await TestEffectFootprints.Run(new[] { f.Session, peer }, cap => tower.KillAsync(Context(run, f.Session), run.Room.Actors[0].Uid, cap, default));
            ground = Assert.Single(result.Drops).Uid;
        });
        await GroundPickupOracle.Check(f, probe, peer, ground, fault, true, () => Assert.True(Tower(f).Find(f.Session)!.GroundItems.Single(x => x.Uid == ground).Claimed));
        Assert.Equal(1, f.Count(3000));
    }
}
public sealed partial class IllusionTowerDomainServiceTests
{
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public async Task Task46_illusion_pickup_real_wire_commit_scope_and_encoder_boundary(int fault)
    {
        await using var probe = new EntryFailureProbe(true, fault == 1, commandOverride: GroundPickupOracle.Whole ? "GameGetItemCommand" : "PickupTowerItemCommand");
        using var f = Fixture(new Rules { Items = [6521] }, configure: probe.Configure); ushort ground = 0;
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            await f.Seed(65, 4183, 1); var tower = Tower(f); var run = (await tower.EnterAsync([new(f.Session, 0)], f.Session, 42, default, 11000)).Run!;
            await tower.LoadAsync(Context(run, f.Session), default); await tower.StartAsync(Context(run, f.Session), default);
            var result = await TestEffectFootprints.Run(new[] { f.Session }, cap => tower.KillAsync(Context(run, f.Session), run.Room.Actors[0].Uid, cap, default));
            ground = Assert.Single(result.Drops).Uid;
        });
        await GroundPickupOracle.Check(f, probe, null, ground, fault, false, () => Assert.True(Tower(f).Find(f.Session)!.Room.Items.Single(x => x.Uid == ground).Claimed));
    }
}

public sealed partial class BossTowerDomainServiceTests
{
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task Task46_ordinary_pickup_real_wire_commit_scope_and_encoder_boundary(int fault)
    {
        await using var probe = new EntryFailureProbe(false, fault == 1, ordinary: true, commandOverride: GroundPickupOracle.Whole ? "GameGetItemCommand" : "PickUpGroundDropCommand");
        using var f = Fixture(configure: probe.Configure); ushort ground = 0;
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            var runs = f.Services.GetRequiredService<IDungeonRunDomainService>();
            var cell = new Dfo.Server.Abstractions.Content.MazeCell(0,0);
            var layout = new DungeonLayout(144,0,1,1,cell,cell,1,42,new Dictionary<Dfo.Server.Abstractions.Content.MazeCell,Dfo.Server.Abstractions.Content.DungeonCellDefinition> { [cell] = Dfo.Server.Abstractions.Content.DungeonCellDefinition.Fixed(1) });
            await using var setup = Scope(f).Begin();
            var run = runs.PreparePartyEntry([new(f.Session.AccountId,f.Session.CharacterId,0,true)], layout,0,
                new(cell,1,1,[],[new(7,3000,1,33,true,DungeonGroundItemSource.FromActor(1))]),DungeonRunId.New(),f.Clock.GetUtcNow()).Run;
            f.Services.GetRequiredService<Dfo.Server.Protocol.Game.Dungeons.DungeonRunDeliveryRegistry>().Register(run.RunId,[f.Session]);
            ground = Assert.Single(run.CurrentCellState.GroundItems).Number;
            await setup.CommitAsync(default);
        });
        // Ordinary projection failure escapes the old handler; unlike the two towers it is not collected.
        if (fault == 2)
        {
            var source = probe.Sender.Add(f.Connection); f.Sender.Outbound.Encoder.Fail((_,_)=>probe.Cause);
            var body = new byte[2]; BinaryPrimitives.WriteUInt16LittleEndian(body,ground);
            var error = await Record.ExceptionAsync(()=>probe.Wire(f,0x2B,body).AsTask());
            if (GroundPickupOracle.Whole) {var typed=Assert.IsType<TransactionOutcomeUncertainException>(error);Assert.Same(probe.Cause,typed.InnerException);Assert.Equal(TransactionUncertaintyReason.PostCommitPublicationFailure,typed.Reason);} else Assert.Same(probe.Cause,error);
            Assert.Equal(DomainTransactionCompletion.Committed,Assert.Single(probe.Transactions).Completion);
            TestGameplay.Run(f.Services,()=>{Assert.Null(f.Services.GetRequiredService<IDungeonRunDomainService>().Find(f.Session.CharacterId)!.CurrentCellState.FindGroundItem(ground));Assert.False(f.Services.GetRequiredService<ISelectedSessionDomainService>().StandingOf(f.Session)!.Fenced);});
            await source.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10)); await probe.Sender.Settled(); Assert.Equal(1,source.CloseCalls);Assert.Empty(source.Sends);
            Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
            Assert.True(f.Services.GetRequiredService<GameplayCloseCauses>().TryTake(f.Connection,out var chain));
            Assert.Equal(probe.Chain.RootId,chain.RootId);Assert.Equal(f.Connection.ConnectionId,chain.SourceConnection);
        }
        else await GroundPickupOracle.Check(f,probe,null,ground,fault,false,()=>Assert.Null(f.Services.GetRequiredService<IDungeonRunDomainService>().Find(f.Session.CharacterId)!.CurrentCellState.FindGroundItem(ground)));
        Assert.Equal(1,f.Count(3000));
    }
}

public sealed partial class BossTowerDomainServiceTests
{
    [Fact]
    public async Task Task46_boss_full_bag_actual_pickup_mails_once_instead_of_refusing()
    {
        await using var probe=new EntryFailureProbe(false,false,commandOverride:GroundPickupOracle.Whole?"GameGetItemCommand":"PickupBossTowerItemCommand");
        using var f=Fixture(drops:new Drops([new(3000,1,0,DropItemKind.Equipment)]),configure:probe.Configure);ushort ground=0;
        await TestGameplay.RunAsync(f.Services,async()=>
        {
            f.SetLevel(65);await f.Seed(65,2749110,3);await f.Seed(9,3000,1,1);await f.Seed(10,3000,1,1);
            var tower=Tower(f);var run=(await Enter(f)).Run!;var context=Context(run,f.Session);
            await tower.LoadAsync(context,default);await tower.StartAsync(context,default);
            ground=Assert.Single((await TestEffectFootprints.Run(new[]{f.Session},cap=>tower.KillAsync(context,run.Room.Actors[0].Uid,cap,default))).Drops).Uid;
        });
        var physical=probe.Sender.Add(f.Connection);var body=new byte[2];BinaryPrimitives.WriteUInt16LittleEndian(body,ground);
        await probe.Wire(f,0x2B,body);await probe.Sender.Settled();
        Assert.Equal(2,f.Count(3000));Assert.Equal(1,f.Scalar("SELECT COUNT(*) FROM mailbox_attachments WHERE claimed_flag=0"));
        var bytes=Assert.Single(physical.Sends);Assert.Equal((byte)1,bytes[15]);Assert.Equal((ushort)0x2B,MonsterKillOracle.Types(bytes)[0]);
        await probe.Wire(f,0x2B,body);await probe.Sender.Settled();
        Assert.Single(physical.Sends);Assert.Equal(1,f.Scalar("SELECT COUNT(*) FROM mailbox_attachments WHERE claimed_flag=0"));
        Assert.All(probe.Transactions,t=>Assert.Equal(DomainTransactionCompletion.Committed,t.Completion));
    }
}
public sealed partial class IllusionTowerDomainServiceTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Task46_illusion_full_temporary_inventory_refuses_and_ack_failure_escapes(bool encoderFailure)
    {
        await using var probe=new EntryFailureProbe(true,false,commandOverride:GroundPickupOracle.Whole?"GameGetItemCommand":"PickupTowerItemCommand");
        using var f=Fixture(new Rules{Items=[6515,6518,6519,6520,6521,6523,6524]},configure:probe.Configure);ushort ground=0;
        await TestGameplay.RunAsync(f.Services,async()=>
        {
            await f.Seed(65,4183,1);var tower=Tower(f);var run=(await tower.EnterAsync([new(f.Session,0)],f.Session,42,default,11000)).Run!;var context=Context(run,f.Session);
            await tower.LoadAsync(context,default);await tower.StartAsync(context,default);
            var drops=(await TestEffectFootprints.Run(new[]{f.Session},cap=>tower.KillAsync(context,run.Room.Actors[0].Uid,cap,default))).Drops;
            foreach(var drop in drops.Take(6))Assert.True((await tower.PickupAsync(context,drop.Uid,default)).Action.Changed);
            ground=drops[6].Uid;
        });
        var physical=probe.Sender.Add(f.Connection);if(encoderFailure)f.Sender.Outbound.Encoder.Fail((_,_)=>probe.Cause);
        var body=new byte[2];BinaryPrimitives.WriteUInt16LittleEndian(body,ground);
        var error=await Record.ExceptionAsync(()=>probe.Wire(f,0x2B,body).AsTask());
        if(encoderFailure)
        {
            if(GroundPickupOracle.Whole){var typed=Assert.IsType<TransactionOutcomeUncertainException>(error);Assert.Same(probe.Cause,typed.InnerException);Assert.Equal(TransactionUncertaintyReason.PostCommitPublicationFailure,typed.Reason);}else Assert.Same(probe.Cause,error);
            await physical.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(f.Services.GetRequiredService<GameplayCloseCauses>().TryTake(f.Connection,out var chain));Assert.Equal(probe.Chain.RootId,chain.RootId);Assert.Equal(f.Connection.ConnectionId,chain.SourceConnection);
        }
        else Assert.Null(error);
        Assert.Equal(DomainTransactionCompletion.Committed,Assert.Single(probe.Transactions).Completion);
        TestGameplay.Run(f.Services,()=>{Assert.False(Tower(f).Find(f.Session)!.Room.Items.Single(x=>x.Uid==ground).Claimed);Assert.False(f.Services.GetRequiredService<ISelectedSessionDomainService>().StandingOf(f.Session)!.Fenced);});
        Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));await probe.Sender.Settled();
        if(encoderFailure)Assert.Empty(physical.Sends);else{var bytes=Assert.Single(physical.Sends);Assert.Equal(new ushort[]{0x2B},MonsterKillOracle.Types(bytes));Assert.Equal(new byte[]{0,4},bytes[15..]);}
    }
}

public sealed partial class BossTowerDomainServiceTests
{
    [Fact]
    public async Task Task46_ordinary_prepare_failure_rolls_back_real_sqlite_and_claim_then_retry_pays_once()
    {
        await using var probe=new EntryFailureProbe(false,false,ordinary:true,commandOverride:GroundPickupOracle.Whole?"GameGetItemCommand":"PickUpGroundDropCommand");
        var fault=new PickupPrepareFault();
        using var f=Fixture(configure:services=>
        {
            probe.Configure(services);
            var descriptor=services.Last(x=>x.ServiceType==typeof(Dfo.Server.Abstractions.Inventory.IItemGrantDomainService));services.Remove(descriptor);
            services.AddSingleton<Dfo.Server.Abstractions.Inventory.IItemGrantDomainService>(p=>new PrepareFailingGrant(
                (Dfo.Server.Abstractions.Inventory.IItemGrantDomainService)(descriptor.ImplementationInstance??descriptor.ImplementationFactory?.Invoke(p)??ActivatorUtilities.CreateInstance(p,descriptor.ImplementationType!)),
                p.GetRequiredService<IDomainTransactionAccessor>(),fault));
        });
        ushort ground=0;
        await TestGameplay.RunAsync(f.Services,async()=>
        {
            var runs=f.Services.GetRequiredService<IDungeonRunDomainService>();var cell=new Dfo.Server.Abstractions.Content.MazeCell(0,0);
            var layout=new DungeonLayout(144,0,1,1,cell,cell,1,42,new Dictionary<Dfo.Server.Abstractions.Content.MazeCell,Dfo.Server.Abstractions.Content.DungeonCellDefinition>{[cell]=Dfo.Server.Abstractions.Content.DungeonCellDefinition.Fixed(1)});
            await using var setup=Scope(f).Begin();
            var run=runs.PreparePartyEntry([new(f.Session.AccountId,f.Session.CharacterId,0,true)],layout,0,
                new(cell,1,1,[],[new(7,3000,1,33,true,DungeonGroundItemSource.FromActor(1))]),DungeonRunId.New(),f.Clock.GetUtcNow()).Run;
            f.Services.GetRequiredService<Dfo.Server.Protocol.Game.Dungeons.DungeonRunDeliveryRegistry>().Register(run.RunId,[f.Session]);
            ground=Assert.Single(run.CurrentCellState.GroundItems).Number;await setup.CommitAsync(default);
        });
        var source=probe.Sender.Add(f.Connection);var body=new byte[2];BinaryPrimitives.WriteUInt16LittleEndian(body,ground);
        var error=await Record.ExceptionAsync(()=>probe.Wire(f,0x2B,body).AsTask());Assert.Same(fault.Cause,error);
        Assert.True(fault.Prepared);Assert.Equal(DomainTransactionCompletion.ConfirmedRollback,Assert.Single(probe.Transactions).Completion);
        Assert.Equal(0,f.Count(3000));
        TestGameplay.Run(f.Services,()=>{Assert.NotNull(f.Services.GetRequiredService<IDungeonRunDomainService>().Find(f.Session.CharacterId)!.CurrentCellState.FindGroundItem(ground));Assert.False(f.Services.GetRequiredService<ISelectedSessionDomainService>().StandingOf(f.Session)!.Fenced);});
        Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));await source.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10));await probe.Sender.Settled();Assert.Empty(source.Sends);
        Assert.True(f.Services.GetRequiredService<GameplayCloseCauses>().TryTake(f.Connection,out var chain));Assert.Equal(probe.Chain.RootId,chain.RootId);Assert.Equal(f.Connection.ConnectionId,chain.SourceConnection);
        // The first failure closed only its original physical connection. Retry through a new admitted physical.
        var replacement=f.Connection with{};probe.Replacements[f.Connection.ConnectionId]=replacement;
        f.Sender.Outbound.Connections.Remove(f.Connection.ConnectionId);f.Sender.Outbound.Connections.Add(replacement);
        var retryPhysical=probe.Sender.Add(replacement);
        async ValueTask Retry()
        {
            var metadata=new GameMessageMetadata(1,0x2B,0,8){InboundOrdinal=8,SequenceIsFresh=true};
            var registration=f.Services.GetServices<GameMessageDecoderRegistration>().Single(x=>x.Selector==1&&x.Type==0x2B);
            var decoded=((IGameMessageDecoder)f.Services.GetRequiredService(registration.DecoderType)).Decode(metadata,body)!;
            await f.Services.GetRequiredService<INetworkMessageDispatcher>().DispatchAsync(new(replacement),decoded,default);
        }
        await Retry();await probe.Sender.Settled();Assert.Equal(1,f.Count(3000));
        Assert.Equal(DomainTransactionCompletion.Committed,probe.Transactions[1].Completion);
        TestGameplay.Run(f.Services,()=>Assert.Null(f.Services.GetRequiredService<IDungeonRunDomainService>().Find(f.Session.CharacterId)!.CurrentCellState.FindGroundItem(ground)));
        var bytes=Assert.Single(retryPhysical.Sends);Assert.Equal((byte)1,bytes[15]);Assert.Equal(new ushort[]{0x2B,0x27},MonsterKillOracle.Types(bytes));
        await Retry();await probe.Sender.Settled();Assert.Single(retryPhysical.Sends);Assert.Equal(1,f.Count(3000));Assert.Equal(0,retryPhysical.CloseCalls);
    }
    private sealed class PickupPrepareFault:ITransactionParticipant
    {
        internal readonly Exception Cause=new IOException("TASK46 prepare failure after real SQLite item grant");internal bool Prepared;internal bool Armed=true;
        public bool CommitCanFail=>false;
        public ValueTask PrepareAsync(CancellationToken ct){Prepared=true;throw Cause;}
        public ValueTask CommitAsync(CancellationToken ct)=>throw new InvalidOperationException("Prepare failure must prevent commit.");
        public ValueTask RollbackAsync(CancellationToken ct)=>ValueTask.CompletedTask;
    }
    private sealed class PrepareFailingGrant(Dfo.Server.Abstractions.Inventory.IItemGrantDomainService actual,IDomainTransactionAccessor transactions,PickupPrepareFault fault):Dfo.Server.Abstractions.Inventory.IItemGrantDomainService
    {
        public async ValueTask<Dfo.Server.Abstractions.Inventory.BusinessItemAdditionResult> GrantAsync(Dfo.Server.Data.Characters.CharacterId character,
            Dfo.Server.Abstractions.Inventory.ICharacterContainerDomainService containers,int item,int count,CancellationToken ct,bool isDefault=false)
        {
            var result=await actual.GrantAsync(character,containers,item,count,ct,isDefault);
            if(fault.Armed){fault.Armed=false;transactions.Current!.Enlist(fault,()=>fault);}return result;
        }
    }
}
