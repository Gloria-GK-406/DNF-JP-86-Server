using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

// The registry exposes this operation; this collaborator owns routing and full-quantity delivery.
internal sealed class CharacterContainerAddition(IItemPlacementRuleService placements,
    IInventorySlotMappingRuleService mapping, IAccountMaterialRuleService accountRules,
    IAccountMaterialBagDomainService accountBags, ICharacterTable owners,
    DomainTransactionScope transactions, IItemDomainStore store,
    IItemArrivalRuleService arrivals, IItemSlotRuleService slotRules)
{
    public async ValueTask<ContainerAddition> AddAsync(ICharacterContainerDomainService containers,
        CharacterId character, int itemId, int count, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(itemId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        var lease = transactions.Current is null ? transactions.Begin() : null;
        try
        {
            var owner = await owners.FindOwnerAsync(character, ct)
                ?? throw new ContainerAdditionException(ContainerAdditionFailure.CharacterNotFound);
            ContainerAddition result;
            if (accountRules.TryGetByItemId(itemId, out var material))
            {
                var changes = await accountBags.Get(owner).AddAsync(
                    new Dictionary<AccountMaterialKind, int> { [material.Kind] = count }, ct);
                result = new([], changes.Single());
            }
            else
            {
                var placement = await placements.PlacementForAsync(itemId, ct)
                    ?? throw new ContainerAdditionException(ContainerAdditionFailure.UnknownItem);
                var type = mapping.ToGameSlot(new BusinessSlot(placement.Destination.Space, placement.Destination.FirstIndex)).Space;
                // Honor a character's current (possibly temporary) bag. Offline delivery uses
                // the persistent bag without publishing a fictitious online registration.
                var bag = containers.FindContainer(character, type) ?? new PersistentItemContainer(
                    ContainerId.ForCharacter(character, type), character, new(type, 0), store,
                    transactions, null, placements, arrivals, slotRules, mapping);
                // Full-quantity rollback currently relies on the store-backed transaction contract.
                // Do not mutate an arbitrary in-memory implementation that cannot be rolled back.
                if (bag is not StoreBackedItemContainer)
                    throw new NotSupportedException("Whole-quantity addition requires a transactional store-backed container.");
                if (bag.Id.TryGetCharacterOwner(out var bagOwner, out var bagType)
                    && (bagOwner != character || bagType != type))
                    throw new InvalidOperationException("The selected container belongs to another character or inventory type.");
                var changes = new List<ContainerItemAddition>();
                var remaining = count;
                while (remaining > 0)
                {
                    var outcome = await bag.AddAsync(itemId, remaining, ct);
                    var (slot, added) = outcome switch
                    {
                        ItemPlacementOutcome.AddedToPile p => (p.SlotIndex, p.Added),
                        ItemPlacementOutcome.PutInEmptySlot p => (p.SlotIndex, p.Count),
                        ItemPlacementOutcome.NoRoom => throw new ContainerAdditionException(ContainerAdditionFailure.NoRoom),
                        ItemPlacementOutcome.UnknownItem => throw new ContainerAdditionException(ContainerAdditionFailure.UnknownItem),
                        _ => throw new InvalidOperationException("Unsupported container addition outcome.")
                    };
                    if (added <= 0 || added > remaining) throw new InvalidDataException("Invalid container addition progress.");
                    changes.Add(new(bag.Id, bag.Type, slot, added));
                    remaining -= added;
                }
                result = new(changes);
            }
            if (lease is not null) await lease.CommitAsync(ct);
            return result;
        }
        catch
        {
            if (transactions.Current is DomainTransaction active) await active.RollbackAsync();
            throw;
        }
        finally { if (lease is not null) await lease.DisposeAsync(); }
    }
}
