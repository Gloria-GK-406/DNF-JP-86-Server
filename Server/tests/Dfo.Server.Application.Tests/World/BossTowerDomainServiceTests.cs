using System.Collections.Immutable;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Events;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Quests;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Abstractions.World.BossTower;
using Dfo.Server.Abstractions.World.IllusionTower;
using Dfo.Server.Application.Tests.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Application.World;
using Dfo.Server.Application.World.BossTower;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.World;

public sealed class BossTowerDomainServiceTests
{
    [Fact]
    public async Task Autoloot_is_in_the_receiving_members_inventory_projection_and_never_repaid()
    {
        using var f = Fixture(drops: new Drops([new(2749094, 1, 0, DropItemKind.Stackable)]));
        f.SetLevel(65);
        await f.Seed(65, 2749110, 3);
        var tower = Tower(f);
        var run = (await Enter(f)).Run!;
        var context = Context(run, f.Session);
        await tower.LoadAsync(context, default);
        await tower.StartAsync(context, default);
        var killed = await tower.KillAsync(context, run.Room.Actors[0].Uid, default);
        Assert.Equal(1, f.Count(2749094));
        var member = Assert.Single(killed.KillRewards);
        Assert.Contains(member.InventoryUpdates, x => x.Item.ItemId == 2749094);
        Assert.Empty(killed.Drops);
        Assert.False((await tower.KillAsync(context, run.Room.Actors[0].Uid, default)).Changed);
        Assert.Equal(1, f.Count(2749094));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Eight_rooms_wait_for_legal_move_and_settle_once_using_current_stage(bool nativeSentinel)
    {
        using var f = Fixture();
        f.SetLevel(65);
        await f.Seed(65, 2749110, 3);
        var tower = Tower(f);
        var run = (await Enter(f)).Run!;
        Assert.Equal(2, f.Count(2749110));
        for (var stage = 1; stage <= 8; stage++)
        {
            Assert.Equal(stage, run.CurrentStage);
            var ctx = Context(run, f.Session);
            var targetX = nativeSentinel ? byte.MaxValue : (byte)((stage + 1) % 2);
            var targetY = nativeSentinel ? byte.MaxValue : (byte)0;
            Assert.False((await tower.NextLevelAsync(ctx, targetX, targetY, default)).Changed);
            await tower.LoadAsync(ctx, default);
            await tower.StartAsync(ctx, default);
            var killed = await tower.KillAsync(ctx, run.Room.Actors[0].Uid, default);
            run = killed.Run!;
            Assert.Equal(stage, run.CurrentStage);
            Assert.Equal(stage, run.LastClearedStage);
            Assert.False((await tower.KillAsync(ctx, run.Room.Actors[0].Uid, default)).Changed);
            if (stage < 8)
            {
                Assert.Null(run.Completion);
                Assert.False((await tower.NextLevelAsync(ctx, (byte)(stage % 2), 0, default)).Changed);
                Assert.False((await tower.NextLevelAsync(ctx, byte.MaxValue, 0, default)).Changed);
                Assert.False((await tower.NextLevelAsync(ctx, 0, byte.MaxValue, default)).Changed);
                var advanced = await tower.NextLevelAsync(ctx, targetX, targetY, default);
                Assert.True(advanced.Changed);
                run = advanced.Run!;
                Assert.False((await tower.NextLevelAsync(ctx, targetX, targetY, default)).Changed);
                Assert.False((await tower.KillAsync(ctx, run.Room.Actors[0].Uid, default)).Changed);
            }
        }
        var completion = run.Completion!;
        Assert.True(completion.Settled);
        Assert.Equal(8, completion.RewardLevel);
        Assert.Equal(10, Assert.Single(completion.Receipt!.Members).Candidates.Length);
        var balance = f.Gold;
        var exp = f.Scalar("SELECT exp FROM characters WHERE character_id=1");
        Assert.False((await tower.DieAsync(Context(run, f.Session), default)).Changed);
        Assert.Equal(balance, f.Gold);
        Assert.Equal(exp, f.Scalar("SELECT exp FROM characters WHERE character_id=1"));
    }

    [Fact]
    public async Task Current_floor_defeat_and_result_selection_preserve_ticket_until_new_admission()
    {
        using var f = Fixture(); f.SetLevel(65); await f.Seed(65, 2749110, 3);
        var tower = Tower(f);
        var run = (await Enter(f)).Run!; var ctx = Context(run, f.Session);
        await tower.LoadAsync(ctx, default); await tower.StartAsync(ctx, default);
        await tower.KillAsync(ctx, run.Room.Actors[0].Uid, default);
        run = (await tower.NextLevelAsync(ctx, 0, 0, default)).Run!; ctx = Context(run, f.Session);
        await tower.LoadAsync(ctx, default); await tower.StartAsync(ctx, default);
        run = (await tower.DieAsync(ctx, default)).Run!;
        Assert.Equal(2, run.Completion!.RewardLevel); Assert.Equal(1, run.LastClearedStage);
        Assert.True(await tower.CanRetryAsync(ctx, default));
        Assert.True((await tower.SelectResultAsync(ctx, 0, default)).Changed);
        Assert.Equal(2, f.Count(2749110));
        Assert.False((await tower.ReleaseForSelectionAsync(ctx, 0, default)).Changed);
        Assert.NotNull(tower.Find(f.Session));
        Assert.True((await tower.ReleaseForSelectionAsync(ctx, 3505, default)).Changed);
        Assert.Null(tower.Find(f.Session));
        Assert.NotNull((await Enter(f)).Run); Assert.Equal(1, f.Count(2749110));
        Assert.False((await tower.LeaveAsync(ctx, false, default)).Changed);
        Assert.NotNull(tower.Find(f.Session));
    }

    [Fact]
    public async Task Outer_abort_restores_debits_occupancy_actor_experience_and_assets()
    {
        using var f = Fixture(drops: new Drops([new(2749094, 1, 0, DropItemKind.Stackable)]));
        f.SetLevel(65); await f.Seed(65, 2749110, 3);
        await using (Scope(f).Begin()) Assert.NotNull((await Enter(f)).Run);
        Assert.Equal(3, f.Count(2749110)); Assert.Null(Tower(f).Find(f.Session));
        Assert.Null(f.Services.GetRequiredService<IDungeonParticipationDomainService>().Find(f.Session.CharacterId));
        var tower = Tower(f); var run = (await Enter(f)).Run!; var ctx = Context(run, f.Session);
        await tower.LoadAsync(ctx, default); await tower.StartAsync(ctx, default);
        var exp = f.Scalar("SELECT exp FROM characters WHERE character_id=1");
        await using (Scope(f).Begin()) Assert.True((await tower.KillAsync(ctx, run.Room.Actors[0].Uid, default)).Changed);
        Assert.False(tower.Find(f.Session)!.Room.Actors[0].Dead); Assert.Equal(0, f.Count(2749094));
        Assert.Equal(exp, f.Scalar("SELECT exp FROM characters WHERE character_id=1"));
    }

    [Fact]
    public async Task Disabled_activity_and_missing_ticket_do_not_create_run_or_charge()
    {
        using var f = Fixture(); f.SetLevel(65);
        Assert.Null((await Enter(f)).Run);
        await f.Seed(65, 2749110, 3);
        await using (var tx = Scope(f).Begin())
        {
            await f.Services.GetRequiredService<IEventManagementDomainService>().ChangeAsync(new(2329, 0, "disable"), "test", f.Clock.GetUtcNow(), default);
            await tx.CommitAsync(default);
        }
        Assert.Null((await Enter(f)).Run); Assert.Equal(3, f.Count(2749110));
        Assert.Null(Tower(f).Find(f.Session));
    }

    [Fact]
    public async Task Loading_deadlines_are_per_room_and_no_battle_deadline_is_invented()
    {
        using var f = Fixture(); f.SetLevel(65); await f.Seed(65, 2749110, 3);
        var tower = Tower(f); var run = (await Enter(f)).Run!;
        f.Clock.Advance(TimeSpan.FromSeconds(121)); Assert.Single(tower.FindReturnsDue(f.Clock.GetUtcNow()));
        var ctx = Context(run, f.Session); await tower.LoadAsync(ctx, default); await tower.StartAsync(ctx, default);
        f.Clock.Advance(TimeSpan.FromHours(1)); Assert.Empty(tower.FindReturnsDue(f.Clock.GetUtcNow()));
        await tower.KillAsync(ctx, run.Room.Actors[0].Uid, default);
        run = (await tower.NextLevelAsync(ctx, 0, 0, default)).Run!;
        Assert.Empty(tower.FindReturnsDue(f.Clock.GetUtcNow()));
        Assert.Equal(f.Clock.GetUtcNow() + TimeSpan.FromSeconds(120), run.LoadDueAt);
    }

    [Fact]
    public async Task Party_ticket_preflight_is_all_or_nothing_and_departure_changes_leader_and_visibility()
    {
        using var f = Fixture(); f.SetLevel(65); await f.Seed(65, 2749110, 3);
        var peer = await AddMember(f, 0);
        var tower = Tower(f);
        Assert.Null((await tower.EnterAsync([new(f.Session, 0), new(peer, 1)], f.Session, 42, default)).Run);
        Assert.Equal(3, f.Count(2749110)); Assert.Null(tower.Find(peer));
        await SeedPeer(f, peer, 2);
        var entered = await tower.EnterAsync([new(f.Session, 0), new(peer, 1)], f.Session, 42, default);
        var run = entered.Run!; Assert.NotNull(run); Assert.Equal(2, f.Count(2749110));
        Assert.Equal(2, entered.InventoryChanges.Count);
        var visibility = f.Services.GetRequiredService<ICharacterVisibleSessionDomainService>().Resolve(f.Session);
        Assert.Contains(peer, visibility);
        var ctx = Context(run, f.Session);
        await tower.LoadAsync(ctx, default);
        await tower.LoadAsync(Context(run, peer), default);
        await tower.StartAsync(ctx, default);
        await tower.KillAsync(ctx, run.Room.Actors[0].Uid, default);
        Assert.False((await tower.NextLevelAsync(Context(run, peer), byte.MaxValue, byte.MaxValue, default)).Changed);
        Assert.Equal(1, tower.Find(f.Session)!.CurrentStage);
        var left = await tower.LeaveAsync(Context(run, f.Session), false, default);
        Assert.Null(tower.Find(f.Session)); Assert.True(Assert.Single(left.Run!.Members).IsLeader);
        Assert.Equal(peer, Assert.Single(left.Run.Members).Session);
        Assert.Null(left.Run.Completion); Assert.Equal(2, f.Count(2749110));
        Assert.Null(f.Services.GetRequiredService<IDungeonParticipationDomainService>().Find(f.Session.CharacterId));
    }

    [Fact]
    public async Task Npc_exchange_requires_the_current_maps_real_npc_and_cannot_open_other_shops()
    {
        var rules = new Rules(); using var f = Fixture(rules); f.SetLevel(65); await f.Seed(65, 2749110, 3);
        f.Content.Shops[1010] = new(1010, 1118, [new(null, [2000])]);
        await f.Seed(66, 1000, 10);
        var tower = Tower(f); var run = (await Enter(f)).Run!; var ctx = Context(run, f.Session);
        Assert.False(tower.CanShop(f.Session, 1010, 1118));
        await tower.LoadAsync(ctx, default); await tower.StartAsync(ctx, default);
        Assert.True(tower.CanShop(f.Session, 1010, 1118));
        var shop = f.Services.GetRequiredService<INpcShopDomainService>();
        await using (var tx = Scope(f).Begin())
        {
            var denied = await shop.PurchaseAsync(f.Session, 2000, 1, 15, 2, f.Clock.GetUtcNow(), default);
            Assert.Equal(NpcShopRejection.ShopUnavailable, denied.Rejection);
            var purchased = await shop.PurchaseAsync(f.Session, 2000, 1, 1118, 1010, f.Clock.GetUtcNow(), default);
            Assert.Equal(NpcShopRejection.None, purchased.Rejection);
            await tx.CommitAsync(default);
        }
        Assert.Equal(9, f.Count(1000)); Assert.Equal(1, f.Count(2000));
        await tower.DieAsync(ctx, default); Assert.False(tower.CanShop(f.Session, 1010, 1118));
    }

    [Fact]
    public async Task Apc_doll_is_refused_without_consumption_but_ordinary_consumable_still_works()
    {
        using var f = Fixture(configure: s => s.AddSingleton<IItemUseRuleDefinitionSource, ItemUseRules>());
        f.SetLevel(65); await f.Seed(65, 2749110, 3); await f.Seed(66, 2000, 2); await f.Seed(67, 2001, 2);
        Assert.NotNull((await Enter(f)).Run);
        var effect = f.Services.GetRequiredService<IItemUseEffectDomainService>();
        var bag = f.Services.GetRequiredService<ICharacterBusinessInventoryDomainService>()
            .GetStore(f.Session.CharacterId, BusinessInventoryStoreKind.Item).GetContainer(BusinessInventorySpace.Consumable);
        await using (var tx = Scope(f).Begin())
        {
            var denied = await effect.UseAsync(f.Session, new(BusinessInventorySpace.Consumable, 1), (await bag.GetAsync(1, default))!, default);
            Assert.Equal(ItemUsability.PlaceNotAllowed, denied.Status);
            var allowed = await effect.UseAsync(f.Session, new(BusinessInventorySpace.Consumable, 2), (await bag.GetAsync(2, default))!, default);
            Assert.Equal(ItemUsability.Allowed, allowed.Status);
            await tx.CommitAsync(default);
        }
        Assert.Equal(2, f.Count(2000)); Assert.Equal(1, f.Count(2001));
    }

    [Fact]
    public async Task Replacement_session_cannot_mutate_old_run_and_exact_old_cleanup_can_release_it()
    {
        using var f = Fixture(); f.SetLevel(65); await f.Seed(65, 2749110, 3);
        var tower = Tower(f); var original = f.Session; var run = (await Enter(f)).Run!;
        f.Select(2);
        Assert.Null(tower.Find(f.Session));
        Assert.False((await tower.LoadAsync(Context(run, original), default)).Changed);
        Assert.True((await tower.LeaveAsync(Context(run, original), true, default)).Changed);
        Assert.Null(tower.Find(original));
        Assert.Null(f.Services.GetRequiredService<IDungeonParticipationDomainService>().Find(original.CharacterId));
    }

    [Fact]
    public async Task Result_leadership_transfer_preserves_receipt_and_gives_only_the_new_leader_selection_authority()
    {
        using var f = Fixture(); f.SetLevel(65); await f.Seed(65, 2749110, 3);
        var peer = await AddMember(f, 3); var tower = Tower(f);
        var run = (await tower.EnterAsync([new(f.Session, 0), new(peer, 1)], f.Session, 42, default)).Run!;
        await tower.LoadAsync(Context(run, f.Session), default);
        await tower.LoadAsync(Context(run, peer), default);
        await tower.StartAsync(Context(run, f.Session), default);
        await tower.DieAsync(Context(run, f.Session), default);
        run = (await tower.DieAsync(Context(run, peer), default)).Run!;
        var receipt = run.Completion!.Receipt;
        var balance = f.Gold;

        run = (await tower.SynchronizeLeaderAsync(Context(run, f.Session), peer, default)).Run!;
        Assert.Equal(peer, run.Members.Single(value => value.IsLeader).Session);
        Assert.Same(receipt, run.Completion!.Receipt);
        Assert.Equal(balance, f.Gold);
        Assert.False(await tower.CanRetryAsync(Context(run, f.Session), default));
        Assert.True(await tower.CanRetryAsync(Context(run, peer), default));
        Assert.False((await tower.SelectResultAsync(Context(run, f.Session), 0, default)).Changed);
        Assert.True((await tower.SelectResultAsync(Context(run, peer), 0, default)).Changed);
        Assert.False((await tower.ReleaseForSelectionAsync(Context(run, f.Session), 3505, default)).Changed);
        Assert.True((await tower.ReleaseForSelectionAsync(Context(run, peer), 3505, default)).Changed);
    }

    private static async Task<SelectedSessionIdentity> AddMember(ShopFixture f, int tickets)
    {
        f.Sql($"INSERT INTO accounts(account_id,m_id) VALUES(2,'boss2'); INSERT INTO characters(character_id,account_id,name,job,level,exp,slot_index) VALUES(3,2,'boss3',12,65,{f.ExperienceForLevel(65)},0); INSERT INTO character_stats(character_id,carried_gold) VALUES(3,100);");
        var registry = f.Services.GetRequiredService<Dfo.Server.Application.Users.OnlineStateRegistry>();
        var connection = new Dfo.Server.Data.Networking.NetworkConnectionContext(new("boss-peer"), Dfo.Server.Data.Networking.NetworkEndpointKind.Game, 10011);
        var user = registry.RegisterConnection(connection).OnlineUserId; registry.Authenticate(user, new(2));
        var session = SelectedSessionIdentity.Create(new(2), new(3), connection.ConnectionId, registry.GetRequiredUser(user).Generation, 10011);
        registry.Select(session);
        registry.EnterWorld(session);
        f.Services.GetRequiredService<ICharacterContainerDomainService>().Register(session.CharacterId,
            new Dfo.Server.Application.Inventory.PersistentItemContainer(ContainerId.ForCharacter(session.CharacterId, InventoryContainerType.Main), new(InventoryContainerType.Main,0),
                f.Services.GetRequiredService<IItemDomainStore>(), f.Services.GetRequiredService<IDomainTransactionAccessor>(),
                f.Services.GetRequiredService<IItemPlacementRuleService>(), f.Services.GetRequiredService<IItemSlotRuleService>()));
        if (tickets > 0) await SeedPeer(f, session, tickets);
        return session;
    }
    private static async Task SeedPeer(ShopFixture f, SelectedSessionIdentity peer, int count)
    {
        await using var tx = Scope(f).Begin();
        var storage = f.Services.GetRequiredService<IItemDomainStore>().SessionIn(Scope(f).Current!);
        await storage.PutAsync(ContainerId.ForCharacter(peer.CharacterId, InventoryContainerType.Main), 65,
            await storage.CreateAsync(ItemSnapshot.FromCore(StackableItemCore.Create(2,2749110,count),true),default),default);
        await tx.CommitAsync(default);
    }
    private sealed class ItemUseRules : IItemUseRuleDefinitionSource
    {
        public ValueTask<ItemUseRule?> GetAsync(int id, CancellationToken ct) => ValueTask.FromResult<ItemUseRule?>(new(true,0,0,1,
            ItemJobRestriction.Any,ItemPlaceRestriction.Anywhere) { SummonsApc=id==2000 });
    }

    [Fact]
    public async Task Full_equipment_bag_delivers_each_frozen_candidate_to_inventory_or_mail_once()
    {
        var content = new TowerRewardTestContent(); content.Table = content.Table with { ItemTypeRates = [0,10000,0,0] };
        using var f = Fixture(new Rules {Probabilities=[0,1000,0], CardCount=10}, configure: s=>TowerRewardTestContent.Register(s,content));
        f.SetLevel(65); await f.Seed(65,2749110,3);
        var tower=Tower(f); var run=(await Enter(f)).Run!; var ctx=Context(run,f.Session);
        await tower.LoadAsync(ctx,default); await tower.StartAsync(ctx,default);
        var result=await tower.DieAsync(ctx,default);
        var receipt=Assert.Single(result.Run!.Completion!.Receipt!.Members);
        Assert.Equal(10,receipt.Candidates.Length); Assert.All(receipt.Candidates,x=>Assert.True(x.Delivered));
        Assert.All(receipt.Candidates,x=>Assert.Equal(3000,x.ItemId));
        var inBag=f.Count(3000); var inMail=f.Scalar("SELECT COUNT(*) FROM mailbox_attachments WHERE claimed_flag=0");
        Assert.Equal(10,inBag+inMail); Assert.True(inMail>0); Assert.All(receipt.InventoryUpdates,x=>Assert.Equal(35,x.Item.Durability));
        Assert.False((await tower.DieAsync(ctx,default)).Changed);
        Assert.Equal(inMail,f.Scalar("SELECT COUNT(*) FROM mailbox_attachments WHERE claimed_flag=0"));
    }

    [Fact]
    public async Task Settlement_failure_rolls_back_wallet_experience_dead_state_and_can_retry_once()
    {
        using var f=Fixture(new Rules {Probabilities=[1000,0,0]}); f.SetLevel(65); await f.Seed(65,2749110,3);
        var tower=Tower(f); var run=(await Enter(f)).Run!; var ctx=Context(run,f.Session);
        await tower.LoadAsync(ctx,default); await tower.StartAsync(ctx,default);
        f.Sql("CREATE TRIGGER fail_boss_exp BEFORE UPDATE OF exp ON characters BEGIN SELECT RAISE(ABORT,'xp failure'); END;");
        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(async()=>await tower.DieAsync(ctx,default));
        Assert.Null(tower.Find(f.Session)!.Completion); Assert.False(tower.Find(f.Session)!.Members[0].Dead);
        Assert.Equal(100000,f.Gold); Assert.Equal(2,f.Count(2749110));
        f.Sql("DROP TRIGGER fail_boss_exp;");
        var settled=await tower.DieAsync(ctx,default); Assert.True(settled.SettlementCreated);
        Assert.Equal(4224,Assert.Single(settled.Run!.Completion!.Receipt!.Members).Gold);
        Assert.Equal(104224,f.Gold); Assert.False((await tower.DieAsync(ctx,default)).Changed); Assert.Equal(104224,f.Gold);
    }

    internal static ShopFixture Fixture(Rules? rules = null, Drops? drops = null, Action<IServiceCollection>? configure = null)
    {
        rules ??= new();
        return new ShopFixture(s =>
        {
            s.AddSingleton<IBossTowerRuleService>(rules);
            s.AddSingleton<IDungeonAdmissionRuleService>(rules);
            s.AddSingleton<IDungeonDifficultyRuleService>(new Support.TestDungeonDifficultyRules());
            s.AddSingleton<IIndependentDropRuleService>(drops ?? new Drops([]));
            s.AddSingleton<IQuestEnemyDropDomainService, NoQuestDrops>();
            s.AddSingleton<IQuestContentRuleService>(new Quests.OrdinaryQuestEffectTests.Rules());
            s.AddSingleton<IExperienceBonusDomainService, NoBonuses>();
            s.AddSingleton<IItemPlacementDefinitionSource, Placements>();
            TowerRewardTestContent.Register(s);
            configure?.Invoke(s);
        });
    }
    private static IBossTowerDomainService Tower(ShopFixture f) => f.Services.GetRequiredService<IBossTowerDomainService>();
    private static DomainTransactionScope Scope(ShopFixture f) => (DomainTransactionScope)f.Services.GetRequiredService<IDomainTransactionAccessor>();
    private static ValueTask<BossTowerEntryResult> Enter(ShopFixture f) => Tower(f).EnterAsync([new(f.Session, 0)], f.Session, 42, default);
    private static BossTowerActionContext Context(BossTowerSnapshot run, SelectedSessionIdentity session) => new(session, run.RunId, run.Room.Version);
    internal sealed class Rules : IBossTowerRuleService, IDungeonAdmissionRuleService
    {
        public TimeSpan LoadingTimeout => TimeSpan.FromSeconds(120);
        public TimeSpan ResultTimeout => TimeSpan.FromSeconds(20);
        public bool CanTrade(int npcId, int shopId) => npcId == 1010 && shopId == 1118;
        public bool IsNextStageTarget(int currentStage, byte targetX, byte targetY) =>
            new BossTowerRuleService(null!).IsNextStageTarget(currentStage, targetX, targetY);
        public int EventId => 2329; public int TicketItemId => 2749110; public int PartyLimit => 4;
        public Dfo.Server.Data.Events.EventConfiguration InitialActivity => new BossTowerRuleService(null!).InitialActivity;
        public bool Supports(int dgn) => dgn == 3505;
        public int[] Probabilities { get; set; } = [0, 0, 100];
        public bool HasNpc { get; set; } = true;
        public int? CardCount { get; set; }
        public ValueTask<BossTowerDefinition> DefinitionAsync(CancellationToken ct) => ValueTask.FromResult(new BossTowerDefinition(
            new(3505, "test", 60, false, [0], false, [new(2749110, 1, true)], [], true, 4), 70,
            Enumerable.Range(1, 8).Select(x => new BossTowerStageDefinition(x, [])).ToArray(),
            new(new double[] {1,1,2,2,3,3,4,4}, CardCount is { } n ? Enumerable.Repeat(n,8).ToArray() : new[] {1,2,3,4,6,8,9,10}, Probabilities, 11, 3, 1), 10)
            { AutoRootItemIds = new HashSet<int> {2749094} });
        public async ValueTask<DungeonAdmissionDefinition?> GetAsync(int dungeonId, CancellationToken ct) => (await DefinitionAsync(ct)).Admission;
        public ValueTask<BossTowerRoomSeed> CreateRoomAsync(int stage, uint seed, CancellationToken ct) => ValueTask.FromResult(
            new BossTowerRoomSeed(new(stage, 60000 + stage, seed, [new(0, 608, 70, 3, true, new(1,2,0))], []), HasNpc ? [1010] : []));
    }
    internal sealed class Drops(IReadOnlyList<RolledDrop> drops) : IIndependentDropRuleService
    {
        public int Initialize() => 1;
        public ValueTask<IReadOnlyList<RolledDrop>> DrawAsync(MonsterDropRequest request, CancellationToken ct, int growthBonusPercent = 0) => ValueTask.FromResult(drops);
    }
    private sealed class NoQuestDrops : IQuestEnemyDropDomainService
    {
        public ValueTask<IReadOnlyList<RolledDrop>> DrawAsync(QuestEnemyDropRequest request, MonsterDropParticipant participant, CancellationToken ct) => ValueTask.FromResult<IReadOnlyList<RolledDrop>>([]);
    }
    private sealed class NoBonuses : IExperienceBonusDomainService
    {
        public ValueTask<IReadOnlyList<ExperienceModifier>> CollectAsync(ExperienceOccasion occasion, CancellationToken ct) => ValueTask.FromResult<IReadOnlyList<ExperienceModifier>>([]);
    }
    private sealed class Placements : IItemPlacementDefinitionSource
    {
        public ValueTask<ItemPlacement?> GetAsync(int id, CancellationToken ct) => ValueTask.FromResult<ItemPlacement?>(
            id == 3000 ? new(1,1,new(BusinessInventorySpace.CarriedEquipment,0,1)) {WorldClass=ItemWorldClass.Equipment}
            : new(2,1000,new(BusinessInventorySpace.Consumable,0,56)));
    }
}
