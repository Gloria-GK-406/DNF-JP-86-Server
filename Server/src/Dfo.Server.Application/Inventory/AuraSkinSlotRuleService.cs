using System.Collections.Immutable;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Abstractions.Inventory;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Application.Inventory;

internal sealed class AuraSkinSlotRuleService : IAuraSkinSlotRuleService
{
    /// <summary>光环幻化栏扩展券. A21 looks this id up itself before it sends CMD 0x0357 (VA 015A4AF3).</summary>
    internal const int TicketItemId = 490700411;

    public ImmutableArray<DomainEffect> PlanOpening(BusinessSlot source, ItemReference item)
    {
        if (item.ItemId != TicketItemId)
            throw new OpeningRejectedException(OpeningRejection.WrongItem, $"Item {item.ItemId} does not open the aura skin slot.");
        return [new ConsumeFromContainerEffect(source, item.ItemId, 1), new OpenAuraSkinSlotEffect()];
    }
}
