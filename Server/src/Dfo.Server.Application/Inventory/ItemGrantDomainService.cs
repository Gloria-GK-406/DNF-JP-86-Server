using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

internal sealed class ItemGrantDomainService(ICharacterBusinessInventoryDomainService inventories,
    IItemPlacementRuleService placements, IItemLifecycleDomainService lifecycle,
    IItemDomainStore items, IDomainTransactionAccessor transactions, ICharacterTable owners,
    ItemGrantDelivery delivery, ICharacterContainerDomainService containers,
    IInventoryCapacityDomainService capacities) : IItemGrantDomainService
{
    public ValueTask<ContainerAddition> GrantAllAsync(CharacterId character, int itemId, int count, CancellationToken ct) =>
        delivery.AddAsync(containers, character, itemId, count, ct);
    public ValueTask<ContainerAddition> GrantAllPersistentAsync(CharacterId character, int itemId, int count, CancellationToken ct) =>
        delivery.AddAsync(containers, character, itemId, count, ct, persistent: true);
    private IDomainTransaction Transaction => transactions.Current ?? throw new InvalidOperationException("Item grants require a domain transaction.");
    public async ValueTask<ItemReference> ApplyPlannedAsync(IBusinessItemContainer container, int slot, ItemSnapshot next, CancellationToken ct)
    {
        _ = Transaction;
        next = await lifecycle.PrepareNewAsync(next, ct).ConfigureAwait(false);
        var current = await container.GetAsync(slot, ct).ConfigureAwait(false);
        if (current is null) return await container.PutAsync(slot, await lifecycle.CreateAsync(next, ct).ConfigureAwait(false), ct).ConfigureAwait(false);
        ValidateStack(current, next);
        return await container.UpdateAsync(current, value => value with { Count = next.Count }, ct).ConfigureAwait(false);
    }
    public async ValueTask<ItemReference> ApplyPlannedAsync(IItemContainer container, int slot, ItemSnapshot next, CancellationToken ct)
    {
        _ = Transaction;
        next = await lifecycle.PrepareNewAsync(next, ct).ConfigureAwait(false);
        var current = await container.GetAsync(slot, ct).ConfigureAwait(false);
        if (current is null) return await container.PutAsync(slot, await lifecycle.CreateAsync(next, ct).ConfigureAwait(false), ct).ConfigureAwait(false);
        ValidateStack(current, next);
        return await container.UpdateAsync(current, value => value with { Count = next.Count }, ct).ConfigureAwait(false);
    }
    private static void ValidateStack(ItemReference current, ItemSnapshot next)
    {
        if (!current.Snapshot.CanStackWith(next) || next.Count < current.Count)
            throw new InvalidOperationException("Item grant target changed after planning.");
    }
    public async ValueTask<ItemReference> PutNewAsync(ContainerId container, int slot, ItemSnapshot item, CancellationToken ct)
    {
        var session = items.SessionIn(Transaction);
        if (!container.TryGetOwner(out _, out var type)) throw new ArgumentException("A new persistent item needs a canonical owner.", nameof(container));
        if (!(type == InventoryContainerType.Main && slot is 1 or 2))
        {
            var business = LegacyInventoryStorageCoordinates.ToBusiness(new(type, slot), item.LegacyKind);
            if (business.Index >= await capacities.OpenSlotCountAsync(container, business.Space, ct).ConfigureAwait(false))
                throw new ContainerAdditionException(ContainerAdditionFailure.NoRoom);
        }
        if (await session.GetAsync(container, slot, ct).ConfigureAwait(false) is not null)
            throw new InvalidOperationException("New item target is occupied.");
        return await session.PutAsync(container, slot, await lifecycle.CreateAsync(item, ct).ConfigureAwait(false), ct).ConfigureAwait(false);
    }
    public async ValueTask<BusinessItemAdditionResult> GrantAsync(AccountId accountId, CharacterId characterId,
        ICharacterContainerDomainService containers, int itemId, int count, CancellationToken cancellationToken, bool isDefault = false)
    {
        ArgumentNullException.ThrowIfNull(containers);
        _ = Transaction;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(itemId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        if (await owners.FindOwnerAsync(characterId, cancellationToken).ConfigureAwait(false) != accountId)
            throw new InvalidOperationException("Item recipient is unavailable.");
        var placement = await placements.PlacementForAsync(itemId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException($"Item {itemId} has no placement definition.");
        var space = placement.Destination.Space;
        var kind = BusinessInventoryOwnership.StoreOf(space);
        var container = inventories.GetStore(characterId, kind).GetContainer(space, isDefault);
        return await container.AddAsync(itemId, count, cancellationToken).ConfigureAwait(false);
    }
}
