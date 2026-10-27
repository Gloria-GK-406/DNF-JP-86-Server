using System.Buffers.Binary;
using Dfo.Server.Abstractions.Networking;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Execution;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.World;
using Dfo.Server.Protocol.Game.MonsterDeath;
using Dfo.Server.Testing;
using Microsoft.Extensions.DependencyInjection;
namespace Dfo.Server.Application.Tests.World;

public sealed partial class BossTowerDomainServiceTests
{
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public async Task Task45_boss_kill_real_wire_commit_scope_and_encoder_boundary(int fault)
    {
        var whole = MonsterKillOracle.Whole;
        await using var probe = new EntryFailureProbe(false, fault == 1,
            commandOverride: whole ? "GameMonsterDeathCommand" : "KillBossTowerActorCommand");
        using var f = Fixture(drops: new Drops([new(2749094, 1, 0, Dfo.Server.Abstractions.World.DropItemKind.Stackable)]), configure: probe.Configure);
        SelectedSessionIdentity peer = null!; ushort actor = 0;
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            f.SetLevel(65); await f.Seed(65, 2749110, 3); peer = await AddMember(f, 3);
            var tower = Tower(f);
            var run = (await tower.EnterAsync([new(f.Session, 0), new(peer, 1)], f.Session, 42, default)).Run!;
            await tower.LoadAsync(Context(run, f.Session), default); await tower.LoadAsync(Context(run, peer), default);
            await tower.StartAsync(Context(run, f.Session), default); actor = run.Room.Actors[0].Uid;
        });
        await MonsterKillOracle.Check(f, probe, peer, actor, fault, boss: true,
            () => { Assert.True(Tower(f).Find(f.Session)!.Room.Actors.Single(x => x.Uid == actor).Dead); });
        Assert.Equal(1, f.Count(2749094));
    }
}
public sealed partial class IllusionTowerDomainServiceTests
{
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public async Task Task45_illusion_kill_real_wire_commit_scope_and_encoder_boundary(int fault)
    {
        await using var probe = new EntryFailureProbe(true, fault == 1,
            commandOverride: MonsterKillOracle.Whole ? "GameMonsterDeathCommand" : "KillIllusionTowerActorCommand");
        using var f = Fixture(configure: probe.Configure);
        ushort actor = 0;
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            await f.Seed(65, 4183, 1); var tower = Tower(f); var run = (await Enter(f)).Run!;
            await tower.LoadAsync(Context(run, f.Session), default); await tower.StartAsync(Context(run, f.Session), default);
            actor = run.Room.Actors[0].Uid;
        });
        await MonsterKillOracle.Check(f, probe, null, actor, fault, boss: false,
            () => Assert.True(Tower(f).Find(f.Session)!.Room.Actors.Single(x => x.Uid == actor).Dead));
    }
}
internal static class MonsterKillOracle
{
    internal static bool Whole => typeof(MonsterDeathHandler).Assembly.GetType("Dfo.Server.Protocol.Game.MonsterDeath.GameMonsterDeathCommand") is not null;
    // Actual decoder/dispatcher, domain transaction, encoder and production outbox. Only the
    // physical transport is controlled. This oracle is compiled and run on both old and new product.
    internal static async Task Check(ShopFixture f, EntryFailureProbe probe, SelectedSessionIdentity? peer,
        ushort actor, int fault, bool boss, Action assertCommitted)
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
        var body = new byte[62]; BinaryPrimitives.WriteUInt32LittleEndian(body, actor);
        BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(4), f.Session.WireUserId);
        var error = await Record.ExceptionAsync(() => probe.Wire(f, 0x27, body).AsTask());
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
            Assert.Contains((ushort)0x26, types);
            Assert.Equal(boss ? new ushort[] { 0x25, 0x0E, 0x26 } : new ushort[] { 0x26 }, types);
            if (physicalPeer is not null) Assert.Equal(new ushort[] { 0x25, 0x26 }, Types(Assert.Single(physicalPeer.Sends)));
        }
        if(fault is 0 or 1)
        {
            var before=f.Scalar("SELECT SUM(exp) FROM characters");var gold=f.Gold;var items=f.Scalar("SELECT COUNT(*) FROM items");
            var retry=await Record.ExceptionAsync(()=>probe.Wire(f,0x27,body).AsTask());
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

public sealed partial class BossTowerDomainServiceTests
{
    [Theory]
    [InlineData(false, 0)] [InlineData(false, 1)] [InlineData(false, 2)] [InlineData(false, 3)] [InlineData(false, 4)] [InlineData(false, 5)]
    [InlineData(true, 0)] [InlineData(true, 1)]
    public async Task Task45_ordinary_and_object_keep_distinct_transactions_and_protection(bool passive, int fault)
    {
        var whole = MonsterKillOracle.Whole;
        await using var probe = new EntryFailureProbe(false, fault == 1, ordinary: true,
            commandOverride: whole ? "GameMonsterDeathCommand" : passive ? "ApplyObjectDestructionCommand" : "ProcessMonsterDeathCommand");
        using var f = Fixture(configure: services => { probe.Configure(services); services.AddSingleton(new ExperienceDiagnostics(fault==4)); });
        SelectedSessionIdentity peer = null!;
        await TestGameplay.RunAsync(f.Services, async () =>
        {
            f.SetLevel(65); peer = await AddMember(f, 0);
            if(fault==5) f.Sql("INSERT INTO character_active_quests VALUES(1,0,2001,1)");
            var parties=f.Services.GetRequiredService<Dfo.Server.Abstractions.Parties.IPartyService>();
            Dfo.Server.Data.Parties.PartyCharacterSnapshot Member(SelectedSessionIdentity x)=>new(x.AccountId,x.CharacterId,x.WireUserId,new(1,0,0));
            Assert.True(parties.RequestInvite(Member(f.Session),Member(peer),new(0,0,0,0)).Succeeded);
            Assert.True(parties.AcceptInvite(Member(peer),Member(f.Session)).Succeeded);
            await using var setup=Scope(f).Begin();
            var runs = f.Services.GetRequiredService<Dfo.Server.Abstractions.World.IDungeonRunDomainService>();
            var layout = new Dfo.Server.Abstractions.World.DungeonLayout(144, 0, 3, 1, new(0,0), new(2,0), 61000, 42,
                new Dictionary<Dfo.Server.Abstractions.Content.MazeCell,Dfo.Server.Abstractions.Content.DungeonCellDefinition>
                { [new(0,0)] = Dfo.Server.Abstractions.Content.DungeonCellDefinition.Fixed(61000), [new(1,0)] = Dfo.Server.Abstractions.Content.DungeonCellDefinition.Fixed(61001), [new(2,0)] = Dfo.Server.Abstractions.Content.DungeonCellDefinition.Fixed(61002) });
            var seed = new Dfo.Server.Abstractions.World.DungeonCellSeed(layout.StartCell, 61000, 1,
                [new(1, new(13099,1,0), false,false,fault==5?[new(2749094,1,DropRarity.Common,DropItemKind.Stackable,OwnerCharacterId:f.Session.CharacterId,Delivery:RolledDropDelivery.DirectQuestItem,QuestId:2001,GrantLimit:15)]:[]), new(2,new(13099,1,0),false,false,[])], [])
                { Objects = [new(new(new(Dfo.Server.Abstractions.Content.DungeonObjectSection.Passive,0),8961,0,0,0),[])] };
            var run = runs.PreparePartyEntry([new(f.Session.AccountId,f.Session.CharacterId,0,true),new(peer.AccountId,peer.CharacterId,1,false)],
                layout, DungeonDifficulty.Normal, seed, DungeonRunId.New(), f.Clock.GetUtcNow()).Run;
            f.Services.GetRequiredService<Dfo.Server.Protocol.Game.Dungeons.DungeonRunDeliveryRegistry>().Register(run.RunId,[f.Session,peer]);
            var online = f.Services.GetRequiredService<IOnlineUserQueryDomainService>();
            Assert.True(online.TryGetByConnection(peer.ConnectionId,out var user));
            probe.PeerContext=user!.Connection; f.Sender.Outbound.Connections.Add(user.Connection);
            await setup.CommitAsync(default);
        });
        var source=probe.Sender.Add(f.Connection);var secondary=probe.Sender.Add(probe.PeerContext!);
        if(fault==2) f.Sender.Outbound.Encoder.Fail((_,_)=>probe.Cause);
        if(fault==3) secondary.SendFailure=probe.Cause;
        var body=new byte[62];BinaryPrimitives.WriteUInt32LittleEndian(body,passive?8961u:1u);body[29]=passive?(byte)1:(byte)0;
        var error=await Record.ExceptionAsync(()=>probe.Wire(f,0x27,body).AsTask());
        if(fault==0 || fault>=3) Assert.Null(error);
        else if(whole || !passive && fault==2) Assert.Same(probe.Cause,Assert.IsType<TransactionOutcomeUncertainException>(error).InnerException);
        else Assert.Same(probe.Cause,error);
        Assert.Equal(DomainTransactionCompletion.Committed,Assert.Single(probe.Transactions).Completion);
        if(whole)
        {
            var d=Assert.Single(probe.Declarations);Assert.Equal(passive?0:2,d.Participants.Count);
            Assert.Equal(passive?CommandFootprintMode.None:CommandFootprintMode.DeclaredNonRequest,d.Footprint);
            if(!passive) {Assert.Equal(2,d.FootprintOwners!.Count);Assert.All(d.Participants,p=>{Assert.NotNull(p.Connection);Assert.Equal((ushort)7,p.Metadata!.Sequence);});}
        }
        TestGameplay.Run(f.Services,()=>
        {
            var run=f.Services.GetRequiredService<Dfo.Server.Abstractions.World.IDungeonRunDomainService>().Find(f.Session.CharacterId)!;
            Assert.Equal(!passive,run.CurrentCellState.FindActor(1)!.IsDead);Assert.Equal(passive,run.CurrentCellState.Objects.Single().IsConsumed);
            foreach(var member in new[]{f.Session,peer}) Assert.Equal(!passive && (fault==2 || whole && fault==1),f.Services.GetRequiredService<ISelectedSessionDomainService>().StandingOf(member)!.Fenced);
        });
        Assert.Equal(!passive && (fault==2 || whole && fault==1),f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(f.Connection));
        Assert.Equal(!passive && (fault==2 || whole && fault==1),f.Services.GetRequiredService<SelectedSessionSafety>().IsBlocked(probe.PeerContext!));
        await probe.Sender.Settled();
        if(fault is 1 or 2) await source.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        if(fault==3) await secondary.Closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(!passive && (fault is 2 or 3 || whole && fault==1)?1:0,secondary.CloseCalls);
        var delivered=!passive && (fault==0 || fault>=3 || whole && fault==1);
        Assert.Equal(delivered?1:0,source.Sends.Count);Assert.Equal(delivered && fault!=3?1:0,secondary.Sends.Count);
        if(delivered)
        {
            var bytes=source.Sends.Single();var types=MonsterKillOracle.Types(bytes);
            Assert.Equal(fault==5?new ushort[]{0x25,0x26,0x0E}:new ushort[]{0x25,0x26},types);
            if(fault!=3) Assert.Equal(new ushort[]{0x25,0x26},MonsterKillOracle.Types(secondary.Sends.Single()));
            Assert.Equal(98,BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(3)));
            var exp=bytes.AsSpan(15,83).ToArray();
            Assert.Equal(checked((uint)f.Scalar("SELECT exp FROM characters WHERE character_id=1")+(fault==4?ExperienceDiagnostics.ProbeTotal:0)),BinaryPrimitives.ReadUInt32LittleEndian(exp.AsSpan(1)));
            var offsets=new[]{5,21,26,30,34,38,42,47,51,55,67,71,75,79};
            for(var i=0;i<offsets.Length;i++) Assert.Equal(fault==4?ExperienceDiagnostics.ProbeAmounts[i]:0u,BinaryPrimitives.ReadUInt32LittleEndian(exp.AsSpan(offsets[i])));
            if(fault==5) Assert.Equal(1,f.Count(2749094));
        }
        if(fault is 1 or 2)
        {
            Assert.True(f.Services.GetRequiredService<GameplayCloseCauses>().TryTake(f.Connection,out var chain));
            Assert.Equal(probe.Chain.RootId,chain.RootId);Assert.Equal(f.Connection.ConnectionId,chain.SourceConnection);
        }
        if(!passive && fault!=3)
        {
            var before=f.Scalar("SELECT SUM(exp) FROM characters");var gold=f.Gold;var items=f.Scalar("SELECT COUNT(*) FROM items");
            var retry=await Record.ExceptionAsync(()=>probe.Wire(f,0x27,body).AsTask());
            if(fault==1 && !whole)Assert.Same(probe.Cause,retry);else Assert.Null(retry);
            if(fault==2 || whole && fault==1)Assert.Single(probe.Transactions);
            Assert.Equal(before,f.Scalar("SELECT SUM(exp) FROM characters"));Assert.Equal(gold,f.Gold);Assert.Equal(items,f.Scalar("SELECT COUNT(*) FROM items"));
        }
    }
}
