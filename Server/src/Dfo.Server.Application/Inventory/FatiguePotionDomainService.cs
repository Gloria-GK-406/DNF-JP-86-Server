using Dfo.Server.Abstractions.Characters;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Abstractions.World;
using Dfo.Server.Data.Inventory;
using Dfo.Server.Data.World;

namespace Dfo.Server.Application.Inventory;

internal sealed class FatiguePotionDomainService(IOpeningInventoryDomainService inventory,
    ICharacterDomainService characters, IConsumableStateDomainService states,
    ISpecialConsumableEffectPlanRuleService rules, ITownAreaDomainService towns) : IFatiguePotionDomainService
{
    public async ValueTask<OpeningInventoryReceipt> UseAsync(SelectedSessionIdentity session,
        BusinessSlot sourceSlot, DateTimeOffset now, IEffectFootprintRecorder footprint, CancellationToken ct)
    {
        footprint.DemandEffects(new(session.AccountId, session.CharacterId, session));
        var source = await inventory.SourceAsync(session, sourceSlot, now, ct).ConfigureAwait(false);
        var progress = await characters.Get(session.CharacterId).ProgressAsync(ct).ConfigureAwait(false)
            ?? throw new OpeningRejectedException(OpeningRejection.ItemUnusable, "Character progress unavailable.");
        var plan = await rules.FatigueAsync(new(sourceSlot, source, progress.Level,
            towns.Find(session.CharacterId) is not null,
            await states.FatigueAsync(session.CharacterId, ct).ConfigureAwait(false),
            await states.UsageAsync(session.CharacterId, source.ItemId, ct).ConfigureAwait(false), now), ct).ConfigureAwait(false);
        return (await inventory.ApplyAsync(session, plan, now, footprint, ct).ConfigureAwait(false)).Inventory;
    }
}
