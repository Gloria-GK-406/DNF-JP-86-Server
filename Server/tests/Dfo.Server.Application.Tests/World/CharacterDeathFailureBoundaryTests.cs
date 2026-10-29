using System.Buffers.Binary;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Execution;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Networking;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.CharacterDeath;
using Dfo.Server.Protocol.Game.Dungeons;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;
namespace Dfo.Server.Application.Tests.World;

public sealed partial class BossTowerDomainServiceTests
{
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public async Task Task47_boss_death_real_wire_scope_encoder_and_exact_physical(int fault)
    {
        await using var probe = new EntryFailureProbe(false, fault == 1, commandOverride: CharacterDeathOracle.Whole ? "GameCharacterDeathCommand" : "DieInBossTowerCommand");
        using var f = Fixture(configure: probe.Configure); SelectedSessionIdentity peer = null!;
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            f.SetLevel(65); await f.Seed(65,2749110,3); peer = await AddMember(f,3);
            var tower=Tower(f); var run=(await tower.EnterAsync([new(f.Session,0),new(peer,1)],f.Session,42,default)).Run!;
            await tower.LoadAsync(Context(run,f.Session),default); await tower.LoadAsync(Context(run,peer),default); await tower.StartAsync(Context(run,f.Session),default);
        });
        await CharacterDeathOracle.Check(f,probe,peer,fault,true,()=>Assert.True(Tower(f).Find(f.Session)!.Members.Single(x=>x.Session==f.Session).Dead));
    }
}
public sealed partial class IllusionTowerDomainServiceTests
{
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public async Task Task47_illusion_death_real_wire_scope_encoder_and_exact_physical(int fault)
    {
        await using var probe = new EntryFailureProbe(true,fault==1,commandOverride:CharacterDeathOracle.Whole?"GameCharacterDeathCommand":"DieInIllusionTowerCommand");
        using var f=Fixture(configure:probe.Configure); SelectedSessionIdentity peer=null!;
        await TestGameplay.RunAsync(f.Services,async()=>
        {
            await f.Seed(65,4183,1); peer=await AddMember(f,3);
            var tower=Tower(f); var run=(await tower.EnterAsync([new(f.Session,0),new(peer,1)],f.Session,42,default, 11001)).Run!;
            await tower.LoadAsync(Context(run,f.Session),default);await tower.LoadAsync(Context(run,peer),default);await tower.StartAsync(Context(run,f.Session),default);
        });
        await CharacterDeathOracle.Check(f,probe,peer,fault,false,()=>Assert.True(Tower(f).Find(f.Session)!.Members.Single(x=>x.Session==f.Session).Dead));
    }
}
internal static class CharacterDeathOracle
{
    internal static bool Whole => typeof(CharacterDeathHandler).Assembly.GetType("Dfo.Server.Protocol.Game.CharacterDeath.GameCharacterDeathCommand") is not null;
    // Actual decoder/dispatcher, domain transaction, encoder and production outbox. Only the
    // physical transport is controlled. This oracle is compiled and run on both old and new product.
    internal static async Task Check(ShopFixture f, EntryFailureProbe probe, SelectedSessionIdentity? peer,
        int fault, bool boss, Action assertCommitted)
    {
        var source = probe.Sender.Add(f.Connection);
        RemainingOpeningProtocolCommandFlowTests.PhysicalConnection? physicalPeer = null;
        if (peer is not null)
        {
            var users = f.Services.GetRequiredService<IOnlineUserQueryDomainService>();
            Assert.True(users.TryGetByConnection(peer.ConnectionId, out var user));
            f.Sender.Outbound.Connections.Add(user!.Connection); probe.PeerContext = user.Connection;
            physicalPeer = probe.Sender.Add(user.Connection);
        }
        RemainingOpeningProtocolCommandFlowTests.PhysicalConnection? sourceReplacement=null,peerReplacement=null;
        if (fault >= 2) f.Sender.Outbound.Encoder.Fail((_, _) =>
        {
            if(fault>=3 && sourceReplacement is null)
            {
                var replacement=f.Connection with {};sourceReplacement=probe.Sender.Add(replacement);probe.Replacements[f.Connection.ConnectionId]=replacement;
                if(fault==4){f.Sender.Outbound.Connections.Remove(f.Connection.ConnectionId);f.Sender.Outbound.Connections.Add(replacement);}
                if(probe.PeerContext is {} old)
                {
                    var next=old with {};peerReplacement=probe.Sender.Add(next);probe.Replacements[old.ConnectionId]=next;
                    if(fault==4){f.Sender.Outbound.Connections.Remove(old.ConnectionId);f.Sender.Outbound.Connections.Add(next);}
                }
            }
            return probe.Cause;
        });
        var body = new byte[4];
        var error = await Record.ExceptionAsync(() => probe.Wire(f, 0x28, body).AsTask());
        if (fault==0 || fault>=2) Assert.Null(error);
        else if (Whole) Assert.Same(probe.Cause, Assert.IsType<TransactionOutcomeUncertainException>(error).InnerException);
        else Assert.Same(probe.Cause, error);
        Assert.Equal(DomainTransactionCompletion.Committed, Assert.Single(probe.Transactions).Completion);
        if (Whole)
        {
            var declaration = Assert.Single(probe.Declarations);
            Assert.Equal(CommandWorkUnitKind.Ordinary, declaration.Kind);
            Assert.Equal(CommandFootprintMode.DeclaredNonRequest, declaration.Footprint);
            Assert.Equal(peer is null ? 1 : 2, declaration.FootprintOwners!.Count);
            Assert.Equal(boss ? peer is null ? 1 : 2 : 0, declaration.Participants.Count);
            if (boss) Assert.Same(f.Connection, declaration.Participants.Single(x => x.Session == f.Session).Connection);
        }
        TestGameplay.Run(f.Services, () =>
        {
            assertCommitted();
            var selected = f.Services.GetRequiredService<ISelectedSessionDomainService>();
            Assert.Equal(Whole && boss && fault == 1, selected.StandingOf(f.Session)!.Fenced);
            if (peer is not null) Assert.Equal(Whole && boss && fault == 1, selected.StandingOf(peer)!.Fenced);
        });
        Assert.Equal(Whole && boss && fault == 1, f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
        await probe.Sender.Settled();
        var closesOriginal=fault is 1 or 2 or 3;
        if(closesOriginal) await source.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(closesOriginal?1:0,source.CloseCalls);
        if(fault==1)
        {
            Assert.True(f.Services.GetRequiredService<GameplayCloseCauses>().TryTake(f.Connection,out var chain));
            Assert.Equal(probe.Chain.RootId,chain.RootId);Assert.Equal(f.Connection.ConnectionId,chain.SourceConnection);
        }
        if(physicalPeer is not null) Assert.Equal(fault is 2 or 3?1:0,physicalPeer.CloseCalls);
        if(fault>=3)
        {
            Assert.NotNull(sourceReplacement);Assert.Empty(sourceReplacement.Sends);Assert.Equal(!Whole && fault==4?1:0,sourceReplacement.CloseCalls);
            if(peerReplacement is not null){Assert.Empty(peerReplacement.Sends);Assert.Equal(!Whole && fault==4?1:0,peerReplacement.CloseCalls);}
        }
        var delivered = fault == 0 || Whole && fault == 1;
        Assert.Equal(delivered ? 1 : 0, source.Sends.Count);
        if (physicalPeer is not null) Assert.Equal(delivered ? 1 : 0, physicalPeer.Sends.Count);
        if (delivered)
        {
            var types = Types(Assert.Single(source.Sends));
            Assert.Equal(new ushort[] { 0x28, 0x20 }, types);
            if (physicalPeer is not null) Assert.Equal(new ushort[] { 0x20 }, Types(Assert.Single(physicalPeer.Sends)));
        }
        if(fault is 0 or 1)
        {
            var before=f.Scalar("SELECT SUM(exp) FROM characters");var gold=f.Gold;var items=f.Scalar("SELECT COUNT(*) FROM items");
            var retry=await Record.ExceptionAsync(()=>probe.Wire(f,0x28,body).AsTask());
            if(fault==0 || Whole && boss)Assert.Null(retry);else Assert.NotNull(retry);
            if(Whole && boss && fault==1)Assert.Single(probe.Transactions);
            Assert.Equal(before,f.Scalar("SELECT SUM(exp) FROM characters"));Assert.Equal(gold,f.Gold);Assert.Equal(items,f.Scalar("SELECT COUNT(*) FROM items"));
        }
    }
    internal static ushort[] Types(byte[] bytes)
    {
        List<ushort> result = []; var offset = 0;
        while (offset < bytes.Length) { result.Add(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + 1))); offset += checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 3))); }
        Assert.Equal(bytes.Length, offset); return result.ToArray();
    }
}

