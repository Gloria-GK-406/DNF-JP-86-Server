using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.Sessions;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;
namespace Dfo.Server.Application.Inventory;
internal sealed class SharedEffectDomainService(ISelectedSessionDomainService sessions, ITownAreaDomainService areas,
    IBusinessInventorySlotDomainService inventory, IItemUseEffectRuleService rules, IItemUseEffectDomainService effects, TimeProvider clock) : ISharedEffectDomainService
{
    public async ValueTask<SharedEffectUseResult> UseAsync(SelectedSessionIdentity actor, BusinessSlot slot, int itemId, IEffectFootprintRecorder footprint, CancellationToken ct)
    {
        footprint.DemandActive(actor);
        if (!sessions.IsInWorld(actor) || areas.Find(actor.CharacterId) is not { } member || !actor.Owns(member)
            || !slot.Space.IsCarriedItemSpace() || itemId <= 0) return new(false);
        var bound = inventory.Find(actor.CharacterId, slot);
        var item = bound is null ? null : await bound.Container.GetAsync(bound.Slot.Index, ct).ConfigureAwait(false);
        if (item is null || item.ItemId != itemId || await rules.RuleForAsync(itemId, ct).ConfigureAwait(false) is not { IsReadable: true, IsConsumedOnUse: true, SharedEffectSelector: { } selector }) return new(false);
        footprint.DemandActive(actor);
        var used = await effects.UseAsync(actor, slot, item, footprint, ct).ConfigureAwait(false);
        if (used.Status != ItemUsability.Allowed) return new(false);
        var remaining = await bound!.Container.GetAsync(bound.Slot.Index, ct).ConfigureAwait(false);
        return new(true, selector, new(remaining?.Snapshot, clock.GetUtcNow()));
    }
}
