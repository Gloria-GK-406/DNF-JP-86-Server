using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Tests.Inventory;

public sealed class SharedMaterialContainerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PublicGrantAndWholeQuantityAdditionUseTheExistingSharedContainer(bool wholeQuantity)
    {
        using var host = Fixture();
        var shared = host.Services.GetRequiredService<ICharacterBusinessInventoryDomainService>()
            .GetStore(new ContainerOwner(InventoryOwnerType.Account, 1), BusinessInventoryStoreKind.Item)
            .GetContainer(BusinessInventorySpace.SharedMaterial);
        await Change(host, async () => { await shared.AddAsync(3033, 7, default); });
        await Change(host, async () =>
        {
            var containers = host.Services.GetRequiredService<ICharacterContainerDomainService>();
            if (wholeQuantity) await containers.AddAsync(new CharacterId(1), 3033, 3, default);
            else await host.Services.GetRequiredService<IItemGrantDomainService>()
                .GrantAsync(new AccountId(1), new CharacterId(1), containers, 3033, 3, default);
        });
        Assert.Equal(10, (await shared.GetAsync(0, default))!.Count);
        Assert.Equal(0, host.Scalar("SELECT cube_black FROM accounts WHERE account_id=1"));
    }

    [Fact]
    public async Task Legacy_columns_cannot_change_current_shared_material_contents_or_entry_projection()
    {
        using var host = Fixture();
        await host.SeedShared(3033, 7);
        host.Sql("UPDATE accounts SET cube_black=999 WHERE account_id=1");
        Assert.Equal(7, (await host.Shared().GetAsync(0, default))!.Count);
        var entry = await host.Services.GetRequiredService<IAccountInventoryEntryRepository>()
            .LoadAccountAsync(new AccountId(1), new CharacterId(1), default);
        Assert.Equal(7, Assert.Single(entry.AccountVirtualResources, x => x.ItemId == 3033).Value);
        Assert.Equal(999, host.Scalar("SELECT cube_black FROM accounts WHERE account_id=1"));
    }

    [Fact]
    public async Task OwnerFactoryAndCapacityRejectIncorrectOwnershipBindings()
    {
        using var host = Fixture();
        var inventories = host.Services.GetRequiredService<ICharacterBusinessInventoryDomainService>();
        Assert.Throws<ArgumentOutOfRangeException>(() => inventories.GetStore(
            new ContainerOwner(InventoryOwnerType.Account, 0), BusinessInventoryStoreKind.Item));
        Assert.Throws<ArgumentOutOfRangeException>(() => inventories.GetStore(
            new ContainerOwner((InventoryOwnerType)99, 1), BusinessInventoryStoreKind.Item));
        Assert.Throws<InvalidOperationException>(() => inventories.GetStore(
            new ContainerOwner(InventoryOwnerType.Account, 1), BusinessInventoryStoreKind.Item)
                .GetContainer(BusinessInventorySpace.Material));
        var capacity = host.Services.GetRequiredService<IInventoryCapacityDomainService>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => capacity.OpenSlotCountAsync(
            ContainerId.ForCharacter(new CharacterId(1), InventoryContainerType.SharedMaterial),
            BusinessInventorySpace.SharedMaterial, default).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => capacity.OpenSlotCountAsync(
            ContainerId.ForAccountCargo(new AccountId(1)), BusinessInventorySpace.SharedMaterial, default).AsTask());
        var physical = new PersistentItemContainer(
            ContainerId.ForCharacter(new CharacterId(1), InventoryContainerType.SharedMaterial), new CharacterId(1),
            new(InventoryContainerType.SharedMaterial, 11), host.Services.GetRequiredService<IItemDomainStore>(),
            host.Services.GetRequiredService<IDomainTransactionAccessor>(), null,
            host.Services.GetRequiredService<IItemPlacementRuleService>(),
            host.Services.GetRequiredService<IItemSlotRuleService>());
        Assert.Throws<InvalidOperationException>(() => new BusinessItemContainer(BusinessInventorySpace.SharedMaterial, physical));
    }

    [Theory]
    [InlineData(9, 7)]
    [InlineData(121, 101)]
    public async Task DirectMaterialWritesStillRespectTheActualOrdinarySlotAndStackLimit(int slot, int count)
    {
        using var host = Fixture();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Change(host, async () =>
        {
            var container = host.Services.GetRequiredService<ICharacterContainerDomainService>()
                .GetContainer(host.Session.CharacterId, InventoryContainerType.Main);
            var record = await host.Services.GetRequiredService<IItemLifecycleDomainService>().CreateAsync(
                ItemSnapshot.FromCore(StackableItemCore.Create(11, 3033, count), true), default);
            await container.PutAsync(slot, record, default);
        }));
        Assert.Equal(0, host.Count(3033));
    }

    [Fact]
    public async Task FixedMaterialSlotsRejectAnotherItemWrongKindOrNonpositiveCount()
    {
        using var host = Fixture();
        var shared = host.Services.GetRequiredService<ICharacterBusinessInventoryDomainService>()
            .GetStore(new ContainerOwner(InventoryOwnerType.Account, 1), BusinessInventoryStoreKind.Item)
            .GetContainer(BusinessInventorySpace.SharedMaterial);
        await Change(host, async () => { await shared.AddAsync(3033, 7, default); });
        foreach (var (index, core) in new[]
        {
            (1, StackableItemCore.Create(11, 3033, 5)),
            (0, StackableItemCore.Create(3, 9999, 5)),
            (0, StackableItemCore.Create(2, 3033, 5)),
            (0, StackableItemCore.Create(11, 3033, 0)),
            (0, StackableItemCore.Create(11, 3033, -1)),
        })
            await Assert.ThrowsAsync(core.Value <= 0 ? typeof(InvalidDataException) : typeof(InvalidOperationException), () => Change(host, async () =>
            {
                var session = host.Services.GetRequiredService<IItemDomainStore>().SessionIn(
                    host.Services.GetRequiredService<IDomainTransactionAccessor>().Current!);
                await shared.PutAsync(index, await session.CreateAsync(ItemSnapshot.FromCore(core, true), default), default);
            }));
        Assert.Equal(7, (await shared.GetAsync(0, default))!.Count);
        Assert.Single(await shared.GetAllAsync(default));
    }

    [Fact]
    public async Task SwapChecksTheDisplacedPilesDestinationCapBeforeEitherWrite()
    {
        using var host = Fixture();
        host.Sql("INSERT INTO account_cargo_state(account_id,selection_key) VALUES(1,8)");
        var store = host.Services.GetRequiredService<ICharacterBusinessInventoryDomainService>()
            .GetStore(new ContainerOwner(InventoryOwnerType.Account, 1), BusinessInventoryStoreKind.Item);
        var shared = store.GetContainer(BusinessInventorySpace.SharedMaterial);
        var cargo = store.GetContainer(BusinessInventorySpace.AccountCargo);
        await Change(host, async () =>
        {
            await shared.PutAsync(0, await host.Services.GetRequiredService<IItemLifecycleDomainService>().CreateAsync(
                ItemSnapshot.FromCore(StackableItemCore.Create(11, 3033, 150) with { Attribute = 1 }, true), default), default);
            await cargo.PutAsync(0, await host.Services.GetRequiredService<IItemLifecycleDomainService>().CreateAsync(
                ItemSnapshot.FromCore(StackableItemCore.Create(11, 3033, 50), true), default), default);
        });
        await Change(host, async () =>
            Assert.IsType<ItemMoveOutcome.Refused>(await host.Services.GetRequiredService<IItemMovementDomainService>()
                .MoveBetweenContainersAsync(new(new(BusinessInventorySpace.AccountCargo, 0), cargo),
                    new(new(BusinessInventorySpace.SharedMaterial, 0), shared), 50, default)));
        Assert.Equal(150, (await shared.GetAsync(0, default))!.Count);
        Assert.Equal(50, (await cargo.GetAsync(0, default))!.Count);
    }

    [Fact]
    public async Task ExplicitAccountAccessWorksWithoutACharacterBindingAndIsolatesOtherAccountsAndCargo()
    {
        using var host = Fixture();
        host.Sql("INSERT INTO accounts(account_id,m_id) VALUES(2,'offline')");
        var inventories = host.Services.GetRequiredService<ICharacterBusinessInventoryDomainService>();
        var owner = new ContainerOwner(InventoryOwnerType.Account, 1);
        var first = inventories.GetStore(owner, BusinessInventoryStoreKind.Item).GetContainer(BusinessInventorySpace.SharedMaterial);
        var second = inventories.GetStore(new ContainerOwner(InventoryOwnerType.Account, 2), BusinessInventoryStoreKind.Item)
            .GetContainer(BusinessInventorySpace.SharedMaterial);
        var cargo = inventories.GetStore(owner, BusinessInventoryStoreKind.Item).GetContainer(BusinessInventorySpace.AccountCargo);
        await Change(host, async () => Assert.IsType<ItemPlacementOutcome.PutInEmptySlot>((await first.AddAsync(3033, 7, default)).Placement));
        Assert.Equal(7, (await first.GetAsync(0, default))!.Count);
        Assert.Empty(await second.GetAllAsync(default));
        Assert.Empty(await cargo.GetAllAsync(default));
        Assert.Equal(11, await first.OpenSlotCountAsync(default));
        Assert.Null(inventories.GetStore(new CharacterId(77), BusinessInventoryStoreKind.Item).FindContainer(BusinessInventorySpace.SharedMaterial));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(50)]
    public async Task MovingToAnOrdinaryContainerKeepsItsStackLimitAndBothCountsOnRefusal(int destinationCount)
    {
        using var host = Fixture();
        Bind(host, 1, 1);
        var source = Bag(host, 1);
        var destination = host.Services.GetRequiredService<ICharacterBusinessInventoryDomainService>()
            .GetStore(new CharacterId(1), BusinessInventoryStoreKind.Item).GetContainer(BusinessInventorySpace.Material);
        await Change(host, async () => { await source.AddAsync(3033, 150, default); });
        if (destinationCount > 0) await host.Seed(121, 3033, destinationCount, 11);
        await Change(host, async () =>
            Assert.IsType<ItemMoveOutcome.Refused>(await host.Services.GetRequiredService<IItemMovementDomainService>()
                .MoveBetweenContainersAsync(new(new(BusinessInventorySpace.SharedMaterial, 0), source),
                    new(new(BusinessInventorySpace.Material, 0), destination), 101, default)));
        Assert.Equal(150, (await source.GetAsync(0, default))!.Count);
        Assert.Equal(destinationCount, (await destination.GetAsync(0, default))?.Count ?? 0);
        await Change(host, async () =>
            Assert.IsType<ItemMoveOutcome.Moved>(await host.Services.GetRequiredService<IItemMovementDomainService>()
                .MoveBetweenContainersAsync(new(new(BusinessInventorySpace.SharedMaterial, 0), source),
                    new(new(BusinessInventorySpace.Material, 0), destination), 40, default)));
        Assert.Equal(110, (await source.GetAsync(0, default))!.Count);
        Assert.Equal(destinationCount + 40, (await destination.GetAsync(0, default))!.Count);
    }

    [Fact]
    public void SharedLocalSlotsUseDedicatedStorageButExistingMainWireSlots()
    {
        var mapping = new InventorySlotMappingRuleService();
        for (var index = 0; index < 11; index++)
        {
            var local = new BusinessSlot(BusinessInventorySpace.SharedMaterial, index);
            var wire = new GameSlot(InventoryContainerType.Main, 354 + index);
            Assert.Equal(local, mapping.ToBusinessSlot(wire));
            Assert.Equal(wire, mapping.ToGameSlot(local));
            Assert.Equal(new(InventoryContainerType.SharedMaterial, index), LegacyInventoryStorageCoordinates.ToStorage(local));
            Assert.Equal(local, LegacyInventoryStorageCoordinates.ToBusiness(new(InventoryContainerType.SharedMaterial, index)));
        }
        Assert.Throws<InventorySlotMappingException>(() => mapping.ToBusinessSlot(new(InventoryContainerType.SharedMaterial, 0)));
    }

    [Fact]
    public async Task MaterialDefaultsToItsFixedAccountSlotWithoutTheOrdinaryStackCap()
    {
        using var host = Fixture();
        var rules = host.Services.GetRequiredService<IItemPlacementRuleService>();
        for (var index = 0; index < 11; index++)
        {
            var placement = await rules.PlacementForAsync(AccountMaterialCatalog.Definitions[index].ItemId, default);
            Assert.Equal(new(BusinessInventorySpace.SharedMaterial, index, index), placement!.Destination);
            Assert.Equal(int.MaxValue, placement.StackLimit);
            Assert.Equal(ItemWorldClass.Interchangeable, placement.WorldClass);
        }
    }

    [Fact]
    public async Task SameAccountCharactersShareOrdinaryContainerContentsAndCapacityFailureIsAtomic()
    {
        using var host = Fixture();
        Bind(host, 1, 1);
        Bind(host, 2, 1);
        var first = Bag(host, 1);
        var second = Bag(host, 2);
        await Change(host, async () =>
        {
            var result = await first.AddAsync(3033, int.MaxValue - 1, default);
            Assert.IsType<ItemPlacementOutcome.PutInEmptySlot>(result.Placement);
        });
        Assert.Equal(first.StorageId, second.StorageId);
        Assert.Equal(int.MaxValue - 1, (await second.GetAsync(0, default))!.Count);
        await Change(host, async () =>
            Assert.IsType<ItemPlacementOutcome.NoRoom>((await second.AddAsync(3033, 2, default)).Placement));
        Assert.Equal(int.MaxValue - 1, (await first.GetAsync(0, default))!.Count);
        Assert.Single(await first.GetAllAsync(default));
    }

    [Fact]
    public void AFullFixedSlotCannotFallBackToQuickSlots()
    {
        var slot = new BusinessSlot(BusinessInventorySpace.SharedMaterial, 0);
        var placement = new ItemPlacement(3, int.MaxValue, new(slot.Space, 0, 0));
        Assert.IsType<ItemArrival.NoRoom>(new ItemArrivalRuleService().ForGrant(
            [new(slot, int.MaxValue)], 1, placement, new HashSet<BusinessSlot> { slot }));
    }

    [Fact]
    public async Task OrdinaryContainerDoesNotGloballyForbidAMaterialItem()
    {
        using var host = Fixture();
        await host.Seed(121, 3033, 7, 11);
        Assert.Equal(7, host.Count(3033));
    }

    [Fact]
    public async Task InPlaceQuantityGrowthStillRespectsTheOrdinaryStackLimit()
    {
        using var host = Fixture();
        await host.Seed(121, 3033, 90, 11);
        var bag = host.Services.GetRequiredService<ICharacterContainerDomainService>()
            .GetContainer(new CharacterId(1), InventoryContainerType.Main);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Change(host, async () =>
        {
            var item = (await bag.GetAsync(121, default))!;
            await bag.UpdateAsync(item, x => x with { Count = 101 }, default);
        }));
        Assert.Equal(90, host.Count(3033));
        await Change(host, async () =>
        {
            var item = (await bag.GetAsync(121, default))!;
            await bag.UpdateAsync(item, x => x with { Count = 100 }, default);
        });
        Assert.Equal(100, host.Count(3033));
    }

    private static ShopFixture Fixture() => new(services =>
        services.AddSingleton<IItemPlacementDefinitionSource, MaterialContent>());

    private static IBusinessItemContainer Bag(ShopFixture host, int character) =>
        host.Services.GetRequiredService<ICharacterBusinessInventoryDomainService>()
            .GetStore(new CharacterId(character), BusinessInventoryStoreKind.Item).GetContainer(BusinessInventorySpace.SharedMaterial);

    private static void Bind(ShopFixture host, int character, int account)
    {
        if (host.Services.GetRequiredService<ICharacterContainerDomainService>().FindContainer(new CharacterId(character), InventoryContainerType.SharedMaterial) is { } old)
        {
            Assert.Equal(ContainerId.ForAccount(new AccountId(account), InventoryContainerType.SharedMaterial), old.Id);
            return;
        }
        host.Services.GetRequiredService<ICharacterContainerDomainService>().Register(new CharacterId(character),
            new PersistentItemContainer(ContainerId.ForAccount(new AccountId(account), InventoryContainerType.SharedMaterial),
                new CharacterId(character), new(InventoryContainerType.SharedMaterial, 11),
                host.Services.GetRequiredService<IItemDomainStore>(), host.Services.GetRequiredService<IDomainTransactionAccessor>(),
                null, host.Services.GetRequiredService<IItemPlacementRuleService>(),
                host.Services.GetRequiredService<IItemSlotRuleService>(), host.Services.GetRequiredService<IInventoryCapacityDomainService>()));
    }

    private static async Task Change(ShopFixture host, Func<Task> action)
    {
        await using var transaction = ((DomainTransactionScope)host.Services.GetRequiredService<IDomainTransactionAccessor>()).Begin();
        await action();
        await transaction.CommitAsync(default);
    }

    private sealed class MaterialContent : IItemPlacementDefinitionSource
    {
        public ValueTask<ItemPlacement?> GetAsync(int itemId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<ItemPlacement?>(new(AccountMaterialCatalog.TryGetSlotIndex(itemId, out var slot) ? AccountMaterialCatalog.Definitions[slot].ItemKind : (byte)3, 100, new(BusinessInventorySpace.Material, 0, 55)));
    }
}