public sealed class CharacterDeathProtocolLifecycleTests
{
    internal static DungeonRunId Enter(ShopFixture f, SelectedSessionIdentity? peer=null, bool dead=false, Func<DungeonLayout,DungeonLayout>? configure=null)
    {
        return TestGameplay.Run(f.Services,()=>
        {
            var runs=f.Services.GetRequiredService<IDungeonRunDomainService>();
            var layout=new DungeonLayout(144,0,1,1,new(0,0),new(0,0),61000,1,
                new Dictionary<Dfo.Server.Abstractions.Content.MazeCell,Dfo.Server.Abstractions.Content.DungeonCellDefinition>{[new(0,0)]=Dfo.Server.Abstractions.Content.DungeonCellDefinition.Fixed(61000)});
            if(configure is not null)layout=configure(layout);
            var members=peer is null?new[]{f.Session}:new[]{f.Session,peer};
            if(peer is not null){var parties=f.Services.GetRequiredService<Dfo.Server.Abstractions.Parties.IPartyService>();Dfo.Server.Data.Parties.PartyCharacterSnapshot Member(SelectedSessionIdentity x)=>new(x.AccountId,x.CharacterId,x.WireUserId,new(1,0,0));Assert.True(parties.RequestInvite(Member(f.Session),Member(peer),new(0,0,0,0)).Succeeded);Assert.True(parties.AcceptInvite(Member(peer),Member(f.Session)).Succeeded);}
            var run=f.InTransaction(()=>runs.PreparePartyEntry(members.Select((x,i)=>new DungeonRunParticipant(x.AccountId,x.CharacterId,(byte)i,i==0)).ToArray(),layout,0)).Run;
            f.Services.GetRequiredService<DungeonRunDeliveryRegistry>().Register(run.RunId,members);
            foreach(var member in members){runs.TryMarkParticipantReady(member.CharacterId,run.RunId,run.RoomVersion);if(dead)runs.TryMarkParticipantDead(member.CharacterId,run.RunId,run.RoomVersion);}
            runs.FindExpiredPartyWipes(DateTimeOffset.UnixEpoch);return run.RunId;
        });
    }
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task Task47_ordinary_death_has_zero_transactions_and_repeat_never_restarts_ui(int fault)
    {
        await using var probe=new EntryFailureProbe(false,fault==1,ordinary:true,commandOverride:CharacterDeathOracle.Whole?"GameCharacterDeathCommand":"MarkDungeonParticipantDeadCommand");
        using var f=new ShopFixture(probe.Configure); var run=Enter(f);var physical=probe.Sender.Add(f.Connection);
        if(fault==2)f.Sender.Outbound.Encoder.Fail((_,_)=>probe.Cause);
        var error=await Record.ExceptionAsync(()=>probe.Wire(f,0x28,new byte[4]).AsTask());
        if(fault==0)Assert.Null(error);else Assert.Same(probe.Cause,error);
        Assert.Empty(probe.Transactions);Assert.Empty(probe.Declarations);
        TestGameplay.Run(f.Services,()=>{Assert.Contains(f.Session.CharacterId,f.Services.GetRequiredService<IDungeonRunDomainService>().Find(f.Session.CharacterId)!.DeadParticipants);Assert.False(f.Services.GetRequiredService<ISelectedSessionDomainService>().StandingOf(f.Session)!.Fenced);});
        Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
        if(fault!=0){await physical.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10));Assert.True(f.Services.GetRequiredService<GameplayCloseCauses>().TryTake(f.Connection,out var chain));Assert.Equal(probe.Chain.RootId,chain.RootId);}
        await probe.Sender.Settled();var delivered=fault==0||CharacterDeathOracle.Whole&&fault==1;
        Assert.Equal(delivered?1:0,physical.Sends.Count);if(delivered)Assert.Equal(new ushort[]{0x28,0x20},CharacterDeathOracle.Types(physical.Sends.Single()));
        // A new admitted physical can report again; the already-dead participant emits nothing.
        var replacement=f.Connection with{};probe.Replacements[f.Connection.ConnectionId]=replacement;
        f.Sender.Outbound.Connections.Remove(f.Connection.ConnectionId);f.Sender.Outbound.Connections.Add(replacement);var retry=probe.Sender.Add(replacement);
        f.Sender.Outbound.Encoder.Fail((_,_)=>null);
        var meta=new GameMessageMetadata(1,0x28,0,8){InboundOrdinal=8,SequenceIsFresh=true};
        var decoded=new CharacterDeathDecoder().Decode(meta,new byte[4]);
        var second=await Record.ExceptionAsync(()=>f.Services.GetRequiredService<INetworkMessageDispatcher>().DispatchAsync(new(replacement),decoded,default).AsTask());
        if(fault==1)Assert.Same(probe.Cause,second);else Assert.Null(second);
        await probe.Sender.Settled();Assert.Empty(retry.Sends);
    }
}

