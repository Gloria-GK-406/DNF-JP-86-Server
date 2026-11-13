using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Abstractions.Inventory;

/// <summary>What opening the aura transmog avatar slot did.</summary>
/// <param name="Inventory">The ticket's consumption, or nothing when the slot was already open and no ticket was spent.</param>
public sealed record AuraSkinSlotResult(OpeningInventoryReceipt? Inventory);

/// <summary>Opens the aura transmog avatar slot (光环幻化栏) by spending one ticket from a main-inventory slot.</summary>
public interface IAuraSkinSlotDomainService
{
    /// <summary>
    /// Spends the ticket at <paramref name="source"/> and marks the slot open, in one transaction. An
    /// already open slot is reported as such without touching the inventory. A source holding anything
    /// else is refused with <see cref="OpeningRejectedException"/>.
    /// </summary>
    ValueTask<AuraSkinSlotResult> OpenAsync(SelectedSessionIdentity session, BusinessSlot source,
        DateTimeOffset at, IEffectFootprintRecorder footprint, CancellationToken ct);
}
