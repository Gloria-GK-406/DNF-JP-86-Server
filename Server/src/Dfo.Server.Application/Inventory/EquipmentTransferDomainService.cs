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
    ICharacterBusinessInventoryDomainService inventories) : IEquipmentTransferDomainService
{
    public async ValueTask<EquipmentTransferOwnedItem?> ReadOwnedAsync(
        CharacterId characterId, ItemIdentity identity, CancellationToken cancellationToken)
    {
        var stored = await this.ReadAsync(
            session => session.ReadOwnedAsync(characterId, identity, cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        return stored is null ? null : Business(stored);
    }

    public async ValueTask<EquipmentTransferOwnedItem> TakeAsync(
        CharacterId characterId, ItemIdentity identity, CancellationToken cancellationToken)
    {
        var transaction = this.RequireTransaction();
        await items.SessionIn(transaction).ApplyDecidedWritesAsync(cancellationToken).ConfigureAwait(false);
        var session = transfers.SessionIn(transaction);
        var item = await session.ReadOwnedAsync(characterId, identity, cancellationToken).ConfigureAwait(false)
            ?? throw new EquipmentTransferException(EquipmentTransferFailure.Missing);
        var placement = await placements.PlacementForAsync(item.Core.ItemId, cancellationToken)
            .ConfigureAwait(false);
        var source = Business(item);
        if (!CanCarry(source.Slot.Space)
            || placement is not { WorldClass: ItemWorldClass.Equipment }
            || item.Core.ItemKind != placement.ItemKind
            || item.Core.EquipmentLockId != 0
            || item.Core.TradeRestriction != 0)
            throw new EquipmentTransferException(EquipmentTransferFailure.NotSendable);

        if (!await session.TakeAsync(characterId, item, cancellationToken).ConfigureAwait(false))
            throw new EquipmentTransferException(EquipmentTransferFailure.StalePosition);
        return source;
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

    public async ValueTask<BusinessSlot> GrantAsync(
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

        var target = inventories.GetStore(characterId,
            BusinessInventoryOwnership.StoreOf(placement.Destination.Space));
        var occupied = new HashSet<BusinessSlot>();
        var spaces = ItemSlotRuleService.AllowsQuickSlot(placement.ItemKind)
            ? new[] { placement.Destination.Space, BusinessInventorySpace.QuickSlot }
            : [placement.Destination.Space];
        foreach (var space in spaces.Distinct())
        {
            var inventory = space == placement.Destination.Space ? target.GetContainer(space)
                : inventories.GetStore(characterId, BusinessInventoryStoreKind.Item).GetContainer(space);
            foreach (var item in await inventory.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                occupied.Add(new BusinessSlot(space, item.SlotIndex));
        }
        var arrival = arrivals.ForGrant([], 1, placement, occupied);
        if (arrival is not ItemArrival.IntoEmptySlot empty || empty.Count != 1)
            throw new EquipmentTransferException(EquipmentTransferFailure.NoRoom);
        var slot = LegacyInventoryStorageCoordinates.ToStorage(empty.Slot);
        if (!await session.GrantAsync(characterId, identity, slot, cancellationToken)
                .ConfigureAwait(false))
            throw new EquipmentTransferException(EquipmentTransferFailure.StalePosition);
        return empty.Slot;
    }

    private static EquipmentTransferOwnedItem Business(EquipmentTransferItem stored) =>
        new(stored.Identity, LegacyInventoryStorageCoordinates.ToBusiness(
            new(stored.ContainerType, stored.SlotIndex), stored.Core.ItemKind), stored.Core);

    private static bool CanCarry(BusinessInventorySpace space) =>
        space.IsCarriedItemSpace() || space is BusinessInventorySpace.Avatar
            or BusinessInventorySpace.CreatureInventory or BusinessInventorySpace.CreatureEquipment
            or BusinessInventorySpace.CreatureConsumable;

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
