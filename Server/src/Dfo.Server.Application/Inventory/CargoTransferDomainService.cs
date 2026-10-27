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
    ICharacterTable characters, IDungeonRunDomainService runs,
    IBusinessInventorySlotDomainService slots, ICargoItemRuleService rules,
    IItemMovementDomainService movement, IItemPlacementRuleService placements, TimeProvider time,
    IIllusionTowerDomainService? towers = null) : ICargoTransferDomainService
{
    public async ValueTask<ItemMoveOutcome> MoveAsync(SelectedSessionIdentity session, BusinessSlot source,
        BusinessSlot destination, int count, CancellationToken cancellationToken)
    {
        if (!sessions.IsInWorld(session) || runs.Find(session.CharacterId) is not null
            || towers?.IsInTower(session.CharacterId) == true
            || !await characters.BelongsToAsync(session.CharacterId, session.AccountId, cancellationToken).ConfigureAwait(false)
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
            if (second is not null && Cargo(from.Slot.Space)
                && !await rules.CanStoreAsync(from.Slot.Space, second, time.GetUtcNow(), cancellationToken).ConfigureAwait(false))
                return new ItemMoveOutcome.Refused(ItemMoveRefusal.DisplacedCannotOccupySlot);
        }
        BusinessSlot? actualDestination = null;
        // A21 click-deposit may nominate an existing copy even when the item stacks only to one.
        // Redirect only incoming cargo deposits; an ordinary same-container drag stays explicit.
        if ((from.Slot.Space.IsCarriedItemSpace() || from.Slot.Space == BusinessInventorySpace.SharedMaterial) && Cargo(into.Slot.Space)
            && first is { IsPile: true } && second is { IsPile: true } && first.ItemId == second.ItemId
            && (await placements.PlacementForAsync(first.ItemId, into.Slot.Space, cancellationToken).ConfigureAwait(false))?.StackLimit == 1)
        {
            var openSlots = await into.Container.OpenSlotCountAsync(cancellationToken).ConfigureAwait(false);
            if (into.Slot.Index < 0 || into.Slot.Index >= openSlots)
                return new ItemMoveOutcome.Refused(ItemMoveRefusal.ItemCannotOccupySlot);
            var occupied = (await into.Container.GetAllAsync(cancellationToken).ConfigureAwait(false))
                .Select(item => item.SlotIndex).ToHashSet();
            var empty = Enumerable.Range(0, openSlots).FirstOrDefault(index => !occupied.Contains(index), -1);
            if (empty < 0) return new ItemMoveOutcome.Refused(ItemMoveRefusal.ContainerFull);
            actualDestination = new BusinessSlot(into.Slot.Space, empty);
            into = new BoundInventorySlot(actualDestination.Value, into.Container);
        }
        var result = await movement.MoveBetweenContainersAsync(from, into, count, cancellationToken).ConfigureAwait(false);
        return result is ItemMoveOutcome.Moved moved && actualDestination is not null
            ? moved with { ActualDestinationSlot = actualDestination } : result;

        bool Owned(BoundInventorySlot slot) => slot.Slot.Space is BusinessInventorySpace.AccountCargo or BusinessInventorySpace.SharedMaterial
            ? slot.Container.StorageId.TryGetAccountOwner(out var owner, out var type)
                && owner == session.AccountId && type == LegacyInventoryStorageCoordinates.ToStorage(slot.Slot).List
            : slot.Container.StorageId.TryGetCharacterOwner(out var character, out _) && character == session.CharacterId;
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
