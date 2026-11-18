using System.Collections.Immutable;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Persistence;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Inventory;

internal sealed class AuraSkinSlotDomainService(IOpeningInventoryDomainService inventory, ICharacterEntryTable characters,
    IAuraSkinSlotRuleService rules, ICharacterBusinessInventoryDomainService bags,
    IItemPlacementRuleService placements) : IAuraSkinSlotDomainService
{
    public ValueTask<AuraSkinSlotResult> OpenAsync(SelectedSessionIdentity session, BusinessSlot source,
        DateTimeOffset at, IEffectFootprintRecorder footprint, CancellationToken ct) =>
        OpenAsync(session, source, at, footprint, actionTicket: false, ct);

    public ValueTask<AuraSkinSlotResult> OpenWithActionTicketAsync(SelectedSessionIdentity session, BusinessSlot source,
        DateTimeOffset at, IEffectFootprintRecorder footprint, CancellationToken ct) =>
        OpenAsync(session, source, at, footprint, actionTicket: true, ct);

    private async ValueTask<AuraSkinSlotResult> OpenAsync(SelectedSessionIdentity session, BusinessSlot source,
        DateTimeOffset at, IEffectFootprintRecorder footprint, bool actionTicket, CancellationToken ct)
    {
        footprint.DemandEffects(new(session.AccountId, session.CharacterId, session));
        var row = await characters.FindEntryAsync(session.CharacterId, ct).ConfigureAwait(false)
            ?? throw new OpeningRejectedException(OpeningRejection.ItemUnusable, "Character state unavailable.");
        // The client skips the request once the slot is open; a repeat that still arrives (a double
        // click before the first answer) gets the open slot back without a second ticket spent.
        if (row.AuraSkinSlot != 0) return new(null);
        var item = await inventory.SourceAsync(session, source, at, ct).ConfigureAwait(false);
        var plan = actionTicket
            ? await PlanActionOpeningAsync(session, source, item, at, ct).ConfigureAwait(false)
            : rules.PlanOpening(source, item);
        var applied = await inventory.ApplyAsync(session, plan, at, footprint, ct).ConfigureAwait(false);
        return new(applied.Inventory);
    }

    private async ValueTask<ImmutableArray<DomainEffect>> PlanActionOpeningAsync(
        SelectedSessionIdentity session, BusinessSlot source, ItemReference ticket, DateTimeOffset at, CancellationToken ct)
    {
        var material = await rules.ActionTicketMaterialAsync(ticket.ItemId, ct).ConfigureAwait(false);
        var placement = await placements.PlacementForAsync(material.ItemId, ct).ConfigureAwait(false);
        if (placement?.Destination.Space is not { } space || !space.IsCarriedItemSpace())
            throw new OpeningRejectedException(OpeningRejection.UnsupportedContent, "Aura action material has no carried inventory space.");
        var bag = bags.GetStore(session.CharacterId, BusinessInventoryOwnership.StoreOf(space)).FindContainer(space);
        OpeningHeldItem[] held = bag is null ? [] : (await bag.GetAllAsync(ct).ConfigureAwait(false))
            .OrderBy(item => item.SlotIndex).Select(item => new OpeningHeldItem(new(space, item.SlotIndex), item)).ToArray();
        return rules.PlanActionOpening(source, ticket, material, held, at);
    }
}
