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
        return [new ConsumeFromContainerEffect(source, item.ItemId, 1), new OpenAuraSkinSlotEffect()];
    }

    public async ValueTask<RecipeAmount> ActionTicketMaterialAsync(int itemId, CancellationToken ct)
    {
        if (itemId != ActionTicketItemId)
            throw new OpeningRejectedException(OpeningRejection.WrongItem, $"Item {itemId} is not a supported aura action ticket.");
        var rule = await openingPlans.SourceRuleAsync(itemId, ct).ConfigureAwait(false);
        if (rule is not { Action: "[open aura skin slot]", IsConsumedOnUse: true, AuraSkinSlotMaterial: { } material }
            || material.ItemId == itemId)
            throw new OpeningRejectedException(OpeningRejection.UnsupportedContent, "Aura action ticket has no valid material declaration.");
        return new(material.ItemId, material.Count);
    }

    public ImmutableArray<DomainEffect> PlanActionOpening(BusinessSlot source, ItemReference item, RecipeAmount material,
        IReadOnlyList<OpeningHeldItem> held, DateTimeOffset at) =>
        [new ConsumeFromContainerEffect(source, item.ItemId, 1),
            .. openingPlans.MaterialCosts([material], 1, held, at).Items.Select(cost =>
                new ConsumeFromContainerEffect(cost.Slot, cost.ItemId, cost.Count)),
            new OpenAuraSkinSlotEffect()];
}
