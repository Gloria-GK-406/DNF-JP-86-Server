using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

internal sealed class CharacterBusinessInventoryDomainService(ICharacterContainerDomainService backing,
    IItemPlacementRuleService placements, IItemArrivalRuleService arrivals,
    ICreatureDefinitionSource creatures, IItemDomainStore items,
    IDomainTransactionAccessor transactions, IItemSlotRuleService slotRules,
    IInventoryCapacityDomainService capacities, IItemLifecycleDomainService lifecycle)
    : ICharacterBusinessInventoryDomainService
{
    public IBusinessInventoryStore GetStore(CharacterId characterId, BusinessInventoryStoreKind kind)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(characterId.Value);
        return new BusinessInventoryStore(characterId, kind, backing, placements, arrivals, creatures, capacities, lifecycle);
    }

    public IBusinessInventoryStore GetPersistentStore(ContainerOwner owner, BusinessInventoryStoreKind kind)
    {
        if (owner.Value <= 0 || !Enum.IsDefined(owner.Scope)) throw new ArgumentOutOfRangeException(nameof(owner));
        return new PersistentBusinessInventoryStore(owner, kind, items, transactions, placements, slotRules,
            arrivals, creatures, capacities, lifecycle);
    }

    private sealed class BusinessInventoryStore(CharacterId characterId, BusinessInventoryStoreKind kind,
        ICharacterContainerDomainService backing, IItemPlacementRuleService placements,
        IItemArrivalRuleService arrivals, ICreatureDefinitionSource creatures,
        IInventoryCapacityDomainService capacities, IItemLifecycleDomainService lifecycle) : IBusinessInventoryStore
    {
        public BusinessInventoryStoreKind Kind => kind;
        public IBusinessItemContainer? FindContainer(BusinessInventorySpace space, bool isDefault = false)
        {
            CheckStore(space, kind);
            var address = LegacyInventoryStorageCoordinates.ToStorage(new BusinessSlot(space, 0));
            var container = backing.FindContainer(characterId, address.List, isDefault);
            return container is null ? null : new BusinessItemContainer(space, container, placements, arrivals, creatures, capacities, lifecycle);
        }
        public IBusinessItemContainer GetContainer(BusinessInventorySpace space, bool isDefault = false) =>
            FindContainer(space, isDefault) ?? throw new InvalidOperationException($"Character {characterId.Value} has no registered {space} container.");
    }

    private sealed class PersistentBusinessInventoryStore(ContainerOwner owner, BusinessInventoryStoreKind kind,
        IItemDomainStore items, IDomainTransactionAccessor transactions, IItemPlacementRuleService placements,
        IItemSlotRuleService slots, IItemArrivalRuleService arrivals, ICreatureDefinitionSource creatures,
        IInventoryCapacityDomainService capacities, IItemLifecycleDomainService lifecycle) : IBusinessInventoryStore
    {
        public BusinessInventoryStoreKind Kind => kind;
        public IBusinessItemContainer? FindContainer(BusinessInventorySpace space, bool isDefault = false) => GetContainer(space, isDefault);
        public IBusinessItemContainer GetContainer(BusinessInventorySpace space, bool isDefault = false)
        {
            CheckStore(space, kind);
            if (space == BusinessInventorySpace.TowerQuickSlot)
                throw new InvalidOperationException("Tower quick slots have no persistent storage.");
            var accountSpace = space is BusinessInventorySpace.AccountCargo or BusinessInventorySpace.SharedMaterial;
            if (accountSpace != (owner.Scope == InventoryOwnerType.Account))
                throw new InvalidOperationException($"{space} does not belong to {owner.Scope}.");
            var type = LegacyInventoryStorageCoordinates.ToStorage(new BusinessSlot(space, 0)).List;
            var id = owner.Scope == InventoryOwnerType.Account ? ContainerId.ForAccount(new AccountId(owner.Value), type)
                : ContainerId.ForCharacter(new CharacterId(owner.Value), type);
            return new BusinessItemContainer(space, new PersistentItemContainer(id, new(type, 0), items,
                transactions, placements, slots, capacities), placements, arrivals, creatures, capacities, lifecycle);
        }
    }
    private static void CheckStore(BusinessInventorySpace space, BusinessInventoryStoreKind kind)
    {
        if (BusinessInventoryOwnership.StoreOf(space) != kind)
            throw new InvalidOperationException($"{space} does not belong to the {kind} store.");
    }

}

