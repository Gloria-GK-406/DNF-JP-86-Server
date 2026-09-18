using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Inventory;

internal sealed class EquipmentDurabilityDomainService(
    ISelectedSessionDomainService sessions, IOnlineUserQueryDomainService onlineUsers,
    ICharacterContainerDomainService containers) : IEquipmentDurabilityDomainService
{
    public async ValueTask<EquipmentDurabilityResult> DecreaseAsync(SelectedSessionIdentity session,
        byte slotIndex, CancellationToken cancellationToken)
    {
        if (sessions.Find(session.AccountId) != session
            || !onlineUsers.TryGetByConnection(session.ConnectionId, out var online)
            || online is not { State: OnlineUserState.CharacterActive }
            || online.AccountId != session.AccountId || online.CurrentCharacterId != session.CharacterId)
            return new(EquipmentDurabilityRejection.NoSession);

        // A21 worn slots: weapon, five armour pieces, support weapon, and charm. Accessories,
        // avatars, title and creature equipment do not receive this wear command upstream.
        if (slotIndex is not (12 or >= 14 and <= 18 or 24 or 30))
            return new(EquipmentDurabilityRejection.InvalidSlot);

        var bag = containers.FindContainer(session.CharacterId, InventoryContainerType.Equipment);
        var item = bag is null ? null : await bag.ReadSlotAsync(slotIndex, cancellationToken).ConfigureAwait(false);
        var equipment = EquipmentInstance.From(item);
        if (equipment is null)
            return new(EquipmentDurabilityRejection.ItemUnavailable);

        if (equipment.DecreaseDurability(1))
            await bag!.PutAsync(slotIndex, equipment.Core, cancellationToken).ConfigureAwait(false);
        return new(EquipmentDurabilityRejection.None,
            new(session, slotIndex, equipment.Core.Durability));
    }
}
