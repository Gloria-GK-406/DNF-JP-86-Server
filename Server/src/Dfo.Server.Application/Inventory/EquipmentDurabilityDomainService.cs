using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.OnlineUsers;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.OnlineUsers;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Inventory;

internal sealed class EquipmentDurabilityDomainService(
    ISelectedSessionDomainService sessions,
    IEquipmentDomainService equipments) : IEquipmentDurabilityDomainService
{
    public async ValueTask<EquipmentDurabilityResult> DecreaseAsync(SelectedSessionIdentity session,
        BusinessSlot slot, CancellationToken cancellationToken)
    {
        if (!sessions.IsInWorld(session))
            return new(EquipmentDurabilityRejection.NoSession);

        // A21 worn slots: weapon, five armour pieces, support weapon, and charm. Accessories,
        // avatars, title and creature equipment do not receive this wear command upstream.
        if (slot is not ({ Space: BusinessInventorySpace.WornEquipment, Index: 0 or >= 2 and <= 6 or 12 }
            or { Space: BusinessInventorySpace.WornEquipmentExtension, Index: 1 }))
            return new(EquipmentDurabilityRejection.InvalidSlot);

        var equipment = await equipments.Of(session.CharacterId).FindAtAsync(slot, cancellationToken).ConfigureAwait(false);
        if (equipment is null) return new(EquipmentDurabilityRejection.ItemUnavailable);
        var changed = await equipment.DecreaseDurabilityAsync(1, cancellationToken).ConfigureAwait(false);
        return new(EquipmentDurabilityRejection.None, new(session, changed.BusinessSlot!.Value, changed.Snapshot.Durability));
    }
}