internal sealed class BusinessItemContainer(BusinessInventorySpace space, IItemContainer backing,
    IItemPlacementRuleService? placements = null, IItemArrivalRuleService? arrivals = null,
    ICreatureDefinitionSource? creatures = null, IInventoryCapacityDomainService? capacities = null, IItemLifecycleDomainService? lifecycle = null)
    : IBusinessItemContainer
{
    public BusinessContainerId Id => new(space, backing.Id);
    public ContainerId StorageId { get; } = ValidateStorage(space, backing);
    public BusinessInventorySpace Space => space;
    public bool IsDefault => backing.IsDefault;
    public bool IsPersistent => backing.IsPersistent;
    public ItemAdmissionRule Admission => ItemContainerAdmission.For(space);

    public ValueTask<int> OpenSlotCountAsync(CancellationToken cancellationToken) =>
        !backing.IsPersistent ? ValueTask.FromResult(new InventoryCapacityRuleService().OpenSlotCount(space, backing.Shape.Parameter))
            : capacities is null ? ValueTask.FromResult(int.MaxValue)
            : capacities.OpenSlotCountAsync(backing.Id, space, cancellationToken);

    public async ValueTask<IEquipmentInstance?> GetEquipmentAsync(int index, CancellationToken ct)
    {
        var reference = await GetAsync(index, ct).ConfigureAwait(false);
        if (reference is null || reference.IsPile || !reference.Type.HasFlag(ItemTraits.Equipment)) return null;
        return new EquipmentInstance(this, reference, lifecycle
            ?? throw new InvalidOperationException("Equipment behavior requires item lifecycle services."));
    }

    public async ValueTask<ItemReference?> GetAsync(int index, CancellationToken cancellationToken)
    {
        var storageIndex = this.StorageIndex(new(space, index));
        var item = await backing.GetAsync(storageIndex, cancellationToken)
            .ConfigureAwait(false);
        if (item is not null && LegacyInventoryStorageCoordinates.ToBusiness(
                new(backing.Type, item.SlotIndex), item.Snapshot.LegacyKind).Space != space)
            return null;
        return item is null ? null : item.WithBusinessSlot(new(space, index));
    }

    public async ValueTask<IReadOnlyList<ItemReference>> GetAllAsync(CancellationToken cancellationToken)
    {
        var items = await backing.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var local = new List<ItemReference>();
        foreach (var item in items)
        {
            try
            {
                var slot = LegacyInventoryStorageCoordinates.ToBusiness(
                    new(backing.Type, item.SlotIndex), item.Snapshot.LegacyKind);
                if (slot.Space == space) local.Add(item.WithBusinessSlot(slot));
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

    private void CheckReference(ItemReference reference)
    {
        var physical = StorageIndex(new(space, reference.SlotIndex));
        if (reference.ContainerId != StorageId || reference.StorageSlotIndex != physical
            || reference.BusinessSlot != new BusinessSlot(space, reference.SlotIndex))
            throw new InvalidOperationException("Reference belongs to another business container.");
    }

    public ValueTask<ItemRecord> TakeAsync(ItemReference reference, int? count, CancellationToken ct)
    {
        CheckReference(reference);
        return backing.TakeAsync(reference, count, ct);
    }

    public async ValueTask<ItemReference> UpdateAsync(ItemReference reference,
        Func<ItemSnapshot, ItemSnapshot> change, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(change);
        CheckReference(reference);
        var open = await OpenSlotCountAsync(ct).ConfigureAwait(false);
        var result = await backing.UpdateAsync(reference, current =>
        {
            var next = change(current);
            if (reference.SlotIndex >= open
                && !(next.Count > 0 && next.Count < current.Count && current.CanStackWith(next)))
                throw new InvalidOperationException($"Inventory slot {space}/{reference.SlotIndex} is not open.");
            return next;
        }, ct).ConfigureAwait(false);
        return result.WithBusinessSlot(new(space, reference.SlotIndex));
    }

    public async ValueTask<ItemReference> PutAsync(int index, ItemRecord record, CancellationToken ct)
    {
        var physical = StorageIndex(new(space, index));
        var snapshot = record.Snapshot;
        var placement = placements is null ? null
            : await placements.PlacementForAsync(record.ItemId, space, ct).ConfigureAwait(false);
        if (!Admission.Matches(record.Type)
            || placement is not null && (record.Type != placement.Traits || snapshot.LegacyKind != placement.ItemKind
                || record.IsPile != (placement.WorldClass == ItemWorldClass.Interchangeable)))
            throw new InvalidOperationException($"Item {record.ItemId} cannot enter {space}.");
        if (index >= await OpenSlotCountAsync(ct).ConfigureAwait(false))
            throw new InvalidOperationException($"Inventory slot {space}/{index} is not open.");
        var existing = await backing.GetAsync(physical, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            var owner = LegacyInventoryStorageCoordinates.ToBusiness(new(backing.Type, physical), existing.Snapshot.LegacyKind);
            if (owner != new BusinessSlot(space, index))
                throw new InvalidDataException($"Physical slot {backing.Type}/{physical} belongs to {owner}.");
        }
        if (record.IsPile && placement is { StackLimit: > 0 }
            && (long)record.Count + (existing?.Count ?? 0) > placement.StackLimit)
            throw new InvalidOperationException("Placement exceeds the template stack limit.");
        var placed = await backing.PutAsync(physical, record, ct).ConfigureAwait(false);
        return placed.WithBusinessSlot(new(space, index));
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
        var creator = (backing is MemoryItemContainer memory ? memory.Lifecycle : lifecycle)
            ?? throw new InvalidOperationException("Item lifecycle service is missing.");
        var isPile = placement.WorldClass == ItemWorldClass.Interchangeable;
        var snapshot = new ItemSnapshot
        {
            ItemId = itemId, Count = 1, Type = placement.Traits, IsPile = isPile, LegacyKind = placement.ItemKind,
        };
        if (placement.WorldClass == ItemWorldClass.Creature)
        {
            var creature = creatures is null ? null : await creatures.FindByEquipmentAsync(itemId, cancellationToken).ConfigureAwait(false);
            if (creature is null) throw new InvalidDataException($"Creature template {itemId} has no definition.");
            snapshot = snapshot with { CreatureDetail = new(100, 0, 0, 0, 0, creature.StartLevel, [], 0) };
        }
        else if (placement.WorldClass == ItemWorldClass.Avatar) snapshot = snapshot with { AvatarDetail = ArrivingAvatar.Facts() };
        snapshot = await (lifecycle ?? creator).PrepareNewAsync(snapshot, cancellationToken).ConfigureAwait(false);
        var quick = backing.Type == InventoryContainerType.Main
            && ItemSlotRuleService.AllowsQuickSlot(placement)
            ? new BusinessItemContainer(BusinessInventorySpace.QuickSlot, backing, placements, arrivals, creatures, capacities, lifecycle)
            : null;
        var own = await GetAllAsync(cancellationToken).ConfigureAwait(false);
        var held = own.Select(item => (Slot: new BusinessSlot(space, item.SlotIndex), Item: item))
            .Concat(quick is null ? [] : (await quick.GetAllAsync(cancellationToken).ConfigureAwait(false))
                .Select(item => (Slot: new BusinessSlot(BusinessInventorySpace.QuickSlot, item.SlotIndex), Item: item)))
            .ToArray();
        var occupied = held.Select(entry => entry.Slot).ToHashSet();
        var open = await OpenSlotCountAsync(cancellationToken).ConfigureAwait(false);
        for (var index = open; index <= placement.Destination.LastIndex; index++)
            occupied.Add(new(space, index));
        var piles = held.Where(entry => entry.Item.Snapshot.CanStackWith(snapshot)
                && (entry.Slot.Space != space || entry.Slot.Index < open))
            .Select(entry => new ItemPile(entry.Slot, entry.Item.Count)).ToArray();
        var arrival = arrivals.ForGrant(piles, count, placement, occupied);
        switch (arrival)
        {
            case ItemArrival.OntoExistingPile pile:
                var target = pile.Slot.Space == space ? this : quick
                    ?? throw new InvalidDataException("Arrival selected a quick slot without a quick container.");
                var existing = held.Single(entry => entry.Slot == pile.Slot).Item;
                await target.UpdateAsync(existing, current => current with { Count = pile.Total }, cancellationToken)
                    .ConfigureAwait(false);
                await target.FlushAsync(cancellationToken).ConfigureAwait(false);
                return new(target, new ItemPlacementOutcome.AddedToPile(pile.Slot.Index, pile.Added, pile.Total));
            case ItemArrival.IntoEmptySlot empty:
                var destination = empty.Slot.Space == space ? this : quick
                    ?? throw new InvalidDataException("Arrival selected a quick slot without a quick container.");
                if (placement.ItemKind == 0)
                    throw new InvalidDataException($"Item {itemId} has no item kind.");
                var record = await creator.CreateAsync(snapshot with
                { Count = isPile ? empty.Count : 1, InstanceValue = isPile ? 0 : ArrivingAvatar.NewIdentity() }, cancellationToken).ConfigureAwait(false);
                await destination.PutAsync(empty.Slot.Index, record, cancellationToken).ConfigureAwait(false);
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

    private static ContainerId ValidateStorage(BusinessInventorySpace space, IItemContainer backing)
    {
        if (backing.IsPersistent && space == BusinessInventorySpace.SharedMaterial
            && (!backing.Id.TryGetAccountOwner(out _, out var type) || type != InventoryContainerType.SharedMaterial
                || backing.Type != InventoryContainerType.SharedMaterial))
            throw new InvalidOperationException($"Shared material container {backing.Id} must belong to an account.");
        return backing.Id;
    }
}
