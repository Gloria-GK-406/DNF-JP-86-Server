using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Inventory;

public sealed record EquipmentCompoundReceipt(SelectedSessionIdentity Session, BusinessSlot Source,
    BusinessSlot Result, IReadOnlyList<InventorySlotEffectChange> Changes, DateTimeOffset SettledAt);

public interface IEquipmentCompoundDomainService
{
    ValueTask<EquipmentCompoundReceipt> CompoundAsync(SelectedSessionIdentity session, BusinessSlot source,
        bool directed, int part, IEffectFootprintRecorder footprint, CancellationToken ct);
}
