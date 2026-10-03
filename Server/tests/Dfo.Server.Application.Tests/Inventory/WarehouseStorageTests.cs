using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Commands;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Protocol.Game.Warehouse;
using Dfo.Server.Protocol.Game.InventorySort;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class WarehouseStorageTests
{
    [Theory]
    [InlineData("account:1:Main")]
    [InlineData("character:1:AccountCargo")]
    public async Task Cargo_access_rejects_a_bound_container_with_the_wrong_ownership_or_type(string storageId)
    {
        using var host = Fixture();
        host.Sql("INSERT INTO account_cargo_state(account_id,selection_key) VALUES(1,8)");
        await host.Seed(65, 2000, 5);
        Bind(host, cargoStorageId: new ContainerId(storageId));
        Assert.IsType<ItemMoveOutcome.Refused>(await Move(host,
            new(BusinessInventorySpace.Consumable, 0), new(BusinessInventorySpace.AccountCargo, 0), 2));
        Assert.Equal(5, host.Count(2000));
        Assert.Equal(0, host.Scalar("SELECT COUNT(*) FROM item_slots WHERE owner_scope='account'"));
    }

    [Fact]
    public async Task Account_creation_upgrade_material_cost_and_duplicate_requests_preserve_assets()
    {
        using var host = Fixture();
        host.Sql("UPDATE characters SET level=59; UPDATE character_stats SET carried_gold=3000000;");
        Assert.Equal(InventoryExpansionRejection.InvalidStage, (await Command(host, 0x0131)).Rejection);
        Assert.Equal(3000000, host.Gold);
        host.Sql("UPDATE characters SET level=60;");
        Assert.NotNull((await Command(host, 0x0131)).Receipt);
        Assert.Equal(2900000, host.Gold);
        Assert.Equal(1, Capacity(host));
        Assert.Equal(InventoryExpansionRejection.InvalidStage, (await Command(host, 0x0131)).Rejection);
        Assert.Equal(2900000, host.Gold);
        Assert.NotNull((await Command(host, 0x0132)).Receipt);
        Assert.Equal(900000, host.Gold);
        Assert.Equal(8, Capacity(host));
        Assert.Equal(InventoryExpansionRejection.InvalidStage, (await Command(host, 0x0132)).Rejection);
        host.Sql("UPDATE account_cargo_state SET selection_key=16;");
        await host.Seed(65, 3299, 249);
        Assert.Equal(InventoryExpansionRejection.InsufficientAssets, (await Command(host, 0x0132)).Rejection);
        Assert.Equal(249, host.Count(3299));
        await host.Seed(66, 3299, 2);
        Assert.NotNull((await Command(host, 0x0132)).Receipt);
        Assert.Equal(24, Capacity(host));
        Assert.Equal(1, host.Count(3299));
        Assert.Equal(900000, host.Gold);
    }

    [Fact]
    public async Task Gold_transfer_is_exact_bounded_and_rejects_stale_sessions()
    {
        using var host = Fixture();
        host.Sql("UPDATE characters SET level=60; UPDATE character_stats SET carried_gold=20000000; INSERT INTO account_cargo_state(account_id,selection_key,value32) VALUES(1,1,9999999);");
        Assert.NotNull((await Command(host, 0x0133, 1)).Receipt);
        Assert.Equal(10000000, CargoGold(host));
        Assert.Equal(19999999, host.Gold);
        Assert.Equal(InventoryExpansionRejection.GoldLimit, (await Command(host, 0x0133, 1)).Rejection);
        Assert.Equal(InventoryExpansionRejection.InvalidRequest, (await Command(host, 0x0133, -1)).Rejection);
        Assert.Equal(InventoryExpansionRejection.InsufficientAssets, (await Command(host, 0x0134, 10000001)).Rejection);
        Assert.NotNull((await Command(host, 0x0134, 10000000)).Receipt);
        Assert.Equal(0, CargoGold(host));
        Assert.Equal(29999999, host.Gold);
        var stale = host.Session;
        host.Select(2); Bind(host);
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.Services.GetRequiredService<ICommandBus>()
            .SendAsync(new WarehouseCommand(host.OnlineUser, stale, 0x0133, 1)).AsTask());
        Assert.Equal(0, CargoGold(host));
    }

    [Fact]
    public async Task Later_storage_failure_rolls_back_gold_or_upgrade_item_with_capacity()
    {
        using var host = Fixture();
        host.Sql("UPDATE characters SET level=60; CREATE TRIGGER reject_cargo BEFORE UPDATE ON account_cargo_state BEGIN SELECT RAISE(ABORT,'forced'); END;");
        await Assert.ThrowsAsync<SqliteException>(() => Command(host, 0x0131));
        Assert.Equal(100000, host.Gold);
        Assert.Equal(0, host.Scalar("SELECT COUNT(*) FROM account_cargo_state"));
        await host.Seed(65, 50, 2);
        host.Sql("CREATE TRIGGER reject_expansion BEFORE UPDATE ON character_container_state BEGIN SELECT RAISE(ABORT,'forced'); END;");
        await Assert.ThrowsAsync<SqliteException>(() => Command(host, 0x0198, 65));
        Assert.Equal(2, host.Count(50));
        Assert.Equal(0, host.Scalar("SELECT COUNT(*) FROM character_container_state WHERE list_type=2"));
    }

    [Fact]
    public async Task Personal_upgrade_consumes_one_ticket_once_and_updates_current_capacity()
    {
        using var host = Fixture();
        await host.Seed(65, 50, 2);
        var bag = Bag(host, BusinessInventorySpace.PersonalCargo);
        Assert.Equal(8, await bag.OpenSlotCountAsync(default));
        var result = await Command(host, 0x0198, 65);
        Assert.NotNull(result.Receipt);
        Assert.Equal(24, await bag.OpenSlotCountAsync(default));
        Assert.Equal(1, host.Count(50));
        Assert.Equal(InventoryExpansionRejection.InvalidStage, (await Command(host, 0x0198, 65)).Rejection);
        Assert.Equal(1, host.Count(50));
        Assert.Equal((ushort)24, result.Receipt!.Character.Containers.Single(c => c.ContainerType == InventoryContainerType.PersonalCargo).Parameter);
    }

    [Fact]
    public async Task Account_equipment_swap_and_cross_character_withdrawal_keep_uid_and_details()
    {
        using var host = Fixture();
        host.Sql("INSERT INTO account_cargo_state(account_id,selection_key) VALUES(1,8)");
        var core = StackableItemCore.Create(1, 3000, 777) with { EnchantCardId = 54321, Durability = 31, Attribute = 7 };
        await host.SeedCore(9, core);
        var original = await Bag(host, BusinessInventorySpace.CarriedEquipment).GetAsync(0, default);
        Assert.IsType<ItemMoveOutcome.Moved>(await Move(host, new(BusinessInventorySpace.CarriedEquipment, 0), new(BusinessInventorySpace.AccountCargo, 0)));
        host.Select(2); Bind(host);
        Assert.IsType<ItemMoveOutcome.Moved>(await Move(host, new(BusinessInventorySpace.AccountCargo, 0), new(BusinessInventorySpace.CarriedEquipment, 0)));
        var received = await Bag(host, BusinessInventorySpace.CarriedEquipment).GetAsync(0, default);
        Assert.Equal(original!.ItemUid, received!.ItemUid);
        Assert.Equal(core, received.Snapshot.ToCore());
        Bind(host, new AccountId(99));
        Assert.IsType<ItemMoveOutcome.Refused>(await Move(host, new(BusinessInventorySpace.CarriedEquipment, 0), new(BusinessInventorySpace.AccountCargo, 0)));
        Assert.NotNull(await Bag(host, BusinessInventorySpace.CarriedEquipment).GetAsync(0, default));
    }

    [Fact]
    public async Task Closed_slots_reject_deposits_but_legacy_piles_can_be_split_out_without_losing_metadata()
    {
        using var host = Fixture();
        var core = StackableItemCore.Create(2, 2000, 8) with { ExpireTime = int.MaxValue, TradeRestriction = 0, Attribute = 2 };
        await host.SeedCore(65, core);
        Assert.IsType<ItemMoveOutcome.Refused>(await Move(host, new(BusinessInventorySpace.Consumable, 0), new(BusinessInventorySpace.PersonalCargo, 8)));
        Assert.IsType<ItemMoveOutcome.Moved>(await Move(host, new(BusinessInventorySpace.Consumable, 0), new(BusinessInventorySpace.PersonalCargo, 7), 3));
        Assert.Equal(core with { Value = 3 }, (await Bag(host, BusinessInventorySpace.PersonalCargo).GetAsync(7, default))!.Snapshot.ToCore());
        host.Sql("UPDATE item_slots SET slot_index=20 WHERE list_type=2;");
        Assert.IsType<ItemMoveOutcome.Moved>(await Move(host, new(BusinessInventorySpace.PersonalCargo, 20), new(BusinessInventorySpace.Consumable, 1), 1));
        Assert.Equal(2, (await Bag(host, BusinessInventorySpace.PersonalCargo).GetAsync(20, default))!.Count);
        Assert.Equal(core with { Value = 1 }, (await Bag(host, BusinessInventorySpace.Consumable).GetAsync(1, default))!.Snapshot.ToCore());
        await InTransaction(host, async () => Assert.True(await host.Services.GetRequiredService<IInventorySortDomainService>()
            .SortAsync(host.Session.CharacterId, BusinessInventorySpace.PersonalCargo, InventorySortOrder.Default, default)));
        Assert.Equal(2, (await Bag(host, BusinessInventorySpace.PersonalCargo).GetAsync(20, default))!.Count);
    }

    [Fact]
    public async Task ClosedLegacyPileCanDecreaseButCannotGrowOrChangeStateInPlace()
    {
        using var host = Fixture();
        await host.SeedCore(65, StackableItemCore.Create(2, 2000, 8));
        Assert.IsType<ItemMoveOutcome.Moved>(await Move(host,
            new(BusinessInventorySpace.Consumable, 0), new(BusinessInventorySpace.PersonalCargo, 7), 3));
        host.Sql("UPDATE item_slots SET slot_index=20 WHERE list_type=2;");
        var bag = Bag(host, BusinessInventorySpace.PersonalCargo);
        await Assert.ThrowsAsync<InvalidOperationException>(() => InTransaction(host, async () =>
        {
            var item = (await bag.GetAsync(20, default))!;
            await bag.UpdateAsync(item, x => x with { Count = 4 }, default);
        }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => InTransaction(host, async () =>
        {
            var item = (await bag.GetAsync(20, default))!;
            await bag.UpdateAsync(item, x => x with { Attribute = 1 }, default);
        }));
        await InTransaction(host, async () =>
        {
            var item = (await bag.GetAsync(20, default))!;
            await bag.UpdateAsync(item, x => x with { Count = 2 }, default);
        });
        Assert.Equal(2, (await bag.GetAsync(20, default))!.Count);
    }

    [Fact]
    public async Task Concurrent_deposits_conserve_gold_and_serialise_the_shared_account()
    {
        using var host=Fixture();
        host.Sql("INSERT INTO account_cargo_state(account_id,selection_key,value32) VALUES(1,1,0)");
        var results=await Task.WhenAll(Enumerable.Range(0,20).Select(_=>Command(host,0x0133,100)));
        Assert.All(results,r=>Assert.NotNull(r.Receipt));
        Assert.Equal(2000,CargoGold(host)); Assert.Equal(98000,host.Gold);
    }

    [Fact]
    public async Task Successful_handler_delivery_starts_with_committed_ack_then_authoritative_snapshots()
    {
        using var host=Fixture();
        host.Sql("UPDATE characters SET level=60");
        host.Sender.BeforeSend=()=>{Assert.Equal(0,host.Gold);Assert.Equal(1,Capacity(host));};
        await host.Services.GetRequiredService<WarehouseHandler>().HandleAsync(new(host.Connection),
            new WarehouseRequest(Dfo.Server.Data.Networking.GameMessageMetadata.Empty with { Type=0x0131 },0),default);
        Assert.IsType<WarehouseResponse>(host.Sender.Messages[0]);
        Assert.Equal(3,host.Sender.Messages.Count);
        Assert.All(host.Sender.Batches.SelectMany(b=>b.Frames),f=>Assert.InRange(f.Length,15,1048576));
    }

    [Dfo.Server.Testing.PvfFact]
    public async Task Replayed_deposit_refreshes_current_balances_without_repeating_the_settlement_or_ack()
    {
        using var host = Fixture();
        host.Sql("INSERT INTO account_cargo_state(account_id,selection_key,value32) VALUES(1,1,0)");
        var first = new WarehouseRequest(new Dfo.Server.Data.Networking.GameMessageMetadata(1, 0x0133, 0, 7), 100);
        await EffectRequestTransport.Send(host, first);
        await EffectRequestTransport.Send(host, new WarehouseRequest(first.Metadata with { Sequence = 8 }, 250));
        var acknowledgements = host.Sender.Messages.OfType<WarehouseResponse>().Count();
        await EffectRequestTransport.Send(host, first);
        Assert.Equal(350, CargoGold(host));
        Assert.Equal(99650, host.Gold);
        Assert.Equal(acknowledgements, host.Sender.Messages.OfType<WarehouseResponse>().Count());
        Assert.DoesNotContain(host.Sender.Batches.Last().Frames, frame => frame[0] == 1);
        Assert.Contains(host.Sender.Batches.Last().Frames, frame => frame[0] == 0 && frame[1] == 0x0D);
    }

    [Theory]
    [InlineData("free", 0, 0, 0, true)]
    [InlineData("account", 0, 0, 0, true)]
    [InlineData("sealing", 1, 0, 0, true)]
    [InlineData("sealing", 0, 0, 0, false)]
    [InlineData("trade delete", 0, 0, 0, false)]
    [InlineData("free", 0, 1, 0, false)]
    [InlineData("free", 0, 0, 1, false)]
    public async Task Account_cargo_obeys_binding_seal_and_instance_restrictions(
        string attach, byte sealedFlag, byte locked, byte restriction, bool allowed)
    {
        var source = new RestrictedCargoContent(new(attach, new HashSet<string>()));
        var item = TestItemReferences.FromCore(0, StackableItemCore.Create(1, 3000, 1)
            with { SealFlag = sealedFlag, EquipmentLockId = locked, TradeRestriction = restriction }, ItemWorldClass.Equipment);
        Assert.Equal(allowed, await new CargoItemRuleService(source).CanStoreAsync(
            BusinessInventorySpace.AccountCargo, item, DateTimeOffset.UnixEpoch, default));
    }

    private sealed class RestrictedCargoContent(CargoItemDefinition definition) : ICargoItemDefinitionSource
    {
        public ValueTask<CargoItemDefinition?> GetAsync(int id, CancellationToken ct) => ValueTask.FromResult<CargoItemDefinition?>(definition);
    }

    [Theory]
    [InlineData(BusinessInventorySpace.Avatar, 0, 105)]
    [InlineData(BusinessInventorySpace.Avatar, 7, 112)]
    [InlineData(BusinessInventorySpace.Avatar, 105, 210)]
    [InlineData(BusinessInventorySpace.Avatar, 483, 0)]
    [InlineData(BusinessInventorySpace.PersonalCargo, 200, 200)]
    [InlineData(BusinessInventorySpace.AccountCargo, 0, 0)]
    [InlineData(BusinessInventorySpace.AccountCargo, 120, 120)]
    [InlineData(BusinessInventorySpace.Consumable, 0, 32)]
    [InlineData(BusinessInventorySpace.Consumable, 24, 56)]
    public void Capacities_respect_each_container_parameter(BusinessInventorySpace space, int parameter, int expected) =>
        Assert.Equal(expected, new InventoryCapacityRuleService().OpenSlotCount(space, parameter));

    internal static ShopFixture Fixture()
    {
        var host = new ShopFixture(s => {
            s.AddSingleton<IInventoryExpansionDefinitionSource, ExpansionContent>();
            s.AddSingleton<ICargoItemDefinitionSource, CargoContent>();
            s.AddSingleton<ICarriedGoldRuleService, GoldRule>();
        });
        Bind(host); return host;
    }
    internal static void Bind(ShopFixture host, AccountId? account = null, ContainerId? cargoStorageId = null)
    {
        var registry = host.Services.GetRequiredService<ICharacterContainerDomainService>();
        var kept = new[] { InventoryContainerType.Main, InventoryContainerType.Equipment, InventoryContainerType.SharedMaterial }.Select(type => registry.GetContainer(host.Session.CharacterId, type)).ToArray();
        registry.Clear(host.Session.CharacterId);
        foreach (var container in kept) registry.Register(host.Session.CharacterId, container);
        foreach (var type in new[] { InventoryContainerType.PersonalCargo, InventoryContainerType.AccountCargo, InventoryContainerType.Avatar })
        {
            registry.Register(host.Session.CharacterId, new PersistentItemContainer(type == InventoryContainerType.AccountCargo
                    ? cargoStorageId ?? ContainerId.ForAccountCargo(account ?? host.Session.AccountId) : ContainerId.ForCharacter(host.Session.CharacterId, type), new(type, 0), host.Services.GetRequiredService<IItemDomainStore>(),
                host.Services.GetRequiredService<IDomainTransactionAccessor>(),
                host.Services.GetRequiredService<IItemPlacementRuleService>(),
                host.Services.GetRequiredService<IItemSlotRuleService>()));
        }
    }
    internal static Task<InventoryExpansionResult> Command(ShopFixture host, ushort type, int value = 0) =>
        host.Services.GetRequiredService<ICommandBus>().SendAsync(new WarehouseCommand(host.OnlineUser, host.Session, type, value,
            type is 0x0198 or 0x01AF ? new InventorySlotMappingRuleService().ToBusinessSlot(new(InventoryContainerType.Main, value)) : null)).AsTask();
    internal static IBusinessItemContainer Bag(ShopFixture host, BusinessInventorySpace space) => host.Services
        .GetRequiredService<ICharacterBusinessInventoryDomainService>().GetStore(host.Session.CharacterId, BusinessInventoryOwnership.StoreOf(space)).GetContainer(space);
    private static async Task<ItemMoveOutcome> Move(ShopFixture host, BusinessSlot from, BusinessSlot into, int count = 0)
    {
        ItemMoveOutcome outcome = null!;
        await InTransaction(host, async () => outcome = await host.Services.GetRequiredService<ICargoTransferDomainService>().MoveAsync(host.Session, from, into, count, default));
        return outcome;
    }
    internal static async Task InTransaction(ShopFixture host, Func<Task> action)
    {
        await using var tx = ((DomainTransactionScope)host.Services.GetRequiredService<IDomainTransactionAccessor>()).Begin();
        await action(); await tx.CommitAsync(default);
    }
    private static int Capacity(ShopFixture host) => host.Scalar("SELECT selection_key FROM account_cargo_state WHERE account_id=1");
    private static int CargoGold(ShopFixture host) => host.Scalar("SELECT value32 FROM account_cargo_state WHERE account_id=1");
    private sealed class CargoContent : ICargoItemDefinitionSource
    { public ValueTask<CargoItemDefinition?> GetAsync(int id, CancellationToken ct) => ValueTask.FromResult<CargoItemDefinition?>(new("free", new HashSet<string>())); }
    private sealed class GoldRule : ICarriedGoldRuleService
    { public int CarryLimitFor(int level, int stored) => 400000000; }
    internal sealed class ExpansionContent : IInventoryExpansionDefinitionSource
    {
        public ValueTask<AccountCargoDefinition> AccountCargoAsync(CancellationToken ct) => ValueTask.FromResult(new AccountCargoDefinition(60,
            [new(1,10000000,-1,0,100000,-1),new(8,100000000,-1,0,2000000,-1),new(16,150000000,-1,0,0,100534),new(24,200000000,3299,250,0,-1),new(32,250000000,3299,250,0,-1)]));
        public ValueTask<InventoryExpansionGrant?> ItemAsync(int id, CancellationToken ct) => ValueTask.FromResult<InventoryExpansionGrant?>(id switch
        { 50 => new(InventoryExpansionKind.PersonalCargo,24), 57 => new(InventoryExpansionKind.PersonalCargo,40),
            2660296 => new(InventoryExpansionKind.Main,8), 2683901 => new(InventoryExpansionKind.Avatar,7), _ => null });
        public ValueTask<InventoryExpansionGrant?> ProductAsync(int product, int item, CancellationToken ct) => product == 100534
            ? ValueTask.FromResult<InventoryExpansionGrant?>(new(InventoryExpansionKind.AccountCargo,16,product)) : ItemAsync(item,ct);
    }
}
