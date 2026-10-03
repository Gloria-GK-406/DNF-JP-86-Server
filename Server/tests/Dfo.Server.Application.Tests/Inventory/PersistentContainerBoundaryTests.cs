using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Application.Inventory;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Persistence.Database;
using Dfo.Server.Persistence.Inventory;

namespace Dfo.Server.Application.Tests.Inventory;

/// <summary>
/// What a container whose items outlive the process will and will not do.
/// </summary>
/// <remarks>
/// These used to be stated as the difference between two carriers, next to a second carrier that
/// held nothing past the run. That second carrier is gone -- nothing ever asked for one -- but the
/// rule was never about the comparison. It is about this container: a change it could not take
/// back, and that nobody would see had happened, is refused; looking is not changing and is not
/// refused; inside a use case the change is accepted and is there afterwards.
///
/// Kept when the carrier they were written beside was retired, because otherwise the refusal in
/// `PersistentItemContainer` could be deleted and nothing would turn red.
/// </remarks>
public sealed class PersistentContainerBoundaryTests
{
    private static readonly ContainerId Main =
        ContainerId.ForCharacter(new CharacterId(60), InventoryContainerType.Main);

    [Fact]
    public async Task ChangingItOutsideAUseCaseIsRefused()
    {
        var container = Persistent(new DomainTransactionScope(), out var lifecycle);

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await lifecycle.CreateAsync(ItemSnapshot.FromCore(Core(1), true), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await container.PutAsync(65, new ItemRecord(ItemSnapshot.FromCore(Core(1), true)), CancellationToken.None));
    }

    /// <remarks>
    /// Reading is not changing. A container is looked at in places that are not use cases at all,
    /// and refusing that would make the boundary a rule about looking rather than about writing.
    /// </remarks>
    [Fact]
    public async Task ReadingItOutsideAUseCaseIsAllowed()
    {
        var container = Persistent(new DomainTransactionScope(), out var lifecycle);

        Assert.Null(await container.GetAsync(3, CancellationToken.None));
    }

    [Fact]
    public async Task InsideAUseCaseItAccepts()
    {
        var scope = new DomainTransactionScope();
        var container = Persistent(scope, out var lifecycle);

        await using (var transaction = scope.Begin())
        {
            await container.PutAsync(65, await lifecycle.CreateAsync(ItemSnapshot.FromCore(Core(2), true), default), default);
            await transaction.CommitAsync(CancellationToken.None);
        }

        Assert.Equal(2, (await container.GetAsync(65, CancellationToken.None))!.Count);
    }

    /// <remarks>
    /// References expose item identity and occupancy without leaking a persistence row or entry model.
    /// </remarks>
    [Fact]
    public void WhatAContainerHandsBackNamesASlotAndWhatIsInIt()
    {
        Assert.DoesNotContain(
            typeof(ItemReference).GetProperties(),
            property => property.Name.Contains("Entry", StringComparison.Ordinal)
                || property.Name.Contains("RowId", StringComparison.Ordinal));
    }

    private static IItemContainer Persistent(DomainTransactionScope scope, out IItemLifecycleDomainService lifecycle)
    {
        var path = Path.Combine(Path.GetTempPath(), $"dfo-boundary-{Guid.NewGuid():N}.db");
        _ = new SqliteDatabase(path, new DomainTransactionScope());
        var store = new SqliteItemDomainStore(path);
        var rules = new ItemPlacementRuleService(new MainBandPlacements(), new NoWearables());
        lifecycle = new ItemLifecycleDomainService(store, scope, rules);
        return new PersistentItemContainer(
            Main,
            new CharacterId(60),
            new ContainerShape(InventoryContainerType.Main, 0),
            store,
            scope,
            null!,
            // A write classifies what it is about to write, so this one is supplied rather than
            // left null. It answers what these piles are: an ordinary consumable of the main band.
            rules,
            new ItemSlotRuleService());
    }

    private sealed class MainBandPlacements : IItemPlacementDefinitionSource
    {
        public ValueTask<ItemPlacement?> GetAsync(int itemId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<ItemPlacement?>(new ItemPlacement(2, 100,
                new(BusinessInventorySpace.Consumable, 0, 55)));
    }

    private sealed class NoWearables : IEquipmentSlotDefinitionSource
    {
        public ValueTask<BusinessSlot?> GetAsync(int itemId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<BusinessSlot?>(null);
    }

    private static InventoryItemCoreState Core(int count) =>
        StackableItemCore.Create(2, 8474, count);
}
