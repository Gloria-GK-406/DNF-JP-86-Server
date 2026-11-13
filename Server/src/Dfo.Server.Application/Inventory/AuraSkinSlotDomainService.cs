using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Inventory;

internal sealed class AuraSkinSlotDomainService(IOpeningInventoryDomainService inventory, ICharacterEntryTable characters)
    : IAuraSkinSlotDomainService
{
    /// <summary>光环幻化栏扩展券. A21 looks this id up itself before it sends CMD 0x0357 (VA 015A4AF3).</summary>
    internal const int TicketItemId = 490700411;

    public async ValueTask<AuraSkinSlotResult> OpenAsync(SelectedSessionIdentity session, BusinessSlot source,
        DateTimeOffset at, IEffectFootprintRecorder footprint, CancellationToken ct)
    {
        footprint.DemandEffects(new(session.AccountId, session.CharacterId, session));
        var row = await characters.FindEntryAsync(session.CharacterId, ct).ConfigureAwait(false)
            ?? throw new OpeningRejectedException(OpeningRejection.ItemUnusable, "Character state unavailable.");
        // The client skips the request once the slot is open; a repeat that still arrives (a double
        // click before the first answer) gets the open slot back without a second ticket spent.
        if (row.AuraSkinSlot != 0) return new(null);
        var item = await inventory.SourceAsync(session, source, at, ct).ConfigureAwait(false);
        if (item.ItemId != TicketItemId)
            throw new OpeningRejectedException(OpeningRejection.WrongItem, $"Item {item.ItemId} does not open the aura skin slot.");
        var applied = await inventory.ApplyAsync(session,
            [new ConsumeFromContainerEffect(source, item.ItemId, 1), new OpenAuraSkinSlotEffect()], at, footprint, ct)
            .ConfigureAwait(false);
        return new(applied.Inventory);
    }
}
