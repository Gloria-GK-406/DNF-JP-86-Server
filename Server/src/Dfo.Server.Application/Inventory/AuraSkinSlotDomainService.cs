using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Inventory;

internal sealed class AuraSkinSlotDomainService(IOpeningInventoryDomainService inventory, ICharacterEntryTable characters,
    IAuraSkinSlotRuleService rules) : IAuraSkinSlotDomainService
{
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
        var applied = await inventory.ApplyAsync(session, rules.PlanOpening(source, item), at, footprint, ct)
            .ConfigureAwait(false);
        return new(applied.Inventory);
    }
}
