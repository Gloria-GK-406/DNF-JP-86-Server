using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Abstractions.Creatures;

namespace Dfo.Server.Application.Inventory;

// Item grants own routing and full-quantity delivery.
internal sealed class ItemGrantDelivery(IItemPlacementRuleService placements, ICharacterTable owners,
    DomainTransactionScope transactions, IItemDomainStore store,
    IItemArrivalRuleService arrivals, IItemSlotRuleService slotRules, IItemLifecycleDomainService lifecycle,
    ICreatureRuleService creatures, IInventoryCapacityDomainService capacities)
{
    public async ValueTask<ContainerAddition> AddAsync(ICharacterContainerDomainService containers,
        CharacterId character, int itemId, int count, CancellationToken ct, bool persistent = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(itemId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        // The caller's unit settles the grant; this delivery never opens a boundary of its own.
        if (transactions.Current is null)
            throw new InvalidOperationException("Item grants require the caller's transaction.");
        try
        {
            var account = await owners.FindOwnerAsync(character, ct)
                ?? throw new ContainerAdditionException(ContainerAdditionFailure.CharacterNotFound);
            var placement = await placements.PlacementForAsync(itemId, ct)
                ?? throw new ContainerAdditionException(ContainerAdditionFailure.UnknownItem);
            var type = LegacyInventoryStorageCoordinates.ToStorage(
                new BusinessSlot(placement.Destination.Space, placement.Destination.FirstIndex)).List;
            if (persistent && placement.Destination.Space == BusinessInventorySpace.TowerQuickSlot)
                throw new InvalidOperationException("Tower items have no persistent destination.");
            var accountOwned = type is InventoryContainerType.AccountCargo or InventoryContainerType.SharedMaterial;
            var expectedOwner = accountOwned ? new ContainerOwner(InventoryOwnerType.Account, account.Value)
                : new ContainerOwner(InventoryOwnerType.Character, character.Value);
            var bag = persistent ? new PersistentItemContainer(
                accountOwned ? ContainerId.ForAccount(account, type) : ContainerId.ForCharacter(character, type),
                new(type, 0), store, transactions, placements, slotRules, capacities)
                : containers.GetContainer(character, type);
            if (bag is not StoreBackedItemContainer)
                throw new NotSupportedException("Whole-quantity addition requires a transactional store-backed container.");
            if (bag.Type != type || bag.IsPersistent && (!bag.Id.TryGetOwner(out var actualOwner, out var actualType)
                || actualOwner != expectedOwner || actualType != type))
                throw new InvalidOperationException("The selected container belongs to another owner or inventory type.");
            var business = new BusinessItemContainer(placement.Destination.Space, bag, placements, arrivals, creatures, capacities, lifecycle);
            var changes = new List<ContainerItemAddition>();
            var remaining = count;
            while (remaining > 0)
            {
                var addition = await business.AddAsync(itemId, remaining, ct);
                var (slot, added) = addition.Placement switch
                {
                    ItemPlacementOutcome.AddedToPile p => (p.SlotIndex, p.Added),
                    ItemPlacementOutcome.PutInEmptySlot p => (p.SlotIndex, p.Count),
                    ItemPlacementOutcome.NoRoom => throw new ContainerAdditionException(ContainerAdditionFailure.NoRoom),
                    ItemPlacementOutcome.UnknownItem => throw new ContainerAdditionException(ContainerAdditionFailure.UnknownItem),
                    _ => throw new InvalidOperationException("Unsupported container addition outcome."),
                };
                if (added <= 0 || added > remaining) throw new InvalidDataException("Invalid container addition progress.");
                changes.Add(new(addition.Container.Id, new BusinessSlot(addition.Container.Space, slot), added));
                remaining -= added;
            }
            return new(changes);
        }
        catch
        {
            // Deliberately the caller's whole transaction: a caller that catches the refusal must not
            // be able to commit the piles this loop already added.
            if (transactions.Current is DomainTransaction active) await active.RollbackAsync();
            throw;
        }
    }
}
