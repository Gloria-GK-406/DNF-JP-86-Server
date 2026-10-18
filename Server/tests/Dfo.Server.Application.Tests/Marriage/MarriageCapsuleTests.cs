using System.Collections.Immutable;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.DomainEvents;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Marriage;
using Dfo.Server.Application.World;
using Dfo.Server.Data.Marriage;
using Microsoft.Extensions.DependencyInjection;
using static Dfo.Server.Application.Tests.Marriage.MarriageHarness;

namespace Dfo.Server.Application.Tests.Marriage;

public sealed partial class MarriageHousingTests
{
    [SkippableFact]
    public async Task Real_couple_dungeon_charges_both_tickets_atomically_and_materializes_its_boss_room_without_fatigue()
    {
        Skip.If(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PVF_ARCHIVE_PATH")), "Real PVF required.");
        await using var w=await MarriedAsync(); var f=w.F;
        f.Services.GetRequiredService<IIndependentDropRuleService>().Initialize();
        f.Services.GetRequiredService<IExtraDropRuleService>().Initialize();
        f.Services.GetRequiredService<IMonsterDeclaredDropRuleService>().Initialize();
        f.Services.GetRequiredService<IWorldDropRuleService>().Initialize();
        var parties=f.Services.GetRequiredService<Dfo.Server.Abstractions.Parties.IPartyService>();
        var scope=new Dfo.Server.Data.Parties.PartyDirectoryScope(1,2,0);
        var first=new Dfo.Server.Data.Parties.PartyCharacterSnapshot(f.Session.AccountId,f.Session.CharacterId,1,scope);
        var second=new Dfo.Server.Data.Parties.PartyCharacterSnapshot(w.Peer.AccountId,w.Peer.CharacterId,2,scope);
        Assert.True(parties.RequestInvite(first,second,new(0,0,0,0)).Succeeded);
        Assert.True(parties.AcceptInvite(second,first).Succeeded);
        var delivery=f.Services.GetRequiredService<Dfo.Server.Abstractions.Inventory.IItemDeliveryDomainService>();
        var containers=f.Services.GetRequiredService<Dfo.Server.Abstractions.Inventory.ICharacterContainerDomainService>();
        async ValueTask<int> Tickets(Dfo.Server.Data.Characters.CharacterId c) => (await containers.GetContainer(c,Dfo.Server.Data.Inventory.InventoryContainerType.Main).GetAllAsync(default)).Where(x=>x.ItemId==10002609).Sum(x=>x.Snapshot.Count);
        await InTx(f,()=>delivery.DeliverAsync(f.Session.AccountId,f.Session.CharacterId,[new(10002609,3)],"entry-test",default,Dfo.Server.Abstractions.Inventory.ItemDeliveryMode.InventoryOnly));
        var context=new DungeonEntryContext(3541,new((Dfo.Server.Data.World.DungeonDifficulty)2,[],[]),0,0,
            [new(f.Session.AccountId,f.Session.CharacterId,0,true),new(w.Peer.AccountId,w.Peer.CharacterId,1,false)]);
        var service=f.Services.GetRequiredService<ICoupleDungeonDomainService>();
        var refused=await InTx(f,()=>service.EnterAsync(context,default));
        Assert.Equal(DungeonEntryFailure.MissingRequiredItem,refused.Failure);Assert.Equal(3,await Tickets(f.Session.CharacterId));
        await InTx(f,()=>delivery.DeliverAsync(w.Peer.AccountId,w.Peer.CharacterId,[new(10002609,3)],"entry-test",default,Dfo.Server.Abstractions.Inventory.ItemDeliveryMode.InventoryOnly));
        var fatigue=f.Services.GetRequiredService<Dfo.Server.Abstractions.Characters.ICharacterFatigueDomainService>();
        var before=await fatigue.BalanceOfAsync(f.Session.CharacterId,default);
        var entered=await InTx(f,()=>service.EnterAsync(context,default));
        Assert.NotNull(entered.Entry);Assert.Equal(2,entered.Entry.Visits.Count);Assert.Equal(entered.Entry.Layout.StartCell,entered.Entry.Layout.BossCell);
        Assert.Contains(entered.Entry.Visits[f.Session.CharacterId].Actors,x=>x.Actor.Code==64762);
        Assert.Equal(0,await Tickets(f.Session.CharacterId));Assert.Equal(0,await Tickets(w.Peer.CharacterId));
        Assert.Equal(before,await fatigue.BalanceOfAsync(f.Session.CharacterId,default));
    }
    internal sealed class CapsuleContent : IMarriageCapsuleContentSource
    { public MarriageCapsuleRules Load() => new(10, 10, 2, 10, 10002910); }
    private sealed class CapsuleDungeon : IDungeonDefinitionSource
    {
        public ValueTask<DungeonDefinition?> GetAsync(int id, CancellationToken ct) => ValueTask.FromResult<DungeonDefinition?>(
            new(id, "test", 30, false, [], 50) { RecommendedLevels = [30, 85] });
    }
    [Fact]
    public async Task Capsules_are_atomic_personal_and_gifting_does_not_mix_love_points()
    {
        await using var w = await MarriedAsync(); var f = w.F;
        var query = f.Services.GetRequiredService<IMarriageCapsuleQueryDomainService>();
        var service = f.Services.GetRequiredService<IMarriageCapsuleDomainService>();
        var relation = (await f.Services.GetRequiredService<IMarriageRelations>().ForSessionAsync(f.Session, default))!;
        var accounts = new[] { f.Session.AccountId, w.Peer.AccountId }.OrderBy(x => x.Value).ToArray();
        async ValueTask<MarriageCapsuleResult> ActCaps(MarriageCapsuleAction a, bool commit = true) =>
            await InTx(f, () => service.CapsuleAsync(f.Session, a, accounts, default), commit);
        Assert.Equal(-4, (await ActCaps(MarriageCapsuleAction.Make)).Status);
        f.Sql($"INSERT INTO marriage_capsules VALUES('{relation.Id:N}',71,10,0,0)");
        Assert.Equal(0, (await ActCaps(MarriageCapsuleAction.Make, false)).Status);
        Assert.Equal(new(10, 0, 0), (await query.ReadAsync(f.Session.AccountId, f.Session.CharacterId, default)).State);
        Assert.Equal(0, (await ActCaps(MarriageCapsuleAction.Make)).Status);
        Assert.Equal(new(0, 1, 0), (await query.ReadAsync(f.Session.AccountId, f.Session.CharacterId, default)).State);
        Assert.Equal(-1, (await InTx(f, () => service.CapsuleAsync(f.Session, MarriageCapsuleAction.Give, [f.Session.AccountId], default))).Status);
        Assert.Equal(0, (await ActCaps(MarriageCapsuleAction.Give)).Status);
        Assert.Equal(-4, (await ActCaps(MarriageCapsuleAction.Give)).Status);
        Assert.True((await query.ReadAsync(f.Session.AccountId, f.Session.CharacterId, default)).AwaitingPartner);
        Assert.Equal((byte)1, (await query.ReadAsync(w.Peer.AccountId, w.Peer.CharacterId, default)).State.Pending);
        var room = await Read(w); Enter(w, w.Peer, room.Instance);
        var take = await InTx(f, () => service.CapsuleAsync(w.Peer, MarriageCapsuleAction.Take, accounts, default));
        Assert.Equal(0, take.Status); Assert.Single(take.Items); Assert.Equal(10002910, take.Items[0].Snapshot.ItemId);
        Assert.Equal((byte)0, (await query.ReadAsync(w.Peer.AccountId, w.Peer.CharacterId, default)).State.Pending);
        Assert.False((await query.ReadAsync(f.Session.AccountId, f.Session.CharacterId, default)).AwaitingPartner);
        Assert.Equal(1, await f.Services.GetRequiredService<IMarriageHousingQueryDomainService>().PointsAsync(f.Session.AccountId, f.Session.CharacterId, default));
        f.Sql("UPDATE marriage_capsules SET energy=10,owned=10 WHERE character_id=71; UPDATE marriage_capsules SET pending=10 WHERE character_id=72");
        Assert.Equal(-5, (await ActCaps(MarriageCapsuleAction.Make)).Status);
        Assert.Equal(-6, (await ActCaps(MarriageCapsuleAction.Give)).Status);
    }
    [Fact]
    public async Task Capsule_clear_progress_is_recommended_boss_only_personal_capped_and_deduplicated_per_run()
    {
        await using var w = await MarriedAsync(); var f = w.F;
        var subscriber = ActivatorUtilities.CreateInstance<CapsuleClearSubscriber>(f.Services, new CapsuleDungeon());
        var q = f.Services.GetRequiredService<IMarriageCapsuleQueryDomainService>();
        var run = DungeonRunId.New();
        DungeonMapCleared Clear(DungeonRunId id, bool boss = true, int dungeon = 3541) => new(id, dungeon, 0, new(0,0), 1, 1,
            w.Clock.Now, [new(f.Session.AccountId, f.Session.CharacterId, 0, true)]) { IsBossRoom = boss };
        async ValueTask Send(DungeonMapCleared c, bool commit = true)
        { await using var tx = f.Scope.Begin(); await subscriber.HandleAsync(new(Guid.NewGuid(), Guid.NewGuid(), w.Clock.Now, Guid.Empty, c), default); if (commit) await tx.CommitAsync(default); }
        await Send(Clear(run, false)); Assert.Equal(0, (await q.ReadAsync(f.Session.AccountId, f.Session.CharacterId, default)).State.Energy);
        await Send(Clear(run), false); Assert.Equal(0, (await q.ReadAsync(f.Session.AccountId, f.Session.CharacterId, default)).State.Energy);
        await Send(Clear(run)); await Send(Clear(run)); Assert.Equal(2, (await q.ReadAsync(f.Session.AccountId, f.Session.CharacterId, default)).State.Energy);
        for (var i=0;i<6;i++) await Send(Clear(DungeonRunId.New()));
        Assert.Equal(10, (await q.ReadAsync(f.Session.AccountId, f.Session.CharacterId, default)).State.Energy);
        var saturatedRun = DungeonRunId.New(); await Send(Clear(saturatedRun));
        var accounts = new[] { f.Session.AccountId,w.Peer.AccountId }.OrderBy(x=>x.Value).ToArray();
        Assert.Equal(0,(await InTx(f,()=>f.Services.GetRequiredService<IMarriageCapsuleDomainService>().CapsuleAsync(f.Session,MarriageCapsuleAction.Make,accounts,default))).Status);
        await Send(Clear(saturatedRun)); Assert.Equal(0,(await q.ReadAsync(f.Session.AccountId,f.Session.CharacterId,default)).State.Energy);
        Assert.Equal(0, (await q.ReadAsync(w.Peer.AccountId, w.Peer.CharacterId, default)).State.Energy);
        Assert.False(CapsuleClearSubscriber.Recommended(new(1,"test",30,true,[],50) {RecommendedLevels=[30,85]}, 29));
        Assert.False(CapsuleClearSubscriber.Recommended(new(1,"test",30,true,[],50) {RecommendedLevels=[30,85]}, 86));
        Assert.True(CapsuleClearSubscriber.Recommended(new(1,"test",30,true,[],50) {RecommendedLevels=[30,85]}, 85));
    }
    [Fact]
    public async Task Couple_dungeon_requires_the_exact_married_pair_and_refuses_other_modes_before_admission()
    {
        await using var w = await MarriedAsync(); var f = w.F;
        var rule = f.Services.GetRequiredService<CoupleDungeonRule>();
        var context = new DungeonEntryContext(3541, new((Dfo.Server.Data.World.DungeonDifficulty)2, [], []), 0, 0,
            [new(f.Session.AccountId,f.Session.CharacterId,0,true), new(w.Peer.AccountId,w.Peer.CharacterId,1,false)]);
        Assert.True(await rule.EligibleAsync(context, default));
        Assert.True(await rule.EligibleAsync(context with { Facts=context.Facts with { Intent=DungeonEntryIntent.Directed(3541) } }, default));
        foreach (var invalid in new[] { context with { PracticeMode=1 },context with {RequestMode=1},context with {Participants=[context.Participants[0]]},
            context with {Participants=[context.Participants[0],context.Participants[1] with {CharacterId=new(999)}]},
            context with {Participants=[context.Participants[0],context.Participants[1] with {AccountId=new(999)}]} })
        {
            Assert.False(await rule.EligibleAsync(invalid, default));
            await using var tx = f.Scope.Begin();
            var refused = await f.Services.GetRequiredService<IDungeonAdmissionDomainService>().PrepareAsync(invalid, new(), default);
            Assert.Null(refused.Batch); Assert.Equal(DungeonEntryFailure.MissingPrerequisite, refused.Refusal!.Failure);
        }
    }
}
