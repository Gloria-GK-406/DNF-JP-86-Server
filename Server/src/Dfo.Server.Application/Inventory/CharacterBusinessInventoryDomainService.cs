using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

internal sealed class CharacterBusinessInventoryDomainService(ICharacterContainerDomainService backing,
    IItemPlacementRuleService? placements = null, IItemArrivalRuleService? arrivals = null,
    ICreatureDefinitionSource? creatures = null, IItemDomainStore? items = null,
    IDomainTransactionAccessor? transactions = null, IItemSlotRuleService? slotRules = null)
    : ICharacterBusinessInventoryDomainService
{
    public IBusinessInventoryStore GetStore(CharacterId characterId, BusinessInventoryStoreKind kind) =>
        new BusinessInventoryStore(characterId, kind, backing, placements, arrivals, creatures,
            items, transactions, slotRules);

    private sealed class BusinessInventoryStore(CharacterId characterId, BusinessInventoryStoreKind kind,
        ICharacterContainerDomainService backing, IItemPlacementRuleService? placements,
        IItemArrivalRuleService? arrivals, ICreatureDefinitionSource? creatures,
        IItemDomainStore? items, IDomainTransactionAccessor? transactions,
        IItemSlotRuleService? slotRules) : IBusinessInventoryStore
    {
        public BusinessInventoryStoreKind Kind => kind;

        public IBusinessItemContainer? FindContainer(BusinessInventorySpace space)
        {
            if (BusinessInventoryOwnership.StoreOf(space) != kind)
                throw new InvalidOperationException($"{space} does not belong to the {kind} store.");
            var address = LegacyInventoryStorageCoordinates.ToStorage(new BusinessSlot(space, 0));
            var container = backing.FindContainer(characterId, address.List);
            if (container is null && items is not null && transactions is not null
                && placements is not null && arrivals is not null && slotRules is not null)
                container = new PersistentItemContainer(ContainerId.ForCharacter(characterId, address.List),
                    characterId, new ContainerShape(address.List, 0), items, transactions,
                    null, placements, arrivals, slotRules);
            return container is null ? null : new BusinessItemContainer(space, container, placements, arrivals, creatures);
        }

        public IBusinessItemContainer GetContainer(BusinessInventorySpace space) =>
            FindContainer(space) ?? throw new InvalidOperationException(
                $"Character {characterId.Value} has no container for {space}.");
    }
}

