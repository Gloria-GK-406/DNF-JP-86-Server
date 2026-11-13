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
    IItemMovementDomainService movement, IItemPlacementRuleService placements, TimeProvider time) : ICargoTransferDomainService
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
                return await PlaceAsync(from, into, first, second, count, cancellationToken).ConfigureAwait(false);
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
        ItemReference moving, ItemReference? occupant, int count, CancellationToken cancellationToken)
    {
        var placement = await placements.PlacementForAsync(moving.ItemId, nominated.Slot.Space, cancellationToken)
            .ConfigureAwait(false);
        var openSlots = await nominated.Container.OpenSlotCountAsync(cancellationToken).ConfigureAwait(false);
        if (placement is null || nominated.Slot.Index < 0 || nominated.Slot.Index >= openSlots)
            return new ItemMoveOutcome.Refused(ItemMoveRefusal.ItemCannotOccupySlot);
        var moveCount = count <= 0 || count > moving.Count ? moving.Count : count;
        var limit = placement.StackLimit > 0 ? placement.StackLimit : int.MaxValue;
        var held = await nominated.Container.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var plan = new List<(int Slot, int Count)>();
        var remaining = moveCount;
        if (moving.IsPile && limit > 1)
        {
            var snapshot = moving.Snapshot;
            foreach (var pile in held.Where(item => item.SlotIndex < openSlots && snapshot.CanStackWith(item.Snapshot))
                         .OrderBy(item => item.SlotIndex))
            {
                var room = (long)limit - pile.Count;
                if (room <= 0) continue;
                var part = (int)Math.Min(room, remaining);
                plan.Add((pile.SlotIndex, part));
                remaining -= part;
                if (remaining == 0) break;
            }
        }
        if (remaining > 0)
        {
            if (moving.IsPile && remaining > limit)
                return new ItemMoveOutcome.Refused(ItemMoveRefusal.MergeExceedsStackLimit);
            var occupied = held.Select(item => item.SlotIndex).ToHashSet();
            var empty = occupant is null ? nominated.Slot.Index
                : Enumerable.Range(0, openSlots).FirstOrDefault(index => !occupied.Contains(index), -1);
            if (empty < 0) return new ItemMoveOutcome.Refused(ItemMoveRefusal.ContainerFull);
            if (await movement.MayOccupySlotAsync(new BusinessSlot(nominated.Slot.Space, empty), moving.ItemId, cancellationToken)
                    .ConfigureAwait(false) != SlotAdmission.Allowed)
                return new ItemMoveOutcome.Refused(ItemMoveRefusal.ItemCannotOccupySlot);
            plan.Add((empty, remaining));
        }

        for (var step = 0; step < plan.Count; step++)
        {
            var target = new BoundInventorySlot(new BusinessSlot(nominated.Slot.Space, plan[step].Slot), nominated.Container);
            var result = await movement.MoveBetweenContainersAsync(from, target, plan[step].Count, cancellationToken)
                .ConfigureAwait(false);
            if (result is ItemMoveOutcome.Moved) continue;
            // Every step was checked above; a later refusal would leave the move half done.
            if (step == 0) return result;
            throw new InvalidOperationException($"Cargo placement step {step} was refused after earlier steps ran: {result}.");
        }
        var landed = plan[0].Slot;
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