public sealed partial class RevivalProtocolLifecycleTests
{
    internal static bool Whole => typeof(UseRevivalCoinHandler).Assembly.GetType("Dfo.Server.Protocol.Game.CharacterDeath.GameUseRevivalCoinCommand") is not null;
    [Theory]
    [InlineData(0,0)] [InlineData(0,1)] [InlineData(0,2)]
    [InlineData(1,0)] [InlineData(1,1)] [InlineData(1,2)]
    public async Task Task47_revival_real_wire_payer_target_flag_scope_encoder_and_repeat(byte flag,int fault)
    {
        await using var probe=new EntryFailureProbe(false,fault==1,ordinary:true,commandOverride:Whole?"GameUseRevivalCoinCommand":"UseRevivalCoinCommand");
        using var f=new ShopFixture(s=>{probe.Configure(s);s.AddSingleton(new RevivalOptions(flag));});
        await f.Seed(1,1,1,0);SelectedSessionIdentity peer=null!;
        await TestGameplay.RunAsync(f.Services,async()=>peer=await BossTowerDomainServiceTests.AddMember(f,0));
        var run=CharacterDeathProtocolLifecycleTests.Enter(f,peer,true);
        var physical=probe.Sender.Add(f.Connection);Assert.True(f.Services.GetRequiredService<IOnlineUserQueryDomainService>().TryGetByConnection(peer.ConnectionId,out var user));
        f.Sender.Outbound.Connections.Add(user!.Connection);var other=probe.Sender.Add(user.Connection);
        if(fault==2)f.Sender.Outbound.Encoder.Fail((_,_)=>probe.Cause);
        var body=new byte[2];BinaryPrimitives.WriteUInt16LittleEndian(body,peer.WireUserId);
        var error=await Record.ExceptionAsync(()=>probe.Wire(f,0x29,body).AsTask());
        if(fault==0)Assert.Null(error);else if(Whole)Assert.Same(probe.Cause,Assert.IsType<TransactionOutcomeUncertainException>(error).InnerException);else Assert.Same(probe.Cause,error);
        Assert.Equal(DomainTransactionCompletion.Committed,Assert.Single(probe.Transactions).Completion);
        if(Whole){var d=Assert.Single(probe.Declarations);Assert.Equal(CommandWorkUnitKind.Ordinary,d.Kind);Assert.Empty(d.Participants);Assert.Equal(CommandFootprintMode.None,d.Footprint);Assert.Null(d.FootprintOwners);}
        Assert.Equal(0,await Coins(f));
        TestGameplay.Run(f.Services,()=>{var state=f.Services.GetRequiredService<IDungeonRunDomainService>().Find(peer.CharacterId)!;Assert.DoesNotContain(peer.CharacterId,state.DeadParticipants);Assert.Contains(f.Session.CharacterId,state.DeadParticipants);foreach(var x in new[]{f.Session,peer})Assert.False(f.Services.GetRequiredService<ISelectedSessionDomainService>().StandingOf(x)!.Fenced);});
        Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
        if(fault!=0){await physical.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10));Assert.True(f.Services.GetRequiredService<GameplayCloseCauses>().TryTake(f.Connection,out var chain));Assert.Equal(probe.Chain.RootId,chain.RootId);}
        await probe.Sender.Settled();var delivered=fault==0||Whole&&fault==1;
        Assert.Equal(delivered?1:0,physical.Sends.Count);Assert.Equal(delivered?1:0,other.Sends.Count);
        if(delivered)
        {
            var bytes=physical.Sends.Single();Assert.Equal(new ushort[]{0x20,0x29,0x0E},CharacterDeathOracle.Types(bytes));
            var offset=(int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(3));Assert.Equal(new byte[]{1,(byte)peer.WireUserId,(byte)(peer.WireUserId>>8),flag},bytes.AsSpan(offset+15,4).ToArray());
            Assert.Equal(new ushort[]{0x20},CharacterDeathOracle.Types(other.Sends.Single()));
        }
        if(fault==0){await probe.Wire(f,0x29,body);await probe.Sender.Settled();Assert.Equal(2,physical.Sends.Count);Assert.Equal(new byte[]{0,0},physical.Sends.ElementAt(1).AsSpan(15).ToArray());Assert.Single(other.Sends);Assert.Equal(0,await Coins(f));}
    }
    [Fact]
    public async Task Task47_revival_prepare_failure_rolls_back_sql_and_life_then_actual_wire_retry_pays_once()
    {
        var fault=new PrepareFault();
        await using var probe=new EntryFailureProbe(false,false,ordinary:true,commandOverride:Whole?"GameUseRevivalCoinCommand":"UseRevivalCoinCommand");
        using var f=new ShopFixture(s=>
        {
            probe.Configure(s);var d=s.Last(x=>x.ServiceType==typeof(IDungeonRevivalDomainService));s.Remove(d);
            s.AddSingleton<IDungeonRevivalDomainService>(p=>new FailAfterRevival((IDungeonRevivalDomainService)(d.ImplementationFactory?.Invoke(p)??ActivatorUtilities.CreateInstance(p,d.ImplementationType!)),p.GetRequiredService<IDomainTransactionAccessor>(),fault));
        });
        await f.Seed(1,1,2,0);var run=CharacterDeathProtocolLifecycleTests.Enter(f,dead:true);var source=probe.Sender.Add(f.Connection);
        var body=new byte[2];BinaryPrimitives.WriteUInt16LittleEndian(body,f.Session.WireUserId);
        var error=await Record.ExceptionAsync(()=>probe.Wire(f,0x29,body).AsTask());Assert.Same(fault.Cause,error);Assert.True(fault.Prepared);
        Assert.Equal(DomainTransactionCompletion.ConfirmedRollback,Assert.Single(probe.Transactions).Completion);Assert.Equal(2,await Coins(f));
        TestGameplay.Run(f.Services,()=>{var runs=f.Services.GetRequiredService<IDungeonRunDomainService>();Assert.Contains(f.Session.CharacterId,runs.Find(f.Session.CharacterId)!.DeadParticipants);Assert.Equal(new[]{run},runs.FindExpiredPartyWipes(DateTimeOffset.UnixEpoch.AddSeconds(11)));});
        await source.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10));await probe.Sender.Settled();Assert.Empty(source.Sends);
        Assert.False(f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
        var replacement=f.Connection with{};probe.Replacements[f.Connection.ConnectionId]=replacement;f.Sender.Outbound.Connections.Remove(f.Connection.ConnectionId);f.Sender.Outbound.Connections.Add(replacement);var retry=probe.Sender.Add(replacement);
        async ValueTask Retry(){var metadata=new GameMessageMetadata(1,0x29,0,8){InboundOrdinal=8,SequenceIsFresh=true}.WithRawBody(body);var registration=f.Services.GetServices<GameMessageDecoderRegistration>().Single(x=>x.Selector==1&&x.Type==0x29);var message=((IGameMessageDecoder)f.Services.GetRequiredService(registration.DecoderType)).Decode(metadata,body)!;await f.Services.GetRequiredService<INetworkMessageDispatcher>().DispatchAsync(new(replacement),message,default);}
        await Retry();await probe.Sender.Settled();Assert.Equal(1,await Coins(f));Assert.Equal(new ushort[]{0x20,0x29,0x0E},CharacterDeathOracle.Types(Assert.Single(retry.Sends)));
        await Retry();await probe.Sender.Settled();Assert.Equal(1,await Coins(f));Assert.Equal(2,retry.Sends.Count);Assert.Equal(new byte[]{0,0},retry.Sends.ElementAt(1).AsSpan(15).ToArray());Assert.Equal(0,retry.CloseCalls);
    }
    private static async ValueTask<int> Coins(ShopFixture f)=>(await f.Services.GetRequiredService<Dfo.Server.Abstractions.Inventory.ICharacterContainerDomainService>().FindContainer(f.Session.CharacterId,InventoryContainerType.Main)!.GetAsync(1,default))!.Count;
    private sealed class PrepareFault:ITransactionParticipant
    {
        internal readonly Exception Cause=new IOException("TASK47 Prepare after SQLite coin update");internal bool Armed=true,Prepared;
        public bool CommitCanFail=>false;public ValueTask PrepareAsync(CancellationToken ct){Prepared=true;throw Cause;}
        public ValueTask CommitAsync(CancellationToken ct)=>throw new InvalidOperationException();public ValueTask RollbackAsync(CancellationToken ct)=>ValueTask.CompletedTask;
    }
    private sealed class FailAfterRevival(IDungeonRevivalDomainService actual,IDomainTransactionAccessor transactions,PrepareFault fault):IDungeonRevivalDomainService
    {
        public async ValueTask<DungeonRevivalResult> ReviveAsync(SelectedSessionIdentity payer,CharacterId target,DungeonRunId run,CancellationToken ct)
        {var result=await actual.ReviveAsync(payer,target,run,ct);if(result.Succeeded&&fault.Armed){fault.Armed=false;transactions.Current!.Enlist(fault,()=>fault);}return result;}
    }
}
public sealed partial class BossTowerDomainServiceTests
{
    [Fact]
    public async Task Task47_boss_whole_wipe_consumes_owners_and_settles_mail_once()
    {
        await using var probe=new EntryFailureProbe(false,false,commandOverride:CharacterDeathOracle.Whole?"GameCharacterDeathCommand":"DieInBossTowerCommand");
        var content=new TowerRewardTestContent();content.Table=content.Table with{ItemTypeRates=[0,10000,0,0]};
        using var f=Fixture(new Rules{Probabilities=[0,1000,0],CardCount=10},configure:s=>{TowerRewardTestContent.Register(s,content);probe.Configure(s);});
        await TestGameplay.RunAsync(f.Services,async()=>{f.SetLevel(65);await f.Seed(65,2749110,3);var tower=Tower(f);var run=(await Enter(f)).Run!;await tower.LoadAsync(Context(run,f.Session),default);await tower.StartAsync(Context(run,f.Session),default);});
        var physical=probe.Sender.Add(f.Connection);await probe.Wire(f,0x28,new byte[4]);await probe.Sender.Settled();
        TestGameplay.Run(f.Services,()=>{var receipt=Assert.Single(Tower(f).Find(f.Session)!.Completion!.Receipt!.Members);Assert.Equal(10,receipt.Candidates.Length);Assert.All(receipt.Candidates,x=>Assert.True(x.Delivered));});
        var mail=f.Scalar("SELECT COUNT(*) FROM mailbox_attachments WHERE claimed_flag=0");Assert.True(mail>0);Assert.Equal(10,mail+f.Count(3000));Assert.Contains((ushort)0x335,CharacterDeathOracle.Types(Assert.Single(physical.Sends)));
        await probe.Wire(f,0x28,new byte[4]);await probe.Sender.Settled();Assert.Equal(mail,f.Scalar("SELECT COUNT(*) FROM mailbox_attachments WHERE claimed_flag=0"));Assert.Single(physical.Sends);
    }
}
public sealed partial class IllusionTowerDomainServiceTests
{
    [Fact]
    public async Task Task47_illusion_whole_wipe_consumes_owners_and_settles_mail_once()
    {
        await using var probe=new EntryFailureProbe(true,false,commandOverride:CharacterDeathOracle.Whole?"GameCharacterDeathCommand":"DieInIllusionTowerCommand");
        var content=new TowerRewardTestContent();content.Table=content.Table with{ItemTypeRates=[0,10000,0,0]};
        using var f=Fixture(new Rules{Probabilities=[0,1001,100]},rewards:true,configure:s=>{TowerRewardTestContent.Register(s,content);probe.Configure(s);});
        await TestGameplay.RunAsync(f.Services,async()=>{await f.Seed(65,4183,1);var tower=Tower(f);var run=(await Enter(f)).Run!;await tower.LoadAsync(Context(run,f.Session),default);await tower.StartAsync(Context(run,f.Session),default);});
        var physical=probe.Sender.Add(f.Connection);await probe.Wire(f,0x28,new byte[4]);await probe.Sender.Settled();
        TestGameplay.Run(f.Services,()=>{var receipt=Assert.Single(Tower(f).Find(f.Session)!.Completion!.Receipt!.Members);Assert.Equal(4,receipt.Candidates.Length);Assert.All(receipt.Candidates,x=>Assert.True(x.Delivered));});
        Assert.Equal(2,f.Scalar("SELECT COUNT(*) FROM mailbox_attachments WHERE claimed_flag=0"));Assert.Equal(2,f.Count(3000));Assert.Contains((ushort)0x90,CharacterDeathOracle.Types(Assert.Single(physical.Sends)));
        await probe.Wire(f,0x28,new byte[4]);await probe.Sender.Settled();Assert.Equal(2,f.Scalar("SELECT COUNT(*) FROM mailbox_attachments WHERE claimed_flag=0"));Assert.Single(physical.Sends);
    }
}
public sealed partial class RevivalProtocolLifecycleTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Task47_altar_counter_and_mirror_target_fact_follow_real_coin_transaction(bool altar)
    {
        await using var probe=new EntryFailureProbe(false,false,ordinary:true,commandOverride:Whole?"GameUseRevivalCoinCommand":"UseRevivalCoinCommand");
        var facts=new RevivalFacts();using var f=new ShopFixture(s=>{probe.Configure(s);s.AddSingleton<IAltarDungeonDomainService>(p=>{facts.Transactions=p.GetRequiredService<IDomainTransactionAccessor>();return facts;});s.AddSingleton<Dfo.Server.Abstractions.Quests.IQuestProgressDomainService>(facts);});
        await f.Seed(1,1,2,0);SelectedSessionIdentity target=null!;await TestGameplay.RunAsync(f.Services,async()=>target=await BossTowerDomainServiceTests.AddMember(f,0));
        var run=CharacterDeathProtocolLifecycleTests.Enter(f,target,true,layout=>altar?layout with{Mode=new DungeonRunMode.Altar(new(AltarDungeonKind.Endless,new DateOnly(2026,10,25)))}:layout with{Mode=new DungeonRunMode.Mirror(new(8000,31,14299))});
        var source=probe.Sender.Add(f.Connection);Assert.True(f.Services.GetRequiredService<IOnlineUserQueryDomainService>().TryGetByConnection(target.ConnectionId,out var user));f.Sender.Outbound.Connections.Add(user!.Connection);probe.Sender.Add(user.Connection);
        var body=new byte[2];BinaryPrimitives.WriteUInt16LittleEndian(body,target.WireUserId);await probe.Wire(f,0x29,body);await probe.Sender.Settled();Assert.Equal(1,await Coins(f));
        if(altar){Assert.Equal(1,facts.Revivals);Assert.Empty(facts.Facts);}else{var report=Assert.Single(facts.Facts);Assert.Equal(target.AccountId,report.Account);Assert.Equal(target.CharacterId,report.Character);Assert.Equal(new Dfo.Server.Abstractions.Quests.QuestCharacterRevived(run,target.CharacterId),Assert.Single(report.Facts));}
        TestGameplay.Run(f.Services,()=>{var runs=f.Services.GetRequiredService<IDungeonRunDomainService>();var current=runs.Find(target.CharacterId)!;Assert.NotNull(runs.TryMarkParticipantDead(target.CharacterId,run,current.RoomVersion));});
        await probe.Wire(f,0x29,body);await probe.Sender.Settled();
        if(altar){Assert.Equal(1,await Coins(f));Assert.Equal(1,facts.Revivals);Assert.Equal(new byte[]{0,0},source.Sends.Last().AsSpan(15).ToArray());}
        else{Assert.Equal(0,await Coins(f));Assert.Equal(2,facts.Facts.Count);Assert.All(facts.Facts,x=>Assert.Equal(target.AccountId,x.Account));}
        Assert.All(probe.Transactions,x=>Assert.Equal(DomainTransactionCompletion.Committed,x.Completion));
    }
    private sealed class RevivalFacts:IAltarDungeonDomainService,Dfo.Server.Abstractions.Quests.IQuestProgressDomainService
    {
        internal IDomainTransactionAccessor Transactions=null!;internal int Revivals;
        internal readonly List<(Dfo.Server.Data.Accounts.AccountId Account,CharacterId Character,IReadOnlyList<Dfo.Server.Abstractions.Quests.QuestProgressFact> Facts)> Facts=[];
        public bool CanRevive(CharacterId character,DungeonRunId run)=>Revivals==0;
        public bool StageRevival(CharacterId character,DungeonRunId run){if(!CanRevive(character,run))return false;Transactions.Current!.Enlist(this,()=>new Counter(this));return true;}
        private sealed class Counter(RevivalFacts owner):ITransactionParticipant
        {public bool CommitCanFail=>false;public ValueTask PrepareAsync(CancellationToken ct)=>ValueTask.CompletedTask;public ValueTask CommitAsync(CancellationToken ct){owner.Revivals++;return ValueTask.CompletedTask;}public ValueTask RollbackAsync(CancellationToken ct)=>ValueTask.CompletedTask;}
        public ValueTask<Dfo.Server.Abstractions.Quests.QuestProgressReport> ReportAsync(Dfo.Server.Data.Accounts.AccountId account,CharacterId character,IReadOnlyList<Dfo.Server.Abstractions.Quests.QuestProgressFact> facts,CancellationToken ct){Facts.Add((account,character,facts));return ValueTask.FromResult(new Dfo.Server.Abstractions.Quests.QuestProgressReport(null,[],null));}
        public bool Supports(int id)=>true;public AltarDungeonState? Find(DungeonRunId id)=>null;public IReadOnlyList<AltarDeadline> Pending()=>[];
        public ValueTask<DungeonEntryAttempt> EnterAsync(DungeonEntryContext c,CancellationToken ct)=>throw new NotSupportedException();
        public ValueTask<AltarActionResult?> ReadyAsync(SelectedSessionIdentity s,DungeonRunId r,long v,CancellationToken ct)=>throw new NotSupportedException();
        public ValueTask<AltarActionResult?> AdvanceAsync(DungeonRunId r,long v,long g,DateTimeOffset n,CancellationToken ct)=>throw new NotSupportedException();
        public ValueTask<AltarActionResult?> SelectDifficultyAsync(SelectedSessionIdentity s,DungeonRunId r,long v,long g,byte d,CancellationToken ct)=>throw new NotSupportedException();
        public ValueTask<AltarActionResult?> FailAsync(DungeonRunId r,CancellationToken ct)=>throw new NotSupportedException();
        public ValueTask<AltarActionResult?> SettleBeforeExitAsync(DungeonRunId r,CancellationToken ct,CharacterId? p=null)=>throw new NotSupportedException();
        public ValueTask<IReadOnlyList<AltarQuotaSnapshot>> ReadQuotaAsync(CharacterId c,CancellationToken ct)=>ValueTask.FromResult<IReadOnlyList<AltarQuotaSnapshot>>([]);
    }
}
