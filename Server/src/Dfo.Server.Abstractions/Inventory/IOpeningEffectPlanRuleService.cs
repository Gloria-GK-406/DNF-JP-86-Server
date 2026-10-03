using System.Collections.Immutable;
using Dfo.Server.Abstractions.Accounts;
using Dfo.Server.Abstractions.Content;
using Dfo.Server.Abstractions.Effects;
using Dfo.Server.Data.Accounts;
using Dfo.Server.Data.Inventory;

namespace Dfo.Server.Abstractions.Inventory;

public sealed record OpeningHeldItem(BusinessSlot Slot, ItemReference Item);
public sealed record RecipeEffectCosts(ImmutableArray<OpeningSlotConsumption> Items);
public interface IOpeningEffectPlanRuleService
{
    ValueTask<ItemUseRule> SourceRuleAsync(int itemId, CancellationToken ct);
    ImmutableArray<OpeningSlotConsumption> SourceCosts(BusinessSlot source, int itemId, ItemUseRule rule, int count);
    RecipeEffectCosts RecipeCosts(RecipePlan recipe, int sourceId, ItemUseRule rule, int quantity,
        IReadOnlyList<OpeningHeldItem> held, DateTimeOffset at);
    DoublePotUsageDecision DoublePot(DoublePotUsageFacts facts);
    ImmutableArray<DomainEffect> Build(int sourceId, ItemUseRule rule, IReadOnlyList<OpeningSlotConsumption> consumed,
        ImmutableArray<OpeningPreparedGrant> grants, IReadOnlyList<AccountPremiumEntryState> premiums, DateTimeOffset at,
        int gold = 0,
        int? luckBefore = null, int? luckAfter = null, DoublePotUsageDecision? quota = null);
}
