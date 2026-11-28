using Dfo.Server.Abstractions.Content;
using Dfo.Server.Data.ExpertJobs;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Inventory;

public sealed record NpcShopQuote(
    NpcShopItemDefinition Definition, ItemPlacement Placement, int Count, int GoldCost,
    IReadOnlyList<NpcShopMaterialCost> Materials);

public sealed record NpcShopQuoteResult(NpcShopRejection Rejection, NpcShopQuote? Quote);

public sealed record NpcShopPurchasePlan(
    IReadOnlyList<NpcShopSlotChange> MaterialChanges, IReadOnlyList<NpcShopGrantedSlot> GrantedSlots);

public sealed record NpcShopPlanningResult(NpcShopRejection Rejection, NpcShopPurchasePlan? Plan);

public readonly record struct NpcShopExpertJob(ExpertJobType Type, int Level);

public interface INpcShopRuleService
{
    ValueTask<NpcShopQuoteResult> QuoteAsync(
        int npcId, int shopId, byte job, int itemId, int rawCount, CancellationToken cancellationToken,
        int? activeVariant = null, NpcShopExpertJob expertJob = default);

    ValueTask<IReadOnlyList<NpcShopItemDefinition>?> LimitedItemsAsync(
        int npcId, byte job, CancellationToken cancellationToken, int? activeVariant = null, NpcShopExpertJob expertJob = default);

    NpcShopPlanningResult Plan(NpcShopQuote quote,
        IReadOnlyDictionary<BusinessInventorySpace, IReadOnlyList<ItemReference>> held,
        int gold, int itemIdentitySeed);

    string PurchasePeriod(NpcShopPurchaseLimit limit, DateTimeOffset now);
}
