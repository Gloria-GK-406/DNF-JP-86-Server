using System.Collections.Immutable;
using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Content;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Inventory;

public enum CeraShopPaymentMode { Ordinary, CeraOnly, PointsOnly }
public sealed record CeraShopCharge(int ProductId, int Gold, int Cera, CeraShopPaymentMode Mode);
public sealed record CeraShopGrant(CeraShopItemDefinition Item, ItemPlacement Placement, int Count,
    byte Ability, int ExpireDate);
public sealed record CeraShopQuote(ImmutableArray<int> ProductIds, ImmutableArray<CeraShopCharge> Charges,
    ImmutableArray<CeraShopGrant> Grants)
{
    public ImmutableArray<InventoryExpansionGrant> Expansions { get; init; } = [];
    public ImmutableArray<CeraShopSubscriptionGrant> Subscriptions { get; init; } = [];
    public ImmutableArray<CeraShopPurchaseRestrictionDeclaration> Restrictions { get; init; } = [];
}
public sealed record CeraShopRestrictionPlanningResult(CeraShopRejection Rejection, IReadOnlyList<DomainEffect>? Effects, string? Detail);
public sealed record CeraShopSubscriptionGrant(PremiumServiceType Service, TimeSpan Duration);
public sealed record CeraShopQuoteResult(CeraShopRejection Rejection, CeraShopQuote? Quote, string? Detail);
public sealed record CeraShopSlotWrite(BusinessSlot Slot,
    InventoryItemCoreState Core, InventoryItemCreationFacts? CreationFacts);
public sealed record CeraShopPurchasePlan(AccountCeraBalances Balances, int GoldCost,
    ImmutableArray<CeraShopSlotWrite> Writes);
public sealed record CeraShopPlanningResult(CeraShopRejection Rejection, CeraShopPurchasePlan? Plan, string? Detail);

public interface ICeraShopRuleService
{
    CeraShopRestrictionPlanningResult PlanRestrictions(CeraShopQuote quote, CeraShopRestrictionSnapshot state, DateTimeOffset now);
    ValueTask<CeraShopQuoteResult> QuoteAsync(CeraShopPurchaseInput input, int characterLevel,
        DateTimeOffset now, CancellationToken cancellationToken);
    CeraShopPlanningResult Plan(CeraShopQuote quote,
        IReadOnlyDictionary<BusinessInventorySpace, IReadOnlyList<ContainerSlotItem>> held,
        int gold, AccountCeraBalances balances, int identitySeed);
}
