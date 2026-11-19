using System.Collections.Immutable;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

internal sealed class AuraSkinSlotRuleService(IOpeningEffectPlanRuleService openingPlans) : IAuraSkinSlotRuleService
{
    /// <summary>光环幻化栏扩展券. A21 looks this id up itself before it sends CMD 0x0357 (VA 015A4AF3).</summary>
    internal const int TicketItemId = 490700411;
    internal const int ActionTicketItemId = 10157209;

    public ImmutableArray<DomainEffect> PlanOpening(BusinessSlot source, ItemReference item)
    {
        if (item.ItemId != TicketItemId)
            throw new OpeningRejectedException(OpeningRejection.WrongItem, $"Item {item.ItemId} does not open the aura skin slot.");
        return OpeningEffects(source, item);
    }

    public async ValueTask<ImmutableArray<DomainEffect>> PlanActionOpeningAsync(BusinessSlot source, ItemReference item, CancellationToken ct)
    {
        if (item.ItemId != ActionTicketItemId)
            throw new OpeningRejectedException(OpeningRejection.WrongItem, $"Item {item.ItemId} is not a supported aura action ticket.");
        var rule = await openingPlans.SourceRuleAsync(item.ItemId, ct).ConfigureAwait(false);
        if (rule is not { Action: "[open aura skin slot]", IsConsumedOnUse: true })
            throw new OpeningRejectedException(OpeningRejection.UnsupportedContent, "Invalid aura action ticket declaration.");
        return OpeningEffects(source, item);
    }

    private static ImmutableArray<DomainEffect> OpeningEffects(BusinessSlot source, ItemReference item) =>
        [new ConsumeFromContainerEffect(source, item.ItemId, 1), new OpenAuraSkinSlotEffect()];
}
