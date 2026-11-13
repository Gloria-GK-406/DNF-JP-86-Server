using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;
using Dfo.Server.Abstractions.World.IllusionTower;

namespace Dfo.Server.Application.Inventory;

internal sealed class CargoTransferDomainService(ISelectedSessionDomainService sessions,
    IDungeonParticipationDomainService participation,
    IBusinessInventorySlotDomainService slots, ICargoItemRuleService rules,
    IItemMovementDomainService movement, IItemPlacementRuleService placements, IItemArrivalRuleService arrivals,
    TimeProvider time) : ICargoTransferDomainService
{
    public async ValueTask<ItemMoveOutcome> MoveAsync(SelectedSessionIdentity session, BusinessSlot source,
        BusinessSlot destination, int count, CancellationToken cancellationToken)
    {
        if (!sessions.IsInWorld(session) || participation.Find(session.CharacterId) is not null
            || !Served(source.Space) || !Served(destination.Space))
            return new ItemMoveOutcome.Refused(ItemMoveRefusal.ItemCannotOccupySlot);
        var from = slots.Find(session.CharacterId, source);
        var into = slots.Find(session.CharacterId, destination);
        if (from is null || into is null || !Owned(from) || !Owned(into))
            return new ItemMoveOutcome.Refused(ItemMoveRefusal.ItemCannotOccupySlot);
        var first = await from.Container.GetAsync(source.Index, cancellationToken).ConfigureAwait(false);
        var second = await into.Container.GetAsync(destination.Index, cancellationToken).ConfigureAwait(false);
        if (first is null && second is null) return new ItemMoveOutcome.Refused(ItemMoveRefusal.BothSlotsEmpty);
        if (first is null) (from, into, first, second) = (into, from, second, first);
        if (from.Container.StorageId != into.Container.StorageId)
        {
            if (Cargo(into.Slot.Space) && !await rules.CanStoreAsync(into.Slot.Space, first!, time.GetUtcNow(), cancellationToken).ConfigureAwait(false))
                return new ItemMoveOutcome.Refused(ItemMoveRefusal.ItemCannotOccupySlot);
            // Deposits and withdrawals never take the nominated slot at face value: the client
            // nominates an empty slot even when a pile of the same item is already there, and an
            // occupied one would otherwise turn into a swap that throws stored items back out.
            // Shared materials keep one fixed slot per item, so they stay on the direct path.
            if (into.Slot.Space != BusinessInventorySpace.SharedMaterial && !first!.Snapshot.IsMirror
                && second?.Snapshot.IsMirror != true)
                return await PlaceAsync(from, into, first, count, cancellationToken).ConfigureAwait(false);
            if (second is not null && Cargo(from.Slot.Space)
                && !await rules.CanStoreAsync(from.Slot.Space, second, time.GetUtcNow(), cancellationToken).ConfigureAwait(false))
                return new ItemMoveOutcome.Refused(ItemMoveRefusal.DisplacedCannotOccupySlot);
        }
        return await movement.MoveBetweenContainersAsync(from, into, count, cancellationToken).ConfigureAwait(false);

        bool Owned(BoundInventorySlot slot) => slot.Slot.Space is BusinessInventorySpace.AccountCargo or BusinessInventorySpace.SharedMaterial
            ? slot.Container.StorageId.TryGetAccountOwner(out var owner, out var type)
                && owner == session.AccountId && type == LegacyInventoryStorageCoordinates.ToStorage(slot.Slot).List
            : slot.Container.StorageId.TryGetCharacterOwner(out var character, out _) && character == session.CharacterId;
    }

    /// <summary>
    /// Lands a cross-storage move in the destination list: first into piles of the same item that
    /// still have room (lowest slot first), then whatever is left into the nominated slot when it
    /// is empty, otherwise into the first empty slot. Nothing already in the destination moves.
    /// </summary>
    private async ValueTask<ItemMoveOutcome> PlaceAsync(BoundInventorySlot from, BoundInventorySlot nominated,
        ItemReference moving, int count, CancellationToken cancellationToken)
    {
        var placement = await placements.PlacementForAsync(moving.ItemId, nominated.Slot.Space, cancellationToken)
            .ConfigureAwait(false);
        var openSlots = await nominated.Container.OpenSlotCountAsync(cancellationToken).ConfigureAwait(false);
        if (placement is null || nominated.Slot.Index < 0 || nominated.Slot.Index >= openSlots)
            return new ItemMoveOutcome.Refused(ItemMoveRefusal.ItemCannotOccupySlot);
        var moveCount = count <= 0 || count > moving.Count ? moving.Count : count;
        var held = await nominated.Container.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var snapshot = moving.Snapshot;
        var piles = moving.IsPile
            ? held.Where(item => snapshot.CanStackWith(item.Snapshot))
                .Select(item => new ItemPile(new BusinessSlot(nominated.Slot.Space, item.SlotIndex), item.Count)).ToArray()
            : [];
        var occupied = held.Select(item => new BusinessSlot(nominated.Slot.Space, item.SlotIndex)).ToHashSet();
        var arrival = arrivals.ForTransfer(piles, moveCount, moving.IsPile ? placement.StackLimit : 1,
            nominated.Slot, occupied, openSlots);
        if (arrival is ItemTransferArrival.ExceedsStackLimit)
            return new ItemMoveOutcome.Refused(ItemMoveRefusal.MergeExceedsStackLimit);
        if (arrival is not ItemTransferArrival.Placed { Steps: var steps })
            return new ItemMoveOutcome.Refused(ItemMoveRefusal.ContainerFull);
        var plan = steps.Select(step => step switch
        {
            ItemArrival.OntoExistingPile pile => (pile.Slot.Index, Count: pile.Added),
            ItemArrival.IntoEmptySlot empty => (empty.Slot.Index, empty.Count),
            _ => throw new InvalidOperationException($"Unexpected transfer step {step}."),
        }).ToArray();
        if (steps[^1] is ItemArrival.IntoEmptySlot { Slot: var free }
            && await movement.MayOccupySlotAsync(free, moving.ItemId, cancellationToken).ConfigureAwait(false) != SlotAdmission.Allowed)
            return new ItemMoveOutcome.Refused(ItemMoveRefusal.ItemCannotOccupySlot);

        for (var step = 0; step < plan.Length; step++)
        {
            var target = new BoundInventorySlot(new BusinessSlot(nominated.Slot.Space, plan[step].Index), nominated.Container);
            var result = await movement.MoveBetweenContainersAsync(from, target, plan[step].Count, cancellationToken)
                .ConfigureAwait(false);
            if (result is ItemMoveOutcome.Moved) continue;
            // Every step was checked above; a later refusal would leave the move half done.
            if (step == 0) return result;
            throw new InvalidOperationException($"Cargo placement step {step} was refused after earlier steps ran: {result}.");
        }
        var landed = plan[0].Index;
        return new ItemMoveOutcome.Moved(moveCount, moving.ItemId)
        {
            ActualDestinationSlot = landed == nominated.Slot.Index ? null : new BusinessSlot(nominated.Slot.Space, landed),
        };
    }

    private static bool Cargo(BusinessInventorySpace space) => space is BusinessInventorySpace.PersonalCargo or BusinessInventorySpace.AccountCargo;
    private static bool Served(BusinessInventorySpace space) => Cargo(space) || space.IsCarriedItemSpace() || space == BusinessInventorySpace.SharedMaterial;
}

internal sealed class CargoItemRuleService(ICargoItemDefinitionSource content) : ICargoItemRuleService
{
    public async ValueTask<bool> CanStoreAsync(BusinessInventorySpace destination, ItemReference item,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!ItemContainerAdmission.For(destination).Matches(item.Type)
            || !item.IsPile && item.Type is ItemTraits.Avatar or ItemTraits.Creature
            || item.Snapshot.Lifetime.IsExpired(now)) return false;
        var definition = await content.GetAsync(item.ItemId, cancellationToken).ConfigureAwait(false);
        if (definition is null) return false;
        if (destination == BusinessInventorySpace.PersonalCargo)
            return definition.AttachType != "trade delete" && !definition.ImpossibleContents.Contains("charac cargo");
        if (destination != BusinessInventorySpace.AccountCargo || definition.ImpossibleContents.Contains("account cargo")
            || item.Snapshot.EquipmentLockId != 0 || item.Snapshot.TradeRestriction != 0) return false;
        return definition.AttachType is "free" or "account"
            || definition.AttachType == "sealing" && item.Snapshot.SealFlag != 0;
    }
}
