using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Inventory;

internal sealed class CargoTransferDomainService(ISelectedSessionDomainService sessions,
    IOnlineUserQueryDomainService onlineUsers, ICharacterTable characters, IDungeonRunDomainService runs,
    IBusinessInventorySlotDomainService slots, ICargoItemRuleService rules,
    IItemMovementDomainService movement, TimeProvider time) : ICargoTransferDomainService
{
    public async ValueTask<ItemMoveOutcome> MoveAsync(SelectedSessionIdentity session, BusinessSlot source,
        BusinessSlot destination, int count, CancellationToken cancellationToken)
    {
        if (sessions.Find(session.AccountId) != session || runs.Find(session.CharacterId) is not null
            || !onlineUsers.TryGetByConnection(session.ConnectionId, out var online)
            || online is not { State: OnlineUserState.CharacterActive }
            || online.AccountId != session.AccountId || online.CurrentCharacterId != session.CharacterId
            || !await characters.BelongsToAsync(session.CharacterId, session.AccountId, cancellationToken).ConfigureAwait(false)
            || !Served(source.Space) || !Served(destination.Space))
            return new ItemMoveOutcome.Refused(ItemMoveRefusal.ItemCannotOccupySlot);
        var from = slots.Find(session.CharacterId, source);
        var into = slots.Find(session.CharacterId, destination);
        if (from is null || into is null || !Owned(from) || !Owned(into))
            return new ItemMoveOutcome.Refused(ItemMoveRefusal.ItemCannotOccupySlot);
        var first = await from.Container.ReadSlotAsync(source.Index, cancellationToken).ConfigureAwait(false);
        var second = await into.Container.ReadSlotAsync(destination.Index, cancellationToken).ConfigureAwait(false);
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
        return await movement.MoveBetweenContainersAsync(from, into, count, cancellationToken).ConfigureAwait(false);

        bool Owned(BoundInventorySlot slot) => slot.Slot.Space == BusinessInventorySpace.AccountCargo
            ? slot.Container.StorageId.TryGetAccountCargoOwner(out var owner) && owner == session.AccountId
            : slot.Container.StorageId.TryGetCharacterOwner(out var character, out _) && character == session.CharacterId;
    }

    private static bool Cargo(BusinessInventorySpace space) => space is BusinessInventorySpace.PersonalCargo or BusinessInventorySpace.AccountCargo;
    private static bool Served(BusinessInventorySpace space) => Cargo(space) || space.IsCarriedItemSpace();
}

internal sealed class CargoItemRuleService(ICargoItemDefinitionSource content) : ICargoItemRuleService
{
    public async ValueTask<bool> CanStoreAsync(BusinessInventorySpace destination, ContainerSlotItem item,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (item.ItemKind is not (1 or 2 or 3 or 10) || item.WorldClass is ItemWorldClass.Avatar or ItemWorldClass.Creature
            || item.Core.ExpireTime > 0 && item.Core.ExpireTime <= now.ToUnixTimeSeconds()) return false;
        var definition = await content.GetAsync(item.ItemId, cancellationToken).ConfigureAwait(false);
        if (definition is null) return false;
        if (destination == BusinessInventorySpace.PersonalCargo)
            return definition.AttachType != "trade delete" && !definition.ImpossibleContents.Contains("charac cargo");
        if (destination != BusinessInventorySpace.AccountCargo || definition.ImpossibleContents.Contains("account cargo")
            || item.Core.EquipmentLockId != 0 || item.Core.TradeRestriction != 0) return false;
        return definition.AttachType is "free" or "account"
            || definition.AttachType == "sealing" && item.Core.SealFlag != 0;
    }
}
