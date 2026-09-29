using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Inventory;

public sealed record NpcShopQuote(
    NpcShopItemDefinition Definition, ItemPlacement Placement, int Count, int GoldCost,
    IReadOnlyList<NpcShopMaterialCost> Materials);

public sealed record NpcShopQuoteResult(NpcShopRejection Rejection, NpcShopQuote? Quote);

public sealed record NpcShopPurchasePlan(
    IReadOnlyList<NpcShopSlotChange> MaterialChanges, IReadOnlyList<NpcShopGrantedSlot> GrantedSlots);

public sealed record NpcShopPlanningResult(NpcShopRejection Rejection, NpcShopPurchasePlan? Plan);

public interface INpcShopRuleService
{
    ValueTask<NpcShopQuoteResult> QuoteAsync(
        int npcId, int shopId, byte job, int itemId, int rawCount, CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<NpcShopItemDefinition>?> LimitedItemsAsync(
        int npcId, byte job, CancellationToken cancellationToken);

    NpcShopPlanningResult Plan(NpcShopQuote quote,
        IReadOnlyDictionary<BusinessInventorySpace, IReadOnlyList<ContainerSlotItem>> held,
        int gold, int itemIdentitySeed);

    string PurchasePeriod(NpcShopPurchaseLimit limit, DateTimeOffset now);
}
