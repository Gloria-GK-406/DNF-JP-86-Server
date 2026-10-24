using Dfo.Server.Application.Tests.Support;
using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Marriage;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.Marriage;
using Dfo.Server.Data.World;
using Microsoft.Extensions.DependencyInjection;
using Dfo.Server.Testing;
using static Dfo.Server.Application.Tests.Marriage.MarriageHarness;
using HousingWorld = Dfo.Server.Application.Tests.Marriage.MarriageHarness.World;

namespace Dfo.Server.Application.Tests.Marriage;

public sealed partial class MarriageHousingTests
{
    internal sealed class Content : IMarriageHousingContentSource, IItemPlacementDefinitionSource, INpcShopDefinitionSource
    {
        public MarriageHousingContent Load() => new(
            new Dictionary<int, HousingFurnitureRule> { [10002688] = new(10002688, 2, new HashSet<int> { 6, 7 }, true), [10002797] = new(10002797, 100, new HashSet<int> { 0 }, false) },
            new Dictionary<int, int> { [10002766] = 1 }, new Dictionary<int, int> { [10002765] = 10, [10002905] = 20 },
            [new(10002901, 1, 2)], [new(2, new Dictionary<string, int> { ["HP MAX"] = 100 })], [])
            { EntrySources = new HashSet<(int, int)> { (2, 0) } };
        public ValueTask<ItemPlacement?> GetAsync(int item, CancellationToken ct) => ValueTask.FromResult<ItemPlacement?>(new(2, 1000, new(BusinessInventorySpace.Consumable, 0, 55)));
        public ValueTask<NpcShopDefinition?> GetShopAsync(int npc, CancellationToken ct) => ValueTask.FromResult<NpcShopDefinition?>(null);
        public ValueTask<NpcShopItemDefinition?> GetItemAsync(int item, CancellationToken ct) => ValueTask.FromResult<NpcShopItemDefinition?>(new(item,
            item == 10002611 ? 500000 : item is 10002765 or 10002766 ? 10000 : null, 0,
            item == 10002688 ? [new(10002606, 2)] : item == 10002797 ? [new(10002606, 100)] : item == 10002611 ? [new(10002606, 50)] : [], null, 0, false, true));
    }
    private static async Task<HousingWorld> MarriedAsync()
    {
        var w = await ChurchTownAsync(configure: s =>
        {
            s.AddSingleton<IMarriageHousingContentSource, Content>(); s.AddSingleton<IItemPlacementDefinitionSource, Content>(); s.AddSingleton<INpcShopDefinitionSource, Content>();
            s.AddSingleton<IMarriageCapsuleContentSource, CapsuleContent>();
        });
        return await TestGameplay.RunAsync(w.F.Services, async () =>
        {
            var (ceremony, church) = await PreparedAsync(w);
            Arrive(w.F, w.F.Session, church); Arrive(w.F, w.Peer, church);
            w.Clock.Now = church.ReadyUntil;
            Assert.Equal(MarriageWeddingPhase.Completed, (await InTx(w.F, () => Venue(w.F).AdvanceAsync(ceremony.Id, default)))!.Wedding.Phase);
            // The current newlywed session must be able to enter before any re-selection/login initialization.
            Assert.True(await w.F.Services.GetRequiredService<IMarriageHousingQueryDomainService>().InstanceAsync(w.F.Session, default) > 0);
            var housing = w.F.Services.GetRequiredService<IMarriageHousingDomainService>();
            foreach (var actor in new[] { w.F.Session, w.Peer })
            {
                await using var tx = w.F.Scope.Begin(); await housing.InitializeAsync(actor.AccountId, actor.CharacterId, default); await tx.CommitAsync(default);
                w.F.Services.GetRequiredService<ICharacterContainerDomainService>().Register(actor.CharacterId, new PersistentItemContainer(
                    ContainerId.ForCharacter(actor.CharacterId, InventoryContainerType.Main), new(InventoryContainerType.Main, 0),
                    w.F.Services.GetRequiredService<IItemDomainStore>(), w.F.Services.GetRequiredService<IDomainTransactionAccessor>(),
                    w.F.Services.GetRequiredService<IItemPlacementRuleService>(), w.F.Services.GetRequiredService<IItemSlotRuleService>()));
            }
            return w;
        });
    }
    private static void Enter(HousingWorld w, SelectedSessionIdentity actor, int instance, int channel = 1)
    {
        TestGameplay.Run(w.F.Services, () =>
        {
            var position = w.F.Services.GetRequiredService<IOnlineCharacterRuntimeDomainService>().CrossInto(actor, 101, 0, 900, 250);
            w.F.Services.GetRequiredService<ITownAreaDomainService>().TransferExact(actor.CharacterId, actor.TownAreaLease(), new(channel, 101, 0, instance), position, true);
        });
    }
    private static async Task<HousingSnapshot> Read(HousingWorld w) => (await w.F.Services.GetRequiredService<IMarriageHousingQueryDomainService>().ReadAsync(w.F.Session.AccountId, w.F.Session.CharacterId, default))!;
    private static async Task<HousingResult> Act(HousingWorld w, SelectedSessionIdentity actor, HousingRequest request, bool commit = true) =>
        await InTx(w.F, () => TestEffectFootprints.Run(actor, footprint => w.F.Services.GetRequiredService<IMarriageHousingDomainService>().ExecuteAsync(actor, request, new[] { w.F.Session.AccountId, w.Peer.AccountId }.OrderBy(x => x.Value).ToArray(), footprint, default)), commit);
    private static async Task<int> Give(HousingWorld w, int item, int count)
    {
        await InTx(w.F, () => w.F.Services.GetRequiredService<IItemDeliveryDomainService>().DeliverAsync(w.F.Session.AccountId, w.F.Session.CharacterId, [new(item, count)], "housing-test", default, ItemDeliveryMode.InventoryOnly));
        var items = await w.F.Services.GetRequiredService<ICharacterContainerDomainService>().GetContainer(w.F.Session.CharacterId, InventoryContainerType.Main).GetAllAsync(default);
        return items.Single(x => x.ItemId == item).StorageSlotIndex;
    }

