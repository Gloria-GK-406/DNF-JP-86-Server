using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

// The registry exposes this operation; this collaborator owns routing and full-quantity delivery.
internal sealed class CharacterContainerAddition(IItemPlacementRuleService placements, ICharacterTable owners,
    DomainTransactionScope transactions, IItemDomainStore store,
    IItemArrivalRuleService arrivals, IItemSlotRuleService slotRules, IItemLifecycleDomainService lifecycle,
    ICreatureDefinitionSource? creatures = null, IInventoryCapacityDomainService? capacities = null)
{
    public async ValueTask<ContainerAddition> AddAsync(ICharacterContainerDomainService containers,
        CharacterId character, int itemId, int count, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(itemId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        var lease = transactions.Current is null ? transactions.Begin() : null;
        try
        {
            var account = await owners.FindOwnerAsync(character, ct)
                ?? throw new ContainerAdditionException(ContainerAdditionFailure.CharacterNotFound);
            var placement = await placements.PlacementForAsync(itemId, ct)
                ?? throw new ContainerAdditionException(ContainerAdditionFailure.UnknownItem);
            var type = LegacyInventoryStorageCoordinates.ToStorage(
                new BusinessSlot(placement.Destination.Space, placement.Destination.FirstIndex)).List;
            var accountOwned = type is InventoryContainerType.AccountCargo or InventoryContainerType.SharedMaterial;
            var expectedOwner = accountOwned ? new ContainerOwner(InventoryOwnerType.Account, account.Value)
                : new ContainerOwner(InventoryOwnerType.Character, character.Value);
            // Honor a selected transactional container, validating both ownership and its kind.
            // Offline delivery does not manufacture an online registration.
            var bag = containers.FindContainer(character, type) ?? new PersistentItemContainer(
                accountOwned ? ContainerId.ForAccount(account, type) : ContainerId.ForCharacter(character, type),
                character, new(type, 0), store, transactions, null, placements, arrivals, slotRules, capacities);
            if (bag is not StoreBackedItemContainer)
                throw new NotSupportedException("Whole-quantity addition requires a transactional store-backed container.");
            if (!bag.Id.TryGetOwner(out var actualOwner, out var actualType)
                || actualOwner != expectedOwner || actualType != type || bag.Type != type)
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
            if (lease is not null) await lease.CommitAsync(ct);
            return new(changes);
        }
        catch
        {
            if (transactions.Current is DomainTransaction active) await active.RollbackAsync();
            throw;
        }
        finally { if (lease is not null) await lease.DisposeAsync(); }
    }
}
