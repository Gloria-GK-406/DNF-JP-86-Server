using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Transactions;
using Dfo.Server.Application.Transactions;
using Dfo.Server.Data.Characters;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

internal sealed class EquipmentTransferDomainService(
    IEquipmentTransferDomainStore transfers,
    IItemDomainStore items,
    IDomainTransactionAccessor transactions,
    IItemPlacementRuleService placements,
    IItemArrivalRuleService arrivals,
    IInventorySlotMappingRuleService slots) : IEquipmentTransferDomainService
{
    public ValueTask<EquipmentTransferItem?> ReadOwnedAsync(
        CharacterId characterId, ItemIdentity identity, CancellationToken cancellationToken) =>
        this.ReadAsync(session => session.ReadOwnedAsync(characterId, identity, cancellationToken), cancellationToken);

    public async ValueTask<EquipmentTransferItem> TakeAsync(
        CharacterId characterId, ItemIdentity identity, CancellationToken cancellationToken)
    {
        var transaction = this.RequireTransaction();
        await items.SessionIn(transaction).ApplyDecidedWritesAsync(cancellationToken).ConfigureAwait(false);
        var session = transfers.SessionIn(transaction);
        var item = await session.ReadOwnedAsync(characterId, identity, cancellationToken).ConfigureAwait(false)
            ?? throw new EquipmentTransferException(EquipmentTransferFailure.Missing);
        var placement = await placements.PlacementForAsync(item.Core.ItemId, cancellationToken)
            .ConfigureAwait(false);
        if (item.ContainerType != InventoryContainerType.Main
            || placement is not { WorldClass: ItemWorldClass.Equipment }
            || item.Core.ItemKind != placement.ItemKind
            || item.Core.EquipmentLockId != 0
            || item.Core.TradeRestriction != 0)
            throw new EquipmentTransferException(EquipmentTransferFailure.NotSendable);

        if (!await session.TakeAsync(characterId, item, cancellationToken).ConfigureAwait(false))
            throw new EquipmentTransferException(EquipmentTransferFailure.StalePosition);
        return item;
    }

    public async ValueTask<InventoryItemCoreState?> ReadDetachedAsync(
        ItemIdentity identity, CancellationToken cancellationToken)
    {
        var core = await this.ReadAsync(
            session => session.ReadDetachedAsync(identity, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        if (core is null)
            return null;
        var placement = await placements.PlacementForAsync(core.ItemId, cancellationToken)
            .ConfigureAwait(false);
        if (placement is not { WorldClass: ItemWorldClass.Equipment }
            || core.ItemKind != placement.ItemKind)
            throw new EquipmentTransferException(EquipmentTransferFailure.NotSendable);
        return core;
    }

    public async ValueTask<GameSlot> GrantAsync(
        CharacterId characterId, ItemIdentity identity, CancellationToken cancellationToken)
    {
        var transaction = this.RequireTransaction();
        var itemSession = items.SessionIn(transaction);
        await itemSession.ApplyDecidedWritesAsync(cancellationToken).ConfigureAwait(false);
        var session = transfers.SessionIn(transaction);
        var core = await session.ReadDetachedAsync(identity, cancellationToken).ConfigureAwait(false)
            ?? throw new EquipmentTransferException(EquipmentTransferFailure.NotDetached);
        var placement = await placements.PlacementForAsync(core.ItemId, cancellationToken)
            .ConfigureAwait(false);
        if (placement is not { WorldClass: ItemWorldClass.Equipment }
            || core.ItemKind != placement.ItemKind)
            throw new EquipmentTransferException(EquipmentTransferFailure.NotSendable);

        var held = await itemSession.ReadAllAsync(
            ContainerId.ForCharacter(characterId, InventoryContainerType.Main), cancellationToken)
            .ConfigureAwait(false);
        var occupied = new HashSet<BusinessSlot>();
        foreach (var item in held)
        {
            try
            {
                occupied.Add(slots.ToBusinessSlot(
                    new GameSlot(InventoryContainerType.Main, item.SlotIndex)));
            }
            catch (InventorySlotMappingException)
            {
                // Virtual positions in the main list do not consume an item band slot.
            }
        }
        var arrival = arrivals.ForGrant([], 1, placement, occupied);
        if (arrival is not ItemArrival.IntoEmptySlot empty || empty.Count != 1)
            throw new EquipmentTransferException(EquipmentTransferFailure.NoRoom);
        var slot = slots.ToGameSlot(empty.Slot);
        if (slot.Space != InventoryContainerType.Main
            || !await session.GrantAsync(characterId, identity, slot, cancellationToken)
                .ConfigureAwait(false))
            throw new EquipmentTransferException(EquipmentTransferFailure.StalePosition);
        return slot;
    }

    private IDomainTransaction RequireTransaction() => transactions.Current
        ?? throw new InvalidOperationException("Equipment transfer requires a use case transaction.");

    private async ValueTask<T> ReadAsync<T>(
        Func<IEquipmentTransferDomainStoreSession, ValueTask<T>> read,
        CancellationToken cancellationToken)
    {
        if (transactions.Current is { } ambient)
        {
            await items.SessionIn(ambient).ApplyDecidedWritesAsync(cancellationToken).ConfigureAwait(false);
            return await read(transfers.SessionIn(ambient)).ConfigureAwait(false);
        }

        var alone = new DomainTransaction();
        try
        {
            return await read(transfers.SessionIn(alone)).ConfigureAwait(false);
        }
        finally
        {
            await alone.RollbackAsync().ConfigureAwait(false);
        }
    }
}