    [Fact]
    public async Task Daily_points_are_personal_capped_and_claimed_once_per_character_period()
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var w = await MarriedAsync(); var q = w.F.Services.GetRequiredService<IMarriageHousingQueryDomainService>();
            Assert.Equal(1, await q.PointsAsync(w.F.Session.AccountId, w.F.Session.CharacterId, default));
            Assert.Equal(1, await q.PointsAsync(w.Peer.AccountId, w.Peer.CharacterId, default));
            var m = w.F.Services.GetRequiredService<IMarriageHousingDomainService>();
            await using (var tx = w.F.Scope.Begin()) { await m.InitializeAsync(w.F.Session.AccountId, w.F.Session.CharacterId, default); await tx.CommitAsync(default); }
            Assert.Equal(1, await q.PointsAsync(w.F.Session.AccountId, w.F.Session.CharacterId, default));
            w.F.Sql("UPDATE marriage_personal_points SET points=999 WHERE character_id=71"); w.Clock.Now += TimeSpan.FromDays(1);
            await using (var tx = w.F.Scope.Begin()) { await m.InitializeAsync(w.F.Session.AccountId, w.F.Session.CharacterId, default); await tx.CommitAsync(default); }
            w.F.Sql("UPDATE marriage_personal_points SET points=998 WHERE character_id=71");
            await using (var tx = w.F.Scope.Begin()) { await m.InitializeAsync(w.F.Session.AccountId, w.F.Session.CharacterId, default); await tx.CommitAsync(default); }
            Assert.Equal(998, await q.PointsAsync(w.F.Session.AccountId, w.F.Session.CharacterId, default));
            Assert.Equal(1, await q.PointsAsync(w.Peer.AccountId, w.Peer.CharacterId, default));
        });
    }

    [Fact]
    public async Task Furniture_purchase_placement_recall_and_editor_generation_are_shared_and_transactional()
    {
        await using var w = await MarriedAsync(); var f = w.F; var room = await Read(w); Enter(w, f.Session, room.Instance); Enter(w, w.Peer, room.Instance);
        Assert.Equal(0, (await Act(w, f.Session, new(HousingAction.Storage))).Status);
        Assert.Equal(-4, (await Act(w, w.Peer, new(HousingAction.Storage))).Status);
        f.Sql("UPDATE marriage_personal_points SET points=20 WHERE character_id=71");
        var m = f.Services.GetRequiredService<IMarriageHousingDomainService>();
        async ValueTask<NpcShopPurchaseResult> Buy() => await m.PurchaseAsync(f.Session, 10002688, 2, 1206, [f.Session.AccountId, w.Peer.AccountId], default);
        Assert.NotNull((await InTx(f, Buy, false)).Receipt); Assert.Empty((await Read(w)).Storage);
        Assert.NotNull((await InTx(f, Buy)).Receipt);
        Assert.Equal(16, f.Scalar("SELECT points FROM marriage_personal_points WHERE character_id=71"));
        Assert.Equal(1, f.Scalar("SELECT points FROM marriage_personal_points WHERE character_id=72"));
        var stored = Assert.Single((await Read(w)).Storage); Assert.Equal(2, stored.Count);
        var query = f.Services.GetRequiredService<IMarriageHousingQueryDomainService>();
        Assert.Equal(0, (await query.BenefitsAsync(f.Session.AccountId, f.Session.CharacterId, false, default)).DecorationValue);
        var place = new HousingRequest(HousingAction.Move, Source: new(1, stored.Slot, stored.ItemId), Destination: new(0, 6, stored.ItemId));
        Assert.Equal(0, (await Act(w, f.Session, place)).Status);
        Assert.Single((await Read(w)).Placements); Assert.Equal(1, Assert.Single((await Read(w)).Storage).Count);
        Assert.Equal(2, (await query.BenefitsAsync(f.Session.AccountId, f.Session.CharacterId, false, default)).DecorationValue);
        Assert.Equal(100, (await query.BenefitsAsync(w.Peer.AccountId, w.Peer.CharacterId, false, default)).Modifiers["HP MAX"]);
        Assert.Empty((await query.BenefitsAsync(f.Session.AccountId, f.Session.CharacterId, true, default)).Modifiers);
        Assert.NotEqual(0, (await Act(w, w.Peer, place)).Status);
        var recall = new HousingRequest(HousingAction.Move, Source: new(0, 6, stored.ItemId), Destination: new(1, stored.Slot, stored.ItemId));
        Assert.Equal(0, (await Act(w, f.Session, recall)).Status); Assert.Empty((await Read(w)).Placements); Assert.Equal(2, Assert.Single((await Read(w)).Storage).Count);
        Assert.Equal(0, (await Act(w, f.Session, new(HousingAction.Storage, Count: 1))).Status);
        Assert.Equal(0, (await Act(w, w.Peer, new(HousingAction.Storage))).Status);
        TestGameplay.Run(f.Services, () => f.Services.GetRequiredService<IMarriageHousingSessionDomainService>().CloseExact(f.Session));
        Assert.Equal(-4, (await Act(w, f.Session, new(HousingAction.Storage))).Status);
    }

    [Fact]
    public async Task Aborted_editor_open_does_not_lock_out_the_spouse_and_house_position_never_overwrites_return()
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var w = await MarriedAsync(); var f = w.F; var room = await Read(w); Enter(w, f.Session, room.Instance); Enter(w, w.Peer, room.Instance);
            Assert.Equal(0, (await Act(w, f.Session, new(HousingAction.Storage), false)).Status);
            Assert.Equal(0, (await Act(w, w.Peer, new(HousingAction.Storage))).Status);
            var town = f.Scalar("SELECT town_id FROM characters WHERE character_id=71");
            var persistence = f.Services.GetRequiredService<ISelectedSessionPositionPersistence>();
            await persistence.PersistAsync(f.Session.AccountId, f.Session.CharacterId, new(101, 0, 300, 250, 5), default);
            Assert.Equal(town, f.Scalar("SELECT town_id FROM characters WHERE character_id=71"));
            var areas = f.Services.GetRequiredService<ITownAreaDomainService>();
            Assert.Null(areas.Find(new TownAreaKey(1, 101, 0)));
            var stranger = AddCharacter(f, 3, 73, 0);
            var position = new Dfo.Server.Data.Characters.CharacterPositionState(101, 0, 900, 250, 5);
            Assert.Null(areas.TransferExact(stranger.CharacterId, stranger.TownAreaLease(), new(1, 101, 0), position, true));
            Assert.DoesNotContain(areas.Find(new TownAreaKey(1, 101, 0, room.Instance))!.Members, x => x.CharacterId == stranger.CharacterId);
            Enter(w, w.Peer, room.Instance, channel: 2);
            Assert.Single(areas.Find(new TownAreaKey(1, 101, 0, room.Instance))!.Members);
            Assert.Single(areas.Find(new TownAreaKey(2, 101, 0, room.Instance))!.Members);
            Assert.Equal(room.Instance, (await f.Services.GetRequiredService<IMarriageHousingQueryDomainService>().ReadAsync(w.Peer.AccountId, w.Peer.CharacterId, default))!.Instance);
        });
    }

    [Fact]
    public async Task Two_married_pairs_in_the_same_native_room_have_separate_rosters_and_editor_leases()
    {
        await TestGameplay.RunAsync(async () =>
        {
            await using var w = await MarriedAsync(); var f = w.F;
            var first = await Read(w);
            var a = AddCharacter(f, 3, 73, 12); var b = AddCharacter(f, 4, 74, 0);
            await EligibleAsync(w, a, b);
            Assert.True((await InTx(f, () => Couple(f).RequestReservationAsync(a, b, 0, 0, default))).Succeeded);
            var ceremony = (await InTx(f, () => Couple(f).RespondReservationAsync(b, a, 0, default))).Ceremony!;
            var entries = await InTx(f, () => Couple(f).OpenWeddingAsync(ceremony, default));
            foreach (var entry in entries) Arrive(f, entry.Actor, entry.Church.Room);
            w.Clock.Now = entries[0].Church.Room.ReadyUntil;
            await InTx(f, () => Venue(f).AdvanceAsync(ceremony.Id, default));
            var second = (await f.Services.GetRequiredService<IMarriageHousingQueryDomainService>().ReadAsync(a.AccountId, a.CharacterId, default))!;
            Assert.NotEqual(first.Instance, second.Instance);
            Enter(w, f.Session, first.Instance); Enter(w, w.Peer, first.Instance); Enter(w, a, second.Instance); Enter(w, b, second.Instance);
            var areas = f.Services.GetRequiredService<ITownAreaDomainService>();
            Assert.Equal(new[] { 71, 72 }, areas.Find(new TownAreaKey(1, 101, 0, first.Instance))!.Members.Select(x => x.CharacterId.Value).Order());
            Assert.Equal(new[] { 73, 74 }, areas.Find(new TownAreaKey(1, 101, 0, second.Instance))!.Members.Select(x => x.CharacterId.Value).Order());
            Assert.Equal(0, (await Act(w, f.Session, new(HousingAction.Storage))).Status);
            var result = await InTx(f, () => TestEffectFootprints.Run(a, footprint => f.Services.GetRequiredService<IMarriageHousingDomainService>().ExecuteAsync(a, new(HousingAction.Storage), [a.AccountId, b.AccountId], footprint, default)));
            Assert.Equal(0, result.Status);
        });
    }

    [Fact]
    public async Task Water_limit_harvest_two_mails_and_repeat_cycle_survive_rollback_and_expiry()
    {
        await using var w = await MarriedAsync(); var f = w.F; var room = await Read(w); Enter(w, f.Session, room.Instance); Enter(w, w.Peer, room.Instance);
        var seed = await Give(w, 10002766, 2); var water = await Give(w, 10002765, 10);
        Assert.Equal(0, (await Act(w, f.Session, new(HousingAction.Plant, seed, 10002766), false)).Status); Assert.Null((await Read(w)).Tree);
        Assert.Equal(0, (await Act(w, f.Session, new(HousingAction.Plant, seed, 10002766))).Status);
        var expires = (await Read(w)).Tree!.ExpiresAt;
        Assert.Equal(0, (await Act(w, f.Session, new(HousingAction.Water, water, 10002765))).Status);
        Assert.Equal(0, (await Act(w, f.Session, new(HousingAction.Water, water, 10002765))).Status);
        Assert.Equal(-6, (await Act(w, f.Session, new(HousingAction.Water, water, 10002765))).Status);
        w.Clock.Now += TimeSpan.FromDays(1);
        Assert.Equal(0, (await Act(w, f.Session, new(HousingAction.Water, water, 10002765))).Status);
        var before = f.Scalar("SELECT COUNT(*) FROM mailbox_messages");
        Assert.Equal(0, (await Act(w, w.Peer, new(HousingAction.Harvest), false)).Status); Assert.Equal(before, f.Scalar("SELECT COUNT(*) FROM mailbox_messages"));
        Assert.Equal(0, (await Act(w, w.Peer, new(HousingAction.Harvest))).Status);
        Assert.Equal(before + 2, f.Scalar("SELECT COUNT(*) FROM mailbox_messages"));
        Assert.Equal(-6, (await Act(w, f.Session, new(HousingAction.Harvest))).Status);
        var tree = (await Read(w)).Tree!; Assert.Equal(0, tree.Growth); Assert.Equal(1, tree.HarvestRound); Assert.Equal(1, tree.Waters); Assert.Equal(expires, tree.ExpiresAt);
        Assert.Equal(1, f.Scalar("SELECT COUNT(*) FROM marriage_housing_harvests"));
        w.Clock.Now = expires;
        Assert.Equal(-11, (await Act(w, f.Session, new(HousingAction.Water, water, 10002765))).Status);
        Assert.Equal(0, (await Act(w, w.Peer, new(HousingAction.Clear))).Status); Assert.Null((await Read(w)).Tree);
    }

    [Fact]
    public async Task Ring_consumes_only_actor_coupon_and_cannot_skip_or_consume_twice()
    {
        await using var w = await MarriedAsync(); var f = w.F; var slot = await Give(w, 10002611, 2);
        Assert.Equal(-6, (await Act(w, f.Session, new(HousingAction.UpgradeRing, slot, Count: 2))).Status);
        Assert.Equal(0, (await Act(w, f.Session, new(HousingAction.UpgradeRing, slot, Count: 1), false)).Status);
        Assert.Equal(0, await f.Services.GetRequiredService<IMarriageHousingQueryDomainService>().RingAsync(f.Session.AccountId, f.Session.CharacterId, default));
        var upgraded = await Act(w, f.Session, new(HousingAction.UpgradeRing, slot, Count: 1));
        Assert.Equal(0, upgraded.Status);
        var silent = await f.Services.GetRequiredService<IMarriageHousingQueryDomainService>().InfoAsync(w.Peer.AccountId, w.Peer.CharacterId, default);
        Assert.Equal(1, silent!.RingLevel); Assert.Equal(f.Session.CharacterId.Value, silent.PartnerId); Assert.Equal(1, silent.LovePoints);
        Assert.Equal(1, await f.Services.GetRequiredService<IMarriageHousingQueryDomainService>().RingAsync(w.Peer.AccountId, w.Peer.CharacterId, default));
        Assert.Equal(-6, (await Act(w, f.Session, new(HousingAction.UpgradeRing, slot, Count: 1))).Status);
        Assert.Equal(1, f.Scalar("SELECT SUM(count) FROM item_slots WHERE owner_id=71 AND id=10002611"));
    }
}