internal sealed class BusinessItemContainer(BusinessInventorySpace space, IItemContainer backing,
    IItemPlacementRuleService? placements = null, IItemArrivalRuleService? arrivals = null,
    ICreatureDefinitionSource? creatures = null)
    : IBusinessItemContainer
{
    public BusinessContainerId Id => new(space, backing.Id);
    public ContainerId StorageId => backing.Id;
    public BusinessInventorySpace Space => space;

    public async ValueTask<ContainerSlotItem?> ReadSlotAsync(int index, CancellationToken cancellationToken)
    {
        var storageIndex = this.StorageIndex(new(space, index));
        var item = await backing.ReadSlotAsync(storageIndex, cancellationToken)
            .ConfigureAwait(false);
        if (item is not null && LegacyInventoryStorageCoordinates.ToBusiness(
                new(backing.Type, item.SlotIndex), item.Core.ItemKind).Space != space)
            return null;
        return item is null ? null : item with { SlotIndex = index };
    }

    public async ValueTask<IReadOnlyList<ContainerSlotItem>> ReadAllAsync(CancellationToken cancellationToken)
    {
        var items = await backing.ReadAllAsync(cancellationToken).ConfigureAwait(false);
        var local = new List<ContainerSlotItem>();
        foreach (var item in items)
        {
            try
            {
                var slot = LegacyInventoryStorageCoordinates.ToBusiness(
                    new(backing.Type, item.SlotIndex), item.Core.ItemKind);
                if (slot.Space == space) local.Add(item with { SlotIndex = slot.Index });
            }
            catch (InventorySlotMappingException) when (
                backing.Type == InventoryContainerType.Main
                && item.SlotIndex is >= 0 and <= 2 or >= 352 and <= 364)
            {
                // Legacy virtual slots are not ordinary business items.
            }
        }
        return local;
    }

    public async ValueTask PutAsync(int index, InventoryItemCoreState core, CancellationToken cancellationToken)
    {
        var physical = await WritableIndexAsync(index, cancellationToken).ConfigureAwait(false);
        await backing.PutAsync(physical, core, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask PutTransferredAsync(int index, InventoryItemCoreState core,
        IBusinessItemContainer source, int sourceIndex, CancellationToken cancellationToken)
    {
        var physical = await WritableIndexAsync(index, cancellationToken).ConfigureAwait(false);
        var from = LegacyInventoryStorageCoordinates.ToStorage(new(source.Space, sourceIndex));
        await backing.PutTransferredAsync(physical, core, source.StorageId, from.Index,
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask PutNewAsync(int index, InventoryItemCoreState core, InventoryItemCreationFacts facts,
        CancellationToken cancellationToken)
    {
        var physical = await WritableIndexAsync(index, cancellationToken).ConfigureAwait(false);
        await backing.PutNewAsync(physical, core, facts, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask ClearAsync(int index, CancellationToken cancellationToken)
    {
        var physical = this.StorageIndex(new(space, index));
        var current = await backing.ReadSlotAsync(physical, cancellationToken).ConfigureAwait(false);
        if (current is null) return;
        var owner = LegacyInventoryStorageCoordinates.ToBusiness(
            new(backing.Type, physical), current.Core.ItemKind);
        if (owner != new BusinessSlot(space, index))
            throw new InvalidDataException($"Physical slot {backing.Type}/{physical} contains {owner}, not {space}/{index}.");
        await backing.ClearAsync(physical, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<int> WritableIndexAsync(int index, CancellationToken ct)
    {
        var physical = this.StorageIndex(new(space, index));
        var existing = await backing.ReadSlotAsync(physical, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            var owner = LegacyInventoryStorageCoordinates.ToBusiness(
                new(backing.Type, physical), existing.Core.ItemKind);
            if (owner != new BusinessSlot(space, index))
                throw new InvalidDataException($"Physical slot {backing.Type}/{physical} contains {owner}; refusing to overwrite it with {space}/{index}.");
        }
        return physical;
    }

    public ValueTask FlushAsync(CancellationToken cancellationToken) =>
        backing.FlushAsync(cancellationToken);

    public async ValueTask<BusinessItemAdditionResult> AddAsync(int itemId, int count,
        CancellationToken cancellationToken)
    {
        if (placements is null || arrivals is null)
            throw new NotSupportedException("This business container has no item arrival rules.");
        var placement = await placements.PlacementForAsync(itemId, cancellationToken).ConfigureAwait(false);
        if (placement is null) return new(this, new ItemPlacementOutcome.UnknownItem());
        if (placement.Destination.Space != space)
            throw new InvalidOperationException($"Item {itemId} belongs to {placement.Destination.Space}, not {space}.");
        var quick = backing.Type == InventoryContainerType.Main
            && ItemSlotRuleService.AllowsQuickSlot(placement.ItemKind)
            ? new BusinessItemContainer(BusinessInventorySpace.QuickSlot, backing, placements, arrivals, creatures)
            : null;
        var own = await ReadAllAsync(cancellationToken).ConfigureAwait(false);
        var held = own.Select(item => (Slot: new BusinessSlot(space, item.SlotIndex), Item: item))
            .Concat(quick is null ? [] : (await quick.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                .Select(item => (Slot: new BusinessSlot(BusinessInventorySpace.QuickSlot, item.SlotIndex), Item: item)))
            .ToArray();
        var occupied = held.Select(entry => entry.Slot).ToHashSet();
        var piles = held.Where(entry => entry.Item.ItemId == itemId)
            .Select(entry => new ItemPile(entry.Slot, entry.Item.Count)).ToArray();
        var arrival = arrivals.ForGrant(piles, count, placement, occupied);
        switch (arrival)
        {
            case ItemArrival.OntoExistingPile pile:
                var target = pile.Slot.Space == space ? this : quick
                    ?? throw new InvalidDataException("Arrival selected a quick slot without a quick container.");
                var existing = held.Single(entry => entry.Slot == pile.Slot).Item;
                await target.PutAsync(pile.Slot.Index, existing.Core with { Value = pile.Total }, cancellationToken)
                    .ConfigureAwait(false);
                await target.FlushAsync(cancellationToken).ConfigureAwait(false);
                return new(target, new ItemPlacementOutcome.AddedToPile(pile.Slot.Index, pile.Added, pile.Total));
            case ItemArrival.IntoEmptySlot empty:
                var destination = empty.Slot.Space == space ? this : quick
                    ?? throw new InvalidDataException("Arrival selected a quick slot without a quick container.");
                if (placement.ItemKind == 0)
                    throw new InvalidDataException($"Item {itemId} has no item kind.");
                var core = StackableItemCore.Create(placement.ItemKind, itemId, empty.Count);
                if (placement.WorldClass == ItemWorldClass.Creature)
                {
                    var creature = creatures is null ? null
                        : await creatures.FindByEquipmentAsync(itemId, cancellationToken).ConfigureAwait(false);
                    if (creature is null)
                        throw new InvalidDataException($"Creature template {itemId} has no creature definition.");
                    await destination.PutNewAsync(empty.Slot.Index,
                        core with { Value = ArrivingAvatar.NewIdentity() },
                        new InventoryItemCreationFacts(Creature: new(100, 0, 0, 0, 0,
                            creature.StartLevel, [], 0)), cancellationToken).ConfigureAwait(false);
                }
                else if (placement.ItemKind == ArrivingAvatar.ItemKind)
                    await destination.PutNewAsync(empty.Slot.Index,
                        core with { Value = ArrivingAvatar.NewIdentity() },
                        new InventoryItemCreationFacts(Avatar: ArrivingAvatar.Facts()),
                        cancellationToken).ConfigureAwait(false);
                else
                    await destination.PutAsync(empty.Slot.Index, core, cancellationToken).ConfigureAwait(false);
                await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
                return new(destination, new ItemPlacementOutcome.PutInEmptySlot(empty.Slot.Index, empty.Count));
            case ItemArrival.NoRoom:
                return new(this, new ItemPlacementOutcome.NoRoom());
            default:
                throw new ArgumentOutOfRangeException(nameof(arrival), arrival, null);
        }
    }

    private int StorageIndex(BusinessSlot slot)
    {
        var address = LegacyInventoryStorageCoordinates.ToStorage(slot);
        if (address.List != backing.Type)
            throw new InvalidOperationException($"{slot.Space} is not backed by {backing.Type}.");
        return address.Index;
    }
}
